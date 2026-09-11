# Tarefas 001: Criação de pedido com pagamento e notificação

**Plano:** ./plan.md
**Spec:** ./spec.md

Legenda: `[ ]` pendente · `[~]` em andamento · `[x]` concluída

**28 tarefas.** Dois marcos no meio do caminho estão sinalizados com 🏁.

---

## Fundação

### T-001 — Criar a solução e a estrutura de projetos
- [x] **Depende de:** —
- **Atende:** constituição, "Estrutura de pastas"
- **Fazer:** `dotnet new sln`; criar os projetos vazios de `src/` e `tests/`;
  `Directory.Build.props` na raiz com `<Nullable>enable</Nullable>`,
  `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` e `<LangVersion>12</LangVersion>`;
  `.gitignore` do .NET; `git init` e commit inicial.
- **Arquivos:** `Microservices.sln`, `Directory.Build.props`, `.gitignore`,
  `src/*/`, `tests/*/`
- **Verificar:** `dotnet build` → `Build succeeded`, 0 warnings
- **Commit:** `chore: T-001 criar solução e estrutura de projetos`

### T-002 — Subir a infraestrutura no Docker Compose
- [x] **Depende de:** —
- **Atende:** critério de sucesso da constituição
- **Fazer:** `docker-compose.yml` com RabbitMQ (com o painel de administração),
  PostgreSQL (script de init criando `orders_db` e `payments_db` com usuários
  distintos — D-9), MongoDB e Redis. Volumes nomeados e `healthcheck` em cada um.
- **Arquivos:** `docker-compose.yml`, `infra/postgres/init.sql`
- **Verificar:** `docker compose up -d` → `docker compose ps` mostra os 4 como
  `healthy`; o painel do RabbitMQ abre em `localhost:15672`
- **Commit:** `chore: T-002 subir infraestrutura no docker compose`

### T-003 — Definir os contratos de evento
- [x] **Depende de:** T-001
- **Atende:** princípio VIII, plano §Contratos de mensagem
- **Fazer:** os quatro `record` em `Shared.Contracts`: `OrderCreated`,
  `PaymentApproved`, `PaymentDeclined`, `PaymentFailed`. Todos com
  `CorrelationId` e `OccurredAt`. O projeto não referencia nada além da BCL.
- **Arquivos:** `src/Shared.Contracts/*.cs`
- **Verificar:** `dotnet build src/Shared.Contracts` → sucesso; o `.csproj` não
  tem nenhum `PackageReference`
- **Commit:** `feat(contracts): T-003 definir contratos de evento do fluxo de pedido`

---

## Domínio de pedidos — sem infraestrutura

### T-004 — Modelar o agregado Order com suas invariantes
- [x] **Depende de:** T-001
- **Atende:** RN-1, RN-2, RN-3, RN-4, RN-6 · CA-2, CA-3, CA-4, CA-5
- **Fazer:** entidades `Order` e `OrderItem` em `OrderService.Domain`, sem EF nem
  MassTransit. Construtor recusa pedido sem itens, quantidade fora de 1..100 e
  preço ≤ 0. `TotalAmount` calculado internamente, nunca recebido. Nasce
  `Pending`. Testes unitários cobrindo cada critério.
- **Arquivos:** `src/OrderService.Domain/Order.cs`, `OrderItem.cs`,
  `OrderStatus.cs`, `tests/OrderService.Domain.Tests/OrderTests.cs`
- **Verificar:** `dotnet test tests/OrderService.Domain.Tests` → verdes,
  incluindo o caso de itens somando 35,00 com total declarado 1,00 resultando 35,00
- **Commit:** `feat(orders): T-004 modelar agregado Order com invariantes`

### T-005 — Implementar as transições de estado do pedido
- [x] **Depende de:** T-004
- **Atende:** RN-7 · CA-9
- **Fazer:** métodos `MarkAsPaid()`, `MarkAsDeclined(reason)`,
  `MarkAsFailed(reason)` no agregado. Transição ocorre **somente** a partir de
  `Pending`; em estado final, a chamada é ignorada sem lançar exceção — evento
  duplicado ou atrasado não é erro, é rotina. Testes de cada transição e da
  recusa de transição a partir de cada estado final.
- **Arquivos:** `src/OrderService.Domain/Order.cs`,
  `tests/OrderService.Domain.Tests/OrderStatusTransitionTests.cs`
