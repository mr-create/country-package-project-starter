FROM node:22-alpine AS web-build
WORKDIR /web
COPY src/CountryPackage.Web/package*.json ./
RUN npm ci
COPY src/CountryPackage.Web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS api-build
WORKDIR /src
COPY src/CountryPackage.Api/CountryPackage.Api.csproj src/CountryPackage.Api/
RUN dotnet restore src/CountryPackage.Api/CountryPackage.Api.csproj
COPY src/CountryPackage.Api/ src/CountryPackage.Api/
COPY openapi.yaml ./openapi.yaml
COPY --from=web-build /web/dist src/CountryPackage.Web/dist
RUN dotnet publish src/CountryPackage.Api/CountryPackage.Api.csproj --configuration Release --output /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=api-build /app/publish ./
COPY sources/ /sources/
RUN mkdir -p /data && chown -R "$APP_UID":"$APP_UID" /app /data /sources
USER $APP_UID
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Development \
    ConnectionStrings__Database="Data Source=/data/country-packages.db" \
    Storage__SourceDirectory=/sources
EXPOSE 8080
VOLUME ["/data"]
HEALTHCHECK CMD curl --fail --silent http://localhost:8080/health/ready || exit 1
ENTRYPOINT ["dotnet", "CountryPackage.Api.dll"]
