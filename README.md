# SecureShop

A secure, production-ready e-commerce platform built with **ASP.NET Core 8**, following Clean Architecture principles and deployed on **Railway**.

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
11. [Testing](#testing)
12. [Code documentation](#code-documentation)

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
| Payments | Stripe (Payment Intents API) |
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
│   │   └── OrdersController.cs      – Order placement and retrieval
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
│   │   └── OrderService.cs          – Order creation, stock reduction, Stripe integration
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
│   │   └── PaymentService.cs        – Stripe Payment Intents integration
│   ├── DependencyInjection.cs       – Infrastructure DI registration
│   └── Migrations/                  – EF Core migration history
│
├── SecureShop.UnitTests/            – 318 xUnit unit tests
└── SecureShop.IntegrationTests/     – 1 integration test (health check)
```

---

## Features

### Customer storefront
- Product catalogue with category filtering and search (paginated, Redis-cached)
- Shopping cart (session-based, client-side)
- Checkout with Stripe payment intent
- Account registration and login (email/password + Google OAuth)
- Password reset via OTP email
- Order history page

### Admin panel (`/admin`)
- **Dashboard** — revenue, order, customer, and product statistics (AJAX from API)
- **Products** — create, edit, soft-delete; image URL support
- **Orders** — list all orders, view details, update order status
- **Customers** — browse registered users
- **Reports** — sales summary and charts
- **Profile** (`/admin/profile`) — view authenticated admin name, email, and role
- **Settings** (`/admin/settings`) — store info, order config, security, notifications, system info tabs

### REST API
- JWT Bearer authentication (8-hour token, HMAC-SHA256)
- Role-based authorization (`Admin`, `Customer`)
- Rate limiting (100 req/min general, 10 req/min auth endpoints)
- Full Swagger/OpenAPI documentation (dev only; `swagger.json` bundled for production reference)

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
| `PUT` | `/api/orders/{id}/status` | Admin | Update order status |

Create order request:
```json
{
  "items": [
    { "productId": "<guid>", "quantity": 2 }
  ]
}
```

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

Admin login is at `/admin/login`. Only users with the `Admin` role can log in. To grant admin access to an account, use the `/account/makeadmin` utility page (accessible only when logged in as an existing admin or via the seeder).

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
    "DefaultConnection": "Host=<host>;Database=<db>;Username=<user>;Password=<pass>;SSL Mode=Require",
    "Redis": "rediss://default:<password>@<host>:6379"
  },
  "Jwt": {
    "Secret": "<random-string-min-32-chars>",
    "Issuer": "SecureShop",
    "Audience": "SecureShopUsers"
  },
  "Stripe": {
    "SecretKey": "sk_test_..."
  },
  "GoogleAuth": {
    "ClientId": "<Google OAuth client ID>"
  },
  "Email": {
    "SmtpHost": "smtp.gmail.com",
    "SmtpPort": "465",
    "SmtpUser": "you@gmail.com",
    "SmtpPassword": "<app-password>",
    "FromName": "SecureShop"
  },
  "AllowedOrigins": "http://localhost:5000"
}
```

> Omit `Redis` entirely to run without caching.  
> Omit `GoogleAuth` to disable Google Sign-In.  
> Omit `Email` credentials to disable password-reset emails (a warning is logged instead).

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

Railway watches the `main` branch and deploys automatically on every push using the `Dockerfile` in the repo root:

```bash
git push origin main
```

EF Core migrations run automatically in a background task 2 seconds after Kestrel starts accepting requests — Railway's health check at `/health` passes immediately.

---

## Environment variables

Set these in the Railway service dashboard (use `__` as the section separator):

| Variable | Required | Description |
|----------|----------|-------------|
| `ConnectionStrings__DefaultConnection` | Yes | Neon PostgreSQL connection string |
| `ConnectionStrings__Redis` | No | Upstash Redis TLS URL — omit to disable caching |
| `Jwt__Secret` | Yes | HMAC-SHA256 signing key, minimum 32 characters |
| `Jwt__Issuer` | Yes | `SecureShop` |
| `Jwt__Audience` | Yes | `SecureShopUsers` |
| `Stripe__SecretKey` | Yes | Stripe secret key (`sk_live_…` or `sk_test_…`) |
| `GoogleAuth__ClientId` | No | Google OAuth 2.0 client ID — omit to disable Google Sign-In |
| `Email__SmtpHost` | No | SMTP host (e.g. `smtp.gmail.com`) |
| `Email__SmtpPort` | No | SMTP port — `465` for implicit SSL (recommended on Railway) |
| `Email__SmtpUser` | No | SMTP username / sender email |
| `Email__SmtpPassword` | No | SMTP password or app-specific password |
| `Email__FromName` | No | Display name for sent emails (default: `SecureShop`) |
| `AllowedOrigins` | No | Comma-separated CORS origins |
| `ASPNETCORE_ENVIRONMENT` | Yes | `Production` |
| `PORT` | Auto | Set by Railway — Kestrel binds to this port |

---

## Security

| Control | Detail |
|---------|--------|
| Password policy | Uppercase + digit + special char + min 8 characters |
| Account lockout | 5 failed attempts → 15-minute lockout |
| Rate limiting | 10 req/min on auth endpoints; 100 req/min on all others |
| CSRF protection | All state-changing Razor Page forms use antiforgery tokens; logout is POST-only |
| Security headers | `X-Content-Type-Options`, `X-XSS-Protection`, `Referrer-Policy`, `Permissions-Policy`, and a strict CSP applied by `SecurityHeadersMiddleware` |
| Clickjacking | `X-Frame-Options: DENY` in production; `SAMEORIGIN` in development |
| Auth cookies | `HttpOnly`, `Secure`, `SameSite=Strict` |
| JWT | HMAC-SHA256, 8-hour expiry, zero clock skew |
| Data Protection keys | Stored in PostgreSQL via EF Core — survive process restarts and multi-instance deployments |
| HTTPS | Terminated at Railway's load balancer; `UseHttpsRedirection` is intentionally disabled inside the container to avoid redirect loops |
| Google OAuth | ID tokens validated server-side with the Google API; not trusted client-side |
| SQL injection | Prevented entirely by EF Core parameterised queries |
| Roles | `Admin` — full catalogue and order management; `Customer` — place and view own orders |

---

## Testing

```bash
dotnet test
```

**319 tests** — 318 unit tests + 1 integration test — all pass, none skipped.

| Project | Count | Coverage areas |
|---------|-------|----------------|
| `SecureShop.UnitTests` | 318 | Domain entities, application services, all API page models, controllers, middleware, validators |
| `SecureShop.IntegrationTests` | 1 | `/health` endpoint end-to-end |

---

## Code documentation

All public classes, interfaces, methods, and properties carry XML documentation comments (`/// <summary>`). This includes:

- **Domain** — `Product`, `Order`, `OrderItem`, `ApplicationUser`, `OrderStatus`, `DomainException`
- **Application** — `IAuthService`, `IProductRepository`, `IOrderRepository`, `ICacheService`, `IEmailService`, `IPaymentService`, `ProductService`, `OrderService`, all DTOs, `RegisterValidator`, `CreateProductValidator`
- **Infrastructure** — `AppDbContext`, `AppDbContextFactory`, `AuthService`, `CacheService`, `NullCacheService`, `EmailService`, `PaymentService`, `ProductRepository`, `OrderRepository`, `DependencyInjection`
- **API** — `AuthController`, `ProductsController`, `OrdersController`, `AdminPageModel`, all admin and account page models, `GlobalExceptionMiddleware`, `SecurityHeadersMiddleware`, `CacheBustingMiddleware`, `JwtCookieHelper`

XML doc comments are compatible with tools like DocFX and Swagger/OpenAPI enrichment.
