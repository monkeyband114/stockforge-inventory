#!/usr/bin/env python3
"""End-to-end smoke test against a running StockForge API seeded with the development demo data.

Usage: python scripts/smoke_test.py [base_url] [demo_password]
Exits non-zero if any check fails. Uses only the standard library.
"""
import json
import sys
import urllib.error
import urllib.request
import uuid

BASE = sys.argv[1] if len(sys.argv) > 1 else "http://localhost:8080"
PASSWORD = sys.argv[2] if len(sys.argv) > 2 else "StockForge!2026"
failures = []


def parse(raw):
    if not raw:
        return None
    try:
        return json.loads(raw)
    except json.JSONDecodeError:
        return raw.decode(errors="replace")


def call(method, path, token=None, body=None, headers=None):
    data = json.dumps(body).encode() if body is not None else None
    request = urllib.request.Request(BASE + path, data=data, method=method)
    request.add_header("Content-Type", "application/json")
    for key, value in (headers or {}).items():
        request.add_header(key, value)
    if token:
        request.add_header("Authorization", f"Bearer {token}")
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            return response.status, parse(response.read()), dict(response.headers)
    except urllib.error.HTTPError as error:
        return error.code, parse(error.read()), dict(error.headers)


def check(name, condition, detail=""):
    print(f"{'PASS' if condition else 'FAIL'}  {name}{('  — ' + str(detail)) if detail else ''}")
    if not condition:
        failures.append(name)


def login(email):
    status, body, _ = call("POST", "/api/auth/login", body={"email": email, "password": PASSWORD})
    check(f"login {email}", status == 200, status)
    return body


status, _, _ = call("GET", "/health/ready")
check("readiness probe", status == 200, status)

admin = login("admin@acme.test")
token = admin["accessToken"]
check("admin is OrganizationAdmin of Acme", admin["user"]["tenant"]["name"] == "Acme Distribution" and "OrganizationAdmin" in admin["user"]["roles"])

status, dash, _ = call("GET", "/api/dashboard/organization", token)
check("organization dashboard", status == 200 and dash["totalProducts"] > 0 and len(dash["movementTrend"]) > 0,
      f"products={dash and dash.get('totalProducts')} value={dash and dash.get('inventoryCostValue')} lowStock={dash and dash.get('lowStockCount')}")

status, products, _ = call("GET", "/api/products?pageSize=5&sort=sku", token)
check("product list", status == 200 and products["totalCount"] > 0, products and products.get("totalCount"))
product = products["items"][0]

status, warehouses, _ = call("GET", "/api/warehouses", token)
cdc = next(w for w in warehouses if w["code"] == "CDC")

# Warehouse-scoped staff only see their assigned warehouse.
staff = login("staff@acme.test")
status, inventory, _ = call("GET", "/api/inventory?pageSize=200", staff["accessToken"])
codes = {row["warehouseCode"] for row in inventory["items"]}
check("staff sees only assigned warehouse", status == 200 and codes == {"CDC"}, codes)

status, pending, _ = call("GET", "/api/stock-adjustments?status=PendingApproval", token)
check("pending adjustments exist", status == 200 and pending["totalCount"] > 0, pending and pending.get("totalCount"))
status, body, _ = call("POST", f"/api/stock-adjustments/{pending['items'][0]['id']}/approve", staff["accessToken"], {"notes": "no"})
check("staff cannot approve adjustments (403)", status == 403, status)
status, _, _ = call("GET", "/api/audit-logs", staff["accessToken"])
check("staff cannot read audit log (403)", status == 403, status)

# Tenant isolation through the API.
globex = login("admin@globex.test")
status, body, _ = call("GET", f"/api/products/{product['id']}", globex["accessToken"])
check("other tenant gets 404 for Acme product", status == 404, status)
status, body, _ = call("POST", "/api/inventory/stock-in", globex["accessToken"],
                       {"warehouseId": cdc["id"], "productId": product["id"], "quantity": 1})
check("other tenant cannot move Acme stock", status == 404, status)

