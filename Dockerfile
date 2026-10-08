# syntax=docker/dockerfile:1

ARG DOTNET_VERSION=10.0

# ----------------------------------------------------------------------------
# build
# ----------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Configuracao da solution: versoes centralizadas, regras de build e estilo.
COPY Directory.Build.props Directory.Packages.props global.json .editorconfig ./

# Restore em camada propria: so e invalidada quando algum .csproj muda.
# Cada .csproj vai para o mesmo caminho do repositorio, porque as
# ProjectReference sao relativas.
COPY src/Shared/PetHost.Shared.Kernel/PetHost.Shared.Kernel.csproj                         src/Shared/PetHost.Shared.Kernel/
COPY src/Shared/PetHost.Shared.Contracts/PetHost.Shared.Contracts.csproj                   src/Shared/PetHost.Shared.Contracts/
COPY src/Shared/PetHost.Shared.Infrastructure/PetHost.Shared.Infrastructure.csproj         src/Shared/PetHost.Shared.Infrastructure/
COPY src/Modules/Auth/PetHost.Modules.Auth.Domain/PetHost.Modules.Auth.Domain.csproj                 src/Modules/Auth/PetHost.Modules.Auth.Domain/
COPY src/Modules/Auth/PetHost.Modules.Auth.Application/PetHost.Modules.Auth.Application.csproj       src/Modules/Auth/PetHost.Modules.Auth.Application/
COPY src/Modules/Auth/PetHost.Modules.Auth.Infrastructure/PetHost.Modules.Auth.Infrastructure.csproj src/Modules/Auth/PetHost.Modules.Auth.Infrastructure/
COPY src/Modules/Auth/PetHost.Modules.Auth.Presentation/PetHost.Modules.Auth.Presentation.csproj     src/Modules/Auth/PetHost.Modules.Auth.Presentation/
COPY src/Host/PetHost.Api/PetHost.Api.csproj                                               src/Host/PetHost.Api/

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
