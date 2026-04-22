# ── Stage 1: Build ────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and project files first for layer-cached restore
COPY SecureShop.sln .
COPY SecureShop.Domain/SecureShop.Domain.csproj           SecureShop.Domain/
COPY SecureShop.Application/SecureShop.Application.csproj SecureShop.Application/
COPY SecureShop.Infrastructure/SecureShop.Infrastructure.csproj SecureShop.Infrastructure/
COPY SecureShop.API/SecureShop.API.csproj                 SecureShop.API/

RUN dotnet restore SecureShop.API/SecureShop.API.csproj

# Copy source and publish
COPY . .
RUN dotnet publish SecureShop.API/SecureShop.API.csproj \
      -c Release \
      --no-restore \
      -o /app/publish

# ── Stage 2: Runtime ──────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# aspnet:8.0 is Ubuntu-based and includes ICU, OpenSSL, libstdc++ — no crashes
COPY --from=build /app/publish .

# Railway injects PORT at runtime; Program.cs reads it via ConfigureKestrel
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "SecureShop.API.dll"]
