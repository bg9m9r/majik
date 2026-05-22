# Multi-stage build for Majik.CardsServer. Context = repo root (majik.core/).
# Mirrors Majik.Server/Dockerfile in structure; differs only in which project
# is built/published and which DLL the entrypoint launches.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props ./
COPY Majik.Core/ ./Majik.Core/
COPY Majik.CardsServer/ ./Majik.CardsServer/

RUN dotnet restore Majik.CardsServer/Majik.CardsServer.csproj
RUN dotnet publish Majik.CardsServer/Majik.CardsServer.csproj \
      -c Release \
      -o /app/publish \
      --no-restore \
      /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# curl pulls cards.db from a GitHub Release on first boot when CARDS_SEED_TAG
# is set. ca-certs already in base image but install explicitly to be safe.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl ca-certificates \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish ./
COPY Majik.CardsServer/docker-entrypoint.sh /usr/local/bin/docker-entrypoint.sh
RUN chmod +x /usr/local/bin/docker-entrypoint.sh

# Same port convention as majik-api so Render templates + intra-network DNS
# lookups stay consistent across services.
EXPOSE 10000
ENTRYPOINT ["/usr/local/bin/docker-entrypoint.sh"]
