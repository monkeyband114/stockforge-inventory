# Deployment, configuration and local development

## Docker Compose

```bash
cp .env.example .env        # change every secret
docker compose up --build
```

| URL | Service |
|---|---|
| http://localhost:3000 | Web app |
| http://localhost:8080/swagger | API via the nginx gateway (Swagger when `OPENAPI_ENABLED=true`) |
| http://localhost:8025 | Mailpit (captured emails: invitations, password resets, alerts) |

Services and start order:

1. `postgres` (18) and `redis` (8) start with health checks.
2. `migrator` (API image, `--migrate`) applies migrations as the owner, provisions the `stockforge_app` login,
   syncs roles/permissions, creates the super-admin and, if `SEED_DEMO_DATA=true`, the demo organizations.
   It then exits.
3. `api` and `worker` start after the migrator **completed successfully**. Both connect as `stockforge_app`.
4. `gateway` (nginx) load-balances the API replicas and supports WebSockets. `web` serves the Next.js app.

### Horizontal scaling

```bash
docker compose up --build --scale api=3 --scale worker=2
```

Nothing is kept in process memory that another instance would need:

| Concern | Shared through |
|---|---|
| Sessions, refresh tokens, lockout | PostgreSQL |
| Membership/permission snapshot cache | Redis (`IDistributedCache`) |
| SignalR fan-out | Redis backplane (`ConnectionStrings:Redis`), channel prefix `stockforge` |
| Data Protection keys (reset/invitation tokens, encrypted job payloads) | PostgreSQL `data_protection_keys` |
| Files (product images) | PostgreSQL (swap `IFileStorage` for S3 in large deployments) |
| Background jobs | PostgreSQL outbox, claimed with `FOR UPDATE SKIP LOCKED` |
| Scheduled jobs | `scheduled_job_runs` unique (job, period) claim: each runs once per period |
| Migrations | Separate one-shot migrator |

The gateway uses consistent hashing on the client IP, so a SignalR negotiate→connect handshake lands on one
instance. An alternative is to configure clients with `skipNegotiation` and WebSockets only. Rate limits are
enforced per instance (account lockout is global, in the database).

## Environment variables

ASP.NET Core maps `__` to `:` (for example `Jwt__SigningKey` → `Jwt:SigningKey`).

| Variable | Service | Required | Description |
|---|---|---|---|
| `ConnectionStrings__Default` | api, worker | ✔ | Runtime role connection (`Username=stockforge_app`) |
| `ConnectionStrings__Migrations` | migrator | ✔ | Owner connection used only for migrations |
| `ConnectionStrings__Redis` | api, worker | for >1 instance | Redis for cache and SignalR backplane; empty = in-memory (single instance only) |
| `Jwt__SigningKey` | api | ✔ | ≥ 32-byte random secret for HS256 |
| `Jwt__Issuer`, `Jwt__Audience` | api | | Defaults `stockforge`, `stockforge-api` |
| `Jwt__AccessTokenMinutes` / `RefreshTokenDays` / `SessionMaxDays` / `RefreshReuseGraceSeconds` | api | | 15 / 14 / 30 / 15 |
| `Database__MigrateOnStartup` | api | | `true` only for single-instance development |
| `Database__SeedOnStartup` | api | | Sync roles/super-admin on start (default `true`; the compose API sets `false`) |
| `Database__AppRolePassword` | migrator | | Grants LOGIN with this password to `stockforge_app` |
| `Seed__SuperAdminEmail`, `Seed__SuperAdminPassword` | migrator | | Platform administrator created on first start |
| `Seed__DemoData`, `Seed__DemoPassword` | migrator | | Demo organizations, users and 30 days of history |
| `App__FrontendBaseUrl` | api, worker | ✔ | Base URL used in email links |
| `Cors__AllowedOrigins__0..n` | api | ✔ | Browser origins allowed with credentials |
| `ForwardedHeaders__Enabled` | api | | Trust `X-Forwarded-*` (only behind your proxy) |
| `Https__Redirect` | api | | HSTS + HTTPS redirect outside Development (default `true`; `false` when TLS ends at the proxy) |
| `OpenApi__Enabled` | api | | Expose `/openapi/v1.json` and `/swagger` outside Development |
| `RateLimiting__AuthPermitsPerMinute` / `RefreshPermitsPerMinute` / `GlobalPermitsPerMinute` | api | | 10 / 60 / 600 |
| `Smtp__Host`, `Smtp__Port`, `Smtp__UseStartTls`, `Smtp__Username`, `Smtp__Password`, `Smtp__FromAddress`, `Smtp__FromName` | worker (api for none) | ✔ | Outgoing email |
| `Outbox__BatchSize`, `Outbox__PollInterval`, `Outbox__LockDuration` | worker | | 20 / 00:00:02 / 00:05:00 (jobs: emails, low-stock alerts, daily summaries, invoice PDFs, invoice/order/shipment emails) |
| `Scheduler__DailySummaryHourUtc` | worker | | Hour after which daily summaries are sent (default 6) |
| `Auth__AllowInsecureRefreshCookie` | api | | Only for plain-HTTP test environments on hosts other than localhost: allows the refresh cookie without `Secure` (never in production) |
| `Serilog__MinimumLevel__Default` | all | | Log level (logs are compact JSON on stdout) |
| `NEXT_PUBLIC_API_URL` | web (build arg) | ✔ | Browser-facing API URL, baked in at build time |

