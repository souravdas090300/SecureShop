# Configuration & Externalized Settings Inventory

The application has JSON configuration, ASP.NET Core environment variables, launch profiles, and Docker/Railway deployment settings. Local secrets can use .NET user-secrets; production secrets should be injected through the host's protected environment-variable store.

## Configuration Sources

| Source | Type | Path/Location | Notes |
|---|---|---|---|
| Application defaults | JSON | `SecureShop.API/appsettings.json` | Non-secret defaults and blank secret/connection placeholders |
| Local run profiles | JSON | `SecureShop.API/Properties/launchSettings.json` | `http` and `https` profiles set Development; configured HTTP port is 8080 |
| Local secret overrides | User secrets or ignored JSON | API project user-secrets store; `SecureShop.API/appsettings.Development.json` | User-secrets ID is defined by the API project; environment-specific appsettings are git-ignored |
| Host overrides | Environment variables | ASP.NET Core configuration | Hierarchical keys use `__`, e.g. `Jwt__Secret`; `PORT` is read directly by `Program.cs` |
| Container build/runtime | Dockerfile | `Dockerfile` | .NET 8 SDK build image and ASP.NET 8 runtime image; Production environment; dynamic `PORT` |
| Railway deployment | Railway JSON/TOML | `railway.json`, `railway.toml` | Both select the root Dockerfile and `/health` as health-check path |
| Alternative build configuration | Nixpacks | `nixpacks.toml` | Present but Railway is configured to use Dockerfile |
| SDK selection | `global.json` | Repository root | .NET SDK 8.0.419, latest-patch roll-forward |

## Build Profiles

| Profile | Activation | Purpose | Key Dependencies/Plugins |
|---|---|---|---|
| Debug | `dotnet build` default | Local development build | .NET 8 SDK |
| Release | `-c Release`; used by Docker build | Optimized build and publish | .NET 8 SDK |
| Docker | Railway builder selection or `docker build` | Multi-stage container build; runs unit tests and publishes API | `mcr.microsoft.com/dotnet/sdk:8.0`, `mcr.microsoft.com/dotnet/aspnet:8.0` |
| Nixpacks | Explicit Nixpacks selection | Alternative self-contained Linux publish | `nixpacks.toml`; not the builder selected in Railway config |

## Runtime Profiles

| Profile | Activation Method | Config Files | Key Overrides |
|---|---|---|---|
| Development | `ASPNETCORE_ENVIRONMENT=Development`; launch profiles set this | `appsettings.json`, optional `appsettings.Development.json`, API user-secrets | Development cookie policy; local service endpoints/secrets |
| Production | Container sets `ASPNETCORE_ENVIRONMENT=Production`; set explicitly in Railway as well | `appsettings.json` plus host environment variables | Production-secure cookies, database and provider credentials, host-provided `PORT` |

## Properties Inventory

| Property Key | Default | Profiles | Source |
|---|---|---|---|
| `ApiBaseUrl` | `http://localhost:8080` | Can be overridden in Production | JSON/env; admin pages otherwise derive the current request host |
| `ConnectionStrings:DefaultConnection` | Blank | Required for database operations | User-secrets locally; host environment variable in Production |
| `ConnectionStrings:Redis` | Blank | Optional | User-secrets or host environment; omit to use no-op cache |
| `Jwt:Secret` | Blank; application startup requires it | All | User-secrets locally; protected host environment in Production |
| `Jwt:Issuer` | `SecureShop` | All | JSON/env |
| `Jwt:Audience` | `SecureShopUsers` | All | JSON/env |
| `BootstrapAdmin:Email` | Unset | Optional one-time setup | Environment/user-secrets; must be supplied with password, remove after initial admin creation |
| `BootstrapAdmin:Password` | Unset | Optional one-time setup | Secret environment variable/user-secret; never commit; remove after initial admin creation |
| `GoogleAuth:ClientId` | Blank | Optional | JSON/env; used to validate Google ID tokens and initialize browser sign-in |
| `GoogleAuth:ClientSecret` | Blank | Not referenced by current application code | JSON only unless separately required by an external Google setup |
| `Email:SmtpHost` | `smtp.gmail.com` | Optional feature | JSON/env |
| `Email:SmtpPort` | `587` | Optional feature | JSON/env; current code disables `SmtpClient.EnableSsl` at 587 and enables it at other ports. .NET `SmtpClient.EnableSsl` negotiates STARTTLS and does not support implicit TLS on 465; fix and test transport before relying on email |
| `Email:SmtpUser`, `Email:SmtpPassword` | Blank | Optional; required for email delivery | User-secrets or protected host environment |
| `Email:FromName` | `SecureShop` | Optional | JSON/env |
| `Stripe:SecretKey` | Blank | Required for checkout; current code accepts only `sk_test_` keys | API project user-secrets locally; protected host environment |
| `Stripe:PublishableKey` | Blank | Required for browser checkout; current code accepts only `pk_test_` keys | API project user-secrets locally; public test-mode value in app configuration |
| `Stripe:WebhookSecret` | Blank | Required for signed webhook processing; `whsec_` value | Stripe CLI locally; protected host environment for a test webhook endpoint |
| `Cloudinary:CloudName` | Blank | Optional image upload UI | JSON/env |
| `Cloudinary:UploadPreset` | `secureshop_products` | Optional image upload UI | JSON/env; unsigned upload preset is exposed to the browser |
| `AllowedOrigins` | Blank | Required only when browser clients use cross-origin API calls | Comma-separated JSON/env |
| `Logging:LogLevel:*` | Information/Warning defaults | All | JSON/env |
| `Serilog:MinimumLevel:*` | Information/Warning defaults | All | JSON/env |
| `ASPNETCORE_ENVIRONMENT` | Development for launch profiles; Production in Dockerfile | Development/Production | Host environment |
| `PORT` | Application fallback 8080 | Host/container | Railway injects it; application reads and binds it |

