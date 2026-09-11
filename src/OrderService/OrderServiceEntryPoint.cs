namespace OrderService;

// Âncora para o `WebApplicationFactory<T>` dos testes de integração: ele só
// precisa de um tipo público para descobrir o assembly que contém o entry point.
// O candidato natural seria a classe `Program`, mas os três serviços usam
// top-level statements e a `Program` gerada fica no namespace global — com os
// três referenciados pelo mesmo projeto de teste, o nome vira ambíguo. Um
// marcador por serviço, cada um no seu namespace, resolve sem obrigar nenhum
// deles a mudar a forma do `Program`.
public sealed class OrderServiceEntryPoint;
