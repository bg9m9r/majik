# Multi-stage build for Majik.Server. Context = repo root (majik.core/).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props ./
COPY Majik.Core/ ./Majik.Core/
COPY Majik.Core.Api/ ./Majik.Core.Api/
COPY Majik.Server/ ./Majik.Server/

RUN dotnet restore Majik.Server/Majik.Server.csproj
RUN dotnet publish Majik.Server/Majik.Server.csproj \
      -c Release \
      -o /app/publish \
      --no-restore \
      /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# curl needed by docker-entrypoint.sh to pull the cards.db seed from
# a GitHub Release on first boot when CARDS_SEED_TAG changes. ca-certs
# already in base image but install explicitly to be safe.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl ca-certificates \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish ./
COPY Majik.Server/docker-entrypoint.sh /usr/local/bin/docker-entrypoint.sh
RUN chmod +x /usr/local/bin/docker-entrypoint.sh

# Render injects PORT (default 10000). ASPNETCORE_URLS pinned to match in render.yaml.
EXPOSE 10000
ENTRYPOINT ["/usr/local/bin/docker-entrypoint.sh"]
