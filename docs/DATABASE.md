# Database

PostgreSQL 18, accessed through EF Core 11 / Npgsql with snake_case naming. The schema is defined by the
migrations in `src/StockForge.Infrastructure/Persistence/Migrations`:

* `InitialSchema` is generated from the model.
* `SecurityPolicies` is hand-written: runtime role, grants, append-only triggers, RLS and platform functions.
* `SalesAndBilling` adds customers, orders, shipments, returns, invoices and payments, and ends with `stockforge_apply_tenant_rls()`.

## Entity-relationship diagram

```mermaid
erDiagram
    tenants ||--o{ tenant_memberships : ""
    users ||--o{ tenant_memberships : ""
    tenant_memberships ||--o{ warehouse_assignments : "scoped to"
    warehouses ||--o{ warehouse_assignments : ""
    users ||--o{ user_roles : "platform roles"
    roles ||--o{ user_roles : ""
    roles ||--o{ role_claims : "permissions"
    users ||--o{ user_sessions : ""
    user_sessions ||--o{ refresh_tokens : "rotates"

    tenants ||--o{ warehouses : ""
    tenants ||--o{ units_of_measure : ""
    tenants ||--o{ product_categories : ""
    product_categories ||--o{ product_categories : "parent"
    product_categories ||--o{ products : ""
    units_of_measure ||--o{ products : "base unit"
    products ||--o{ product_units : "alternate units"
    units_of_measure ||--o{ product_units : ""

    warehouses ||--o{ inventory_items : ""
    products ||--o{ inventory_items : ""
    inventory_items ||--o{ stock_movements : "ledger"
    warehouses ||--o{ stock_reservations : ""
    products ||--o{ stock_reservations : ""
    warehouses ||--o{ stock_adjustments : ""
    products ||--o{ stock_adjustments : ""
    warehouses ||--o{ stock_transfers : "source / destination"
    stock_transfers ||--|{ stock_transfer_lines : ""
    products ||--o{ stock_transfer_lines : ""

    tenants ||--o{ customers : ""
    customers ||--o{ sales_orders : "places"
    warehouses ||--o{ sales_orders : "fulfils"
    sales_orders ||--|{ sales_order_lines : ""
    products ||--o{ sales_order_lines : ""
    sales_orders ||--o{ shipments : ""
    shipments ||--|{ shipment_lines : ""
    sales_order_lines ||--o{ shipment_lines : ""
    sales_orders ||--o{ sales_returns : ""
    sales_returns ||--|{ sales_return_lines : ""
    sales_orders ||--o| invoices : "billed (unique)"
    customers ||--o{ invoices : ""
    invoices ||--|{ invoice_lines : ""
    invoices ||--o{ payments : ""

    tenants ||--o{ tenant_sequences : "numbering"
    tenants ||--o{ idempotency_records : ""
    tenants ||--o{ stored_files : ""
    stored_files ||--|| stored_file_contents : ""
    tenants ||--o{ audit_logs : ""
    tenants ||--o{ outbox_messages : ""

    tenants {
        uuid id PK
        varchar slug UK
        varchar status
        varchar base_currency
        varchar time_zone
        numeric settings_order_approval_amount
        xid xmin "row version"
    }
    inventory_items {
        uuid id PK
        uuid tenant_id FK
        uuid warehouse_id FK "(tenant_id, warehouse_id)"
        uuid product_id FK "(tenant_id, product_id)"
        numeric quantity_on_hand "CHECK >= 0"
        numeric quantity_reserved "CHECK 0..on_hand"
        numeric average_unit_cost
        xid xmin "concurrency token"
    }
    stock_movements {
        uuid id PK
        uuid tenant_id FK
        uuid inventory_item_id FK
        varchar type
        numeric quantity_delta
        numeric reserved_delta
        numeric on_hand_after
        numeric reserved_after
        numeric unit_cost
        varchar reference_type
        uuid reference_id
        timestamptz created_at
    }
```

Procurement tables (phase 3: suppliers, purchase orders, goods receipts) will follow the same conventions.

## Conventions

* **Keys.** UUIDv7 primary keys generated in the domain (`Guid.CreateVersion7()`), configured
  `ValueGeneratedNever`. Tenant principals also expose a unique `(tenant_id, id)` alternate key, and
  dependents reference it with composite FKs.
* **Indexes.** `tenant_id` leads every index on tenant-owned tables.
* **Types.** Money `numeric(18,2)`, costs `numeric(18,4)`, quantities `numeric(18,3)`, fractional tax rates
  `numeric(6,5)`. Enums are stored as strings, timestamps as `timestamptz` in UTC, and lists (roles,
  permissions, changed properties) as `text[]`.
* **Concurrency.** `xmin` is the row version on tenants, memberships, warehouses, products, inventory items,
  reservations, adjustments and transfers. Update DTOs carry `version` for optimistic UI concurrency.
