FROM node:24-bookworm-slim AS frontend
WORKDIR /src/frontend
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY frontend/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend
WORKDIR /src
COPY backend/PasskeyDemo.Api/PasskeyDemo.Api.csproj backend/PasskeyDemo.Api/
RUN dotnet restore backend/PasskeyDemo.Api/PasskeyDemo.Api.csproj
COPY backend/PasskeyDemo.Api/ backend/PasskeyDemo.Api/
RUN dotnet publish backend/PasskeyDemo.Api/PasskeyDemo.Api.csproj -c Release --no-restore -o /publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=10000
COPY --from=backend /publish ./
COPY --from=frontend /src/frontend/dist/passkey-demo/browser ./wwwroot
USER $APP_UID
EXPOSE 10000
ENTRYPOINT ["dotnet", "PasskeyDemo.Api.dll"]
