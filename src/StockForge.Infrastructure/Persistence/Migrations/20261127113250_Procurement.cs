using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockForge.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261127113250_Procurement : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "settings_purchase_approval_threshold",
            table: "tenants",
            type: "numeric(18,2)",
            precision: 18,
            scale: 2,
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.CreateTable(
            name: "suppliers",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                contact_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                tax_number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                address_line1 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                address_line2 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                address_city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                address_state = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                address_postal_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                address_country = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                payment_terms_days = table.Column<int>(type: "integer", nullable: true),
                lead_time_days = table.Column<int>(type: "integer", nullable: true),
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
                table.PrimaryKey("pk_suppliers", x => x.id);
                table.UniqueConstraint("ak_suppliers_tenant_id_id", x => new { x.tenant_id, x.id });
                table.ForeignKey(
                    name: "fk_suppliers_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "purchase_orders",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                order_date = table.Column<DateOnly>(type: "date", nullable: false),
                expected_date = table.Column<DateOnly>(type: "date", nullable: true),
                currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                subtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                tax_total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                requires_approval = table.Column<bool>(type: "boolean", nullable: false),
                created_by_user = table.Column<Guid>(type: "uuid", nullable: false),
                submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                approved_by = table.Column<Guid>(type: "uuid", nullable: true),
                approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                rejected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                rejection_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                table.PrimaryKey("pk_purchase_orders", x => x.id);
                table.UniqueConstraint("ak_purchase_orders_tenant_id_id", x => new { x.tenant_id, x.id });
                table.CheckConstraint("ck_purchase_orders_totals", "subtotal >= 0 AND tax_total >= 0 AND total >= 0");
                table.ForeignKey(
                    name: "fk_purchase_orders_suppliers_tenant_id_supplier_id",
                    columns: x => new { x.tenant_id, x.supplier_id },
                    principalTable: "suppliers",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_purchase_orders_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_purchase_orders_warehouses_tenant_id_warehouse_id",
                    columns: x => new { x.tenant_id, x.warehouse_id },
                    principalTable: "warehouses",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "supplier_products",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                product_id = table.Column<Guid>(type: "uuid", nullable: false),
                supplier_sku = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                unit_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                minimum_order_quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                lead_time_days = table.Column<int>(type: "integer", nullable: true),
                is_preferred = table.Column<bool>(type: "boolean", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: true),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_supplier_products", x => x.id);
                table.CheckConstraint("ck_supplier_products_non_negative", "unit_cost >= 0 AND minimum_order_quantity >= 0");
                table.ForeignKey(
                    name: "fk_supplier_products_products_tenant_id_product_id",
                    columns: x => new { x.tenant_id, x.product_id },
                    principalTable: "products",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_supplier_products_suppliers_tenant_id_supplier_id",
                    columns: x => new { x.tenant_id, x.supplier_id },
                    principalTable: "suppliers",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_supplier_products_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "goods_receipts",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                purchase_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                delivery_note = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
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
                table.PrimaryKey("pk_goods_receipts", x => x.id);
                table.UniqueConstraint("ak_goods_receipts_tenant_id_id", x => new { x.tenant_id, x.id });
                table.ForeignKey(
                    name: "fk_goods_receipts_purchase_orders_tenant_id_purchase_order_id",
                    columns: x => new { x.tenant_id, x.purchase_order_id },
                    principalTable: "purchase_orders",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_goods_receipts_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_goods_receipts_warehouses_tenant_id_warehouse_id",
                    columns: x => new { x.tenant_id, x.warehouse_id },
                    principalTable: "warehouses",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "purchase_order_lines",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                purchase_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                line_number = table.Column<int>(type: "integer", nullable: false),
                product_id = table.Column<Guid>(type: "uuid", nullable: false),
                sku = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                product_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                unit_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                tax_rate = table.Column<decimal>(type: "numeric(6,5)", precision: 6, scale: 5, nullable: false),
                subtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                tax_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                quantity_received = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: true),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_purchase_order_lines", x => x.id);
                table.UniqueConstraint("ak_purchase_order_line_tenant_id_id", x => new { x.tenant_id, x.id });
                table.CheckConstraint("ck_po_lines_quantities", "quantity > 0 AND quantity_received >= 0 AND quantity_received <= quantity");
                table.ForeignKey(
                    name: "fk_purchase_order_lines_products_tenant_id_product_id",
                    columns: x => new { x.tenant_id, x.product_id },
                    principalTable: "products",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_purchase_order_lines_purchase_orders_tenant_id_purchase_ord",
                    columns: x => new { x.tenant_id, x.purchase_order_id },
                    principalTable: "purchase_orders",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_purchase_order_lines_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "supplier_invoices",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                reference = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                purchase_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                supplier_invoice_number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                invoice_date = table.Column<DateOnly>(type: "date", nullable: false),
                due_date = table.Column<DateOnly>(type: "date", nullable: false),
                currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                subtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                tax_total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                amount_paid = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                matches_receipts = table.Column<bool>(type: "boolean", nullable: false),
                notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: true),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_supplier_invoices", x => x.id);
                table.UniqueConstraint("ak_supplier_invoices_tenant_id_id", x => new { x.tenant_id, x.id });
                table.CheckConstraint("ck_supplier_invoices_amounts", "total >= 0 AND amount_paid >= 0 AND amount_paid <= total");
                table.CheckConstraint("ck_supplier_invoices_dates", "due_date >= invoice_date");
                table.ForeignKey(
                    name: "fk_supplier_invoices_purchase_orders_tenant_id_purchase_order_",
                    columns: x => new { x.tenant_id, x.purchase_order_id },
                    principalTable: "purchase_orders",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_supplier_invoices_suppliers_tenant_id_supplier_id",
                    columns: x => new { x.tenant_id, x.supplier_id },
                    principalTable: "suppliers",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_supplier_invoices_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "goods_receipt_lines",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                goods_receipt_id = table.Column<Guid>(type: "uuid", nullable: false),
                purchase_order_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                product_id = table.Column<Guid>(type: "uuid", nullable: false),
                quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                unit_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: true),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_goods_receipt_lines", x => x.id);
                table.CheckConstraint("ck_grn_lines_quantity_positive", "quantity > 0 AND unit_cost >= 0");
                table.ForeignKey(
                    name: "fk_goods_receipt_lines_goods_receipts_tenant_id_goods_receipt_",
                    columns: x => new { x.tenant_id, x.goods_receipt_id },
                    principalTable: "goods_receipts",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_goods_receipt_lines_products_tenant_id_product_id",
                    columns: x => new { x.tenant_id, x.product_id },
                    principalTable: "products",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_goods_receipt_lines_purchase_order_line_tenant_id_purchase_",
                    columns: x => new { x.tenant_id, x.purchase_order_line_id },
                    principalTable: "purchase_order_lines",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_goods_receipt_lines_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "supplier_payments",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                supplier_invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                table.PrimaryKey("pk_supplier_payments", x => x.id);
                table.CheckConstraint("ck_supplier_payments_amount_positive", "amount > 0");
                table.ForeignKey(
                    name: "fk_supplier_payments_supplier_invoices_tenant_id_supplier_invo",
                    columns: x => new { x.tenant_id, x.supplier_invoice_id },
                    principalTable: "supplier_invoices",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_supplier_payments_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ix_goods_receipt_lines_tenant_id_goods_receipt_id",
            table: "goods_receipt_lines",
            columns: new[] { "tenant_id", "goods_receipt_id" });

        migrationBuilder.CreateIndex(
            name: "ix_goods_receipt_lines_tenant_id_product_id",
            table: "goods_receipt_lines",
            columns: new[] { "tenant_id", "product_id" });

        migrationBuilder.CreateIndex(
            name: "ix_goods_receipt_lines_tenant_id_purchase_order_line_id",
            table: "goods_receipt_lines",
            columns: new[] { "tenant_id", "purchase_order_line_id" });

        migrationBuilder.CreateIndex(
            name: "ix_goods_receipts_tenant_id_number",
            table: "goods_receipts",
            columns: new[] { "tenant_id", "number" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_goods_receipts_tenant_id_purchase_order_id",
            table: "goods_receipts",
            columns: new[] { "tenant_id", "purchase_order_id" });

        migrationBuilder.CreateIndex(
            name: "ix_goods_receipts_tenant_id_warehouse_id",
            table: "goods_receipts",
            columns: new[] { "tenant_id", "warehouse_id" });

        migrationBuilder.CreateIndex(
            name: "ix_purchase_order_lines_tenant_id_product_id",
            table: "purchase_order_lines",
            columns: new[] { "tenant_id", "product_id" });

        migrationBuilder.CreateIndex(
            name: "ix_purchase_order_lines_tenant_id_purchase_order_id_product_id",
            table: "purchase_order_lines",
            columns: new[] { "tenant_id", "purchase_order_id", "product_id" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_purchase_orders_tenant_id_idempotency_key",
            table: "purchase_orders",
            columns: new[] { "tenant_id", "idempotency_key" },
            unique: true,
            filter: "idempotency_key IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "ix_purchase_orders_tenant_id_number",
            table: "purchase_orders",
            columns: new[] { "tenant_id", "number" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_purchase_orders_tenant_id_status_created_at",
            table: "purchase_orders",
            columns: new[] { "tenant_id", "status", "created_at" });

        migrationBuilder.CreateIndex(
            name: "ix_purchase_orders_tenant_id_supplier_id",
            table: "purchase_orders",
            columns: new[] { "tenant_id", "supplier_id" });

        migrationBuilder.CreateIndex(
            name: "ix_purchase_orders_tenant_id_warehouse_id_status",
            table: "purchase_orders",
            columns: new[] { "tenant_id", "warehouse_id", "status" });

        migrationBuilder.CreateIndex(
            name: "ix_supplier_invoices_tenant_id_purchase_order_id",
            table: "supplier_invoices",
            columns: new[] { "tenant_id", "purchase_order_id" });

        migrationBuilder.CreateIndex(
            name: "ix_supplier_invoices_tenant_id_reference",
            table: "supplier_invoices",
            columns: new[] { "tenant_id", "reference" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_supplier_invoices_tenant_id_status_due_date",
            table: "supplier_invoices",
            columns: new[] { "tenant_id", "status", "due_date" });

        migrationBuilder.CreateIndex(
            name: "ix_supplier_invoices_tenant_id_supplier_id_supplier_invoice_nu",
            table: "supplier_invoices",
            columns: new[] { "tenant_id", "supplier_id", "supplier_invoice_number" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_supplier_payments_tenant_id_paid_at",
            table: "supplier_payments",
            columns: new[] { "tenant_id", "paid_at" });

        migrationBuilder.CreateIndex(
            name: "ix_supplier_payments_tenant_id_supplier_invoice_id",
            table: "supplier_payments",
            columns: new[] { "tenant_id", "supplier_invoice_id" });

        migrationBuilder.CreateIndex(
            name: "ix_supplier_products_tenant_id_product_id",
            table: "supplier_products",
            columns: new[] { "tenant_id", "product_id" },
            unique: true,
            filter: "is_preferred");

        migrationBuilder.CreateIndex(
            name: "ix_supplier_products_tenant_id_supplier_id_product_id",
            table: "supplier_products",
            columns: new[] { "tenant_id", "supplier_id", "product_id" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_suppliers_tenant_id_code",
            table: "suppliers",
            columns: new[] { "tenant_id", "code" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_suppliers_tenant_id_name",
            table: "suppliers",
            columns: new[] { "tenant_id", "name" });

        // New tenant-owned tables get row-level security (see docs/MULTI-TENANCY.md).
        migrationBuilder.Sql("SELECT stockforge_apply_tenant_rls();");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "goods_receipt_lines");

        migrationBuilder.DropTable(
            name: "supplier_payments");

        migrationBuilder.DropTable(
            name: "supplier_products");

        migrationBuilder.DropTable(
            name: "goods_receipts");

        migrationBuilder.DropTable(
            name: "purchase_order_lines");

        migrationBuilder.DropTable(
            name: "supplier_invoices");

        migrationBuilder.DropTable(
            name: "purchase_orders");

        migrationBuilder.DropTable(
            name: "suppliers");

        migrationBuilder.DropColumn(
            name: "settings_purchase_approval_threshold",
            table: "tenants");
    }
}
