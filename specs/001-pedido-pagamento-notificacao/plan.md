# Plano 001: Criação de pedido com pagamento e notificação

**Spec:** ./spec.md
**Status:** rascunho
**Criado em:** 2026-08-16

## Visão geral

Três serviços de domínio conversam exclusivamente por eventos no RabbitMQ, em
**coreografia** — não há orquestrador. `OrderService` recebe o pedido por HTTP,
persiste no PostgreSQL e publica `OrderCreated` na mesma transação, via padrão
outbox. `PaymentService` consome esse evento, chama um gateway de pagamento
simulado e publica um de três desfechos possíveis. `OrderService` consome o
desfecho e transiciona o pedido para seu estado final; `NotificationService`
consome o mesmo desfecho, em paralelo, e registra a notificação no MongoDB. Um
gateway YARP é a única porta de entrada externa. O cliente recebe resposta no
passo 2 e nunca espera pelo pagamento.

## Serviços afetados

Todos são criados nesta feature — o repositório está vazio.

| Serviço | Responsabilidade | Estado |
|---|---|---|
| **Shared.Contracts** | Biblioteca de classe com os contratos de evento. Sem dependência de infraestrutura | — |
| **ApiGateway** | Porta de entrada única. Roteia para Orders e Notifications, gera o `CorrelationId` | Sem estado |
| **OrderService** | Dono do agregado `Order`. Valida, calcula o total, persiste, publica `OrderCreated`, consome os desfechos e aplica a transição de estado | PostgreSQL `orders_db` |
| **PaymentService** | Dono do agregado `Payment`. Consome `OrderCreated`, decide aprovação, publica o desfecho | PostgreSQL `payments_db` |
| **NotificationService** | Registra a notificação do desfecho. Somente escrita e leitura de histórico | MongoDB `notifications_db` |

`PaymentService` e `NotificationService` não expõem HTTP público; ambos sobem só
com `/health` e, no caso de Notifications, um endpoint de consulta interno usado
pelo gateway e pelos testes.

### Camadas internas

Cada serviço segue `Api` → `Application` → `Domain` ← `Infrastructure`, com as
dependências apontando para dentro (constituição, "Estrutura de pastas").
`Domain` referencia apenas a BCL: nenhum EF, nenhum MassTransit. Isso é o que
permite testar as regras de negócio sem subir nada.

**O domínio é um projeto separado, não uma pasta** (revisão de 2026-08-16, na
T-001): `OrderService.Domain` e `PaymentService.Domain` são bibliotecas com zero
`PackageReference`. Pasta dentro do projeto de API não impede um `using` de EF
no domínio — a regra dependeria da disciplina de quem escreve. Como projeto, o
compilador a garante. `Api`, `Application` e `Infrastructure` continuam sendo
pastas dentro do projeto do serviço; a fronteira que precisa de força é só a do
domínio.

`NotificationService` permanece projeto único: registra notificações e não tem
regra de negócio para isolar. Camada vazia é cerimônia sem função.

## Contratos de mensagem

Todos em `Shared.Contracts`, `record` imutável, nome no passado (princípio
VIII). Todos carregam `CorrelationId` para rastreio ponta a ponta (princípio
VII).

### `OrderCreated`

Publicado por **OrderService** na mesma transação da criação do pedido.
Consumido por **PaymentService**.

| Campo | Tipo | Nota |
|---|---|---|
| `OrderId` | `Guid` | |
| `CustomerId` | `Guid` | |
| `TotalAmount` | `decimal` | Calculado pelo sistema (RN-4) |
| `Currency` | `string` | Sempre `"BRL"` (RN-3) |
| `OccurredAt` | `DateTime` | UTC |
| `CorrelationId` | `Guid` | |

Os itens do pedido **não** viajam no evento: `PaymentService` só precisa do
valor total para cobrar. Evento carrega o mínimo que o consumidor precisa —
contrato menor, acoplamento menor.

### `PaymentApproved`

