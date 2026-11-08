# ── Stage 1: Build ───────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution + project files first (layer cache for NuGet restore)
COPY SecureShop.sln ./
COPY SecureShop.API/SecureShop.API.csproj                         SecureShop.API/
COPY SecureShop.Application/SecureShop.Application.csproj         SecureShop.Application/
COPY SecureShop.Domain/SecureShop.Domain.csproj                   SecureShop.Domain/
COPY SecureShop.Infrastructure/SecureShop.Infrastructure.csproj   SecureShop.Infrastructure/
COPY SecureShop.UnitTests/SecureShop.UnitTests.csproj             SecureShop.UnitTests/
COPY SecureShop.IntegrationTests/SecureShop.IntegrationTests.csproj  SecureShop.IntegrationTests/

# Restore NuGet packages
RUN dotnet restore

# Copy all source code
COPY . .

# Publish the API project (implicit restore ensures its closure is complete —
# running this after `dotnet test` hit NETSDK1064 on a transitive analyzer)
RUN dotnet publish SecureShop.API --configuration Release --output /app/publish

# Run tests
RUN dotnet test SecureShop.UnitTests --no-restore --configuration Release --verbosity minimal

# ── Stage 2: Runtime ─────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# App_Data directory for any file-based keys/artifacts (DP keys are stored in
# PostgreSQL; this is a fallback location). Owned by the runtime user.
RUN mkdir -p /app/App_Data/DataProtection-Keys && chown -R $APP_UID /app/App_Data

# The .NET 10 base image ships a built-in non-root user exposed as $APP_UID
# (the image no longer provides addgroup/adduser).
USER $APP_UID

# Copy published output from build stage
COPY --from=build /app/publish .

# Railway sets PORT automatically — declare ARG so Docker doesn't warn about undefined var
ARG PORT=8080

# Railway sets PORT automatically — Kestrel must listen on it
ENV ASPNETCORE_URLS=http://+:${PORT:-8080}
ENV ASPNETCORE_ENVIRONMENT=Production

# Health check so Railway knows when the container is ready
HEALTHCHECK --interval=30s --timeout=10s --start-period=30s --retries=3 \
  CMD wget --no-verbose --tries=1 --spider http://localhost:${PORT:-8080}/health || exit 1

EXPOSE 8080
ENTRYPOINT ["dotnet", "SecureShop.API.dll"]