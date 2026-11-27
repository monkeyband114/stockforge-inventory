# StockForge

Multi-tenant inventory, order management and fulfillment platform built with ASP.NET Core 11, EF Core,
PostgreSQL 18, SignalR and Next.js.

Many organizations share one deployment. Each one's data is isolated by four independent layers:
application scoping, EF Core query filters, composite tenant foreign keys and PostgreSQL row-level
security. Inventory cannot be oversold under concurrency: domain invariants, `xmin` optimistic
concurrency with transactional retries, and database `CHECK` constraints all guard it.

## What works today (phases 1 and 2)

* **Identity & tenancy:** organization onboarding, JWT access tokens plus rotating refresh tokens with
  reuse detection, sessions and revocation, password reset, invitations, lockout, organization switching,
  platform super-admin, granular permissions with warehouse scoping.
* **Catalog:** products (auto SKUs, EAN-13 barcodes, alternate units, images), categories, units of measure.
* **Inventory:** per-warehouse balances, an immutable movement ledger (stock in/out, damaged, returned,
  reservation, release, adjustment, transfer in/out), counts and corrections with approval, transfers
  (request → dispatch → receive), manual reservations with expiry, weighted-average valuation, low-stock
  alerts.
* **Platform:** immutable audit log, transactional outbox with retries/dead-lettering and failed-job
  monitoring, scheduled jobs (daily summaries, reservation expiry, cleanup), idempotency keys, real-time
  updates over SignalR (tenant- and warehouse-scoped), rate limiting, structured logging, health checks.
* **Orders & fulfillment:** customers (terms, credit limits), sales orders with line/order discounts and
  taxes, configurable approval rules (amount, bulk quantity, approve-all, credit limit) with segregation of
  duties, and transactional approval: reserve stock, issue the invoice and queue the PDF/emails all at once.
  Also picking/packing queue, partial shipments, delivery confirmation, returns with restock and credit,
  cancellation with reservation release, and required idempotency keys on create/submit/approve/ship/
  returns/payments.
* **Invoicing & finance:** gap-free invoice numbering, PDF invoices rendered by the worker (QuestPDF) and
  emailed to customers, payments, outstanding and overdue tracking (daily job), finance summary API, and
  order KPIs on the dashboard.
* **Web app:** dashboards, inventory operations, products, warehouses, users and roles, organization
  settings, audit, jobs, and platform administration, all wired to the API. Order, invoice and finance
  screens are being added.

* **Procurement & payables (API):** suppliers with negotiated terms, purchase orders with an approval
  threshold, goods receipts with partial deliveries that book stock at purchase cost, supplier invoices with
  a three-way match, supplier payments, and reorder suggestions.

Procurement screens and further finance analytics (phase 4) are planned in
[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md#9-implementation-roadmap).

## Quick start

```bash
cp .env.example .env
docker compose up --build
# Web http://localhost:3000 · API http://localhost:8080/swagger · Mail http://localhost:8025
```

Sign in as `admin@acme.test` / `StockForge!2026`. Other demo accounts, local development without Docker and
all configuration options are in [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md).

## Repository layout

```
src/StockForge.Domain          entities, invariants, domain events (no framework dependencies)
src/StockForge.Application     use cases, DTOs, validators, permissions, abstractions
src/StockForge.Infrastructure  EF Core + PostgreSQL, Identity/JWT, outbox, SignalR, email, seeding, migrations
src/StockForge.API             controllers, auth pipeline, problem details, rate limiting, OpenAPI
src/StockForge.Worker          outbox processor, job handlers, scheduler
tests/StockForge.Tests         unit + integration tests (real PostgreSQL)
web/                           Next.js frontend
docker/, docker-compose.yml    container build and reference deployment
docs/                          architecture and reference documentation
```

## Tests

```bash
dotnet test --project tests/StockForge.Tests     # Testcontainers PostgreSQL, or set STOCKFORGE_TEST_POSTGRES
```

The suite has 95 tests, covering among other things:

* Two tenants cannot access each other's data through the API, EF filters, foreign keys or RLS.
* Every tenant table has RLS.
* 20 simultaneous orders for limited stock never oversell.
* Concurrent order approvals competing for the same units never overcommit.
* A failed approval rolls back reservations, the invoice and the invoice number.
* Retried approvals never reserve or bill twice.
* Partial goods receipts book stock at purchase cost; supplier bills are three-way matched and never booked twice.
* Failed multi-line transfers roll back completely.
* Idempotent retries never double-book.
* SignalR clients only receive their own organization's events.
* Refresh-token reuse revokes the session.
* Failing background jobs back off and dead-letter.

## Documentation

| | |
|---|---|
| [Architecture](docs/ARCHITECTURE.md) | Requirements analysis, layers, design decisions, roadmap |
| [Multi-tenancy](docs/MULTI-TENANCY.md) | The four isolation layers; adding tenant tables safely |
| [Authentication](docs/AUTHENTICATION.md) | Token model, sign-in, refresh rotation, sessions, RBAC |
| [Inventory](docs/INVENTORY.md) | Ledger, concurrency guarantees, workflows |
| [Orders](docs/ORDERS.md) | Order lifecycle, transactional approval, fulfillment, invoicing |
| [Procurement](docs/PROCUREMENT.md) | Suppliers, purchase orders, goods receipts, payables |
| [Database](docs/DATABASE.md) | ERD, conventions, table reference, roles |
| [API](docs/API.md) | Conventions, errors, idempotency, endpoints, realtime events |
| [Deployment](docs/DEPLOYMENT.md) | Docker, scaling, environment variables, local setup, production checklist |
