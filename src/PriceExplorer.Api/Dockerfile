# syntax=docker/dockerfile:1
# Build context: repository root (see docker-compose.yml).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props ./
COPY src/PriceExplorer.Api/PriceExplorer.Api.csproj src/PriceExplorer.Api/
RUN dotnet restore src/PriceExplorer.Api/PriceExplorer.Api.csproj
COPY src/ src/
RUN dotnet publish src/PriceExplorer.Api/PriceExplorer.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
# Downloaded rate files and the DuckDB database live on a volume mounted at /data, owned by the non-root app user.
RUN mkdir -p /data && chown "$APP_UID":"$APP_UID" /data
COPY --from=build /app/publish .
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "PriceExplorer.Api.dll"]