## Local development without Docker

Prerequisites: .NET SDK 11.0.100+, Node 22, PostgreSQL 18 on `localhost:5432` (user `postgres` / password
`postgres`). Redis and SMTP are optional locally.

```bash
createdb -h localhost -U postgres stockforge

# API: Development settings migrate, provision stockforge_app (password stockforge_app_dev) and seed demo data
dotnet run --project src/StockForge.API --urls http://localhost:8080      # http://localhost:8080/swagger

# Worker (optional: emails, alerts, scheduled jobs)
dotnet run --project src/StockForge.Worker

# Web
cd web && cp .env.example .env.local && npm install && npm run dev       # http://localhost:3000
```

Demo accounts (password `StockForge!2026`):

| Email | Organization | Role / scope |
|---|---|---|
| admin@acme.test | Acme Distribution | OrganizationAdmin |
| inventory@acme.test | Acme Distribution **and** Globex Retail | InventoryManager / Auditor (organization switching) |
| whmanager@acme.test | Acme Distribution | WarehouseManager, CDC |
| staff@acme.test | Acme Distribution | WarehouseStaff, CDC only |
| staff.north@acme.test | Acme Distribution | WarehouseStaff, NHB only |
| sales@acme.test | Acme Distribution | SalesOfficer: customers, create/submit orders |
| finance@acme.test | Acme Distribution | FinanceManager: approve orders, payments, finance summary |
| procurement@, auditor@acme.test | Acme Distribution | matching role |
| admin@globex.test, staff@globex.test | Globex Retail | OrganizationAdmin / WarehouseStaff (BER) |
| superadmin@stockforge.local (`SuperAdmin!2026`) | platform | SuperAdmin |

## Tests

```bash
# Uses Testcontainers (PostgreSQL 18) when Docker is available…
dotnet test --project tests/StockForge.Tests
# …or an existing server: a throw-away database is created and dropped.
STOCKFORGE_TEST_POSTGRES="Host=localhost;Username=postgres;Password=postgres;Database=postgres" dotnet test --project tests/StockForge.Tests

python scripts/smoke_test.py http://localhost:8080   # end-to-end smoke test against a seeded instance
```

## Production checklist

* [ ] Generate unique secrets: `POSTGRES_PASSWORD`, `APP_DB_PASSWORD`, `JWT_SIGNING_KEY` (≥ 32 bytes), `SUPERADMIN_PASSWORD`. Keep them in your secret store, not in `.env` files committed anywhere.
* [ ] `SEED_DEMO_DATA=false`; sign in as the super-admin, change its password, create organizations.
* [ ] TLS at the ingress; `Https__Redirect=false` only if TLS terminates in front of the API; `ForwardedHeaders__Enabled=true` only behind your own proxy.
* [ ] `Cors__AllowedOrigins__0` set to the real web origin; `App__FrontendBaseUrl` set to the same.
* [ ] Encrypt Data Protection keys at rest (`ProtectKeysWithCertificate` or a cloud KMS); today they are stored unprotected in `data_protection_keys`, which is acceptable only when the database itself is encrypted and access-controlled.
* [ ] Redis configured (required for more than one API instance); Redis/Valkey with persistence disabled is fine (cache + pub/sub only).
* [ ] Real SMTP credentials; SPF/DKIM for the sender domain.
* [ ] PostgreSQL backups (PITR) and monitoring; the migrator is the only component with DDL rights.
* [ ] `OpenApi__Enabled=false` unless the API documentation should be public.
* [ ] Invoice PDFs use QuestPDF under its Community licence (free below USD 1M annual revenue); larger organizations need a commercial licence.
* [ ] Run at least one worker: invoice PDFs, emails, low-stock alerts, overdue marking and reservation expiry depend on it. Its realtime events (low stock, shipments) need the Redis backplane.
* [ ] Ship logs (compact JSON on stdout) to your log platform; alert on `Job … failed permanently` and on `/health/ready` failures.