- **Verificar:** `dotnet test --filter Transition` → verdes; um pedido `Paid` que
  recebe `MarkAsDeclined` continua `Paid`
- **Commit:** `feat(orders): T-005 implementar transições de estado do pedido`

---

## Persistência e publicação em Orders

### T-006 — Persistir pedidos no PostgreSQL
- [x] **Depende de:** T-002, T-004
- **Atende:** RN-8, plano §Modelo de dados
- **Fazer:** `OrdersDbContext` com EF Core e Npgsql; mapeamento de `orders` e
  `order_items`; `status` como texto (D-10), dinheiro como `numeric(18,2)`;
  **índice único `(customer_id, idempotency_key)`**; primeira migration aplicada
  no startup (D-14).
- **Arquivos:** `src/OrderService/Infrastructure/Persistence/*`,
  `Migrations/*`
- **Verificar:** `docker compose up -d` e rodar o serviço → tabelas criadas;
  `\d orders` no psql mostra o índice único
- **Commit:** `feat(orders): T-006 persistir pedidos no postgresql`

### T-007 — Publicar OrderCreated pelo padrão outbox
- [x] **Depende de:** T-003, T-006
- **Atende:** princípio V, D-3
- **Fazer:** MassTransit com RabbitMQ e `AddEntityFrameworkOutbox` sobre o
  `OrdersDbContext`; migration das tabelas de outbox. O evento é gravado **na
  mesma transação** do pedido e publicado pelo *delivery service*.
- **Arquivos:** `src/OrderService/Infrastructure/Messaging/*`, `Program.cs`,
  `Migrations/*`
- **Verificar:** com o RabbitMQ **parado**, criar um pedido → linha na tabela
  `outbox_message`; subir o RabbitMQ → a mensagem some da tabela e aparece na
  fila. Este é o teste que prova o outbox (R-1).
- **Commit:** `feat(orders): T-007 publicar OrderCreated pelo padrão outbox`

### T-008 — Expor POST /orders
- [x] **Depende de:** T-007
- **Atende:** RN-14 · CA-1, CA-4, CA-5
- **Fazer:** caso de uso `CreateOrderHandler` e endpoint mínimo `POST /orders`;
  validação de borda com FluentValidation devolvendo `400` com `ProblemDetails`
  e todas as violações (D-11); resposta `201` com `{ orderId, status, totalAmount }`
  sem esperar o pagamento.
- **Arquivos:** `src/OrderService/Api/Endpoints/OrdersEndpoints.cs`,
  `Application/CreateOrder/*`
- **Verificar:** `curl -X POST localhost:8081/orders` com corpo válido → `201`
  e `status: "Pending"`; com lista de itens vazia → `400` com a violação
- **Commit:** `feat(orders): T-008 expor endpoint de criação de pedido`

### T-009 — Garantir idempotência na criação do pedido
- [x] **Depende de:** T-008
- **Atende:** RN-8 · CA-11, CA-12
- **Fazer:** header `Idempotency-Key` obrigatório; ao violar o índice único
  `(customer_id, idempotency_key)`, capturar a exceção do Postgres, buscar o
  pedido existente e devolver `200` com ele (D-5). Sem checar-antes-de-inserir.
- **Arquivos:** `src/OrderService/Application/CreateOrder/*`,
  `Api/Endpoints/OrdersEndpoints.cs`
- **Verificar:** o mesmo `curl` com a mesma chave duas vezes → `201` e depois
  `200`, com o mesmo `orderId`; chave diferente → dois ids distintos
- **Commit:** `feat(orders): T-009 garantir idempotência na criação do pedido`

### T-010 — Expor GET /orders/{id}
- [ ] **Depende de:** T-008
- **Atende:** CA-15, CA-16
- **Fazer:** endpoint de consulta devolvendo o pedido com itens, situação, motivo
  e total; `404` quando não existe.
- **Arquivos:** `src/OrderService/Api/Endpoints/OrdersEndpoints.cs`,
  `Application/GetOrder/*`
- **Verificar:** `curl localhost:8081/orders/{id}` → `200` com os dados;
  id aleatório → `404`
- **Commit:** `feat(orders): T-010 expor consulta de pedido por id`

### T-011 — Adicionar observabilidade e health checks
- [ ] **Depende de:** T-008
- **Atende:** princípios VI e VII · D-13
- **Fazer:** projeto `BuildingBlocks` com Serilog em JSON, middleware que lê ou
  gera o `X-Correlation-Id` e o enriquece nos logs, e extensões de health check.
  Aplicar no OrderService: `/health` e `/health/ready` checando Postgres,
  RabbitMQ e Redis.