* **Numbering.** `tenant_sequences (tenant_id, name, next_value)` is incremented with an atomic upsert inside
  the business transaction. Formats: `SO-000001`, `INV-000001`, `SHP-000001`, `RMA-000001`, `CUS-00001`, `TRF-000001`, `ADJ-000001`, SKU `{CATEGORY}-000001`, internal EAN-13
  barcodes in the GS1 in-store range (prefix 2).

## Table reference

| Table | Purpose | Key constraints |
|---|---|---|
| `tenants` | Organizations and their settings (approval thresholds, tax default, alerts) | unique `slug` |
| `users`, `roles`, `user_roles`, `role_claims`, `user_claims`, `user_logins`, `user_tokens` | ASP.NET Core Identity | unique normalized email |
| `tenant_memberships` | User ↔ organization, tenant roles (`text[]`), extra grants | unique `(tenant_id, user_id)` |
| `warehouse_assignments` | Warehouse scope of a membership | FK `(tenant_id, membership_id)`, `(tenant_id, warehouse_id)` |
| `user_sessions` | Signed-in devices; revocation | |
| `refresh_tokens` | SHA-256 of rotating refresh tokens, replacement chain | unique `token_hash` |
| `warehouses` | Locations (DC, warehouse, store, 3PL) | unique `(tenant_id, code)` |
| `units_of_measure` | EA, BOX, KG… | unique `(tenant_id, code)` |
| `product_categories` | Hierarchy; code doubles as the SKU prefix | unique `(tenant_id, code)`, `(tenant_id, name)` |
| `products` | Catalog, prices, tax, reorder thresholds, image | unique `(tenant_id, sku)`, partial unique `(tenant_id, barcode)`; price CHECKs |
| `product_units` | Alternate units and conversion factors | unique `(tenant_id, product_id, unit_of_measure_id)`; `factor > 0` |
| `inventory_items` | Balances | unique `(tenant_id, warehouse_id, product_id)`; non-negative CHECKs |
| `stock_movements` | Immutable ledger | append-only (privileges + trigger) |
| `stock_reservations` | Holds with partial consumption | `consumed + released <= quantity` |
| `stock_adjustments` | Counts and corrections, approval state | unique `(tenant_id, number)` |
| `stock_transfers`, `stock_transfer_lines` | Inter-warehouse transfers | unique number; source ≠ destination; one line per product |
| `customers` | Billing/shipping addresses, payment terms, credit limit | unique `(tenant_id, code)` |
| `sales_orders` | Orders, totals, approval and fulfillment timestamps | unique `(tenant_id, number)`, partial unique `(tenant_id, idempotency_key)`; totals ≥ 0 |
| `sales_order_lines` | Priced lines (snapshotted SKU/name) and pick/pack/ship/return progress | CHECK `returned ≤ shipped ≤ packed ≤ picked ≤ quantity` |
| `shipments`, `shipment_lines` | Consignments with carrier and tracking | unique number; FK to the order line |
| `sales_returns`, `sales_return_lines` | Customer returns (restock flag, credit) | unique number |
| `invoices`, `invoice_lines` | Invoices snapshotted from the order | partial unique `(tenant_id, sales_order_id)` (one per order); CHECK `paid + credited ≤ total`, `due ≥ issue` |
| `payments` | Payments against invoices | `amount > 0` |
| `tenant_sequences` | Gap-free document numbering | PK `(tenant_id, name)` |
| `idempotency_records` | Stored responses for idempotent retries (24 h) | unique `(tenant_id, key)` |
| `stored_files`, `stored_file_contents` | Product images, generated documents | content FK `(tenant_id, file_id)` |
| `audit_logs` | Immutable audit trail (JSON old/new values) | append-only; RLS on read |
| `outbox_messages` | Durable background jobs | index `(status, next_attempt_at)`; partial unique dedup key |
| `scheduled_job_runs` | One execution per scheduled period across instances | unique `(job_name, run_key)` |
| `data_protection_keys` | ASP.NET Core Data Protection key ring shared by all instances | |

## Roles and privileges

| Role | Used by | Privileges |
|---|---|---|
| Owner (e.g. `stockforge`/`postgres`) | Migrator only | DDL; owns tables (RLS does not apply to the owner, triggers do) |
| `stockforge_app` | API and worker | `SELECT/INSERT/UPDATE/DELETE` on tables except `UPDATE/DELETE/TRUNCATE` on `stock_movements` and `audit_logs`; no access to `__ef_migrations_history`; RLS enforced |

## Changing the schema

```bash
dotnet tool restore
dotnet ef migrations add <Name> -p src/StockForge.Infrastructure -s src/StockForge.Infrastructure -o Persistence/Migrations
# New tenant-owned table? End Up() with:  migrationBuilder.Sql("SELECT stockforge_apply_tenant_rls();");
dotnet ef migrations has-pending-model-changes -p src/StockForge.Infrastructure -s src/StockForge.Infrastructure   # CI runs this
```

The design-time factory uses `STOCKFORGE_MIGRATIONS_CONNECTION` (owner connection) or local defaults.
