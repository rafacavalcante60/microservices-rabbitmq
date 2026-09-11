namespace OrderService.Application.CreateOrder;

// O endpoint precisa distinguir "criei agora" de "já existia" para escolher
// entre 201 e 200 (plano, §Endpoints HTTP). Quem sabe disso é o handler, porque
// só ele viu a violação de unicidade acontecer.
//
// Um `bool` solto na assinatura esconderia o significado no ponto de chamada:
// `result.AlreadyExisted` se lê sozinho, um `true` não.
public record CreateOrderResult(CreateOrderResponse Response, bool AlreadyExisted);
