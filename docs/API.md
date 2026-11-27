# API reference

The live, authoritative reference is the OpenAPI document (`/openapi/v1.json`) with Swagger UI at
`/swagger`. Both are enabled in Development or with `OpenApi:Enabled=true`. This page summarizes the
conventions and endpoints.

## Conventions

* JSON with camelCase properties; enums as strings (`"StockIn"`, `"PendingApproval"`).
* `Authorization: Bearer <access token>` on every endpoint except the anonymous auth endpoints and health.
* Lists accept `page` (1-based), `pageSize` (max 200) and `search`, and return
  `{ items, page, pageSize, totalCount, totalPages }`.
* Errors use RFC 9457 problem details with a stable `code` and a `traceId` (echoed as `X-Correlation-Id`):

  | Status | `code` examples |
  |---|---|
  | 400 | `validation_failed` (`errors: { field: [messages] }`), `idempotency_key_required` |
  | 401 | `unauthenticated`, `token_expired`, `session_invalid`, `invalid_credentials`, `account_locked`, `invalid_refresh_token` |
  | 403 | `forbidden`, `tenant_violation` |
  | 404 | `not_found` (also returned for other tenants' ids) |
  | 409 | `insufficient_stock` (+ `available`, `requested`), `invalid_state`, `concurrency_conflict`, `duplicate`, `duplicate_sku`, `request_in_progress` |
  | 422 | `idempotency_key_reused`, domain rules such as `last_admin`, `product_in_use`, `no_variance`, `self_approval`, `over_pick`, `over_return`, `overpayment`, `order_partially_shipped`, `invoice_paid` |
  | 429 | `rate_limited` (+ `Retry-After`) |

* Send `X-Correlation-Id` to trace a request through logs, audit rows and background jobs.

### Idempotency

`POST` endpoints marked ⟳ accept `Idempotency-Key: <1–100 chars>`. The first request executes and its
response is stored for 24 hours, keyed per tenant:

| Situation | Result |
|---|---|
| Retry with the same key and the same request | Stored response replayed, header `Idempotent-Replayed: true`; nothing executes again |
| Same key while the first request is still running | `409 request_in_progress` with `Retry-After: 1` |
| Same key, different body, path or user | `422 idempotency_key_reused` |
| The original request failed | Key released; the client may retry with it |

## Endpoints

Permissions in brackets; `a | b` means either permission suffices.

### Authentication: `/api/auth`
| Method | Path | Notes |
|---|---|---|
| POST | `/register` | Organization onboarding (anonymous, rate-limited) |
| POST | `/login` | `{email, password, tenantId?}` |
| POST | `/refresh` | Cookie `sf_refresh` or `{refreshToken}` |
| POST | `/logout` | Revokes the current session |
| POST | `/switch-organization` | `{tenantId}`; issues a new session |
| POST | `/forgot-password`, `/reset-password`, `/accept-invitation` | Anonymous, rate-limited |
| POST | `/change-password` | Revokes other sessions |
| GET | `/me` | User, tenant, roles, permissions, warehouse scope, memberships |
| GET / DELETE | `/sessions`, `/sessions/{id}` | Own sessions |

### Organization & users
| Method | Path | Permission |
|---|---|---|
| GET / PUT | `/api/organization` | organization.view / organization.manage |
| PUT | `/api/organization/settings` | organization.manage |
| GET | `/api/users`, `/api/users/{userId}` | users.view |
| POST | `/api/users` (invite) | users.manage |
| PUT | `/api/users/{userId}` (roles, warehouses, grants) | users.manage |
| POST | `/api/users/{userId}/deactivate` · `/activate` · `/unlock` · `/revoke-sessions` | users.manage |
| GET | `/api/roles`, `/api/permissions` | users.view |

### Catalog & warehouses
| Method | Path | Permission |
|---|---|---|
| GET / POST / PUT | `/api/warehouses`, `/api/warehouses/{id}` | warehouses.view / warehouses.manage |
| GET / POST / PUT | `/api/units`, `/api/units/{id}` | products.view / products.manage |
| GET / POST / PUT / DELETE | `/api/categories`, `/api/categories/{id}` | products.view / products.manage |
| GET | `/api/products` (`categoryId`, `isActive`, `sort=sku|price|-price|newest`) | products.view |
| GET | `/api/products/{id}`, `/api/products/by-barcode/{code}` | products.view |
| POST / PUT / DELETE | `/api/products`, `/api/products/{id}` | products.manage |
| PUT / GET / DELETE | `/api/products/{id}/image` (multipart `file`, PNG/JPEG/WebP ≤ 2 MB) | products.manage / products.view |

### Inventory
| Method | Path | Permission |
|---|---|---|
| GET | `/api/inventory` (`warehouseId`, `productId`, `categoryId`, `lowStockOnly`, `inStockOnly`) | inventory.view |
| GET | `/api/inventory/low-stock` | inventory.view |
| GET | `/api/inventory/valuation?warehouseId=` | inventory.valuation |
| POST ⟳ | `/api/inventory/stock-in` · `/returns` | inventory.receive |
| POST ⟳ | `/api/inventory/stock-out` · `/damaged` | inventory.issue |
| GET | `/api/stock-movements` (`warehouseId`, `productId`, `type`, `referenceType`, `referenceId`, `from`, `to`), `/{id}` | inventory.view |
| GET / POST ⟳ | `/api/reservations` · POST `/{id}/release` | inventory.view / inventory.reserve |
| GET | `/api/stock-adjustments` (`status`, `warehouseId`), `/{id}` | inventory.view |
| POST ⟳ | `/api/stock-adjustments/counts` | inventory.count |
| POST ⟳ | `/api/stock-adjustments/corrections` | inventory.adjust.request |
| POST | `/api/stock-adjustments/{id}/approve` · `/reject` | inventory.adjust.approve |
| GET / POST ⟳ | `/api/stock-transfers`, `/{id}` | inventory.view / inventory.transfer |
| POST | `/api/stock-transfers/{id}/dispatch` · `/cancel` | inventory.transfer |
| POST | `/api/stock-transfers/{id}/receive` | inventory.receive |

Stock operations are also limited to the caller's assigned warehouses unless they hold
`inventory.view_all_warehouses`.

### Customers, orders & fulfillment
| Method | Path | Permission |
|---|---|---|
| GET / POST / PUT | `/api/customers`, `/api/customers/{id}` | customers.view / customers.manage |
| GET | `/api/orders` (`status`, `customerId`, `warehouseId`, `from`, `to`), `/api/orders/{id}` | orders.view |
| POST ⟳! | `/api/orders` (`submit: true` to submit immediately) | orders.create |
| PUT | `/api/orders/{id}` (draft only) | orders.create |
| POST ⟳! | `/api/orders/{id}/submit` | orders.create |
| POST ⟳! | `/api/orders/{id}/approve` · POST `/reject` | orders.approve |
| POST | `/api/orders/{id}/cancel` | orders.cancel |
| POST | `/api/orders/{id}/process` · `/start-picking` · `/pick` · `/pack` | orders.fulfill (+ warehouse access) |
| POST ⟳! | `/api/orders/{id}/ship` | orders.fulfill (+ warehouse access) |
| POST ⟳! | `/api/orders/{id}/returns` | orders.returns (+ warehouse access) |
| GET / POST | `/api/shipments/{id}` · `/api/shipments/{id}/deliver` | orders.view / orders.fulfill |
| GET | `/api/fulfillment/queue?warehouseId=` | orders.fulfill \| orders.view |

### Invoices & finance
| Method | Path | Permission |
|---|---|---|
| GET | `/api/invoices` (`status`, `customerId`, `outstandingOnly`, `from`, `to`), `/api/invoices/{id}` | invoices.view |
| GET | `/api/invoices/{id}/pdf` (`404 pdf_pending` until generated) | invoices.view |
| POST | `/api/invoices/{id}/regenerate-pdf` | invoices.manage |
| POST ⟳! | `/api/invoices/{id}/payments` | payments.record |
| GET | `/api/finance/summary?months=12` | finance.reports |

⟳! = `Idempotency-Key` **required** (`400 idempotency_key_required` without it). Lifecycle rules and error
codes are in [ORDERS.md](ORDERS.md).

### Procurement & payables
| Method | Path | Permission |
|---|---|---|
| GET / POST / PUT | `/api/suppliers`, `/api/suppliers/{id}` | suppliers.view / suppliers.manage |
| GET / PUT / DELETE | `/api/suppliers/{id}/products`, `/products/{productId}` | suppliers.view / suppliers.manage |
| GET | `/api/purchase-orders` (`status`, `supplierId`, `warehouseId`, `outstandingOnly`), `/{id}` | purchasing.view |
| GET | `/api/purchase-orders/suggestions?warehouseId=` | purchasing.create |
| POST ⟳! | `/api/purchase-orders` · `/{id}/submit` | purchasing.create |
| PUT · POST | `/api/purchase-orders/{id}` · `/{id}/cancel` | purchasing.create |
| POST ⟳! | `/api/purchase-orders/{id}/approve` · POST `/reject` | purchasing.approve |
| POST | `/api/purchase-orders/{id}/close` (close short) | purchasing.create \| purchasing.approve |
| POST ⟳! | `/api/purchase-orders/{id}/receipts` (goods received note) | purchasing.receive (+ warehouse access) |
| GET | `/api/supplier-invoices` (`status`, `supplierId`, `outstandingOnly`), `/{id}` | payables.view |
| POST ⟳! | `/api/supplier-invoices` · `/{id}/payments` | payables.manage |
| POST | `/api/supplier-invoices/{id}/cancel` | payables.manage |

See [PROCUREMENT.md](PROCUREMENT.md).

### Dashboards, audit, jobs, platform
| Method | Path | Permission |
|---|---|---|
| GET | `/api/dashboard/organization?days=30` | reports.organization \| inventory.analytics |
| GET | `/api/dashboard/warehouses/{id}?days=14` | inventory.view (+ warehouse access) |
| GET | `/api/audit-logs` (`entityType`, `entityId`, `userId`, `action`, `from`, `to`) | audit.view |
| GET | `/api/jobs` (`status`, `type`), `/api/jobs/stats` | jobs.view \| platform.jobs.view |
| POST | `/api/jobs/{id}/retry` | jobs.manage \| platform.jobs.view |
| GET | `/api/platform/stats`, `/api/platform/tenants` | platform.tenants.manage |
| POST | `/api/platform/tenants` · `/{id}/suspend` · `/{id}/activate` | platform.tenants.manage |

### Health
`GET /health/live` (process up) and `GET /health/ready` (PostgreSQL and distributed cache). Neither is
rate-limited.

## Real-time hub

`/hubs/notifications` (SignalR). Authenticate with `accessTokenFactory`; WebSockets send the token as
`access_token` in the query string. The server assigns groups from the session, so clients cannot choose
them. Every event is delivered as `{ type, tenantId, payload, at }`:

| Event | Payload | Audience |
|---|---|---|
| `inventory.updated` | warehouseId, productId, quantityOnHand, quantityReserved, quantityAvailable | Warehouse members and all-warehouse viewers |
| `warehouse.activity` | movementId, warehouseId, productId, type, quantityDelta, reservedDelta | same |
| `transfer.updated` | transferId, number, status, source/destination warehouse | Both warehouses |
| `adjustment.updated` | adjustmentId, number, warehouseId, productId, status, quantityChange | Warehouse; approvers when pending |
| `inventory.low_stock` | warehouseId, productId, sku, name, warehouseName, quantityAvailable, reorderLevel | Warehouse members and all-warehouse viewers |
| `order.created`, `order.status_changed` | orderId, number, status, warehouseId, total | Fulfillment warehouse; approvers when pending |
| `shipment.updated` | id, number, salesOrderId, orderNumber, status, trackingNumber | Fulfillment warehouse |

Events published by the worker (`inventory.low_stock`, `shipment.updated`) reach browsers through the Redis
backplane, so they require `ConnectionStrings:Redis` on both the API and the worker.

Server method: `WhoAmI()` returns the subscribed tenant id (diagnostics).