Publicado por **PaymentService** quando o gateway aprova. Consumido por
**OrderService** e **NotificationService**.

| Campo | Tipo |
|---|---|
| `PaymentId` | `Guid` |
| `OrderId` | `Guid` |
| `CustomerId` | `Guid` |
| `Amount` | `decimal` |
| `OccurredAt` | `DateTime` |
| `CorrelationId` | `Guid` |

### `PaymentDeclined`

Publicado quando o gateway responde recusando (RN-10). Mesmos consumidores.

Campos de `PaymentApproved`, mais `Reason` (`string`) — o motivo mostrado ao
cliente na notificação (RN-12, CA-7).

### `PaymentFailed`

Publicado quando o gateway não respondeu em nenhuma das 5 tentativas (RN-11).
Mesmos consumidores.

Campos de `PaymentApproved`, mais `Reason` (`string`) e `Attempts` (`int`).

**Por que três eventos em vez de um `PaymentProcessed` com um campo de status:**
consumidores diferentes reagem a desfechos diferentes, e roteamento por tipo é
nativo no MassTransit. Um evento único forçaria todo consumidor a receber tudo e
filtrar com `if` — e adicionar um quarto desfecho amanhã quebraria os `if` de
todo mundo. Alternativa descartada: evento único com enum de status.

## Modelo de dados

### OrderService — PostgreSQL `orders_db`

**`orders`**

| Coluna | Tipo | Nota |
|---|---|---|
| `id` | `uuid` PK | |
| `customer_id` | `uuid` NOT NULL | |
| `idempotency_key` | `text` NOT NULL | Enviada pelo cliente (RN-8) |
| `total_amount` | `numeric(18,2)` NOT NULL | |
| `currency` | `char(3)` NOT NULL DEFAULT `'BRL'` | |
| `status` | `text` NOT NULL | `Pending`, `Paid`, `PaymentDeclined`, `PaymentFailed` |
| `status_reason` | `text` NULL | Motivo, quando o desfecho é negativo |
| `created_at` | `timestamptz` NOT NULL | |
| `updated_at` | `timestamptz` NOT NULL | |

- **Índice único `(customer_id, idempotency_key)`** — é isto que garante CA-11 e
  CA-12. Não é uma checagem em código: é o banco recusando a segunda inserção.
- Índice em `status` para consulta operacional.

**`order_items`**

`id` PK, `order_id` FK → `orders(id)` ON DELETE CASCADE, `product_id` `uuid`,
`product_name` `text`, `quantity` `int`, `unit_price` `numeric(18,2)`.

**Tabelas do MassTransit:** `outbox_message`, `outbox_state`, `inbox_state`
(ver Decisão D-3).

`status` é gravado como texto e não como inteiro: um `int` no banco torna o
suporte cego — ninguém sabe se `2` é Pago ou Recusado sem abrir o código, e uma
reordenação do enum corrompe o histórico silenciosamente.

`numeric(18,2)` e `decimal` em C#, nunca `float`/`double`: ponto flutuante
binário não representa `0,10` exatamente, e erro de arredondamento em dinheiro é
defeito, não imprecisão aceitável.

### PaymentService — PostgreSQL `payments_db`

**`payments`**

| Coluna | Tipo | Nota |
|---|---|---|
| `id` | `uuid` PK | |
| `order_id` | `uuid` NOT NULL **UNIQUE** | Garante RN-9 / CA-10 |
| `customer_id` | `uuid` NOT NULL | |
| `amount` | `numeric(18,2)` NOT NULL | |
| `status` | `text` NOT NULL | `Approved`, `Declined`, `Failed` |
| `reason` | `text` NULL | |
| `attempts` | `int` NOT NULL | Quantas chamadas ao gateway |
| `created_at` / `completed_at` | `timestamptz` | |

A **restrição única em `order_id`** é a garantia final de "cobra uma vez só". A
checagem em Redis evita o trabalho; a constraint impede o dano. Redundância
deliberada: a primeira é otimização, a segunda é correção.

