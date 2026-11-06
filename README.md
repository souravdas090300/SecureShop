# SecureShop

A full-stack e-commerce application built with ASP.NET Core 8, Clean Architecture, PostgreSQL, and Stripe (test mode). It combines a Razor Pages storefront, a separate admin area, a JWT-secured REST API, and a payment lifecycle designed to stay correct under retries and concurrent orders.

[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![PostgreSQL](https://img.shields.io/badge/PostgreSQL-Neon-4169E1?style=flat-square&logo=postgresql&logoColor=white)](https://neon.tech/)
[![Stripe](https://img.shields.io/badge/Stripe-test_mode-635BFF?style=flat-square&logo=stripe&logoColor=white)](https://stripe.com/)
[![Tests](https://img.shields.io/badge/tests-452_passing-brightgreen?style=flat-square)](#testing)
[![Hosting](https://img.shields.io/badge/hosted_on-Railway-0B0D0E?style=flat-square&logo=railway&logoColor=white)](https://railway.app/)

**Live demo:** https://secureshop-production-ff66.up.railway.app

> **Test mode only.** Payments use Stripe's test environment. At checkout, use card `4242 4242 4242 4242` with any future expiry and any CVC. No real money moves. See [Known Limitations](#known-limitations) before treating this as production-ready.

<!-- Add screenshots here before sharing: ![Storefront](docs/images/storefront.png) ![Checkout](docs/images/checkout.png) ![Admin dashboard](docs/images/admin.png) -->

## Table of Contents

- [Highlights](#highlights)
- [Features](#features)
- [Architecture](#architecture)
- [Tech Stack](#tech-stack)
- [Project Structure](#project-structure)
- [Getting Started](#getting-started)
- [Configuration](#configuration)
- [Payment Lifecycle](#payment-lifecycle)
- [Stock Integrity](#stock-integrity)
- [Admin Area](#admin-area)
- [API Overview](#api-overview)
- [Security](#security)
- [Testing](#testing)
- [CI/CD and Deployment](#cicd-and-deployment)
- [Known Limitations](#known-limitations)
- [Roadmap](#roadmap)

## Highlights

- Clean Architecture with enforced dependency direction: Domain and Application have no knowledge of EF Core, Stripe, or ASP.NET.
- Payments that survive real-world behaviour: signed Stripe webhooks, idempotent event handling, atomic order transitions, and an abandoned-order sweeper.
- No overselling: stock is reserved with a single atomic conditional `UPDATE`, proven by a concurrency test against real PostgreSQL.
- 452 automated tests (389 unit, 63 integration), including database-backed tests that run in CI against a PostgreSQL service container and fail the build if skipped.
- Defence in depth: account lockout, rate limiting, security headers, a CSP that allows Stripe, antiforgery, and a separately authorised admin area with optional TOTP two-factor authentication.

## Features

### Storefront

- Product catalogue with search, filtering, and featured products
- Client-side shopping cart
- Checkout with the Stripe Payment Element (card data never reaches this server)

### Accounts

- Registration and login with lockout after repeated failures
- Google sign-in
- OTP-based password reset
- Order history

### Admin area (at `/admin`, not linked from the storefront)

- Dashboard, product management, customer management, order management, and reports
- Separate sign-in, cookie scheme, and authorisation from customer accounts
- Optional TOTP two-factor authentication with recovery codes
- Dedicated layout, `noindex`/`no-store` response headers, and a stricter login rate limit

### Platform

- JWT-secured REST API with Swagger
- Optional Redis cache (falls back to a no-op cache when not configured)
- Cloudinary product image uploads
- Structured logging with Serilog
- Health endpoint at `/health`

## Architecture

Dependencies point inward.

```
SecureShop.API              Controllers, Razor Pages, middleware, background services
   └─ depends on Application + Infrastructure
SecureShop.Infrastructure   EF Core, Identity, JWT, Redis, Stripe, repositories
   └─ depends on Application
SecureShop.Application      Use cases, interfaces, DTOs, validators
   └─ depends on Domain
SecureShop.Domain           Entities, enums, domain exceptions (no dependencies)
```

Controllers and page models stay thin. Business rules live in the Domain and Application layers and are tested without a web host.

## Tech Stack

| Area | Technology |
|------|------------|
| Runtime | .NET 8 / ASP.NET Core (see [Known Limitations](#known-limitations) for the .NET 10 upgrade) |
| UI | Razor Pages |
| Data | PostgreSQL (Neon) via EF Core and Npgsql |
| Cache | Redis via StackExchange.Redis (optional) |
| Auth | ASP.NET Identity, JWT bearer, cookie auth (customer + dedicated admin cookie), Google sign-in, TOTP 2FA for admins |
| Payments | Stripe.net: PaymentIntents, Payment Element, webhooks |
| Images | Cloudinary |
| Validation | FluentValidation |
| Logging | Serilog |
| Tests | xUnit, Moq, FluentAssertions, real PostgreSQL integration tests |
| CI/CD | GitHub Actions |
| Hosting | Railway (app), Neon (database) |

## Project Structure

```
SecureShop/
├── SecureShop.Domain/             Entities, enums, DomainException
├── SecureShop.Application/        Services, interfaces, DTOs, validators
├── SecureShop.Infrastructure/     DbContext, migrations, repositories, payment/cache/auth services
├── SecureShop.API/
│   ├── Controllers/               Auth, Products, Orders, StripeWebhook
│   ├── Pages/                     Storefront, Checkout, Account, Admin (own layout, login, 2FA)
│   ├── Middleware/                Security headers, global exception handling
│   ├── Services/                  Background services (order abandonment sweep)
│   └── Program.cs                 Composition root
├── SecureShop.UnitTests/          389 unit tests
├── SecureShop.IntegrationTests/   63 integration tests (HTTP and real PostgreSQL)
├── docs/
│   └── pre-deployment-blockers.md
├── .env.example                   Template of every setting (no secrets)
└── .github/workflows/ci.yml       Build and test pipeline
```

## Getting Started

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- PostgreSQL (local Docker is fine) or a free [Neon](https://neon.tech/) database
- A Stripe account in test mode
- EF Core CLI: `dotnet tool install --global dotnet-ef`

### Run locally

```bash
git clone https://github.com/souravdas090300/SecureShop.git
cd SecureShop
dotnet restore
dotnet build
```

ASP.NET Core does not read `.env` files. Set configuration with user-secrets (see [Configuration](#configuration)):

```bash
cd SecureShop.API
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=secureshop_dev;SSL Mode=Disable"
dotnet user-secrets set "Jwt:Secret" "<32+ random characters>"
dotnet user-secrets set "Stripe:SecretKey" "sk_test_..."
dotnet user-secrets set "Stripe:PublishableKey" "pk_test_..."
dotnet run
```

Migrations are applied automatically at startup. To apply them manually:

```bash
dotnet ef database update `
  --project SecureShop.Infrastructure `
  --startup-project SecureShop.API
```

### Test the Stripe webhook locally

```bash
stripe listen --forward-to localhost:<port>/api/webhooks/stripe
```

Copy the `whsec_...` it prints into `Stripe:WebhookSecret`, then pay with `4242 4242 4242 4242`. The order should move to `Paid`.

## Configuration

Nested keys use a double underscore in environment variables (for example `Stripe__SecretKey`). Never commit real secrets; see `.env.example` for the full template.

| Setting | Required | Purpose |
|---------|----------|---------|
| `ConnectionStrings__DefaultConnection` | Yes | PostgreSQL connection string (Npgsql format, not a URI) |
| `ConnectionStrings__Redis` | No | Redis connection string. When blank, a no-op cache is used |
| `Jwt__Secret` | Yes | JWT signing secret, 32+ characters |
| `Jwt__Issuer`, `Jwt__Audience` | Yes | JWT validation values |
| `Stripe__SecretKey` | Yes | Must start with `sk_test_`; other keys are rejected |
| `Stripe__PublishableKey` | Yes | `pk_test_...` for the Payment Element |
| `Stripe__WebhookSecret` | Yes | Signing secret of the webhook endpoint |
| `GoogleAuth__ClientId`, `GoogleAuth__ClientSecret` | For Google sign-in | OAuth credentials |
| `Email__SmtpHost`, `SmtpPort`, `SmtpUser`, `SmtpPassword`, `FromName` | For emails | SMTP settings (used for password reset) |
| `Cloudinary__CloudName`, `Cloudinary__UploadPreset` | For images | Product image uploads |
| `AllowedOrigins` | No | Comma-separated CORS origins, no trailing slash |
| `BootstrapAdmin__Email`, `BootstrapAdmin__Password` | First run only | Creates the first admin if none exists. Remove after use |
| `Orders__AbandonAfterMinutes` | No | Default 30 |
| `Orders__AbandonmentIntervalMinutes` | No | Default 5 |
| `ASPNETCORE_ENVIRONMENT` | Yes | `Production` on Railway |

`PORT` is provided by Railway and must not be set manually.

In Production the app refuses to start if the database connection string is missing, points at localhost, or is still the placeholder — it logs whether the key is present, the parsed host/database (never the password), and the names of `ConnectionStrings*`/`DATABASE*` environment variables before exiting.

## Payment Lifecycle

Stripe runs in test mode only; any secret key that does not start with `sk_test_` is rejected.

**Order creation.** The server loads each product, calculates the total itself, reserves stock atomically, and creates a Stripe PaymentIntent with an idempotency key. The order stores the PaymentIntent ID, never its client secret.

**Payment.** The browser confirms payment with the Stripe Payment Element.

**Webhook.** Stripe calls `POST /api/webhooks/stripe`. The server verifies the signature and checks intent, amount, and currency against the order before marking it `Paid`.

Webhook processing is designed for retries and out-of-order delivery:

| Event | Behaviour |
|-------|-----------|
| `payment_intent.succeeded` | Order moves `PaymentProcessing` to `Paid` atomically. A late success on an already cancelled order triggers a refund (idempotent) instead of marking it paid |
| `payment_intent.payment_failed` | Records the failure on the order and keeps the reservation, because the customer can retry on the same PaymentIntent |
| `payment_intent.canceled` | Cancels the order and restores stock exactly once |

Supporting mechanisms:

- **Idempotent events:** each event ID is recorded in the same database transaction as its effects, so duplicate or parallel deliveries have no extra effect.
- **Atomic transitions:** status changes use `UPDATE ... WHERE Status = @from`, so stock is restored only by the request that wins the transition.
- **Abandonment sweep:** a background service cancels orders left in `PaymentProcessing` beyond the configured time (default 30 minutes). It asks Stripe for the intent's state first and skips intents that already succeeded or are still processing.

No tax is charged. A production tax policy has not been implemented.

## Stock Integrity

Stock is reserved with one conditional statement:

```sql
UPDATE "Products"
SET "StockQuantity" = "StockQuantity" - @qty
WHERE "Id" = @id AND "StockQuantity" >= @qty;
```

If no row changes, the reservation fails. Orders reserve lines in a fixed order, and any failure restores the lines already reserved. A concurrency test fires parallel orders for limited stock against real PostgreSQL and asserts that exactly the available number succeed. Admin edits persist only changed columns, so they cannot overwrite a concurrent reservation.

## Admin Area

The admin area lives at `/admin` and is not linked from the storefront. Hiding a URL is not security, so protection rests on server-side rules:

- Every page under `/Admin` requires the `Admin` role via an `AdminOnly` policy (`AuthorizeFolder`), except the admin login page and the short-lived 2FA challenge page.
- Admin pages are tested as a group: anonymous users are redirected to `/admin/login` and authenticated non-admins receive **403**.
- A dedicated `AdminCookie` scheme (`.SecureShop.Admin`, `Path=/admin`, `SameSite=Strict`, `HttpOnly`, `Secure` outside Development, 45-minute sliding expiry, no remember-me) is fully isolated from the customer cookie — customer sessions cannot reach `/admin/*` and admin sessions cannot reach customer areas.
- Optional **TOTP two-factor authentication** (ASP.NET Identity authenticator tokens) with recovery codes, enrolled at `/admin/twofactor` and enforced at `/admin/login2fa` when enabled.
- `/admin/*` responses carry `X-Robots-Tag: noindex, nofollow` and `Cache-Control: no-store`; admin pages use a dedicated `_AdminLayout`, never the storefront layout.
- `/admin/login` has its own stricter rate limit (~5 attempts/minute per IP) on top of Identity lockout; unknown-email and wrong-password failures return identical text.
- Sign-out is a POST action.

The first admin is created from `BootstrapAdmin__*` variables on first start and only if no admin exists. Remove those variables afterwards.

## API Overview

Swagger documents every endpoint. Authenticated routes need `Authorization: Bearer <token>`.

| Area | Endpoints | Access |
|------|-----------|--------|
| Auth | `POST /api/auth/register`, `POST /api/auth/login` | Public, rate-limited |
| Products | `GET /api/products`, `GET /api/products/{id}` | Public (`pageSize` capped at 50) |
| Products | `POST`, `PUT`, `DELETE /api/products` | Admin |
| Orders | `POST /api/orders`, `GET /api/orders`, `GET /api/orders/{id}` | Authenticated, own orders only |
| Stripe | `POST /api/webhooks/stripe` | Stripe signature required |
| Health | `GET /health` | Public |

Order statuses: `Pending` → `PaymentProcessing` → `Paid` → `Shipped` → `Delivered`, or `Cancelled` (not allowed once shipped or delivered).

## Security

- JWT bearer and cookie authentication; role-based authorisation
- Account lockout after 5 failed attempts for 15 minutes
- Identical errors for unknown email and wrong password (anti-enumeration)
- Per-client-IP rate limiting, with a stricter policy on `/admin/login`
- Forwarded-header handling for Railway's proxy so rate limits see real client IPs
- Antiforgery on form posts; secure cookies in production
- Security headers and a Content-Security-Policy that allows only the Stripe origins needed
- Stripe webhook signature verification; card data never touches the server
- Order totals calculated server-side; quantities validated at the application and domain layers
- Pagination limits to prevent oversized queries
- No default admin password in code
- Data Protection keys persisted in PostgreSQL so cookies survive redeploys

## Testing

```bash
dotnet build SecureShop.sln -c Release /warnaserror
dotnet test SecureShop.sln -c Release
```

| Suite | Tests |
|-------|-------|
| Unit tests | 389 |
| Integration tests | 63 (HTTP and real PostgreSQL) |

Database-backed tests (stock concurrency, webhook idempotency, payment lifecycle, abandonment sweep) need a disposable PostgreSQL database. A safety guard refuses to run them unless the database name ends with `_test` and the host is not a known production/Railway host — point `SECURESHOP_TEST_DATABASE_URL` only at throwaway databases:

```powershell
$env:SECURESHOP_TEST_DATABASE_URL="Host=...;Database=secureshop_test;Username=...;Password=...;SSL Mode=Require"
dotnet test SecureShop.IntegrationTests
```

In CI these tests run against a PostgreSQL service container, and the build fails if the variable is missing. Never point this variable at a production database.

Stripe API calls are exercised against fakes in automated tests. Confirm the real flow with the Stripe CLI (`stripe listen`) and a test payment.

## CI/CD and Deployment

**CI (GitHub Actions):** restore, build with warnings as errors, then unit and integration tests against a PostgreSQL 16 service container.

**Hosting:** the app runs on Railway and the database on Neon. Use the same region for both where possible.

Deployment checklist:

1. Create a Neon database and copy the direct connection string (pooling off) in Npgsql format.
2. In Railway, set the variables from [Configuration](#configuration). Variable names are case-sensitive and use a double underscore.
3. In Stripe (test mode), add a webhook endpoint at `https://<your-domain>/api/webhooks/stripe` for `payment_intent.succeeded`, `payment_intent.payment_failed`, and `payment_intent.canceled`, then set its signing secret as `Stripe__WebhookSecret`.
4. Enable Wait for CI in Railway so a failing build cannot deploy.
5. Deploy, then check `/health` and place a test order.

## Known Limitations

This project is not approved for live payments.

- **Payments:** no admin-initiated refunds, no tax handling, and no live-mode support
- **Checkout data:** shipping and contact details are collected but not yet saved with the order
- **Operations:** SMTP over TLS needs validation, readiness checks for the database are not implemented, and migrations run at application startup rather than as a separate release step
- **Data Protection keys** are stored unencrypted in the database; add key protection before real customers
- **Runtime:** .NET 8 reaches end of support on 10 November 2026. An upgrade to .NET 10 is in progress in a separate pull request
- **Admin 2FA is opt-in:** admins who have not enrolled a TOTP key sign in with password only

## Roadmap

- [ ] Persist shipping and contact details on orders
- [ ] Admin-initiated refunds
- [ ] Tax handling (for example Stripe Tax)
- [ ] Database readiness checks
- [ ] Migrations as a release step
- [ ] Encrypted Data Protection keys
- [ ] Enforce admin 2FA enrollment (currently opt-in)
- [ ] Upgrade to .NET 10
- [ ] Docker support

## License

Add a `LICENSE` file before publishing. MIT is a common choice for portfolio projects.

---

Built by Sourav · [GitHub](https://github.com/souravdas090300/SecureShop)
