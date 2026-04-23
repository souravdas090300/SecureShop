# SecureShop

A secure, production-ready e-commerce REST API built with **ASP.NET Core 8**, deployed on **Railway**.

---

## Live deployment

| Endpoint | Description |
|---|---|
| `GET /` | Service status |
| `GET /health` | Health check |
| `POST /api/auth/register` | Register a new user |
| `POST /api/auth/login` | Login and receive JWT |
| `GET /api/products` | Browse products (public) |
| `GET /api/orders/my` | Get your orders (auth required) |

Base URL: `https://secureshop-production.up.railway.app`

Swagger UI is available at `/swagger` in **Development** only. Use the bundled [swagger.json](swagger.json) for local exploration.

---

## Architecture

```
SecureShop.sln
├── SecureShop.API           – Controllers, middleware, startup
├── SecureShop.Application   – Services, DTOs, interfaces, validators
├── SecureShop.Domain        – Entities, enums, domain exceptions
├── SecureShop.Infrastructure – EF Core, Identity, Redis, Stripe, Auth
└── SecureShop.UnitTests     – xUnit unit tests
```

**Key patterns:** Clean Architecture · Repository pattern · JWT Bearer authentication · Role-based authorization (`Admin` / `Customer`) · FluentValidation · Serilog structured logging

---

## Tech stack

| Concern | Technology |
|---|---|
| Framework | ASP.NET Core 8 |
| Database | PostgreSQL via [Neon](https://neon.tech) (EF Core + Npgsql) |
| Cache | Redis via [Upstash](https://upstash.com) (StackExchange.Redis) |
| Auth | ASP.NET Core Identity + JWT Bearer |
| Payments | Stripe |
| Logging | Serilog → Console (Railway collects stdout) |
| Deployment | [Railway](https://railway.app) |

---

## API reference

### Auth — `POST /api/auth/register`

```json
{
  "email": "user@example.com",
  "password": "SecurePass1!",
  "firstName": "Jane",
  "lastName": "Doe"
}
```

### Auth — `POST /api/auth/login`

```json
{ "email": "user@example.com", "password": "SecurePass1!" }
```

Response includes a `token` field. Pass it as `Authorization: Bearer <token>` on protected routes.

### Products — `GET /api/products`

Query params: `category` (string), `page` (int, default 1), `pageSize` (int, default 10)

Admin-only mutations: `POST /api/products`, `PUT /api/products/{id}`, `DELETE /api/products/{id}`

### Orders (requires auth)

| Method | Route | Description |
|---|---|---|
| `POST` | `/api/orders` | Create an order |
| `GET` | `/api/orders/{id}` | Get order by ID |
| `GET` | `/api/orders/my` | Get all your orders |

---

## Running locally

### Prerequisites

- .NET 8 SDK
- PostgreSQL instance (or a Neon connection string)
- Redis instance — **optional**. If the `Redis` connection string is absent or unreachable, the app automatically falls back to an in-process no-op cache (all reads are cache misses; data always comes from the database). Performance will be lower but the app functions correctly.

### 1. Clone and restore

```bash
git clone <repo-url>
cd SecureShop
dotnet restore
```

### 2. Configure secrets

Copy `appsettings.Development.json` and fill in your values:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=...;Database=...;Username=...;Password=...;SSL Mode=Require",
    "Redis": "rediss://default:<password>@<host>:6379"
  },
  "Jwt": {
    "Secret": "<min-32-char-secret>",
    "Issuer": "SecureShop",
    "Audience": "SecureShopUsers"
  },
  "Stripe": {
    "SecretKey": "sk_test_..."
  },
  "AllowedOrigins": "http://localhost:3000"
}
```

> Omit the `Redis` key entirely to run without Redis.

### 3. Run

```bash
dotnet run --project SecureShop.API
```

Browse to `http://localhost:<port>/swagger` for interactive docs.

---

## Deploying to Railway

1. Push to your connected GitHub repository. Railway auto-deploys on push using the `Dockerfile` in the repo root.
2. Set the following environment variables in the Railway service dashboard:

| Variable | Description |
|---|---|
| `ConnectionStrings__DefaultConnection` | Neon PostgreSQL connection string |
| `ConnectionStrings__Redis` | Upstash Redis TLS URL (optional — omit to disable caching) |
| `Jwt__Secret` | Random secret, min 32 characters |
| `Jwt__Issuer` | `SecureShop` |
| `Jwt__Audience` | `SecureShopUsers` |
| `Stripe__SecretKey` | Stripe secret key |
| `AllowedOrigins` | Comma-separated list of allowed CORS origins |
| `ASPNETCORE_ENVIRONMENT` | `Production` |

EF Core migrations run **in a background task that starts 2 seconds after Kestrel begins serving**, so Railway's health check at `/health` passes immediately on startup. Migration failures are logged but do not crash the process.

---

## Security notes

- Passwords require digit, uppercase, non-alphanumeric, and min length 8.
- Account lockout triggers after 5 failed login attempts (15-minute lockout).
- Rate limiting: 100 req/min on API routes, 10 req/min on auth routes.
- Security headers (CSP, X-Frame-Options, etc.) applied via `SecurityHeadersMiddleware`.
- HTTPS is terminated at Railway's load balancer. `UseHttpsRedirection` and `UseHsts` are intentionally **not** called inside the container — doing so causes redirect loops behind a TLS-terminating proxy.
- JWT clock skew is set to zero (`ClockSkew = TimeSpan.Zero`).
- Two roles exist: `Admin` (full product/order management) and `Customer` (place and view own orders).

---

## Running tests

```bash
dotnet test
```
