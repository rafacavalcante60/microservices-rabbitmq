# Spec 001: Criação de pedido com pagamento e notificação

**Status:** aprovada — sem pendências
**Criado em:** 2026-08-15
**Aprovado em:** 2026-08-15

## Problema

Um cliente precisa conseguir fechar uma compra sem ficar preso esperando a
confirmação do pagamento, que depende de um processador externo e pode demorar
ou falhar. O sistema deve aceitar o pedido imediatamente, cobrar em seguida, e
avisar o cliente do resultado quando ele existir.

Esta é a espinha dorsal do sistema: todo o resto do projeto se apoia neste
fluxo.

## Fora de escopo

Deliberadamente **não** faz parte desta feature:

- Catálogo de produtos e controle de estoque
- Cadastro, autenticação e autorização de clientes
- Cálculo de frete, impostos, cupons ou descontos
- Cancelamento ou alteração de pedido depois de criado
- Estorno ou reembolso de pagamento aprovado
- Devolução, troca ou logística de entrega
- Nova tentativa de cobrança após uma recusa do processador
- Envio real de e-mail ou SMS
- Interface visual de qualquer tipo

## Decisões e simplificações assumidas

Registradas aqui porque são conscientes, não descuidos. Cada uma tem um custo
conhecido e um motivo.

| Decisão | Motivo | O que se perde |
|---|---|---|
| O cliente informa o preço unitário | Não há catálogo no escopo | O cliente dita o preço. Num sistema real o preço viria do catálogo no momento da criação |
| Não há autenticação; a identificação do cliente vem na solicitação e não é verificada | Autenticação está fora de escopo | Qualquer um pode criar pedido em nome de qualquer cliente |
| O processador de pagamento é simulado, com regra determinística | Permite demonstrar aprovação e recusa ao vivo, sem depender de sorte ou de serviço externo | Não exercita integração real com gateway |
| A notificação é apenas registrada, não enviada | O valor do projeto está no fluxo assíncrono, não no envio de e-mail | Não exercita integração com provedor de envio |
| Moeda única e fixa (BRL) | Multi-moeda traria conversão e arredondamento, que não somam ao objetivo | Sistema não é internacionalizável |

## Atores

| Ator | Papel |
|---|---|
| **Cliente** | Solicita a criação do pedido e recebe a notificação do resultado |
| **Processador de pagamento** | Serviço simulado que aprova ou recusa a cobrança |
| **Operador** | Pessoa que precisa consultar o estado de um pedido para dar suporte |

## Fluxo principal

1. O cliente envia um pedido informando sua identificação, uma **chave de
   idempotência** de sua escolha, e os itens desejados — cada item com produto,
   quantidade e preço unitário.
2. O sistema valida o pedido e calcula o valor total.
3. O sistema registra o pedido com a situação **Pendente** e devolve
   imediatamente ao cliente o identificador do pedido e essa situação. O cliente
   não espera pelo pagamento.
4. O sistema solicita a cobrança do valor total ao processador de pagamento.
5. O processador aprova a cobrança.
6. O sistema passa a situação do pedido para **Pago**.
7. O sistema registra uma notificação de confirmação destinada ao cliente.
8. A partir daí, uma consulta ao pedido mostra a situação **Pago**.

## Fluxos alternativos e de erro

### FA-1 — Pedido inválido

No passo 2, o pedido não atende às regras de validação. O sistema recusa o
pedido, informa ao cliente quais regras foram violadas, e **nada é registrado**:
não existe pedido, não há cobrança, não há notificação.

### FA-2 — Pagamento recusado

No passo 5, o processador recusa a cobrança. A situação do pedido passa para
**Pagamento recusado** e o cliente recebe uma notificação informando a recusa e
o motivo. O pedido permanece nesse estado final; esta feature não tenta cobrar
de novo.

### FA-3 — Processador de pagamento indisponível

No passo 4, o processador não responde. O pedido continua **Pendente** e o
sistema repete a solicitação de cobrança até **5 tentativas**, com espera
crescente entre elas.

Esgotadas as 5 tentativas, a situação do pedido passa para **Pagamento falhou**,
o cliente é notificado, e o caso fica registrado para um operador investigar.

**Pagamento falhou** é distinto de **Pagamento recusado**: recusado é uma
resposta do processador (decisão de negócio); falhou é ausência de resposta
(problema técnico). Para o operador que dá suporte, a diferença muda a ação.

### FA-4 — Solicitação duplicada do mesmo pedido

O cliente envia a mesma solicitação de criação duas vezes — clicou duas vezes,
ou a resposta se perdeu e ele repetiu.

Duas solicitações são a mesma quando trazem a **mesma chave de idempotência do
mesmo cliente**. O sistema cria **um único pedido** e cobra **uma única vez**. A
segunda solicitação devolve o mesmo pedido da primeira, sem efeito adicional.

A chave é escolhida pelo cliente porque só ele sabe se está tentando de novo ou
fazendo uma compra nova legítima — dois pedidos idênticos em sequência podem ser
ambos intencionais, e nenhuma heurística de conteúdo acerta isso.

### FA-5 — Consulta a pedido inexistente

Um cliente ou operador consulta um identificador que não existe. O sistema
informa que o pedido não foi encontrado.

## Regras de negócio

