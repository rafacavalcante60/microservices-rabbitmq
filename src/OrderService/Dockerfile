# syntax=docker/dockerfile:1

# Build multi-stage. O primeiro estágio traz o SDK inteiro (~800 MB) para
# compilar; o segundo leva só o runtime (~220 MB) e recebe apenas o resultado do
# publish. A imagem final não contém compilador, código-fonte nem NuGet cache —
# menos coisa para baixar e menos superfície para atacar.

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Directory.Build.props vale para todos os projetos (nullable, warnings como
# erro). Sem ele no contexto, a imagem compilaria com regras diferentes das da
# máquina de desenvolvimento — e "compila aqui, quebra lá" é exatamente o que
# conteinerizar deveria eliminar.
COPY Directory.Build.props ./

# Os .csproj entram sozinhos, antes do código. O Docker invalida uma camada e
# todas as seguintes quando o que ela copia muda — se o código viesse junto, cada
# linha editada refaria o `restore` e baixaria os pacotes de novo. Assim o
# restore só reexecuta quando uma dependência realmente muda, e o ciclo de build
# cai de minutos para segundos.
COPY src/BuildingBlocks/BuildingBlocks.csproj src/BuildingBlocks/
COPY src/Shared.Contracts/Shared.Contracts.csproj src/Shared.Contracts/
COPY src/OrderService.Domain/OrderService.Domain.csproj src/OrderService.Domain/
COPY src/OrderService/OrderService.csproj src/OrderService/
RUN dotnet restore src/OrderService/OrderService.csproj

COPY src/ src/
RUN dotnet publish src/OrderService/OrderService.csproj \
    -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# curl entra só para o HEALTHCHECK: a imagem do ASP.NET não traz nenhum cliente
# HTTP, e sem isso o `depends_on: condition: service_healthy` do compose não tem
# como saber se o serviço subiu de verdade — ele só saberia que o processo
# existe, que é pergunta diferente (princípio VI).
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app .

# O usuário `app` já vem nas imagens .NET 8. Rodar como root dentro do contêiner
# é desnecessário aqui — a porta 8080 não é privilegiada — e um processo
# comprometido rodando como root tem muito mais o que fazer.
USER app

# Todos os serviços escutam 8080 **dentro** do contêiner; quem separa é o
# mapeamento de portas do compose. Uniformizar evita que a configuração do
# gateway precise saber a porta particular de cada destino.
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

HEALTHCHECK --interval=10s --timeout=5s --start-period=30s --retries=5 \
    CMD curl -fsS http://localhost:8080/health/ready || exit 1

ENTRYPOINT ["dotnet", "OrderService.dll"]