- **Arquivos:** `src/BuildingBlocks/*`, `src/OrderService/Program.cs`
- **Verificar:** `curl localhost:8081/health/ready` → `200` com as dependências;
  parar o Postgres → `503`. Os logs saem em JSON com `CorrelationId`
- **Commit:** `feat(orders): T-011 adicionar observabilidade e health checks`

---

## Domínio e serviço de pagamentos

### T-012 — Modelar o agregado Payment e a regra de aprovação
- [ ] **Depende de:** T-001
- **Atende:** RN-10 · CA-7, CA-8
- **Fazer:** entidade `Payment` e a regra determinística: acima de
  R$ 10.000,00 recusa, `10.000,00` exato aprova. Testes nos dois lados do limite
  e no centavo acima.
- **Arquivos:** `src/PaymentService.Domain/*`,
  `tests/PaymentService.Domain.Tests/*`
- **Verificar:** `dotnet test tests/PaymentService.Domain.Tests` → verdes,
  com casos de 10.000,00 e 10.000,01
- **Commit:** `feat(payments): T-012 modelar agregado Payment e regra de aprovação`

### T-013 — Criar a abstração do gateway de pagamento
- [ ] **Depende de:** T-012
- **Atende:** D-8
- **Fazer:** interface `IPaymentGateway` e `SimulatedPaymentGateway` aplicando
  RN-10. Um modo de configuração que simula indisponibilidade, para exercitar
  RN-11 nos testes e na demonstração.
- **Arquivos:** `src/PaymentService.Domain/IPaymentGateway.cs`,
  `src/PaymentService/Infrastructure/SimulatedPaymentGateway.cs`
- **Verificar:** `dotnet test --filter Gateway` → verdes
- **Commit:** `feat(payments): T-013 criar abstração do gateway de pagamento`

### T-014 — Implementar a política de retry do gateway
- [ ] **Depende de:** T-013
- **Atende:** RN-11 · CA-13, CA-14
- **Fazer:** política Polly com 5 tentativas e backoff exponencial em volta da
  chamada ao gateway (D-6). Esgotadas, o desfecho é `Failed`. Testes com um
  gateway falso: falha nas 5 → `Failed`; falha em 2 e aprova na 3ª → `Approved`,
  sem notificação de falha.
- **Arquivos:** `src/PaymentService/Application/*`,
  `tests/PaymentService.Domain.Tests/RetryPolicyTests.cs`
- **Verificar:** `dotnet test --filter Retry` → verdes, com a contagem de
  tentativas conferida
- **Commit:** `feat(payments): T-014 implementar política de retry do gateway`

### T-015 — Persistir pagamentos no PostgreSQL
- [ ] **Depende de:** T-002, T-012
- **Atende:** RN-9 · CA-10
- **Fazer:** `PaymentsDbContext` em `payments_db`; **restrição única em
  `order_id`**, que é a garantia final contra cobrança dupla; migration no
  startup.
- **Arquivos:** `src/PaymentService/Infrastructure/Persistence/*`, `Migrations/*`
- **Verificar:** inserir dois pagamentos com o mesmo `order_id` via psql → o
  segundo é rejeitado pelo banco
- **Commit:** `feat(payments): T-015 persistir pagamentos no postgresql`

### T-016 — Criar o filtro de idempotência de consumidor
- [ ] **Depende de:** T-002, T-011
- **Atende:** princípio IV · D-4, R-4
- **Fazer:** filtro do MassTransit que consulta o Redis por
  `idem:{consumer}:{messageId}` com `SET NX EX 7d`; mensagem já vista é
  descartada e confirmada. **Falha aberta**: se o Redis estiver fora, processa
  mesmo assim e registra warning — as constraints únicas seguram a correção.
- **Arquivos:** `src/BuildingBlocks/Messaging/IdempotencyFilter.cs`
- **Verificar:** `dotnet test --filter Idempotency` → verde; com o Redis parado,
  o consumidor continua processando e loga o warning
- **Commit:** `feat(shared): T-016 criar filtro de idempotência de consumidor`

