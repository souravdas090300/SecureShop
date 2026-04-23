# ── Stage 1: Build ───────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution + project files first (layer cache for NuGet restore)
COPY SecureShop.sln ./
COPY SecureShop.API/SecureShop.API.csproj                         SecureShop.API/
COPY SecureShop.Application/SecureShop.Application.csproj         SecureShop.Application/
COPY SecureShop.Domain/SecureShop.Domain.csproj                   SecureShop.Domain/
COPY SecureShop.Infrastructure/SecureShop.Infrastructure.csproj   SecureShop.Infrastructure/
COPY SecureShop.UnitTests/SecureShop.UnitTests.csproj             SecureShop.UnitTests/

# Restore NuGet packages
RUN dotnet restore

# Copy all source code
COPY . .

# Run tests
RUN dotnet test SecureShop.UnitTests --no-restore --configuration Release --verbosity minimal

# Publish the API project
RUN dotnet publish SecureShop.API --no-restore --configuration Release --output /app/publish

# ── Stage 2: Runtime ─────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Non-root user for security
RUN addgroup --system appgroup && adduser --system --ingroup appgroup appuser
USER appuser

# Copy published output from build stage
COPY --from=build /app/publish .

# Railway sets PORT automatically — Kestrel must listen on it
ENV ASPNETCORE_URLS=http://+:${PORT:-8080}
ENV ASPNETCORE_ENVIRONMENT=Production

# Health check so Railway knows when the container is ready
HEALTHCHECK --interval=30s --timeout=10s --start-period=15s --retries=3 \
  CMD wget --no-verbose --tries=1 --spider http://localhost:${PORT:-8080}/health || exit 1

EXPOSE 8080
ENTRYPOINT ["dotnet", "SecureShop.API.dll"]