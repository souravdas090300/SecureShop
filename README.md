# SecureShop

An e-commerce application built with **ASP.NET Core 8**, following Clean Architecture principles and deployed on **Railway**. Public production readiness is still gated; see [Known limitations and pre-deployment blockers](#known-limitations-and-pre-deployment-blockers).

The application ships a full customer-facing storefront (Razor Pages), a separate admin panel, and a RESTful JSON API — all in a single host process.

**Live URL:** `https://secureshop-production-2eac.up.railway.app`

---

## Table of contents

1. [Architecture](#architecture)
2. [Tech stack](#tech-stack)
3. [Project structure](#project-structure)
4. [Features](#features)
5. [API reference](#api-reference)
6. [Admin panel](#admin-panel)
7. [Running locally](#running-locally)
8. [Deploying to Railway](#deploying-to-railway)
9. [Environment variables](#environment-variables)
10. [Security](#security)
11. [Known limitations and pre-deployment blockers](#known-limitations-and-pre-deployment-blockers)
12. [Testing](#testing)
13. [Code documentation](#code-documentation)

---

## Architecture

SecureShop follows **Clean Architecture** — dependencies only point inward:

```
┌─────────────────────────────────────────┐
│  SecureShop.API                         │  ← Presentation (controllers, Razor Pages, middleware)
│  ┌───────────────────────────────────┐  │
│  │  SecureShop.Application           │  │  ← Business logic (services, interfaces, DTOs, validators)
│  │  ┌─────────────────────────────┐  │  │
│  │  │  SecureShop.Domain          │  │  │  ← Entities, enums, domain exceptions (no dependencies)
│  │  └─────────────────────────────┘  │  │
│  └───────────────────────────────────┘  │
│  SecureShop.Infrastructure              │  ← EF Core, Identity, Redis, Stripe, SMTP
└─────────────────────────────────────────┘
```

**Key patterns:**
- Clean Architecture with dependency inversion
- Repository pattern (`IProductRepository`, `IOrderRepository`)
- Domain aggregates with factory methods and invariant enforcement
- Service layer for orchestration (caching, payment, stock management)
- FluentValidation for request validation
- Structured logging (ILogger throughout; stdout collected by Railway)

---

## Tech stack

| Concern | Technology |
|---|---|
| Framework | ASP.NET Core 8 (Razor Pages + Web API) |
| Database | PostgreSQL via [Neon](https://neon.tech) (EF Core 8 + Npgsql) |
| Cache | Redis via [Upstash](https://upstash.com) (StackExchange.Redis) |
| Auth (customers) | ASP.NET Core Identity + JWT Bearer + Cookie |
| Auth (admin) | ASP.NET Core Identity + `AdminCookie` scheme |
| Auth (OAuth) | Google Sign-In (ID token validation) |
| Payments | Stripe Payment Intents + Payment Element (test mode only) |
| Validation | FluentValidation |
| Data Protection | EF Core key storage (PostgreSQL) — survives restarts |
| Email | SMTP (configurable — Gmail, SendGrid, etc.) |
| Deployment | [Railway](https://railway.app) — Docker, PORT=8080 |

---

## Project structure

```
SecureShop.sln
├── SecureShop.API/
│   ├── Controllers/
│   │   ├── AuthController.cs        – Register, login, Google OAuth, JWT status
│   │   ├── ProductsController.cs    – Product catalogue (public reads, admin writes)
│   │   ├── OrdersController.cs      – Order placement and retrieval
│   │   └── StripeWebhookController.cs – Signed Stripe payment confirmation
│   ├── Middleware/
│   │   ├── GlobalExceptionMiddleware.cs   – Maps exceptions → HTTP status codes
│   │   ├── SecurityHeadersMiddleware.cs   – CSP, X-Frame-Options, Permissions-Policy
│   │   └── CacheBustingMiddleware.cs      – no-store headers for account/auth pages
│   ├── Pages/
│   │   ├── (customer storefront)          – Index, Products, Cart, Checkout, About, Contact
│   │   ├── Account/                       – Login, Register, Logout, Orders, ForgotPassword, VerifyOtp, GoogleCallback
│   │   └── Admin/                         – Dashboard, Products, Orders, Customers, Reports, Profile, Settings
│   ├── Helpers/
│   │   └── JwtCookieHelper.cs       – Converts JWT → ClaimsPrincipal for cookie sign-in
│   └── Program.cs                   – Host setup, middleware pipeline, DI registrations
│
├── SecureShop.Application/
│   ├── Services/
│   │   ├── ProductService.cs        – Catalogue management + Redis caching
│   │   └── OrderService.cs          – Order creation, stock handling, payment confirmation
│   ├── Interfaces/                  – IAuthService, IProductRepository, IOrderRepository,
│   │                                   ICacheService, IEmailService, IPaymentService
│   ├── DTOs/                        – Auth, Product, Order request/response records
│   └── Validators/                  – FluentValidation for RegisterDto, CreateProductDto
│
├── SecureShop.Domain/
│   ├── Entities/                    – ApplicationUser, Product, Order, OrderItem
│   ├── Enums/                       – OrderStatus
│   └── Exceptions/                  – DomainException
│
├── SecureShop.Infrastructure/
│   ├── Data/
│   │   ├── AppDbContext.cs          – EF Core context (Identity + DataProtection + store tables)
│   │   └── AppDbContextFactory.cs   – Design-time factory for EF migrations
│   ├── Repositories/
│   │   ├── ProductRepository.cs     – EF Core IProductRepository implementation
│   │   └── OrderRepository.cs       – EF Core IOrderRepository implementation
│   ├── Services/
│   │   ├── AuthService.cs           – Identity + JWT token generation
│   │   ├── CacheService.cs          – Redis-backed ICacheService (resilient to failures)
│   │   ├── NullCacheService.cs      – No-op fallback when Redis is unavailable
│   │   ├── EmailService.cs          – SMTP email sender
│   │   └── PaymentService.cs        – Test-mode Stripe Payment Intents
│   ├── DependencyInjection.cs       – Infrastructure DI registration
│   └── Migrations/                  – EF Core migration history
│
├── SecureShop.UnitTests/            – 354 xUnit unit tests
└── SecureShop.IntegrationTests/     – 6 HTTP integration tests
```

---

## Features

### Customer storefront
- Product catalogue with category filtering and search (paginated, Redis-cached)
- Shopping cart (session-based, client-side)
- Test-mode Stripe Payment Element checkout; only a valid signed webhook matching the PaymentIntent, order amount, and currency marks an order paid
- Server-calculated order totals; no tax is currently collected
- Account registration and login (email/password + Google OAuth)
- Password reset via OTP email
- Order history page
- Checkout collects contact and shipping details, but those details are not currently saved with the order

### Admin panel (`/admin`)
- **Dashboard** — revenue, order, customer, and product statistics (AJAX from API)
- **Products** — create, edit, soft-delete; image URL support
- **Orders** — list and view orders; advance paid orders through shipping and delivery
- **Customers** — browse registered users
- **Reports** — sales summary and charts
- **Profile** (`/admin/profile`) — view authenticated admin name, email, and role
- **Settings** (`/admin/settings`) — store info, order config, security, notifications, system info tabs

### REST API
- JWT Bearer authentication (8-hour token, HMAC-SHA256)
- Role-based authorization (`Admin`, `Customer`)
- Rate limiting (100 req/min on controller endpoints, 10 req/min on auth endpoints; partitioned by connection IP)
- Swagger/OpenAPI documentation, available in Development only

---

## API reference

Base URL: `https://secureshop-production-2eac.up.railway.app`

### Authentication

#### `POST /api/auth/register`
Creates a new `Customer` account and returns a signed JWT.

```json
{
  "firstName": "Jane",
  "lastName": "Doe",
  "email": "jane@example.com",
  "password": "SecurePass1!"
}
```

#### `POST /api/auth/login`
Authenticates with email and password; returns a signed JWT.

```json
{ "email": "jane@example.com", "password": "SecurePass1!" }
```

Response:
```json
{
  "token": "<JWT>",
  "email": "jane@example.com",
  "firstName": "Jane",
  "lastName": "Doe",
  "expiresAt": "2026-05-15T20:00:00Z"
}
```

Pass the token as `Authorization: Bearer <token>` on all authenticated endpoints.

#### `POST /api/auth/google`
Exchanges a Google ID token for a local JWT. Creates the account automatically on first sign-in.

```json
{ "idToken": "<Google ID token>" }
```

---

### Products

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| `GET` | `/api/products` | None | List products (paginated, filterable) |
| `GET` | `/api/products/{id}` | None | Get single product |
| `POST` | `/api/products` | Admin | Create product |
| `PUT` | `/api/products/{id}` | Admin | Update product |
| `DELETE` | `/api/products/{id}` | Admin | Soft-delete product |

Query parameters for `GET /api/products`:

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `category` | string | — | Filter by category name |
| `search` | string | — | Search name and description |
| `page` | int | 1 | Page number (1-based) |
| `pageSize` | int | 10 | Items per page |

---

### Orders

All order endpoints require a valid JWT (`Authorization: Bearer <token>`).

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| `POST` | `/api/orders` | Customer JWT | Place a new order |
| `GET` | `/api/orders/my` | Customer JWT | Get your orders |
| `GET` | `/api/orders/{id}` | Customer JWT or Admin | Get order by ID |
| `GET` | `/api/orders` | Admin | Get all orders |
| `PUT` | `/api/orders/{id}/status` | Admin | Advance an order through allowed fulfillment statuses |
| `POST` | `/api/webhooks/stripe` | Stripe signature | Verify Stripe events; only a matching signed success event marks an order paid |

Create order request:
```json
{
  "items": [
    { "productId": "<guid>", "quantity": 2 }
  ]
}
```

For a successful `POST /api/orders`, `stripePaymentIntentId` is the Stripe PaymentIntent ID. `paymentClientSecret` is returned for the checkout page to confirm the payment with Stripe.js; it is not stored as the order's payment ID. The order remains unpaid until the signed webhook confirms it. Stripe.net 50.4.1 expects webhook API version `2026-02-25.clover`.

---

## Admin panel

The admin panel is accessible at `/admin`. It uses a separate `AdminCookie` authentication scheme independent from the customer JWT flow.

| Page | Route | Description |
|------|-------|-------------|
| Dashboard | `/admin` | KPI cards and recent activity |
| Products | `/admin/products` | Product management (CRUD) |
| Orders | `/admin/orders` | Order list and status management |
| Customers | `/admin/customers` | Registered customer list |
| Reports | `/admin/reports` | Sales charts and summaries |
| Profile | `/admin/profile` | Current admin's account info |
| Settings | `/admin/settings` | Store configuration (4 tabs) |
| Logout | `/admin/logout` | Signs out the AdminCookie session |

Admin login is at `/admin/login`. Only users with the `Admin` role can log in. New installations can optionally create the first administrator using `BootstrapAdmin__Email` and `BootstrapAdmin__Password`; set both together, then remove both after creation. There is no source-coded default admin password.

---

## Running locally

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- PostgreSQL (or a [Neon](https://neon.tech) free-tier database)
- Redis — **optional**. Without it the app uses an in-memory no-op cache (data always comes from the DB; performance is lower but everything works).

### 1. Clone and restore

```bash
git clone https://github.com/souravdas090300/SecureShop.git
cd SecureShop
dotnet restore
```

### 2. Configure secrets

Create `SecureShop.API/appsettings.Development.json`:

```json
{
  "ConnectionStrings": {
    "Redis": "",
    "DefaultConnection": "Host=<host>;Database=<db>;Username=<user>;Password=<pass>;SSL Mode=Require"
  },
  "Jwt": {
    "Secret": "<random-string-min-32-chars>",
    "Issuer": "SecureShop",
    "Audience": "SecureShopUsers"
  },
  "Stripe": {
    "SecretKey": "sk_test_...",
    "PublishableKey": "pk_test_...",
    "WebhookSecret": "whsec_..."
  },
  "GoogleAuth": {
    "ClientId": "<Google OAuth client ID>"
  },
  "AllowedOrigins": "http://localhost:8080"
}
```

> Leave `Redis` blank to run without caching; if enabling it, use a connection-string format accepted by `StackExchange.Redis.ConfigurationOptions.Parse`.
> Omit `GoogleAuth` to disable Google Sign-In.  
> Omit `Email` credentials to disable password-reset emails (a warning is logged instead).
> **Email caveat:** email settings are omitted because the current SMTP implementation has a TLS-mode mismatch. .NET's `SmtpClient` supports STARTTLS, not implicit TLS on port 465, while this implementation disables TLS on 587. Fix and verify the transport before relying on email flows.

### 3. Apply migrations

```bash
dotnet ef database update --project SecureShop.Infrastructure --startup-project SecureShop.API
```

### 4. Run

```bash
dotnet run --project SecureShop.API
```

Visit `http://localhost:<port>/swagger` for the interactive API explorer.

---

## Deploying to Railway

Review the [pre-deployment blockers](./docs/pre-deployment-blockers.md) before exposing the app publicly, and consult the [configuration inventory](./.github/modernize/assessment/engines/facts/configuration-inventory.md) for settings. Configure Stripe using test credentials only; never use live payment keys with the current implementation.

Railway watches the `main` branch and deploys automatically on every push using the `Dockerfile` in the repo root:

```bash
git push origin main
```

EF Core migrations run automatically in a background task 2 seconds after Kestrel starts accepting requests. `/health` is liveness-only and may pass before migrations finish; verify startup logs and database operations.

---

## Environment variables

Set these in the Railway service dashboard (use `__` as the section separator):

| Variable | Required | Description |
|----------|----------|-------------|
| `ConnectionStrings__DefaultConnection` | Yes | Neon PostgreSQL connection string |
| `ConnectionStrings__Redis` | No | StackExchange.Redis connection string, e.g. `host:port,password=...,ssl=True`; omit to disable caching |
| `Jwt__Secret` | Yes | HMAC-SHA256 signing key, minimum 32 characters |
| `Jwt__Issuer` | Yes | `SecureShop` |
| `Jwt__Audience` | Yes | `SecureShopUsers` |
| `BootstrapAdmin__Email` | No | Optional one-time initial admin setup; set together with password, then remove after creation |
| `BootstrapAdmin__Password` | No | Optional one-time secret for initial admin; use a unique strong password and remove after creation |
| `Stripe__SecretKey` | For test payments | `sk_test_...` only; current code rejects live secret keys |
| `Stripe__PublishableKey` | For test checkout | `pk_test_...` only |
| `Stripe__WebhookSecret` | For payment confirmation | Test webhook signing secret (`whsec_...`) |
| `GoogleAuth__ClientId` | No | Google OAuth 2.0 client ID — omit to disable Google Sign-In |
| `Email__SmtpHost` | No | SMTP host (e.g. `smtp.gmail.com`) |
| `Email__SmtpPort` | No | SMTP port; current code's TLS handling must be corrected and tested before relying on email |
| `Email__SmtpUser` | No | SMTP username / sender email |
| `Email__SmtpPassword` | No | SMTP password or app-specific password |
| `Email__FromName` | No | Display name for sent emails (default: `SecureShop`) |
| `AllowedOrigins` | No | Comma-separated CORS origins |
| `ASPNETCORE_ENVIRONMENT` | Yes | `Production` |
| `PORT` | Auto | Set by Railway — Kestrel binds to this port |

---

## Known limitations and pre-deployment blockers

- **Payments are test-mode only.** Checkout uses Stripe Payment Element. A signed `payment_intent.succeeded` event must match the order's PaymentIntent ID, amount, and currency before the order becomes paid. No live payments are authorized.
- **Payment lifecycle is incomplete.** Refunds, abandoned-checkout cancellation and stock release, and complete failed-payment handling are not implemented or verified. Checkout currently collects no tax; a production tax policy remains to be decided and implemented.
- **Checkout details are not persisted.** Contact and shipping fields are shown in the front end but are not included in the order request or saved with the order.
- **Email delivery needs a TLS fix and provider testing** before password-reset email can be relied on.
- **`/health` is liveness-only** and does not check PostgreSQL or migration readiness.
- **Runtime support needs attention.** This project targets .NET 8; the blocker review records its support end date as November 10, 2026. Upgrade and validate against a supported runtime.
- **External and deployment validation remains open.** The automated suite does not establish live Stripe behavior, provider integrations, production database readiness, or an HTTPS deployment smoke test.

The latest Release build passed with `/warnaserror` and 0 warnings/errors; **360 tests passed** (354 unit, 6 HTTP integration). Stripe signature and order-confirmation tests are local unit tests, not a live Stripe transaction. Production approval requires resolving the items above and validating the target environment; see [docs/pre-deployment-blockers.md](./docs/pre-deployment-blockers.md).

---

## Security

| Control | Detail |
|---------|--------|
| Password policy | Uppercase + digit + special char + min 8 characters |
| Account lockout | 5 failed attempts → 15-minute lockout |
| Rate limiting | 10 req/min on auth endpoints; 100 req/min on controller endpoints, partitioned by connection IP. Configure trusted forwarded headers before relying on per-client limits behind a reverse proxy. |
| CSRF protection | Razor Page forms and cookie-authenticated admin API mutations validate antiforgery tokens; logout is POST-only |
| Security headers | `X-Content-Type-Options`, `Referrer-Policy`, `Permissions-Policy`, and a CSP with Stripe Payment Element origins; script/style inline allowances remain and need a nonce-based hardening pass. |
| Clickjacking | `X-Frame-Options: DENY` in production; `SAMEORIGIN` in development |
| Auth cookies | `HttpOnly`, `SameSite=Lax`; `Secure=Always` outside Development and `SameAsRequest` in Development |
| JWT | HMAC-SHA256, 8-hour expiry, zero clock skew |
| Data Protection keys | Stored in PostgreSQL via EF Core — survive process restarts and multi-instance deployments |
| HTTPS | Terminated at Railway's load balancer; `UseHttpsRedirection` is intentionally disabled inside the container to avoid redirect loops |
| Google OAuth | ID tokens validated server-side with the Google API; unverified Google email claims are rejected; app JWT is exchanged by POST rather than placed in a URL |
| SQL injection | Prevented entirely by EF Core parameterised queries |
| Roles | `Admin` — full catalogue and order management; `Customer` — place and view own orders |

---

## Testing

```bash
dotnet test
```

**360 test cases pass** — 354 unit tests and 6 HTTP integration tests. The integration tests verify `/health`, production authentication-cookie settings, production Swagger unavailability, Stripe CSP directives, invalid product pagination, and that authentication debug pages are unavailable in Production. The Release build passes with `/warnaserror`, and the current transitive NuGet vulnerability scan reports no vulnerable packages.

| Project | Count | Coverage areas |
|---------|-------|----------------|
| `SecureShop.UnitTests` | 354 | Domain entities, account lockout, pagination bounds, Stripe payment/webhook behavior, controllers, middleware, antiforgery handling, validators, and Razor Page models |
| `SecureShop.IntegrationTests` | 4 | `/health`, production authentication-cookie settings, Swagger unavailability, CSP directives, and pagination response, using `WebApplicationFactory` |

---

## Code documentation

All public classes, interfaces, methods, and properties carry XML documentation comments (`/// <summary>`). This includes:

- **Domain** — `Product`, `Order`, `OrderItem`, `ApplicationUser`, `OrderStatus`, `DomainException`
- **Application** — `IAuthService`, `IProductRepository`, `IOrderRepository`, `ICacheService`, `IEmailService`, `IPaymentService`, `ProductService`, `OrderService`, all DTOs, `RegisterValidator`, `CreateProductValidator`
- **Infrastructure** — `AppDbContext`, `AppDbContextFactory`, `AuthService`, `CacheService`, `NullCacheService`, `EmailService`, `PaymentService`, `ProductRepository`, `OrderRepository`, `DependencyInjection`
- **API** — `AuthController`, `ProductsController`, `OrdersController`, `AdminPageModel`, all admin and account page models, `GlobalExceptionMiddleware`, `SecurityHeadersMiddleware`, `CacheBustingMiddleware`, `JwtCookieHelper`

XML doc comments are compatible with tools like DocFX and Swagger/OpenAPI enrichment.
