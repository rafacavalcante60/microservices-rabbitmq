# Sistema de Pedidos — Microsserviços .NET 8 + RabbitMQ

Sistema de pedidos de e-commerce em microsserviços, com comunicação por eventos
assíncronos no RabbitMQ. O pedido é aceito imediatamente, a cobrança acontece
depois e o cliente é notificado quando houver resultado.

> Status: em construção, 8 de 28 tarefas concluídas. O `OrderService` já aceita
> um pedido por `POST /orders`, persiste no PostgreSQL e publica `OrderCreated`
> pelo padrão outbox — inclusive com o RabbitMQ fora do ar. O `PaymentService` e
> o `NotificationService` ainda não consomem nada. O [roadmap](#roadmap) mostra
> o andamento.

---

## O problema

O cliente não pode ficar esperando a confirmação de um pagamento que depende de
um processador externo, pode demorar ou pode não responder. O sistema precisa
aceitar o pedido na hora e resolver a cobrança depois, sem perder nada no
caminho. É um cenário de consistência eventual.

## Arquitetura

Três serviços de domínio se comunicam apenas por eventos. Não há orquestrador:
cada serviço reage a fatos que já aconteceram. Esse modelo se chama coreografia.

```mermaid
flowchart LR
    C[Cliente] -->|POST /orders| GW[API Gateway<br/>YARP]
    GW --> OS[OrderService]

    OS -.->|OrderCreated| MQ{{RabbitMQ}}
    MQ -.-> PS[PaymentService]
    PS -.->|PaymentApproved<br/>PaymentDeclined<br/>PaymentFailed| MQ
    MQ -.-> OS
    MQ -.-> NS[NotificationService]

    OS --- ODB[(PostgreSQL<br/>orders_db)]
    PS --- PDB[(PostgreSQL<br/>payments_db)]
    NS --- NDB[(MongoDB<br/>notifications)]
```

1. O cliente cria o pedido e recebe de imediato o identificador e a situação
   `Pending`, sem esperar pelo pagamento.
2. `OrderService` persiste o pedido e publica `OrderCreated`.
3. `PaymentService` consome, decide a cobrança e publica um dos três desfechos.
4. `OrderService` consome o desfecho e move o pedido para o estado final.
5. `NotificationService` consome o mesmo desfecho, em paralelo, e registra a
   notificação.

Nenhum serviço acessa o banco de outro. Um schema compartilhado acoplaria os
serviços no nível dos dados e tiraria a possibilidade de evoluí-los em separado.

## Decisões técnicas

As decisões abaixo valem para o sistema inteiro, mas nem todas já estão em
código. As que ainda não estão trazem um aviso com a tarefa em que chegam.

### Outbox

Salvar o pedido no PostgreSQL e publicar `OrderCreated` no RabbitMQ são duas
operações em sistemas diferentes. Se a segunda falha depois da primeira, o
pedido existe e ninguém fica sabendo, e o sistema fica inconsistente sem gerar
erro.

O evento é gravado na mesma transação do pedido, numa tabela `outbox`, e um
processo separado publica a partir dali.

Isso já é verificável: com o RabbitMQ parado, o `POST /orders` continua
respondendo `201` e o evento fica esperando na tabela `outbox_message`. Quando o
broker volta, a linha some da tabela e a mensagem aparece no exchange, sem
nenhuma intervenção.

### Consumidores idempotentes

> Parcial: vale para o PaymentService (T-016, T-017). Chega ao OrderService e ao
> NotificationService nas T-018 e T-019.

RabbitMQ garante entrega pelo menos uma vez, então mensagem repetida é um caso
normal: basta um `ack` se perder para a mesma mensagem chegar de novo.

Um filtro no pipeline do MassTransit registra o `MessageId` no Redis com
`SET NX EX` e descarta o que já foi processado, sem que nenhum consumidor
precise lembrar de se proteger. Como garantia adicional, o banco tem restrições
únicas (`payments.order_id`, e `notifications.orderId` na T-019): se o Redis
ficar indisponível, o filtro falha aberto e é a constraint que impede o dano.

### Tratamento de falhas

O domínio separa pagamento recusado (o processador respondeu "não", é decisão de
negócio) de pagamento falhou (o processador não respondeu, é problema técnico).
A ação de suporte é diferente em cada caso, e por isso são dois eventos
distintos em `Shared.Contracts`.

> Parcial: vale para o PaymentService (T-014, T-017). Os demais consumidores
> chegam nas T-018 e T-019.

São **dois** retries, e eles não se confundem. A chamada ao processador tem
política Polly de 5 tentativas com backoff exponencial: esgotadas, o desfecho é
`PaymentFailed` e o cliente é notificado — falha de gateway é caminho de
negócio, não incidente. O consumidor tem o retry do MassTransit, de 3
tentativas, para falha de infraestrutura (o banco caiu); o que falhar nas três
vai para a dead-letter queue com log de erro, nunca descartado em silêncio.

Juntar os dois faria um Postgres fora do ar virar "pagamento recusado" na cara
do cliente.

### Rastreabilidade

> Parcial: o identificador já atravessa Orders → Payments e aparece nos logs
> JSON dos dois. Ele passa a nascer no gateway na T-021; hoje nasce na criação
> do pedido.

Todo evento carrega um `CorrelationId` propagado pela cadeia inteira, o que
permite seguir um fluxo assíncrono de três saltos nos logs.

## Stack

| Item | Escolha |
|---|---|
| Runtime | .NET 8 (LTS) |
| Mensageria | RabbitMQ via MassTransit |
| Banco transacional | PostgreSQL + EF Core (bancos separados por serviço) |
| Banco documental | MongoDB (histórico de notificações) |
| Cache / idempotência | Redis |
| Gateway | YARP |
| Logs | Serilog estruturado, saída JSON |
| Testes | xUnit + FluentAssertions + Testcontainers |
| Orquestração local | Docker Compose |

MassTransit em vez de `RabbitMQ.Client` puro porque já traz retry, DLQ,
serialização e outbox prontos, o que evita reimplementar essa camada à mão.

Dois tipos de banco porque os padrões de acesso são diferentes: Orders e
Payments precisam de transação e integridade referencial; Notifications é
histórico append-only, com formato que varia por canal.

## Rodando localmente

Requisitos: Docker e .NET 8 SDK.

```bash
git clone https://github.com/rafacavalcante60/microservices-rabbitmq.git
cd microservices-rabbitmq

# Sobe RabbitMQ, PostgreSQL, MongoDB e Redis
docker compose up -d

# Testes de domínio, rodam sem infraestrutura
dotnet test
```

Painel do RabbitMQ: <http://localhost:15672> (`guest` / `guest`)

Com a infraestrutura de pé, o serviço de pedidos roda direto pelo SDK:

```bash
dotnet run --project src/OrderService --urls http://localhost:8081

curl -X POST localhost:8081/orders \
  -H 'Content-Type: application/json' \
  -d '{
    "customerId": "11111111-1111-1111-1111-111111111111",
    "items": [
      {"productId":"22222222-2222-2222-2222-222222222222","productName":"Teclado","quantity":2,"unitPrice":10.00},
      {"productId":"33333333-3333-3333-3333-333333333333","productName":"Mouse","quantity":3,"unitPrice":5.00}
    ]
  }'
# 201 → {"orderId":"…","status":"Pending","totalAmount":35.00}
```

O total nasce da soma dos itens: um `totalAmount` enviado no corpo não é lido,
porque o campo não existe no contrato de entrada.

> Os serviços ainda não sobem pelo Compose, isso chega na T-022. O pedido também
> ainda não sai de `Pending`: quem o move é o desfecho do pagamento, na T-018.

## Estrutura

```
├── specs/                   # spec, plano e tarefas de cada feature
├── src/
│   ├── Shared.Contracts/    # contratos de evento, sem dependências
│   ├── ApiGateway/
│   ├── OrderService/        # + OrderService.Domain
│   ├── PaymentService/      # + PaymentService.Domain
│   └── NotificationService/
├── tests/
└── docker-compose.yml
```

Cada serviço segue `Api` → `Application` → `Domain` ← `Infrastructure`, com as
dependências apontando para dentro. O domínio é um projeto separado sem nenhum
`PackageReference`, então um `using` de EF Core nas regras de negócio não
compila.

## Roadmap

| Etapa | Tarefas | Status |
|---|---|---|
| Fundação — solução, Docker Compose, contratos | T-001 → T-003 | ✅ |
| Domínio de pedidos | T-004 → T-005 | ✅ |
| OrderService — persistência, outbox, `POST /orders` | T-006 → T-008 | ✅ |
| OrderService — idempotência, consulta, observabilidade | T-009 → T-011 | ⬜ |
| PaymentService — regra, retry, consumidor | T-012 → T-017 | ⬜ |
| Fluxo completo ponta a ponta | T-018 | ⬜ |
| Notificações e API Gateway | T-019 → T-021 | ⬜ |
| Tudo em contêineres | T-022 | ⬜ |
| Testes de integração com Testcontainers | T-023 → T-026 | ⬜ |
| ADRs e documentação | T-027 → T-028 | ⬜ |

## Como este projeto é construído

Desenvolvimento spec-driven: nenhuma linha de código de produção é escrita antes
de existir uma spec aprovada e um plano derivado dela.

```
spec (o quê e por quê) → plano (como) → tarefas → código → teste verde
```

- [`specs/001-pedido-pagamento-notificacao/spec.md`](specs/001-pedido-pagamento-notificacao/spec.md) — 15 regras de negócio e 16 critérios de aceite, em linguagem de negócio
- [`specs/001-pedido-pagamento-notificacao/plan.md`](specs/001-pedido-pagamento-notificacao/plan.md) — cada decisão técnica com a justificativa e a alternativa descartada

Um commit por tarefa concluída.
