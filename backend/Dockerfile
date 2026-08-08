# syntax=docker/dockerfile:1.7
# Contexto esperado: raiz del repo (.)
# Uso: docker build -t jade-api -f backend/Dockerfile .

# ---------- build ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# csproj layer cache.
COPY Directory.Build.props ./
COPY src/1.Api/JadeCapital.Host/JadeCapital.Host.csproj                          src/1.Api/JadeCapital.Host/
COPY src/2.Modules/Identity/JadeCapital.Identity.Api/JadeCapital.Identity.Api.csproj src/2.Modules/Identity/JadeCapital.Identity.Api/
COPY src/2.Modules/Identity/JadeCapital.Identity.Application/JadeCapital.Identity.Application.csproj src/2.Modules/Identity/JadeCapital.Identity.Application/
COPY src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/JadeCapital.Identity.Infrastructure.csproj src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/
COPY src/2.Modules/Identity/JadeCapital.Identity.Domain/JadeCapital.Identity.Domain.csproj src/2.Modules/Identity/JadeCapital.Identity.Domain/
COPY src/2.Modules/Identity/JadeCapital.Identity.Contracts/JadeCapital.Identity.Contracts.csproj src/2.Modules/Identity/JadeCapital.Identity.Contracts/
COPY src/3.Shared/JadeCapital.Shared.Kernel/JadeCapital.Shared.Kernel.csproj src/3.Shared/JadeCapital.Shared.Kernel/

RUN dotnet restore src/1.Api/JadeCapital.Host/JadeCapital.Host.csproj

# Código completo.
COPY src/ src/

RUN dotnet publish src/1.Api/JadeCapital.Host/JadeCapital.Host.csproj \
    -c Release -o /app/publish /p:UseAppHost=false

# ---------- runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true

RUN groupadd --system --gid 1001 jade \
 && useradd  --system --uid 1001 --gid jade --home /app jade

COPY --from=build --chown=jade:jade /app/publish .
USER jade

EXPOSE 8080
HEALTHCHECK --interval=15s --timeout=3s --start-period=10s --retries=4 \
    CMD wget -q --spider http://localhost:8080/health/live || exit 1

ENTRYPOINT ["dotnet", "JadeCapital.Host.dll"]