### T-017 — Consumir OrderCreated e publicar o desfecho
- [ ] **Depende de:** T-014, T-015, T-016
- **Atende:** RN-9, RN-10, RN-11 · CA-6, CA-7, CA-13
- **Fazer:** `OrderCreatedConsumer`: aplica o filtro de idempotência, cobra pelo
  gateway com a política de retry, grava `payments` e publica
  `PaymentApproved` | `PaymentDeclined` | `PaymentFailed` pelo outbox do
  PaymentService, na mesma transação. Retry de consumidor + DLQ do MassTransit
  configurados (D-7). Observabilidade e health checks aplicados.
- **Arquivos:** `src/PaymentService/Application/Consumers/*`, `Program.cs`
- **Verificar:** criar um pedido pelo `curl` → o painel do RabbitMQ mostra o
  evento de desfecho publicado; a tabela `payments` tem a linha
- **Commit:** `feat(payments): T-017 consumir OrderCreated e publicar desfecho`

### T-018 — Aplicar o desfecho ao pedido 🏁
- [ ] **Depende de:** T-005, T-017
- **Atende:** RN-7 · CA-6, CA-7, CA-9
- **Fazer:** consumidores dos três desfechos no OrderService, chamando as
  transições do agregado (T-005). Filtro de idempotência aplicado.
- **Arquivos:** `src/OrderService/Application/Consumers/*`
- **Verificar:** `curl` cria um pedido → poucos segundos depois
  `GET /orders/{id}` mostra `Paid`. Pedido de R$ 10.000,01 → `PaymentDeclined`
- **Commit:** `feat(orders): T-018 aplicar desfecho de pagamento ao pedido`

> 🏁 **Marco 1 — o fluxo funciona.** A partir daqui um pedido percorre os três
> serviços sozinho. Ainda não é link compartilhável (falta o Marco 2), mas é o
> momento de parar e conferir se você entende cada salto.

---

## Notificações

### T-019 — Registrar notificações no MongoDB
- [ ] **Depende de:** T-002, T-016, T-017
- **Atende:** RN-12, RN-13 · CA-6, CA-7, CA-10
- **Fazer:** `NotificationService` consumindo os três desfechos e inserindo na
  coleção `notifications`. **Índice único em `orderId`** garantindo exatamente
  uma notificação por pedido; `DuplicateKeyException` tratada como sucesso, não
  como erro. Observabilidade e health checks aplicados.
- **Arquivos:** `src/NotificationService/**`
- **Verificar:** criar um pedido → um documento em `notifications`; publicar o
  mesmo desfecho de novo → continua um só documento
- **Commit:** `feat(notifications): T-019 registrar notificações no mongodb`

### T-020 — Expor a consulta de notificações
- [ ] **Depende de:** T-019
- **Atende:** RN-13 · CA-6, CA-7, CA-13
- **Fazer:** `GET /notifications/order/{orderId}` devolvendo as notificações do
  pedido.
- **Arquivos:** `src/NotificationService/Api/Endpoints/*`
- **Verificar:** `curl localhost:8083/notifications/order/{id}` → a notificação
  com tipo, mensagem e instante
- **Commit:** `feat(notifications): T-020 expor consulta de notificações`

---

## Borda e empacotamento

### T-021 — Criar o API Gateway
- [ ] **Depende de:** T-010, T-020
- **Atende:** D-12, princípio VII
- **Fazer:** YARP roteando `/orders/**` → OrderService e `/notifications/**` →
  NotificationService, configurado em `appsettings`. Middleware de
  `CorrelationId` na borda — é aqui que o identificador nasce.
- **Arquivos:** `src/ApiGateway/**`
- **Verificar:** `curl localhost:8080/orders/{id}` → mesma resposta da porta
  8081; o `CorrelationId` gerado aparece nos logs dos três serviços
- **Commit:** `feat(gateway): T-021 criar api gateway com yarp`

### T-022 — Empacotar tudo em contêineres 🏁
- [ ] **Depende de:** T-021
- **Atende:** critério de sucesso da constituição
- **Fazer:** `Dockerfile` multi-stage para cada serviço; estender o
  `docker-compose.yml` com os quatro serviços, variáveis de ambiente,
  `depends_on` com `condition: service_healthy`.
- **Arquivos:** `src/*/Dockerfile`, `docker-compose.yml`, `.dockerignore`
- **Verificar:** `docker compose down -v && docker compose up --build` numa
  máquina limpa → `curl` cria um pedido e ele chega a `Paid` sem nenhum passo
  manual
- **Commit:** `chore: T-022 empacotar serviços em contêineres`