Banco relacional aqui e não documental porque cobrança exige integridade e a
unicidade precisa ser garantida pelo próprio armazenamento.

### NotificationService — MongoDB `notifications_db`

Coleção **`notifications`**

```
{ _id, orderId, customerId, type, message, reason, createdAt, correlationId }
```

`type` ∈ `OrderPaid`, `PaymentDeclined`, `PaymentFailed`.

**Índice único em `orderId`** — garante RN-12 e CA-6/CA-7/CA-10 ("exatamente uma
notificação"). Índice em `customerId` para o histórico.

MongoDB aqui porque notificação é registro *append-only*, nunca atualizado,
nunca relacionado a outra entidade, e o formato tende a variar por canal quando
novos tipos surgirem. É o caso em que schema flexível paga; não há transação a
proteger.

### Redis

Chaves `idem:{consumer}:{messageId}` gravadas com `SET NX EX 604800` (7 dias).
Ver Decisão D-4 e o risco R-5.

## Endpoints HTTP

Todos atravessam o gateway em `http://localhost:8080`.

### `POST /orders`

Header obrigatório: `Idempotency-Key: <string>` (RN-8).

```json
{
  "customerId": "uuid",
  "items": [
    { "productId": "uuid", "productName": "string", "quantity": 2, "unitPrice": 10.00 }
  ]
}
```

| Código | Quando | Corpo |
|---|---|---|
| `201 Created` | Pedido novo criado | `{ orderId, status: "Pending", totalAmount }` |
| `200 OK` | Repetição com a mesma chave (CA-11) | O mesmo corpo da primeira resposta |
| `400 Bad Request` | Violação de RN-1/2/3 | `ProblemDetails` com as violações (CA-4, CA-5) |
| `422` | — | Não usado; validação de entrada é 400 |

`201` na criação e `200` na repetição porque o segundo caso não criou recurso
nenhum. O cliente que não distingue os dois continua funcionando; o que
distingue, ganha informação. Alternativa descartada: `201` sempre, mais simples
mas mente sobre o que aconteceu.

O corpo **não** traz o resultado do pagamento — ele ainda não existe (RN-14).

### `GET /orders/{id}`

`200` com `{ orderId, customerId, status, statusReason, totalAmount, currency, items[], createdAt, updatedAt }` (CA-15).
`404` quando não existe (CA-16).

### `GET /notifications/order/{orderId}`

`200` com a lista de notificações do pedido — usado para verificar CA-6, CA-7,
CA-10 e CA-13.

### `GET /health` e `GET /health/ready`

Em todos os serviços (princípio VI). `ready` checa Postgres/Mongo, RabbitMQ e
Redis conforme a dependência de cada um.

## Fluxo ponta a ponta

1. Requisição chega ao **gateway**. Middleware lê o header `X-Correlation-Id` ou
   gera um novo `Guid`, e o propaga adiante. *(princípio VII)*
2. **OrderService** valida a requisição contra RN-1, RN-2, RN-3. Falhou → `400`,
   nada é persistido (FA-1).
3. O domínio constrói o agregado `Order`, calcula `TotalAmount` a partir dos
   itens e ignora qualquer total que tenha vindo do cliente (RN-4, CA-3).
4. **Uma transação** grava `orders`, `order_items` e a mensagem `OrderCreated`
   na tabela outbox. *(outbox — princípio V)*
   - Violação do índice único `(customer_id, idempotency_key)` → o serviço
     captura, busca o pedido existente e devolve `200` com ele (CA-11).
5. Commit. A API responde `201 Pending`. **O cliente termina aqui** (RN-14, CA-1).
6. O *delivery service* do outbox lê a tabela e publica `OrderCreated` no
   RabbitMQ. Se o broker estiver fora, a mensagem fica na tabela e é publicada
   quando ele voltar — nada se perde.
7. **PaymentService** consome `OrderCreated`.
   - Consulta o Redis pelo `MessageId`. Já visto → descarta e confirma.
     *(idempotência — princípio IV)*
8. Chama `IPaymentGateway.ChargeAsync`. O simulador aplica RN-10: acima de
   R$ 10.000,00 recusa, caso contrário aprova (CA-6, CA-7, CA-8).
   - Sem resposta → **Polly** repete, até 5 tentativas com backoff exponencial
     (RN-11). Recuperou na 3ª → segue normal (CA-14).
9. Grava `payments` com o desfecho. A restrição única em `order_id` barra
   qualquer cobrança duplicada que tenha escapado do passo 7 (RN-9, CA-10).
10. Publica `PaymentApproved` | `PaymentDeclined` | `PaymentFailed` pelo outbox
    do próprio PaymentService, na mesma transação da escrita.
11. **OrderService** consome o desfecho. Idempotência via Redis. Aplica a
    transição **somente se** o pedido estiver `Pending`; em qualquer outro
    estado, ignora silenciosamente (RN-7, CA-9). A guarda vive no agregado, não
    no consumidor.
12. **NotificationService** consome o mesmo desfecho, em paralelo com o passo
    11, e insere na coleção `notifications`. O índice único em `orderId` garante
    a notificação única (RN-12); `DuplicateKeyException` é tratada como sucesso.
13. Consistência final atingida. `GET /orders/{id}` mostra o estado final
    (CA-15).

Os passos 11 e 12 são independentes: `NotificationService` não espera
`OrderService`. É consistência eventual — durante alguns milissegundos existe
notificação de pagamento aprovado enquanto o pedido ainda consta `Pending`. Isso
é aceito conscientemente; a alternativa (encadear os dois) reintroduziria
acoplamento síncrono, que é justamente o que a arquitetura evita.

## Decisões técnicas

| # | Decisão | Escolha | Por quê | Alternativa descartada |
|---|---|---|---|---|
| **D-1** | Coreografia vs. orquestração | Coreografia: cada serviço reage a fatos | O fluxo é linear e curto (3 saltos). Orquestrador seria uma peça a mais para coordenar o que já é sequencial | Saga orquestrada (MassTransit Saga State Machine) — indicada quando há compensação e ramificação, que esta spec não tem por excluir estorno do escopo |
| **D-2** | Biblioteca de mensageria | MassTransit 8.x | Retry, DLQ, serialização e outbox prontos e testados | `RabbitMQ.Client` puro — escrever isso à mão consumiria o projeto em encanamento (constituição, "Padrões técnicos fixos") |
| **D-3** | Implementação do outbox | `AddEntityFrameworkOutbox` do MassTransit | Transacional com o `DbContext`, com *delivery service* e limpeza inclusos. Corrigir concorrência e ordenação à mão é armadilha | Outbox artesanal (~100 linhas + um `BackgroundService`) — maior valor didático, maior risco de bug sutil. Ver "Pergunta ao autor" |
| **D-4** | Idempotência de consumidor | Redis `SET NX EX` com o `MessageId` | Exigido pelo princípio IV; barato e independente do banco de cada serviço | Inbox transacional do MassTransit (`InboxState`) — tecnicamente superior, ver risco R-5 |
| **D-5** | Idempotência de criação de pedido | Índice único `(customer_id, idempotency_key)` no PostgreSQL | Garantia do próprio armazenamento; imune a corrida entre duas instâncias | Checar-antes-de-inserir em código — tem janela de corrida e falha justamente sob concorrência, que é quando importa |
| **D-6** | Retry da chamada ao gateway | Polly dentro do `PaymentService` | Falha do gateway é **caso de negócio previsto** (RN-11) e termina em `PaymentFailed`, não em DLQ | Deixar o retry do MassTransit cuidar — a mensagem acabaria no `_error` sem publicar o desfecho que a spec exige |
| **D-7** | Retry do consumidor | `UseMessageRetry` + `_error` queue do MassTransit | Falha de **infraestrutura** (nosso banco caiu) não é caso de negócio: retenta e, persistindo, vai para a DLQ com log (princípio VI) | Confundir os dois retries num só — mascara defeito de infra como recusa de pagamento |
| **D-8** | Gateway de pagamento | Interface `IPaymentGateway` + `SimulatedPaymentGateway` | Permite testar o domínio sem rede e trocar por integração real sem tocar no caso de uso | Chamar um sandbox externo — deixaria o teste dependente de rede e de credencial |
| **D-9** | Bancos separados por serviço | Uma instância PostgreSQL, **dois bancos lógicos** (`orders_db`, `payments_db`), usuários distintos | Cumpre o princípio III sem gastar dois contêineres numa máquina de 8 GB | Duas instâncias PostgreSQL — mais fiel a produção, custo de memória sem ganho didático |
| **D-10** | Estado do pedido | `string` no banco, `enum` em C# | Legível em suporte; imune a reordenação do enum | `int` — compacto e ilegível |
| **D-11** | Validação | FluentValidation na borda + invariantes no construtor do agregado | Borda dá `400` com todas as violações de uma vez; o agregado garante que não existe `Order` inválido nem vindo de outro caminho | Só validação de borda — o domínio ficaria dependente de quem o chama |
| **D-12** | Gateway HTTP | YARP | Nativo .NET, configuração declarativa em `appsettings` | Ocelot (menos mantido), Nginx (mais uma stack) |
| **D-13** | Logs | Serilog com saída JSON e enricher de `CorrelationId` | Princípio VII: sem correlação, depurar 3 saltos assíncronos é impossível | `ILogger` padrão com saída texto — não correlaciona |
| **D-14** | Migrations | EF Core Migrations, aplicadas no startup do serviço | Um `docker compose up` e o sistema funciona, sem passo manual (critério de sucesso da constituição) | Script SQL manual — quebra o "clone e roda" |

## Riscos e modos de falha

| # | Risco | Comportamento do sistema | Mitigação |
|---|---|---|---|
| **R-1** | RabbitMQ cai | Pedidos continuam sendo aceitos e persistidos; eventos acumulam na tabela outbox | O *delivery service* publica quando o broker volta. Nada se perde. `/health/ready` acusa |
| **R-2** | PostgreSQL de Orders cai | `POST /orders` retorna `503`. Desfechos de pagamento não são aplicados: o consumidor falha, retenta e vai para a DLQ | Retry do MassTransit + DLQ. Mensagens na DLQ são reprocessáveis depois |
| **R-3** | Gateway de pagamento indisponível | 5 tentativas com backoff; depois `PaymentFailed` e notificação (RN-11, CA-13) | Previsto na spec — é caminho de negócio, não incidente |
| **R-4** | Redis cai | Consumidores perdem a checagem de duplicata | **Falha aberta** (processa mesmo assim): as constraints únicas em `payments.order_id` e `notifications.orderId` seguram a correção. Falhar fechado pararia o sistema inteiro por causa de um cache |
| **R-5** | **Redis e banco não são transacionais entre si** | Se o processo morre entre marcar no Redis e commitar no banco, a mensagem é reprocessada e descartada como duplicata — perdendo o efeito | Janela pequena, e as constraints únicas cobrem o caso inverso (marca depois de commitar). Ver "Pergunta ao autor" |
| **R-6** | MongoDB cai | Notificações falham, retentam, vão para a DLQ. Pedido e pagamento **não** são afetados | Isolamento por serviço: falha de notificação não impede a cobrança |
| **R-7** | Mensagem venenosa (sempre falha) | Vai para `_error` após o retry, com log de erro | Princípio VI: nunca descartada em silêncio, nunca em loop infinito |
| **R-8** | Evento de desfecho chega duas vezes ao Order já finalizado | Ignorado pela guarda de estado no agregado (CA-9) | Transição só a partir de `Pending` |

## Estratégia de teste

### Unitários — sem infraestrutura, milissegundos

- **Domínio de Order:** cálculo do total (CA-2, CA-3), rejeição de pedido sem
  itens (CA-4), quantidade fora de 1..100 e preço ≤ 0 (CA-5), transição de
  estado a partir de `Pending` e recusa de transição em estado final (CA-9).
- **Domínio de Payment:** regra do limite de R$ 10.000,00, incluindo o limite
  exato (CA-8) e o centavo acima (CA-7).
- **Política de retry:** com um `IPaymentGateway` falso — falha nas 5 tentativas
  → `Failed` (CA-13); falha em 2 e aprova na 3ª → `Approved` (CA-14).

### Integração — Testcontainers, sobe infraestrutura real

Um único `IAsyncLifetime` compartilhado sobe RabbitMQ, PostgreSQL, MongoDB e
Redis reais em contêineres descartáveis. Sem mock de broker: o objetivo é
exercitar a serialização, o roteamento e a entrega de verdade.

- **Ponta a ponta feliz:** `POST /orders` → aguarda consistência → pedido `Paid`
  e uma notificação de confirmação (CA-6).
- **Ponta a ponta recusado:** pedido de R$ 10.000,01 → `PaymentDeclined` + uma
  notificação com motivo (CA-7).
- **Idempotência de criação:** mesma chave duas vezes → `201` e depois `200`,
  um único pedido (CA-11); chaves diferentes → dois pedidos (CA-12).
- **Idempotência de consumidor:** publicar `OrderCreated` duas vezes com o mesmo
  `MessageId` → um `payments` e uma notificação (CA-10).
- **Consulta:** `GET /orders/{id}` reflete o estado final (CA-15); id
  inexistente → `404` (CA-16).
- **Outbox sob falha do broker:** criar pedido com o RabbitMQ pausado, religar,
  verificar que o fluxo completa (R-1).

Testes assíncronos usam *polling* com timeout (`aguardar até 10s que o status
seja Paid`), nunca `Task.Delay` fixo — espera fixa gera teste instável, que é
pior que teste ausente.

### Cobertura de critérios

Todos os 16 critérios da spec têm teste correspondente. CA-1 e CA-14 são
verificados no nível de integração e de unidade respectivamente.

## Conformidade com a constituição

| Princípio | Como este plano atende |
|---|---|
| I — Spec antes de código | Spec 001 aprovada antes deste documento |
| II — Spec ≠ plano | Nenhum termo técnico vazou para a spec |
| III — Assíncrono, banco por serviço | Só eventos entre serviços; D-9 separa os bancos |
| IV — Consumidor idempotente | D-4 (Redis) + D-5 e as constraints únicas |
| V — Outbox | D-3, nos dois serviços que publicam |
| VI — Falha esperada | D-6, D-7, R-7, health checks |
| VII — Rastreabilidade | `CorrelationId` em todo contrato, D-13 |
| VIII — Contratos versionados | `Shared.Contracts`, `record`, nome no passado |
| IX — Teste na definição de pronto | Unitário + integração com Testcontainers |

Nenhum conflito com a constituição. Uma tensão está registrada em R-5 e na
pergunta abaixo.

## Pergunta ao autor antes de `/tasks`

**D-3 — outbox pronto ou artesanal?** O do MassTransit é a escolha correta em
produção e a que este plano assume. Mas um outbox escrito à mão é o trecho de
código mais impressionante que este repositório poderia ter, e a diferença entre
"usei a lib" e "sei por que a lib existe" aparece em entrevista.

Meio-termo recomendado, e o que sugiro adotar: usar o do MassTransit **e**
escrever `docs/decisions/0001-outbox.md` explicando o mecanismo — a tabela, o
processo de publicação, o que acontece se o processo morrer entre o commit e a
publicação. Você defende o conceito sem carregar o risco de uma implementação
artesanal com bug sutil.
