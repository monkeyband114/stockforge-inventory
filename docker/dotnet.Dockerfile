# syntax=docker/dockerfile:1.7
# Builds either backend service from the repository root:
#   docker build -f docker/dotnet.Dockerfile --build-arg PROJECT=StockForge.API    -t stockforge-api .
#   docker build -f docker/dotnet.Dockerfile --build-arg PROJECT=StockForge.Worker -t stockforge-worker .
ARG DOTNET_VERSION=11.0

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
ARG PROJECT
WORKDIR /src

# Restore first (cached layer): only project files and central package versions.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/StockForge.Domain/StockForge.Domain.csproj src/StockForge.Domain/
COPY src/StockForge.Application/StockForge.Application.csproj src/StockForge.Application/
COPY src/StockForge.Infrastructure/StockForge.Infrastructure.csproj src/StockForge.Infrastructure/
COPY src/StockForge.API/StockForge.API.csproj src/StockForge.API/
COPY src/StockForge.Worker/StockForge.Worker.csproj src/StockForge.Worker/
RUN dotnet restore src/${PROJECT}/${PROJECT}.csproj

COPY src/ src/
RUN dotnet publish src/${PROJECT}/${PROJECT}.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime
ARG PROJECT
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    STOCKFORGE_ENTRYPOINT=${PROJECT}.dll
EXPOSE 8080
# Non-root user provided by the official .NET images.
USER $APP_UID
ENTRYPOINT ["sh", "-c", "exec dotnet \"$STOCKFORGE_ENTRYPOINT\" \"$@\"", "--"]