> 🏁 **Marco 2 — repositório compartilhável.** `git clone` + `docker compose up`
> + um `curl` e o fluxo roda. É a partir daqui que o link vale ser mandado,
> mesmo com os testes de integração ainda por fazer.

---

## Testes de integração

### T-023 — Montar a base de testes com Testcontainers
- [ ] **Depende de:** T-022
- **Atende:** princípio IX
- **Fazer:** `IAsyncLifetime` compartilhado subindo RabbitMQ, PostgreSQL,
  MongoDB e Redis reais; `WebApplicationFactory` para os serviços; utilitário de
  *polling* com timeout — nunca `Task.Delay` fixo.
- **Arquivos:** `tests/Integration.Tests/Fixtures/*`
- **Verificar:** um teste de fumaça que sobe tudo e checa `/health/ready` →
  verde, sem `docker compose` rodando na máquina
- **Commit:** `test: T-023 montar base de testes com testcontainers`

### T-024 — Testar o fluxo ponta a ponta, aprovado e recusado
- [ ] **Depende de:** T-023
- **Atende:** CA-1, CA-6, CA-7, CA-8, CA-15
- **Fazer:** dois testes atravessando os três serviços: pedido de R$ 500,00 →
  `Paid` + uma notificação de confirmação; pedido de R$ 10.000,01 →
  `PaymentDeclined` + uma notificação com motivo.
- **Arquivos:** `tests/Integration.Tests/OrderFlowTests.cs`
- **Verificar:** `dotnet test tests/Integration.Tests --filter Flow` → verdes
- **Commit:** `test: T-024 testar fluxo ponta a ponta aprovado e recusado`

### T-025 — Testar as duas idempotências
- [ ] **Depende de:** T-023
- **Atende:** RN-8, RN-9, RN-12 · CA-10, CA-11, CA-12
- **Fazer:** criação com a mesma chave duas vezes → um pedido, `201` e `200`;
  chaves diferentes → dois pedidos; `OrderCreated` publicado duas vezes com o
  mesmo `MessageId` → um pagamento e uma notificação.
- **Arquivos:** `tests/Integration.Tests/IdempotencyTests.cs`
- **Verificar:** `dotnet test --filter Idempotency` → verdes
- **Commit:** `test: T-025 testar idempotência de criação e de consumidor`

### T-026 — Testar falha do gateway e resiliência do outbox
- [ ] **Depende de:** T-023
- **Atende:** RN-11 · CA-13, CA-14 · R-1
- **Fazer:** gateway configurado para nunca responder → após 5 tentativas o
  pedido vai a `PaymentFailed` com uma notificação; gateway que falha 2 vezes e
  aprova na 3ª → `Paid` sem notificação de falha; criar pedido com o RabbitMQ
  pausado, religar, verificar que o fluxo completa.
- **Arquivos:** `tests/Integration.Tests/ResilienceTests.cs`
- **Verificar:** `dotnet test --filter Resilience` → verdes
- **Commit:** `test: T-026 testar falha do gateway e resiliência do outbox`

---

## Documentação

### T-027 — Escrever o ADR do outbox e a visão de arquitetura
- [ ] **Depende de:** T-018
- **Atende:** decisão D-3 (meio-termo acordado)
- **Fazer:** `docs/decisions/0001-outbox.md` explicando o mecanismo por dentro —
  a tabela, a transação compartilhada, o processo publicador, e o que acontece se
  o processo morrer entre o commit e a publicação. `docs/architecture.md` com o
  diagrama do fluxo e as fronteiras de serviço.
- **Arquivos:** `docs/decisions/0001-outbox.md`, `docs/architecture.md`
- **Verificar:** leitura — o autor consegue explicar o outbox sem consultar o
  código
- **Commit:** `docs: T-027 escrever adr do outbox e visão de arquitetura`

### T-028 — Escrever o README
- [ ] **Depende de:** T-022, T-026, T-027
- **Atende:** critério de sucesso da constituição
- **Fazer:** README com o diagrama do fluxo, como rodar em um comando, os `curl`
  de demonstração (aprovado, recusado, idempotente), os padrões usados com uma
  linha de motivo cada, e link para `specs/` explicando o método spec-driven.
  Escrito por último, quando tudo já é verdade.
- **Arquivos:** `README.md`
- **Verificar:** alguém que nunca viu o projeto consegue rodá-lo seguindo só o
  README
- **Commit:** `docs: T-028 escrever readme`
