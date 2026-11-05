# SecureShop Pre-Deployment Blockers

**Reviewed:** 2026-11-04
**Status:** Not approved for public production deployment.

Earlier project-history references to `AUDIT-REPORT.md` refer to a file not present in this workspace; this document supersedes it as the durable readiness gate. Local and Railway setup notes are in the [README](../README.md); settings are listed in the [configuration inventory](../.github/modernize/assessment/engines/facts/configuration-inventory.md). The inventory is tool-generated; if its path changes, update the links in `README.md`.

## Readiness gate

**Deployment approval requires the code blockers below to be resolved, external-service validation, and a deployment-environment smoke test. Code-layer build/test verification alone is not production approval.**

## Required before public production traffic

| Priority | Blocker | Evidence / risk | Exit criteria |
|---|---|---|---|
| Resolved in code; verify deployment | Initial administrator provisioning | Removed the source-coded default admin and credential logging. Optional bootstrap now requires both `BootstrapAdmin__Email` and `BootstrapAdmin__Password`, skips creation if any Admin-role user exists, and removes a newly created account if role assignment fails. Eight unit tests cover configuration, success, existing admin/account, and operation failures. | Verify provisioning in the target database; remove both environment values after successful creation; rotate/disable legacy default admin accounts created by prior versions. |
| Partially resolved in code; production remains blocked | Stripe payment lifecycle and tax policy | Checkout now uses Stripe Payment Element, accepts test-mode keys only, stores the PaymentIntent ID rather than the client secret, and marks orders paid only after a signed `payment_intent.succeeded` webhook whose intent, amount, and currency match. The webhook requires the API version expected by Stripe.net 50.4.1 (`2026-02-25.clover`). No-tax checkout is test-only. Refunds, customer cancellation/abandoned-order stock release, failed-payment lifecycle, actual Stripe test-mode acceptance, and a production tax policy remain unimplemented/unverified. | Configure and exercise Stripe CLI/test mode for success, failure, signature rejection, replay/idempotency, and order persistence. Implement cancellation/stock-release and refund handling; determine jurisdiction-appropriate tax treatment; obtain business acceptance before considering live keys. Never use live keys with the current code. |
| High for email features | SMTP TLS mode is mismatched | `EmailService` disables TLS for port 587 and sets `SmtpClient.EnableSsl` on other ports. .NET `SmtpClient.EnableSsl` negotiates STARTTLS and does not support implicit TLS generally used on port 465. Current settings therefore do not provide a reliable secure SMTP configuration. | Replace/configure the transport correctly (e.g. MailKit with explicit STARTTLS or implicit TLS support); validate TLS certificate handling and successful verification/reset delivery with a test sender. Disable affected email flows until then. |
| High for service verification | `/health` is liveness-only | The mapped health check has no database, migration, or provider checks. It may return HTTP 200 while PostgreSQL is unavailable or background migrations/seeding have failed. | Add appropriate readiness checks or keep platform health separate from readiness; ensure DB migration completion is observable and perform a DB-backed smoke test before traffic promotion. |
| High / time-sensitive | .NET 8 support ends 2026-11-10 | The API targets `net8.0`, and Docker uses .NET 8 SDK/runtime images. This review date is 2026-11-03, one week before end of support. | Prioritize a tested upgrade to a supported runtime (prefer .NET 10 LTS for a new long-lived deployment), update SDK/container/build configuration and dependencies, then run Release build and all tests. If deploying before upgrade, document an owner and immediate upgrade date. |

## Required before production sign-off

- Validate PostgreSQL connectivity, backups/restore, migrations, role/data seeding, and a persisted order flow in an isolated deployment-like environment.
- Validate the integrations that will be enabled: Redis (if configured), Google OAuth, SMTP after its fix, Stripe test-mode payment lifecycle, and Cloudinary upload restrictions (if configured).
- Run a deployment smoke test over HTTPS: health/readiness, storefront/catalogue, registration/login, admin authorization, and relevant enabled integrations.
- Swagger UI/JSON is disabled outside Development; keep it private in production unless a reviewed, authenticated docs endpoint is intentionally introduced.
- Confirm production cookies use `Secure=Always` and `SameSite=Lax`; verify behavior through the actual TLS-terminating proxy.
- Keep secrets in Railway's protected environment-variable store, separate test/live credentials, restrict who can view production secrets, and rotate any credentials that may have been exposed.
- For first-admin setup, deploy one app instance with both bootstrap values set, verify successful creation in logs, then remove both values and restart/redeploy.

## Verified code-layer status

Admin login uses Identity lockout checks and its protected `AdminCookie`; the admin UI no longer reads or sends a JavaScript-accessible admin JWT. Cookie-authenticated admin API mutations validate antiforgery tokens, and the sample-seed operation now uses POST.

The 2026-11-04 security pass added domain-level positive quantity/price validation, failed-login lockout recording, controller pagination caps, connection-IP-partitioned API limits, production-only Swagger disablement, Stripe Payment Element CSP origins, Google email verification, and POST-based Google JWT exchange with secure production cookies. Admin login now uses Identity lockout checks and its protected `AdminCookie`; the admin UI no longer reads or sends a JavaScript-accessible admin JWT. The deprecated `X-XSS-Protection` header was removed. CSP still permits inline scripts/styles, and trusted proxy forwarding is not configured; do not treat the CSP or per-client rate limits as fully hardened until those are addressed and verified.

The latest run passed all 354 unit tests and 6 HTTP integration tests. The Release build passed with `/warnaserror` (zero warnings), and `dotnet list package --vulnerable --include-transitive` reported no vulnerable packages. Stripe signature and order-confirmation behavior has local unit coverage, not a live Stripe integration test. These checks do not verify external services, production database readiness, live payment settlement, or deployment behavior. Re-run build and tests on the exact revision selected for release.
