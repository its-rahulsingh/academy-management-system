# Build Angular. This stage is discarded from the final runtime image.
FROM node:22-alpine AS frontend-build
WORKDIR /source/client
COPY client/package*.json ./
# Use a committed lockfile for reproducible builds once npm install creates one.
RUN if [ -f package-lock.json ]; then npm ci --no-audit --no-fund; else npm install --no-audit --no-fund; fi
COPY client/ ./
RUN npm run build

# Build ASP.NET Core 10.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api-build
WORKDIR /source
COPY server/Academy.Api/Academy.Api.csproj server/Academy.Api/
RUN dotnet restore server/Academy.Api/Academy.Api.csproj
COPY server/Academy.Api/ server/Academy.Api/
RUN dotnet publish server/Academy.Api/Academy.Api.csproj -c Release -o /app/publish --no-restore

# One small runtime container serves both Angular and API.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=api-build /app/publish/ ./
COPY --from=frontend-build /source/client/dist/academy-web/browser/ ./wwwroot/

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://0.0.0.0:10000
EXPOSE 10000
USER $APP_UID
ENTRYPOINT ["dotnet", "Academy.Api.dll"]
