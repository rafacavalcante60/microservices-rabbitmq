# 1. Publicar eventos pela tabela outbox

**Data:** 2026-09-11
**Situação:** aceita
**Contexto na spec:** princípio V da constituição, decisão D-3 e risco R-1 do
`plan.md`

## O problema

Criar um pedido são duas escritas em dois sistemas diferentes: a linha em
`orders`, no PostgreSQL, e a mensagem `OrderCreated`, no RabbitMQ. Não existe
transação que abranja os dois.

O código ingênuo é este:

```csharp
await dbContext.SaveChangesAsync();          // pedido gravado
await publishEndpoint.Publish(orderCreated); // e se morrer aqui?
```

Entre uma linha e outra cabe o processo ser morto, o contêiner ser reciclado, o
broker estar fora. O resultado é um pedido gravado que ninguém jamais vai
cobrar. Ele fica `Pending` para sempre: o cliente vê o pedido na tela, o
PaymentService nunca soube que ele existe, e nenhum alarme dispara — não houve
erro, houve uma mensagem que não saiu.

Inverter a ordem não resolve, só troca o defeito de lado: publicar antes de
salvar produz cobrança de pedido que não existe.

## A decisão

O evento é gravado como uma **linha numa tabela do próprio banco do serviço**,
dentro da mesma transação do agregado. Um processo separado lê essa tabela,
publica no broker e só então apaga a linha.

A implementação é o `AddEntityFrameworkOutbox` do MassTransit (D-3), com três
tabelas por serviço que publica — `outbox_message`, `outbox_state` e
`inbox_state`.

## Como funciona por dentro

**1. `Publish` não fala com o broker.** A linha `outbox.UseBusOutbox()` em
`MessagingExtensions` troca a implementação de `IPublishEndpoint` no escopo da
requisição. Quando `CreateOrderHandler` chama `Publish`, o que acontece é um
`INSERT` em `outbox_message` — corpo serializado, tipo da mensagem, headers e
`MessageId` incluídos. Nada sai pela rede.

**2. Uma transação, duas escritas.** O `SaveChangesAsync` seguinte grava
`orders`, `order_items` e `outbox_message` no mesmo commit. É aqui que o
problema deixa de existir: as duas escritas agora são uma só, e o banco garante
que ou ambas acontecem ou nenhuma acontece.

**3. O publicador roda por fora.** Um serviço de segundo plano do MassTransit
varre a tabela a cada `QueryDelay` (1 segundo, neste projeto), publica as
mensagens pendentes em ordem de `SequenceNumber` e apaga as linhas publicadas. A
linha `outbox_state` guarda até onde ele chegou e serve de trava entre
instâncias: a varredura usa `SELECT ... FOR UPDATE SKIP LOCKED`, então duas
instâncias do serviço não publicam a mesma linha.

## O que acontece se o processo morrer entre o commit e a publicação

Nada se perde, e este é o ponto inteiro da decisão.

A linha continua em `outbox_message`, porque só é apagada **depois** de o broker
confirmar a publicação. Quando o processo volta, a varredura seguinte a encontra
e publica. O preço é que, se o processo morrer *depois* de publicar e *antes* de
apagar a linha, a mensagem é publicada duas vezes.

Ou seja: o outbox não entrega exatamente uma vez. Ele entrega **pelo menos uma
vez**, e transforma "mensagem perdida para sempre" em "mensagem possivelmente
repetida" — um problema que tem solução do lado do consumidor, enquanto o
primeiro não tem nenhuma.

É por isso que o princípio IV existe e não é negociável: cada consumidor registra
o `MessageId` no Redis antes de processar e descarta o que já viu
(`IdempotencyFilter`), com as restrições únicas em `payments.order_id` e
`notifications.orderId` como rede embaixo. **Outbox e idempotência são a mesma
decisão vista dos dois lados** — adotar um sem o outro é trocar perda silenciosa
por cobrança duplicada.

## O que se ganha quando o broker cai

Enquanto o RabbitMQ está fora, os eventos se acumulam na tabela e o serviço
continua aceitando pedidos: a criação depende do PostgreSQL, não da mensageria.
Quando o broker volta, a fila represada sai sozinha e o fluxo completa (R-1).

Isso está verificado, não suposto:
`tests/Integration.Tests/ResilienceTests.cs` derruba o broker de verdade
(`rabbitmqctl stop_app`), cria um pedido — que responde `201` —, religa o broker
e espera o pedido chegar a `Paid`.

## Custos aceitos

- **Latência.** O evento sai em até 1 segundo (`QueryDelay`), não no instante do
  commit. Para este fluxo é irrelevante; para algo sensível a latência, o
  intervalo é configurável e existe a variante que notifica o publicador.
- **Carga no banco.** Uma varredura por segundo por serviço, e escrita a mais
  em cada transação. Em troca, elimina-se uma classe inteira de inconsistência.
- **Ordem.** A publicação segue o `SequenceNumber`, mas o consumo depois disso
  não tem ordem garantida entre filas diferentes. O sistema não depende de
  ordem: cada evento carrega o estado de que precisa.

## Alternativas descartadas

**Publicar direto e torcer.** É o código do começo deste documento. Funciona em
demonstração e falha em produção, no dia em que o processo morre no meio — e
falha em silêncio, que é a pior forma.

**Outbox escrito à mão** (~100 linhas e um `BackgroundService`). Seria o trecho
de código mais vistoso do repositório, e foi tentador. Ficou de fora porque a
parte difícil não é o `INSERT` — é a trava entre instâncias, a ordenação e a
limpeza, exatamente onde um bug sutil se esconde por meses. Este documento existe
para que a escolha da biblioteca não custe o entendimento do mecanismo: a
diferença entre "usei a lib" e "sei por que a lib existe" está aqui.

**Inbox transacional** no lugar do Redis para idempotência de consumo. É
tecnicamente superior — marcar a mensagem e gravar o efeito na mesma transação
fecha a janela do R-5. Ficou registrada como alternativa em D-4; o Redis foi
escolhido por ser independente do banco de cada serviço e mais simples de
explicar.