The EF design-time factory reads environment variables. Its user-secrets lookup is associated with the Infrastructure assembly, which does not define the API project's user-secrets ID; set `ConnectionStrings__DefaultConnection` in the process environment when invoking `dotnet ef`.

## Startup Parameters & Resource Requirements

| Service | JVM/Runtime Options | Memory | Instance Count |
|---|---|---|---|
| SecureShop API | .NET 8; binds Kestrel to `PORT`, fallback 8080; Docker runs as non-root | Not specified in repository | Not specified |
| PostgreSQL | Managed provider or local PostgreSQL | Provider-managed | Not specified |
| Redis | Optional; no-op cache fallback if absent | Provider-managed | Not specified |

## Startup Dependency Chain

1. Container starts the .NET API and Kestrel binds to the host-provided port.
2. Docker/Railway probes `/health`; current health checks are not configured to test database connectivity.
3. About two seconds after the host starts, a background task connects to PostgreSQL, applies EF migrations, creates roles, seeds an admin if absent, and seeds sample products.
4. Redis is optional and is not on the startup critical path.

The health endpoint can return healthy before background migration/seeding completes. Check application logs and perform a database-backed smoke test after deployment.

## Secrets & Sensitive Configuration

| Secret Reference | Type | Storage (masked) |
|---|---|---|
| `ConnectionStrings__DefaultConnection` | PostgreSQL credentials/connection | User-secrets locally; protected platform variable in Production |
| `ConnectionStrings__Redis` | Redis endpoint/password, if enabled | User-secrets locally; protected platform variable in Production |
| `Jwt__Secret` | JWT signing key | User-secrets locally; protected platform variable in Production |
| `BootstrapAdmin__Password` | One-time administrator bootstrap password | User-secrets locally or temporary protected host variable; remove immediately after bootstrap |
| `Stripe__SecretKey` | Stripe API credential | Test secret key only (`sk_test_...`); protected platform variable |
| `Stripe__WebhookSecret` | Stripe webhook signature secret | Test endpoint signing secret (`whsec_...`); protected platform variable |
| `Email__SmtpPassword` | SMTP credential | Provider app password; protected platform variable |
| `GoogleAuth__ClientId` | OAuth client identifier (not secret) | JSON/environment; restrict authorized origins at Google |
| Cloudinary unsigned upload configuration | Public cloud name/preset | Browser-visible; restrict preset capabilities; do not expose an API secret |

### Secrets Provisioning Workflow

For local development, store values with the API project's .NET user-secrets provider or an ignored Development settings file. Do not commit credentials. For Production, create secrets in the hosting provider's secret/environment-variable UI and use ASP.NET Core's `Section__Key` variable syntax. Rotate credentials independently at the provider and application-host layers. Limit access to production environment variables and use separate test and live credentials.

Initial admin provisioning is optional and uses `BootstrapAdmin:Email` and `BootstrapAdmin:Password` only when no Admin-role user exists. Remove these temporary values after successful first-admin creation; rotate/disable any legacy default admin created by an earlier release.

## Feature Flags

| Flag Name | Default | Controlled By |
|---|---|---|
| Redis cache enabled | Disabled when no usable Redis connection is provided | `ConnectionStrings:Redis` |
| Google sign-in configured | Disabled when no client ID is configured | `GoogleAuth:ClientId` |
| Email delivery configured | Disabled when SMTP user/password are absent | `Email:SmtpUser`, `Email:SmtpPassword` |
| Stripe checkout enabled | Test-only; requires a `pk_test_` publishable key and valid `sk_test_` secret key | `Stripe:PublishableKey`, `Stripe:SecretKey` |
| Stripe payment confirmation | Only signed `payment_intent.succeeded` events can mark an order paid | `Stripe:WebhookSecret` |
| Product image upload UI | Unavailable without cloud name/preset | `Cloudinary:CloudName`, `Cloudinary:UploadPreset` |
| Swagger | Enabled in all environments in current middleware | No environment flag |

## Framework & Runtime Versions

| Component | Version | Source |
|---|---|---|
| .NET SDK | 8.0.419 (latest-patch roll-forward; .NET 8 support ends 2026-11-10) | `global.json`; Microsoft support policy |
| Target framework | `net8.0` (plan tested upgrade to .NET 10 LTS for a new long-lived deployment) | Project files |
| Container SDK image | .NET SDK 8.0 | `Dockerfile`; upgrade alongside target framework |
| Container runtime image | ASP.NET Core 8.0 | `Dockerfile`; upgrade alongside target framework |
| Stripe.net | 50.4.1 (expects webhook API version `2026-02-25.clover`) | Infrastructure project package reference |
| EF Core design package | 8.0.11 | API project file |
| ASP.NET Core Identity/JWT bearer packages | 8.0.0 | API project file |
| Test framework | xUnit 2.9.2; Microsoft.NET.Test.Sdk 17.11.1 | Test project files |
