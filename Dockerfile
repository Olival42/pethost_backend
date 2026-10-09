# syntax=docker/dockerfile:1

ARG DOTNET_VERSION=10.0

# ----------------------------------------------------------------------------
# projects: so os .csproj, na mesma arvore de pastas do repositorio
# ----------------------------------------------------------------------------
# Copia o src/ inteiro e apaga tudo que nao e .csproj. Modulo novo entra sozinho,
# sem editar este arquivo. O estagio build copia so o resultado daqui; como o
# Docker compara o conteudo copiado, mudar um .cs nao invalida o restore — so
# mudar um .csproj.
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS projects
WORKDIR /src
COPY src/ src/
RUN find src -type f ! -name '*.csproj' -delete \
 && find src -type d -empty -delete

# ----------------------------------------------------------------------------
# build
# ----------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Configuracao da solution: versoes centralizadas, regras de build e estilo.
COPY Directory.Build.props Directory.Packages.props global.json .editorconfig ./

# Restore em camada propria. Cada .csproj fica no mesmo caminho do repositorio,
# porque as ProjectReference sao relativas.
COPY --from=projects /src/src src/
RUN dotnet restore src/Host/PetHost.Api/PetHost.Api.csproj

# Testes ficam fora da imagem: so o codigo da API.
COPY src/ src/

RUN dotnet publish src/Host/PetHost.Api/PetHost.Api.csproj \
        --configuration "${BUILD_CONFIGURATION}" \
        --no-restore \
        --output /app/publish \
        -p:UseAppHost=false

# ----------------------------------------------------------------------------
# runtime
# ----------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS final

# A imagem aspnet nao traz cliente HTTP; o curl existe so para o HEALTHCHECK.
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl \
 && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish ./

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=1

EXPOSE 8080

# APP_UID vem da imagem base (uid 1654). A API nunca roda como root.
USER $APP_UID

HEALTHCHECK --interval=15s --timeout=3s --start-period=25s --retries=5 \
    CMD curl -fsS http://localhost:8080/health/live || exit 1

ENTRYPOINT ["dotnet", "PetHost.Api.dll"]
