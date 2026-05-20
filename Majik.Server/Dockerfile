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
COPY --from=build /app/publish ./

# Render injects PORT (default 10000). ASPNETCORE_URLS pinned to match in render.yaml.
EXPOSE 10000
ENTRYPOINT ["dotnet", "Majik.Server.dll"]