| # | Regra |
|---|---|
| **RN-1** | Um pedido precisa ter ao menos um item. Pedido sem itens é inválido. |
| **RN-2** | Quantidade de item é número inteiro entre 1 e 100, inclusive. |
| **RN-3** | Preço unitário é maior que zero, informado pelo cliente, em reais (BRL). |
| **RN-4** | O valor total do pedido é a soma de (quantidade × preço unitário) de todos os itens, e é calculado pelo sistema — nunca aceito do cliente. |
| **RN-5** | Não há valor mínimo nem máximo para o total de um pedido. |
| **RN-6** | Todo pedido nasce na situação **Pendente**. |
| **RN-7** | As situações possíveis são: **Pendente**, **Pago**, **Pagamento recusado**, **Pagamento falhou**. As três últimas são finais: uma vez atingidas, a situação não muda mais. |
| **RN-8** | Toda solicitação de criação traz uma chave de idempotência. Duas solicitações do mesmo cliente com a mesma chave referem-se ao mesmo pedido. |
| **RN-9** | Um mesmo pedido é cobrado no máximo uma vez com sucesso, mesmo que a solicitação de cobrança seja processada em duplicidade. |
| **RN-10** | O processador simulado **recusa** cobranças de valor acima de R$ 10.000,00 e **aprova** as demais. A decisão é determinística: o mesmo valor produz sempre o mesmo resultado. |
| **RN-11** | Uma cobrança sem resposta do processador é repetida até 5 tentativas, com espera crescente. Esgotadas as tentativas, o pedido vai para **Pagamento falhou**. |
| **RN-12** | O cliente recebe exatamente uma notificação por pedido: a do resultado final da cobrança. Reprocessamento interno não gera notificação repetida. |
| **RN-13** | A notificação é registrada e fica consultável; não há envio por e-mail ou SMS. Toda notificação guarda a qual pedido se refere, a qual cliente, o resultado comunicado e o instante do registro. |
| **RN-14** | A resposta ao cliente na criação do pedido não depende do resultado da cobrança. |
| **RN-15** | A identificação do cliente é informada na solicitação e não é verificada. |

## Critérios de aceite

**CA-1** — *Aceite imediato* (RN-6, RN-14)
Dado um pedido válido com dois itens
Quando o cliente solicita a criação
Então recebe o identificador do pedido e a situação **Pendente**
E a resposta chega sem depender do resultado da cobrança.

**CA-2** — *Cálculo do total* (RN-4)
Dado um pedido com 2 unidades a 10,00 e 3 unidades a 5,00
Quando o pedido é criado
Então o valor total registrado é 35,00.

**CA-3** — *Total informado pelo cliente é ignorado* (RN-4)
Dado um pedido cujos itens somam 35,00 mas que declara total de 1,00
Quando o pedido é criado
Então o valor total registrado é 35,00.

**CA-4** — *Pedido sem itens é recusado* (RN-1)
Dado um pedido sem nenhum item
Quando o cliente solicita a criação
Então a solicitação é recusada com a violação indicada
E nenhum pedido é registrado.

**CA-5** — *Quantidade e preço inválidos são recusados* (RN-2, RN-3)
Dado um pedido com um item de quantidade 0, de quantidade 101, ou de preço zero
ou negativo
Quando o cliente solicita a criação
Então a solicitação é recusada com a violação indicada.

**CA-6** — *Caminho feliz até o fim* (RN-7, RN-10, RN-12)
Dado um pedido válido de R$ 500,00 criado como Pendente
Quando o processador avalia a cobrança
Então a situação do pedido passa a **Pago**
E existe exatamente uma notificação de confirmação registrada para esse pedido.

**CA-7** — *Recusa por valor acima do limite* (RN-10, RN-12)
Dado um pedido válido de R$ 10.000,01 criado como Pendente
Quando o processador avalia a cobrança
Então a situação passa a **Pagamento recusado**
E existe exatamente uma notificação de recusa, com o motivo, registrada.

**CA-8** — *Limite é inclusivo* (RN-10)
Dado um pedido de exatamente R$ 10.000,00
Quando o processador avalia a cobrança
Então a cobrança é aprovada.

**CA-9** — *Estado final é imutável* (RN-7)
Dado um pedido na situação **Pago**
Quando um novo resultado de cobrança chega para o mesmo pedido
Então a situação permanece **Pago**.

**CA-10** — *Cobrança não duplica* (RN-9, RN-12)
Dado um pedido válido
Quando a solicitação de cobrança é processada duas vezes
Então o cliente é cobrado uma única vez
E existe uma única notificação registrada.

**CA-11** — *Criação duplicada não duplica pedido* (RN-8)
Dada a mesma solicitação de criação, com a mesma chave de idempotência do mesmo
cliente, enviada duas vezes
Quando ambas são processadas
Então existe um único pedido
E a segunda resposta traz o mesmo identificador da primeira.

**CA-12** — *Chaves diferentes criam pedidos diferentes* (RN-8)
Dadas duas solicitações de conteúdo idêntico mas com chaves de idempotência
diferentes
Quando ambas são processadas
Então existem dois pedidos distintos.

**CA-13** — *Indisponibilidade leva a falha após as tentativas* (RN-11)
Dado um pedido válido e um processador que nunca responde
Quando as 5 tentativas de cobrança se esgotam
Então a situação passa a **Pagamento falhou**
E existe exatamente uma notificação de falha registrada.

**CA-14** — *Recuperação antes de esgotar as tentativas* (RN-11)
Dado um processador que falha nas duas primeiras tentativas e aprova na terceira
Quando a cobrança é processada
Então a situação passa a **Pago**
E nenhuma notificação de falha é registrada.

**CA-15** — *Consulta reflete o estado atual* (RN-7)
Dado um pedido que já teve a cobrança aprovada
Quando o operador consulta o pedido pelo identificador
Então vê a situação **Pago** e o valor total.

**CA-16** — *Consulta a pedido inexistente* (FA-5)
Dado um identificador que não corresponde a nenhum pedido
Quando alguém o consulta
Então o sistema informa que não foi encontrado.

## Pendências

Nenhuma. Spec liberada para `/plan`.
