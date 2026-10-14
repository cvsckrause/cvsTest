FROM node:22-alpine AS web
WORKDIR /web
COPY src/erp-web/package.json src/erp-web/package-lock.json ./
RUN npm ci
COPY src/erp-web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
COPY --from=web /web/dist ./src/Erp.Api/wwwroot
RUN dotnet publish src/Erp.Api/Erp.Api.csproj \
    --configuration Release \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "Erp.Api.dll"]
