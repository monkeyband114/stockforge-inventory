using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockForge.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261127101717_SalesAndBilling : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "customers",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                tax_number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                billing_line1 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                billing_line2 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                billing_city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                billing_state = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                billing_postal_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                billing_country = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                shipping_line1 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                shipping_line2 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                shipping_city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                shipping_state = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                shipping_postal_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                shipping_country = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                payment_terms_days = table.Column<int>(type: "integer", nullable: true),
                credit_limit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                is_active = table.Column<bool>(type: "boolean", nullable: false),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: true),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_customers", x => x.id);
                table.UniqueConstraint("ak_customers_tenant_id_id", x => new { x.tenant_id, x.id });
                table.CheckConstraint("ck_customers_credit_limit", "credit_limit IS NULL OR credit_limit >= 0");
                table.ForeignKey(
                    name: "fk_customers_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "sales_orders",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                order_date = table.Column<DateOnly>(type: "date", nullable: false),
                required_date = table.Column<DateOnly>(type: "date", nullable: true),
                customer_reference = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                order_discount_percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                ship_to_line1 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                ship_to_line2 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                ship_to_city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                ship_to_state = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                ship_to_postal_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                ship_to_country = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                subtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                discount_total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                tax_total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                total_quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                requires_approval = table.Column<bool>(type: "boolean", nullable: false),
                approval_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                created_by_user = table.Column<Guid>(type: "uuid", nullable: false),
                submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                approved_by = table.Column<Guid>(type: "uuid", nullable: true),
                approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                rejected_by = table.Column<Guid>(type: "uuid", nullable: true),
                rejected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                rejection_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                cancelled_by = table.Column<Guid>(type: "uuid", nullable: true),
                cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                cancellation_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                shipped_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                delivered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                idempotency_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: true),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_sales_orders", x => x.id);
                table.UniqueConstraint("ak_sales_orders_tenant_id_id", x => new { x.tenant_id, x.id });
                table.CheckConstraint("ck_sales_orders_discount", "order_discount_percent >= 0 AND order_discount_percent <= 100");
                table.CheckConstraint("ck_sales_orders_totals_non_negative", "subtotal >= 0 AND discount_total >= 0 AND tax_total >= 0 AND total >= 0");
                table.ForeignKey(
                    name: "fk_sales_orders_customers_tenant_id_customer_id",
                    columns: x => new { x.tenant_id, x.customer_id },
                    principalTable: "customers",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_sales_orders_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_sales_orders_warehouses_tenant_id_warehouse_id",
                    columns: x => new { x.tenant_id, x.warehouse_id },
                    principalTable: "warehouses",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "invoices",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                sales_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                issue_date = table.Column<DateOnly>(type: "date", nullable: false),
                due_date = table.Column<DateOnly>(type: "date", nullable: false),
                currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                subtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                discount_total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                tax_total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                amount_paid = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                credited_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                billing_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                billing_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                billing_tax_number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                billing_line1 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                billing_line2 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                billing_city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                billing_state = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                billing_postal_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                billing_country = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                pdf_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                pdf_generated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: true),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_invoices", x => x.id);
                table.UniqueConstraint("ak_invoices_tenant_id_id", x => new { x.tenant_id, x.id });
                table.CheckConstraint("ck_invoices_amounts", "total >= 0 AND amount_paid >= 0 AND credited_amount >= 0 AND amount_paid + credited_amount <= total");
                table.CheckConstraint("ck_invoices_dates", "due_date >= issue_date");
                table.ForeignKey(
                    name: "fk_invoices_customers_tenant_id_customer_id",
                    columns: x => new { x.tenant_id, x.customer_id },
                    principalTable: "customers",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_invoices_sales_orders_tenant_id_sales_order_id",
                    columns: x => new { x.tenant_id, x.sales_order_id },
                    principalTable: "sales_orders",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_invoices_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "sales_order_lines",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                order_id = table.Column<Guid>(type: "uuid", nullable: false),
                line_number = table.Column<int>(type: "integer", nullable: false),
                product_id = table.Column<Guid>(type: "uuid", nullable: false),
                sku = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                product_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                unit_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                discount_percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                tax_rate = table.Column<decimal>(type: "numeric(6,5)", precision: 6, scale: 5, nullable: false),
                subtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                discount_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                tax_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                quantity_picked = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                quantity_packed = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                quantity_shipped = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                quantity_returned = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: true),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_sales_order_lines", x => x.id);
                table.UniqueConstraint("ak_sales_order_lines_tenant_id_id", x => new { x.tenant_id, x.id });
                table.CheckConstraint("ck_order_lines_fulfillment_order", "quantity_picked >= 0 AND quantity_picked <= quantity AND quantity_packed <= quantity_picked AND quantity_shipped <= quantity_packed AND quantity_returned <= quantity_shipped AND quantity_returned >= 0");
                table.CheckConstraint("ck_order_lines_quantity_positive", "quantity > 0");
                table.ForeignKey(
                    name: "fk_sales_order_lines_products_tenant_id_product_id",
                    columns: x => new { x.tenant_id, x.product_id },
                    principalTable: "products",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_sales_order_lines_sales_orders_tenant_id_order_id",
                    columns: x => new { x.tenant_id, x.order_id },
                    principalTable: "sales_orders",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_sales_order_lines_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "sales_returns",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                sales_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                credit_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                received_by = table.Column<Guid>(type: "uuid", nullable: false),
                received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: true),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_sales_returns", x => x.id);
                table.UniqueConstraint("ak_sales_returns_tenant_id_id", x => new { x.tenant_id, x.id });
                table.ForeignKey(
                    name: "fk_sales_returns_sales_orders_tenant_id_sales_order_id",
                    columns: x => new { x.tenant_id, x.sales_order_id },
                    principalTable: "sales_orders",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_sales_returns_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_sales_returns_warehouses_tenant_id_warehouse_id",
                    columns: x => new { x.tenant_id, x.warehouse_id },
                    principalTable: "warehouses",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "shipments",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                sales_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                carrier = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                tracking_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                package_count = table.Column<int>(type: "integer", nullable: false),
                status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                shipped_by = table.Column<Guid>(type: "uuid", nullable: false),
                shipped_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                delivered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: true),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_shipments", x => x.id);
                table.UniqueConstraint("ak_shipments_tenant_id_id", x => new { x.tenant_id, x.id });
                table.CheckConstraint("ck_shipments_packages", "package_count > 0");
                table.ForeignKey(
                    name: "fk_shipments_sales_orders_tenant_id_sales_order_id",
                    columns: x => new { x.tenant_id, x.sales_order_id },
                    principalTable: "sales_orders",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_shipments_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_shipments_warehouses_tenant_id_warehouse_id",
                    columns: x => new { x.tenant_id, x.warehouse_id },
                    principalTable: "warehouses",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "invoice_lines",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                line_number = table.Column<int>(type: "integer", nullable: false),
                product_id = table.Column<Guid>(type: "uuid", nullable: true),
                sku = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                unit_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                discount_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                tax_rate = table.Column<decimal>(type: "numeric(6,5)", precision: 6, scale: 5, nullable: false),
                tax_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: true),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_invoice_lines", x => x.id);
                table.ForeignKey(
                    name: "fk_invoice_lines_invoices_tenant_id_invoice_id",
                    columns: x => new { x.tenant_id, x.invoice_id },
                    principalTable: "invoices",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_invoice_lines_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "payments",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                method = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                recorded_by = table.Column<Guid>(type: "uuid", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: true),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_payments", x => x.id);
                table.CheckConstraint("ck_payments_amount_positive", "amount > 0");
                table.ForeignKey(
                    name: "fk_payments_invoices_tenant_id_invoice_id",
                    columns: x => new { x.tenant_id, x.invoice_id },
                    principalTable: "invoices",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_payments_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "sales_return_lines",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                return_id = table.Column<Guid>(type: "uuid", nullable: false),
                sales_order_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                product_id = table.Column<Guid>(type: "uuid", nullable: false),
                quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                restock = table.Column<bool>(type: "boolean", nullable: false),
                credit_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: true),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_sales_return_lines", x => x.id);
                table.CheckConstraint("ck_return_lines_quantity_positive", "quantity > 0 AND credit_amount >= 0");
                table.ForeignKey(
                    name: "fk_sales_return_lines_products_tenant_id_product_id",
                    columns: x => new { x.tenant_id, x.product_id },
                    principalTable: "products",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_sales_return_lines_sales_order_lines_tenant_id_sales_order_",
                    columns: x => new { x.tenant_id, x.sales_order_line_id },
                    principalTable: "sales_order_lines",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_sales_return_lines_sales_returns_tenant_id_return_id",
                    columns: x => new { x.tenant_id, x.return_id },
                    principalTable: "sales_returns",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_sales_return_lines_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "shipment_lines",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                shipment_id = table.Column<Guid>(type: "uuid", nullable: false),
                sales_order_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                product_id = table.Column<Guid>(type: "uuid", nullable: false),
                quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: true),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_shipment_lines", x => x.id);
                table.CheckConstraint("ck_shipment_lines_quantity_positive", "quantity > 0");
                table.ForeignKey(
                    name: "fk_shipment_lines_products_tenant_id_product_id",
                    columns: x => new { x.tenant_id, x.product_id },
                    principalTable: "products",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_shipment_lines_sales_order_lines_tenant_id_sales_order_line",
                    columns: x => new { x.tenant_id, x.sales_order_line_id },
                    principalTable: "sales_order_lines",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_shipment_lines_shipments_tenant_id_shipment_id",
                    columns: x => new { x.tenant_id, x.shipment_id },
                    principalTable: "shipments",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_shipment_lines_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ix_customers_tenant_id_code",
            table: "customers",
            columns: new[] { "tenant_id", "code" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_customers_tenant_id_name",
            table: "customers",
            columns: new[] { "tenant_id", "name" });

        migrationBuilder.CreateIndex(
            name: "ix_invoice_lines_tenant_id_invoice_id_line_number",
            table: "invoice_lines",
            columns: new[] { "tenant_id", "invoice_id", "line_number" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_invoices_tenant_id_customer_id",
            table: "invoices",
            columns: new[] { "tenant_id", "customer_id" });

        migrationBuilder.CreateIndex(
            name: "ix_invoices_tenant_id_number",
            table: "invoices",
            columns: new[] { "tenant_id", "number" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_invoices_tenant_id_sales_order_id",
            table: "invoices",
            columns: new[] { "tenant_id", "sales_order_id" },
            unique: true,
            filter: "sales_order_id IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "ix_invoices_tenant_id_status_due_date",
            table: "invoices",
            columns: new[] { "tenant_id", "status", "due_date" });

        migrationBuilder.CreateIndex(
            name: "ix_payments_tenant_id_invoice_id",
            table: "payments",
            columns: new[] { "tenant_id", "invoice_id" });

        migrationBuilder.CreateIndex(
            name: "ix_payments_tenant_id_paid_at",
            table: "payments",
            columns: new[] { "tenant_id", "paid_at" });

        migrationBuilder.CreateIndex(
            name: "ix_sales_order_lines_tenant_id_order_id_product_id",
            table: "sales_order_lines",
            columns: new[] { "tenant_id", "order_id", "product_id" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_sales_order_lines_tenant_id_product_id",
            table: "sales_order_lines",
            columns: new[] { "tenant_id", "product_id" });

        migrationBuilder.CreateIndex(
            name: "ix_sales_orders_tenant_id_customer_id",
            table: "sales_orders",
            columns: new[] { "tenant_id", "customer_id" });

        migrationBuilder.CreateIndex(
            name: "ix_sales_orders_tenant_id_idempotency_key",
            table: "sales_orders",
            columns: new[] { "tenant_id", "idempotency_key" },
            unique: true,
            filter: "idempotency_key IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "ix_sales_orders_tenant_id_number",
            table: "sales_orders",
            columns: new[] { "tenant_id", "number" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_sales_orders_tenant_id_status_created_at",
            table: "sales_orders",
            columns: new[] { "tenant_id", "status", "created_at" });

        migrationBuilder.CreateIndex(
            name: "ix_sales_orders_tenant_id_warehouse_id_status",
            table: "sales_orders",
            columns: new[] { "tenant_id", "warehouse_id", "status" });

        migrationBuilder.CreateIndex(
            name: "ix_sales_return_lines_tenant_id_product_id",
            table: "sales_return_lines",
            columns: new[] { "tenant_id", "product_id" });

        migrationBuilder.CreateIndex(
            name: "ix_sales_return_lines_tenant_id_return_id",
            table: "sales_return_lines",
            columns: new[] { "tenant_id", "return_id" });

        migrationBuilder.CreateIndex(
            name: "ix_sales_return_lines_tenant_id_sales_order_line_id",
            table: "sales_return_lines",
            columns: new[] { "tenant_id", "sales_order_line_id" });

        migrationBuilder.CreateIndex(
            name: "ix_sales_returns_tenant_id_number",
            table: "sales_returns",
            columns: new[] { "tenant_id", "number" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_sales_returns_tenant_id_sales_order_id",
            table: "sales_returns",
            columns: new[] { "tenant_id", "sales_order_id" });

        migrationBuilder.CreateIndex(
            name: "ix_sales_returns_tenant_id_warehouse_id",
            table: "sales_returns",
            columns: new[] { "tenant_id", "warehouse_id" });

        migrationBuilder.CreateIndex(
            name: "ix_shipment_lines_tenant_id_product_id",
            table: "shipment_lines",
            columns: new[] { "tenant_id", "product_id" });

        migrationBuilder.CreateIndex(
            name: "ix_shipment_lines_tenant_id_sales_order_line_id",
            table: "shipment_lines",
            columns: new[] { "tenant_id", "sales_order_line_id" });

        migrationBuilder.CreateIndex(
            name: "ix_shipment_lines_tenant_id_shipment_id",
            table: "shipment_lines",
            columns: new[] { "tenant_id", "shipment_id" });

        migrationBuilder.CreateIndex(
            name: "ix_shipments_tenant_id_number",
            table: "shipments",
            columns: new[] { "tenant_id", "number" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_shipments_tenant_id_sales_order_id",
            table: "shipments",
            columns: new[] { "tenant_id", "sales_order_id" });

        migrationBuilder.CreateIndex(
            name: "ix_shipments_tenant_id_warehouse_id",
            table: "shipments",
            columns: new[] { "tenant_id", "warehouse_id" });

        // New tenant-owned tables get row-level security (see docs/MULTI-TENANCY.md).
        migrationBuilder.Sql("SELECT stockforge_apply_tenant_rls();");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "invoice_lines");

        migrationBuilder.DropTable(
            name: "payments");

        migrationBuilder.DropTable(
            name: "sales_return_lines");

        migrationBuilder.DropTable(
            name: "shipment_lines");

        migrationBuilder.DropTable(
            name: "invoices");

        migrationBuilder.DropTable(
            name: "sales_returns");

        migrationBuilder.DropTable(
            name: "sales_order_lines");

        migrationBuilder.DropTable(
            name: "shipments");

        migrationBuilder.DropTable(
            name: "sales_orders");

        migrationBuilder.DropTable(
            name: "customers");
    }
}