# Idempotent stock-in.
key = f"smoke-{uuid.uuid4().hex}"
request = {"warehouseId": cdc["id"], "productId": product["id"], "quantity": 5, "unitCost": 2.5, "referenceNumber": "SMOKE-1", "notes": "smoke"}
s1, m1, _ = call("POST", "/api/inventory/stock-in", token, request, {"Idempotency-Key": key})
s2, m2, h2 = call("POST", "/api/inventory/stock-in", token, request, {"Idempotency-Key": key})
check("idempotent retry replays response", s1 == 201 and s2 == 201 and m1["id"] == m2["id"] and h2.get("Idempotent-Replayed") == "true",
      f"{s1}/{s2} replayed={h2.get('Idempotent-Replayed')}")
s3, b3, _ = call("POST", "/api/inventory/stock-in", token, {**request, "quantity": 6}, {"Idempotency-Key": key})
check("idempotency key reuse with different body rejected", s3 == 422 and b3["code"] == "idempotency_key_reused", s3)

status, body, _ = call("POST", "/api/inventory/stock-out", token, {"warehouseId": cdc["id"], "productId": product["id"], "quantity": 99999999})
check("oversell rejected with insufficient_stock", status == 409 and body["code"] == "insufficient_stock", f"{status} available={body and body.get('available')}")

status, body, _ = call("POST", "/api/inventory/stock-in", token, {"warehouseId": cdc["id"], "productId": product["id"], "quantity": -3})
check("validation errors are 400 problem details", status == 400 and body["code"] == "validation_failed", status)

status, body, _ = call("GET", "/api/products")
check("anonymous request is 401", status == 401 and body["code"] == "unauthenticated", status)

# Refresh rotation and reuse detection (API clients opt into body transport).
s, auth, _ = call("POST", "/api/auth/login", body={"email": "inventory@acme.test", "password": PASSWORD}, headers={"X-Refresh-Token-Transport": "body"})
first = auth["refreshToken"]
s, rotated, _ = call("POST", "/api/auth/refresh", body={"refreshToken": first}, headers={"X-Refresh-Token-Transport": "body"})
check("refresh rotates the token", s == 200 and rotated["refreshToken"] != first, s)
s, body, _ = call("POST", "/api/auth/switch-organization", rotated["accessToken"], {"tenantId": next(m["id"] for m in rotated["user"]["memberships"] if m["slug"] == "globex-retail")})
check("multi-org user can switch organization", s == 200 and body["user"]["tenant"]["slug"] == "globex-retail", s)

# Orders: create + submit (auto-approved), invoice issued, appears in the fulfillment queue.
status, customers_page, _ = call("GET", "/api/customers?pageSize=1", token)
check("customers listed", status == 200 and customers_page["totalCount"] > 0, customers_page and customers_page.get("totalCount"))
order_key = {"Idempotency-Key": f"smoke-order-{uuid.uuid4().hex}"}
order_request = {"customerId": customers_page["items"][0]["id"], "warehouseId": cdc["id"], "discountPercent": 0,
                 "lines": [{"productId": product["id"], "quantity": 1}], "submit": True}
s1, order, _ = call("POST", "/api/orders", token, order_request, order_key)
s2, replay, h2 = call("POST", "/api/orders", token, order_request, order_key)
check("order submitted and approved with an invoice", s1 == 201 and order["status"] == "Approved" and order["invoice"] is not None,
      f"{s1} {order and order.get('status')}")
check("order creation retry is replayed", s2 == 201 and replay["id"] == order["id"] and h2.get("Idempotent-Replayed") == "true", s2)
status, _, _ = call("POST", "/api/orders", token, order_request)
check("order creation without idempotency key rejected", status == 400, status)
status, queue, _ = call("GET", "/api/fulfillment/queue", token)
check("approved order is in the fulfillment queue", status == 200 and any(q["orderId"] == order["id"] for q in queue), status)
status, cancelled, _ = call("POST", f"/api/orders/{order['id']}/cancel", token, {"reason": "smoke test cleanup"})
check("cancel releases stock and cancels the invoice", status == 200 and cancelled["invoice"]["status"] == "Cancelled", status)
finance = login("finance@acme.test")
status, summary, _ = call("GET", "/api/finance/summary", finance["accessToken"])
check("finance summary", status == 200 and len(summary["monthly"]) == 12, status)

status, _, _ = call("GET", "/openapi/v1.json")
check("OpenAPI document", status == 200, status)

print(f"\n{len(failures)} failure(s)" if failures else "\nAll smoke checks passed.")
sys.exit(1 if failures else 0)
