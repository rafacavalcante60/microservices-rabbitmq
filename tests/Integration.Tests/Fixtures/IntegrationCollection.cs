namespace Integration.Tests.Fixtures;

// Uma coleção só para todos os testes de integração: o xUnit cria o fixture uma
// vez e o compartilha, em vez de subir quatro contêineres por classe de teste.
// O preço é que as classes desta coleção não rodam em paralelo entre si — o que
// é desejável aqui, já que dividem o mesmo banco e as mesmas filas.
[CollectionDefinition(Name)]
public sealed class IntegrationCollection : ICollectionFixture<IntegrationFixture>
{
    public const string Name = "integration";
}
