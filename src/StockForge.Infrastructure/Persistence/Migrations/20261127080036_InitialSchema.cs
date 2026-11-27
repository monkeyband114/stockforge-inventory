using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace StockForge.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261127080036_InitialSchema : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "audit_logs",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                user_id = table.Column<Guid>(type: "uuid", nullable: true),
                user_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                action = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                entity_type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                entity_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                old_values = table.Column<string>(type: "jsonb", nullable: true),
                new_values = table.Column<string>(type: "jsonb", nullable: true),
                changed_properties = table.Column<List<string>>(type: "text[]", nullable: true),
                ip_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                user_agent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                correlation_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_audit_logs", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "data_protection_keys",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                friendly_name = table.Column<string>(type: "text", nullable: true),
                xml = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_data_protection_keys", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "outbox_messages",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                payload = table.Column<string>(type: "text", nullable: false),
                is_payload_protected = table.Column<bool>(type: "boolean", nullable: false),
                status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                attempts = table.Column<int>(type: "integer", nullable: false),
                max_attempts = table.Column<int>(type: "integer", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                locked_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                locked_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                last_error = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                correlation_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                deduplication_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_outbox_messages", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "roles",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                is_platform_role = table.Column<bool>(type: "boolean", nullable: false),
                name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                normalized_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                concurrency_stamp = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_roles", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "scheduled_job_runs",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                job_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                run_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                instance_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                error = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_scheduled_job_runs", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "tenants",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                slug = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                contact_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                address_line1 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                address_line2 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                address_city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                address_state = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                address_postal_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                address_country = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                base_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                time_zone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                suspended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                suspension_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                settings_order_approval_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                settings_order_approval_quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                settings_require_approval_for_all_orders = table.Column<bool>(type: "boolean", nullable: false),
                settings_default_tax_rate = table.Column<decimal>(type: "numeric(6,5)", precision: 6, scale: 5, nullable: false),
                settings_invoice_due_days = table.Column<int>(type: "integer", nullable: false),
                settings_adjustment_requires_approval = table.Column<bool>(type: "boolean", nullable: false),
                settings_low_stock_alerts_enabled = table.Column<bool>(type: "boolean", nullable: false),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: true),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                updated_by = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_tenants", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "users",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                is_active = table.Column<bool>(type: "boolean", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                last_login_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                last_tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                normalized_user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                normalized_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                email_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                password_hash = table.Column<string>(type: "text", nullable: true),
                security_stamp = table.Column<string>(type: "text", nullable: true),
                concurrency_stamp = table.Column<string>(type: "text", nullable: true),
                phone_number = table.Column<string>(type: "text", nullable: true),
                phone_number_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                two_factor_enabled = table.Column<bool>(type: "boolean", nullable: false),
                lockout_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                lockout_enabled = table.Column<bool>(type: "boolean", nullable: false),
                access_failed_count = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_users", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "role_claims",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                role_id = table.Column<Guid>(type: "uuid", nullable: false),
                claim_type = table.Column<string>(type: "text", nullable: true),
                claim_value = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_role_claims", x => x.id);
                table.ForeignKey(
                    name: "fk_role_claims_roles_role_id",
                    column: x => x.role_id,
                    principalTable: "roles",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "idempotency_records",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                method = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                path = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                response_status_code = table.Column<int>(type: "integer", nullable: true),
                response_body = table.Column<string>(type: "text", nullable: true),
                response_content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_idempotency_records", x => x.id);
                table.ForeignKey(
                    name: "fk_idempotency_records_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "product_categories",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                code = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                is_active = table.Column<bool>(type: "boolean", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: true),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_product_categories", x => x.id);
                table.UniqueConstraint("ak_product_categories_tenant_id_id", x => new { x.tenant_id, x.id });
                table.ForeignKey(
                    name: "fk_product_categories_product_categories_tenant_id_parent_id",
                    columns: x => new { x.tenant_id, x.parent_id },
                    principalTable: "product_categories",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_product_categories_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "stored_files",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                size_bytes = table.Column<long>(type: "bigint", nullable: false),
                sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                category = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_stored_files", x => x.id);
                table.UniqueConstraint("ak_stored_files_tenant_id_id", x => new { x.tenant_id, x.id });
                table.ForeignKey(
                    name: "fk_stored_files_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "tenant_sequences",
            columns: table => new
            {
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                next_value = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_tenant_sequences", x => new { x.tenant_id, x.name });
                table.ForeignKey(
                    name: "fk_tenant_sequences_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "units_of_measure",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                allows_fractions = table.Column<bool>(type: "boolean", nullable: false),
                is_active = table.Column<bool>(type: "boolean", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: true),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_units_of_measure", x => x.id);
                table.UniqueConstraint("ak_units_of_measure_tenant_id_id", x => new { x.tenant_id, x.id });
                table.ForeignKey(
                    name: "fk_units_of_measure_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "warehouses",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                address_line1 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                address_line2 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                address_city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                address_state = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                address_postal_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                address_country = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
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
                table.PrimaryKey("pk_warehouses", x => x.id);
                table.UniqueConstraint("ak_warehouses_tenant_id_id", x => new { x.tenant_id, x.id });
                table.ForeignKey(
                    name: "fk_warehouses_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "tenant_memberships",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                is_active = table.Column<bool>(type: "boolean", nullable: false),
                job_title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                joined_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                roles = table.Column<List<string>>(type: "text[]", nullable: false),
                granted_permissions = table.Column<List<string>>(type: "text[]", nullable: false),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: true),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                updated_by = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_tenant_memberships", x => x.id);
                table.UniqueConstraint("ak_tenant_memberships_tenant_id_id", x => new { x.tenant_id, x.id });
                table.ForeignKey(
                    name: "fk_tenant_memberships_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_tenant_memberships_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "user_claims",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                claim_type = table.Column<string>(type: "text", nullable: true),
                claim_value = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_user_claims", x => x.id);
                table.ForeignKey(
                    name: "fk_user_claims_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "user_logins",
            columns: table => new
            {
                login_provider = table.Column<string>(type: "text", nullable: false),
                provider_key = table.Column<string>(type: "text", nullable: false),
                provider_display_name = table.Column<string>(type: "text", nullable: true),
                user_id = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_user_logins", x => new { x.login_provider, x.provider_key });
                table.ForeignKey(
                    name: "fk_user_logins_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "user_roles",
            columns: table => new
            {
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                role_id = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_user_roles", x => new { x.user_id, x.role_id });
                table.ForeignKey(
                    name: "fk_user_roles_roles_role_id",
                    column: x => x.role_id,
                    principalTable: "roles",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_user_roles_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "user_sessions",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                ip_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                user_agent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                revoked_reason = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_user_sessions", x => x.id);
                table.ForeignKey(
                    name: "fk_user_sessions_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "user_tokens",
            columns: table => new
            {
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                login_provider = table.Column<string>(type: "text", nullable: false),
                name = table.Column<string>(type: "text", nullable: false),
                value = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_user_tokens", x => new { x.user_id, x.login_provider, x.name });
                table.ForeignKey(
                    name: "fk_user_tokens_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "stored_file_contents",
            columns: table => new
            {
                file_id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                data = table.Column<byte[]>(type: "bytea", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_stored_file_contents", x => x.file_id);
                table.ForeignKey(
                    name: "fk_stored_file_contents_stored_files_tenant_id_file_id",
                    columns: x => new { x.tenant_id, x.file_id },
                    principalTable: "stored_files",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_stored_file_contents_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "products",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                sku = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                category_id = table.Column<Guid>(type: "uuid", nullable: true),
                base_unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                barcode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                cost_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                selling_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                tax_rate = table.Column<decimal>(type: "numeric(6,5)", precision: 6, scale: 5, nullable: true),
                reorder_level = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                reorder_quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                image_file_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                table.PrimaryKey("pk_products", x => x.id);
                table.UniqueConstraint("ak_products_tenant_id_id", x => new { x.tenant_id, x.id });
                table.CheckConstraint("ck_products_prices_non_negative", "cost_price >= 0 AND selling_price >= 0");
                table.CheckConstraint("ck_products_tax_rate", "tax_rate IS NULL OR (tax_rate >= 0 AND tax_rate <= 1)");
                table.CheckConstraint("ck_products_thresholds_non_negative", "reorder_level >= 0 AND reorder_quantity >= 0");
                table.ForeignKey(
                    name: "fk_products_product_categories_tenant_id_category_id",
                    columns: x => new { x.tenant_id, x.category_id },
                    principalTable: "product_categories",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_products_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_products_units_of_measure_tenant_id_base_unit_id",
                    columns: x => new { x.tenant_id, x.base_unit_id },
                    principalTable: "units_of_measure",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "stock_transfers",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                source_warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                destination_warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                requested_by = table.Column<Guid>(type: "uuid", nullable: false),
                requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                dispatched_by = table.Column<Guid>(type: "uuid", nullable: true),
                dispatched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                received_by = table.Column<Guid>(type: "uuid", nullable: true),
                received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                cancelled_by = table.Column<Guid>(type: "uuid", nullable: true),
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
                table.PrimaryKey("pk_stock_transfers", x => x.id);
                table.UniqueConstraint("ak_stock_transfers_tenant_id_id", x => new { x.tenant_id, x.id });
                table.CheckConstraint("ck_transfers_distinct_warehouses", "source_warehouse_id <> destination_warehouse_id");
                table.ForeignKey(
                    name: "fk_stock_transfers_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_stock_transfers_warehouses_tenant_id_destination_warehouse_",
                    columns: x => new { x.tenant_id, x.destination_warehouse_id },
                    principalTable: "warehouses",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_stock_transfers_warehouses_tenant_id_source_warehouse_id",
                    columns: x => new { x.tenant_id, x.source_warehouse_id },
                    principalTable: "warehouses",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "warehouse_assignments",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: true),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                updated_by = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_warehouse_assignments", x => x.id);
                table.ForeignKey(
                    name: "fk_warehouse_assignments_tenant_memberships_tenant_id_membersh",
                    columns: x => new { x.tenant_id, x.membership_id },
                    principalTable: "tenant_memberships",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_warehouse_assignments_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_warehouse_assignments_warehouses_tenant_id_warehouse_id",
                    columns: x => new { x.tenant_id, x.warehouse_id },
                    principalTable: "warehouses",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "refresh_tokens",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                session_id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by_ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                revoked_reason = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                replaced_by_token_id = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_refresh_tokens", x => x.id);
                table.ForeignKey(
                    name: "fk_refresh_tokens_user_sessions_session_id",
                    column: x => x.session_id,
                    principalTable: "user_sessions",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "inventory_items",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                product_id = table.Column<Guid>(type: "uuid", nullable: false),
                quantity_on_hand = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                quantity_reserved = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                average_unit_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                last_movement_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: true),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_inventory_items", x => x.id);
                table.UniqueConstraint("ak_inventory_items_tenant_id_id", x => new { x.tenant_id, x.id });
                table.CheckConstraint("ck_inventory_on_hand_non_negative", "quantity_on_hand >= 0");
                table.CheckConstraint("ck_inventory_reserved_non_negative", "quantity_reserved >= 0");
                table.CheckConstraint("ck_inventory_reserved_within_on_hand", "quantity_reserved <= quantity_on_hand");
                table.ForeignKey(
                    name: "fk_inventory_items_products_tenant_id_product_id",
                    columns: x => new { x.tenant_id, x.product_id },
                    principalTable: "products",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_inventory_items_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_inventory_items_warehouses_tenant_id_warehouse_id",
                    columns: x => new { x.tenant_id, x.warehouse_id },
                    principalTable: "warehouses",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "product_units",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                product_id = table.Column<Guid>(type: "uuid", nullable: false),
                unit_of_measure_id = table.Column<Guid>(type: "uuid", nullable: false),
                conversion_factor = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                barcode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: true),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_product_units", x => x.id);
                table.CheckConstraint("ck_product_units_factor_positive", "conversion_factor > 0");
                table.ForeignKey(
                    name: "fk_product_units_products_tenant_id_product_id",
                    columns: x => new { x.tenant_id, x.product_id },
                    principalTable: "products",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_product_units_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_product_units_units_of_measure_tenant_id_unit_of_measure_id",
                    columns: x => new { x.tenant_id, x.unit_of_measure_id },
                    principalTable: "units_of_measure",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "stock_adjustments",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                product_id = table.Column<Guid>(type: "uuid", nullable: false),
                reason = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                quantity_change = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                system_quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                counted_quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                requested_by = table.Column<Guid>(type: "uuid", nullable: false),
                requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                reviewed_by = table.Column<Guid>(type: "uuid", nullable: true),
                reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                review_notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: true),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_stock_adjustments", x => x.id);
                table.ForeignKey(
                    name: "fk_stock_adjustments_products_tenant_id_product_id",
                    columns: x => new { x.tenant_id, x.product_id },
                    principalTable: "products",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_stock_adjustments_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_stock_adjustments_warehouses_tenant_id_warehouse_id",
                    columns: x => new { x.tenant_id, x.warehouse_id },
                    principalTable: "warehouses",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "stock_reservations",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                product_id = table.Column<Guid>(type: "uuid", nullable: false),
                quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                quantity_consumed = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                quantity_released = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                reference_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                reference_id = table.Column<Guid>(type: "uuid", nullable: true),
                reference_number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: true),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_stock_reservations", x => x.id);
                table.CheckConstraint("ck_reservations_quantity_positive", "quantity > 0");
                table.CheckConstraint("ck_reservations_within_quantity", "quantity_consumed >= 0 AND quantity_released >= 0 AND quantity_consumed + quantity_released <= quantity");
                table.ForeignKey(
                    name: "fk_stock_reservations_products_tenant_id_product_id",
                    columns: x => new { x.tenant_id, x.product_id },
                    principalTable: "products",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_stock_reservations_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_stock_reservations_warehouses_tenant_id_warehouse_id",
                    columns: x => new { x.tenant_id, x.warehouse_id },
                    principalTable: "warehouses",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "stock_transfer_lines",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                transfer_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                table.PrimaryKey("pk_stock_transfer_lines", x => x.id);
                table.CheckConstraint("ck_transfer_lines_quantity_positive", "quantity > 0");
                table.ForeignKey(
                    name: "fk_stock_transfer_lines_products_tenant_id_product_id",
                    columns: x => new { x.tenant_id, x.product_id },
                    principalTable: "products",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_stock_transfer_lines_stock_transfers_tenant_id_transfer_id",
                    columns: x => new { x.tenant_id, x.transfer_id },
                    principalTable: "stock_transfers",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_stock_transfer_lines_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "stock_movements",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                inventory_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                product_id = table.Column<Guid>(type: "uuid", nullable: false),
                type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                quantity_delta = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                reserved_delta = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                on_hand_after = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                reserved_after = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                unit_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                reference_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                reference_id = table.Column<Guid>(type: "uuid", nullable: true),
                reference_number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                correlation_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                created_by = table.Column<Guid>(type: "uuid", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_stock_movements", x => x.id);
                table.ForeignKey(
                    name: "fk_stock_movements_inventory_items_tenant_id_inventory_item_id",
                    columns: x => new { x.tenant_id, x.inventory_item_id },
                    principalTable: "inventory_items",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_stock_movements_products_tenant_id_product_id",
                    columns: x => new { x.tenant_id, x.product_id },
                    principalTable: "products",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_stock_movements_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_stock_movements_warehouses_tenant_id_warehouse_id",
                    columns: x => new { x.tenant_id, x.warehouse_id },
                    principalTable: "warehouses",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ix_audit_logs_tenant_id_entity_type_entity_id",
            table: "audit_logs",
            columns: new[] { "tenant_id", "entity_type", "entity_id" });

        migrationBuilder.CreateIndex(
            name: "ix_audit_logs_tenant_id_timestamp",
            table: "audit_logs",
            columns: new[] { "tenant_id", "timestamp" });

        migrationBuilder.CreateIndex(
            name: "ix_audit_logs_tenant_id_user_id",
            table: "audit_logs",
            columns: new[] { "tenant_id", "user_id" });

        migrationBuilder.CreateIndex(
            name: "ix_idempotency_records_expires_at",
            table: "idempotency_records",
            column: "expires_at");

        migrationBuilder.CreateIndex(
            name: "ix_idempotency_records_tenant_id_key",
            table: "idempotency_records",
            columns: new[] { "tenant_id", "key" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_inventory_items_tenant_id_product_id",
            table: "inventory_items",
            columns: new[] { "tenant_id", "product_id" });

        migrationBuilder.CreateIndex(
            name: "ix_inventory_items_tenant_id_warehouse_id_product_id",
            table: "inventory_items",
            columns: new[] { "tenant_id", "warehouse_id", "product_id" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_outbox_messages_deduplication_key",
            table: "outbox_messages",
            column: "deduplication_key",
            unique: true,
            filter: "deduplication_key IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "ix_outbox_messages_status_next_attempt_at",
            table: "outbox_messages",
            columns: new[] { "status", "next_attempt_at" });

        migrationBuilder.CreateIndex(
            name: "ix_outbox_messages_tenant_id_created_at",
            table: "outbox_messages",
            columns: new[] { "tenant_id", "created_at" });

        migrationBuilder.CreateIndex(
            name: "ix_product_categories_tenant_id_code",
            table: "product_categories",
            columns: new[] { "tenant_id", "code" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_product_categories_tenant_id_name",
            table: "product_categories",
            columns: new[] { "tenant_id", "name" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_product_categories_tenant_id_parent_id",
            table: "product_categories",
            columns: new[] { "tenant_id", "parent_id" });

        migrationBuilder.CreateIndex(
            name: "ix_product_units_tenant_id_barcode",
            table: "product_units",
            columns: new[] { "tenant_id", "barcode" },
            unique: true,
            filter: "barcode IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "ix_product_units_tenant_id_product_id_unit_of_measure_id",
            table: "product_units",
            columns: new[] { "tenant_id", "product_id", "unit_of_measure_id" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_product_units_tenant_id_unit_of_measure_id",
            table: "product_units",
            columns: new[] { "tenant_id", "unit_of_measure_id" });

        migrationBuilder.CreateIndex(
            name: "ix_products_tenant_id_barcode",
            table: "products",
            columns: new[] { "tenant_id", "barcode" },
            unique: true,
            filter: "barcode IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "ix_products_tenant_id_base_unit_id",
            table: "products",
            columns: new[] { "tenant_id", "base_unit_id" });

        migrationBuilder.CreateIndex(
            name: "ix_products_tenant_id_category_id",
            table: "products",
            columns: new[] { "tenant_id", "category_id" });

        migrationBuilder.CreateIndex(
            name: "ix_products_tenant_id_name",
            table: "products",
            columns: new[] { "tenant_id", "name" });

        migrationBuilder.CreateIndex(
            name: "ix_products_tenant_id_sku",
            table: "products",
            columns: new[] { "tenant_id", "sku" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_refresh_tokens_session_id",
            table: "refresh_tokens",
            column: "session_id");

        migrationBuilder.CreateIndex(
            name: "ix_refresh_tokens_token_hash",
            table: "refresh_tokens",
            column: "token_hash",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_role_claims_role_id",
            table: "role_claims",
            column: "role_id");

        migrationBuilder.CreateIndex(
            name: "RoleNameIndex",
            table: "roles",
            column: "normalized_name",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_scheduled_job_runs_job_name_run_key",
            table: "scheduled_job_runs",
            columns: new[] { "job_name", "run_key" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_stock_adjustments_tenant_id_number",
            table: "stock_adjustments",
            columns: new[] { "tenant_id", "number" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_stock_adjustments_tenant_id_product_id",
            table: "stock_adjustments",
            columns: new[] { "tenant_id", "product_id" });

        migrationBuilder.CreateIndex(
            name: "ix_stock_adjustments_tenant_id_status_requested_at",
            table: "stock_adjustments",
            columns: new[] { "tenant_id", "status", "requested_at" });

        migrationBuilder.CreateIndex(
            name: "ix_stock_adjustments_tenant_id_warehouse_id",
            table: "stock_adjustments",
            columns: new[] { "tenant_id", "warehouse_id" });

        migrationBuilder.CreateIndex(
            name: "ix_stock_movements_tenant_id_created_at",
            table: "stock_movements",
            columns: new[] { "tenant_id", "created_at" });

        migrationBuilder.CreateIndex(
            name: "ix_stock_movements_tenant_id_inventory_item_id",
            table: "stock_movements",
            columns: new[] { "tenant_id", "inventory_item_id" });

        migrationBuilder.CreateIndex(
            name: "ix_stock_movements_tenant_id_product_id_created_at",
            table: "stock_movements",
            columns: new[] { "tenant_id", "product_id", "created_at" });

        migrationBuilder.CreateIndex(
            name: "ix_stock_movements_tenant_id_reference_type_reference_id",
            table: "stock_movements",
            columns: new[] { "tenant_id", "reference_type", "reference_id" });

        migrationBuilder.CreateIndex(
            name: "ix_stock_movements_tenant_id_warehouse_id_created_at",
            table: "stock_movements",
            columns: new[] { "tenant_id", "warehouse_id", "created_at" });

        migrationBuilder.CreateIndex(
            name: "ix_stock_reservations_tenant_id_product_id",
            table: "stock_reservations",
            columns: new[] { "tenant_id", "product_id" });

        migrationBuilder.CreateIndex(
            name: "ix_stock_reservations_tenant_id_reference_type_reference_id",
            table: "stock_reservations",
            columns: new[] { "tenant_id", "reference_type", "reference_id" });

        migrationBuilder.CreateIndex(
            name: "ix_stock_reservations_tenant_id_status_expires_at",
            table: "stock_reservations",
            columns: new[] { "tenant_id", "status", "expires_at" });

        migrationBuilder.CreateIndex(
            name: "ix_stock_reservations_tenant_id_warehouse_id_product_id_status",
            table: "stock_reservations",
            columns: new[] { "tenant_id", "warehouse_id", "product_id", "status" });

        migrationBuilder.CreateIndex(
            name: "ix_stock_transfer_lines_tenant_id_product_id",
            table: "stock_transfer_lines",
            columns: new[] { "tenant_id", "product_id" });

        migrationBuilder.CreateIndex(
            name: "ix_stock_transfer_lines_tenant_id_transfer_id_product_id",
            table: "stock_transfer_lines",
            columns: new[] { "tenant_id", "transfer_id", "product_id" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_stock_transfers_tenant_id_destination_warehouse_id",
            table: "stock_transfers",
            columns: new[] { "tenant_id", "destination_warehouse_id" });

        migrationBuilder.CreateIndex(
            name: "ix_stock_transfers_tenant_id_number",
            table: "stock_transfers",
            columns: new[] { "tenant_id", "number" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_stock_transfers_tenant_id_source_warehouse_id",
            table: "stock_transfers",
            columns: new[] { "tenant_id", "source_warehouse_id" });

        migrationBuilder.CreateIndex(
            name: "ix_stock_transfers_tenant_id_status_requested_at",
            table: "stock_transfers",
            columns: new[] { "tenant_id", "status", "requested_at" });

        migrationBuilder.CreateIndex(
            name: "ix_stored_file_contents_tenant_id_file_id",
            table: "stored_file_contents",
            columns: new[] { "tenant_id", "file_id" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_stored_files_tenant_id_category",
            table: "stored_files",
            columns: new[] { "tenant_id", "category" });

        migrationBuilder.CreateIndex(
            name: "ix_tenant_memberships_tenant_id_user_id",
            table: "tenant_memberships",
            columns: new[] { "tenant_id", "user_id" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_tenant_memberships_user_id",
            table: "tenant_memberships",
            column: "user_id");

        migrationBuilder.CreateIndex(
            name: "ix_tenants_slug",
            table: "tenants",
            column: "slug",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_units_of_measure_tenant_id_code",
            table: "units_of_measure",
            columns: new[] { "tenant_id", "code" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_user_claims_user_id",
            table: "user_claims",
            column: "user_id");

        migrationBuilder.CreateIndex(
            name: "ix_user_logins_user_id",
            table: "user_logins",
            column: "user_id");

        migrationBuilder.CreateIndex(
            name: "ix_user_roles_role_id",
            table: "user_roles",
            column: "role_id");

        migrationBuilder.CreateIndex(
            name: "ix_user_sessions_user_id_revoked_at",
            table: "user_sessions",
            columns: new[] { "user_id", "revoked_at" });

        migrationBuilder.CreateIndex(
            name: "EmailIndex",
            table: "users",
            column: "normalized_email");

        migrationBuilder.CreateIndex(
            name: "UserNameIndex",
            table: "users",
            column: "normalized_user_name",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_warehouse_assignments_tenant_id_membership_id_warehouse_id",
            table: "warehouse_assignments",
            columns: new[] { "tenant_id", "membership_id", "warehouse_id" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_warehouse_assignments_tenant_id_warehouse_id",
            table: "warehouse_assignments",
            columns: new[] { "tenant_id", "warehouse_id" });

        migrationBuilder.CreateIndex(
            name: "ix_warehouses_tenant_id_code",
            table: "warehouses",
            columns: new[] { "tenant_id", "code" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "audit_logs");

        migrationBuilder.DropTable(
            name: "data_protection_keys");

        migrationBuilder.DropTable(
            name: "idempotency_records");

        migrationBuilder.DropTable(
            name: "outbox_messages");

        migrationBuilder.DropTable(
            name: "product_units");

        migrationBuilder.DropTable(
            name: "refresh_tokens");

        migrationBuilder.DropTable(
            name: "role_claims");

        migrationBuilder.DropTable(
            name: "scheduled_job_runs");

        migrationBuilder.DropTable(
            name: "stock_adjustments");

        migrationBuilder.DropTable(
            name: "stock_movements");

        migrationBuilder.DropTable(
            name: "stock_reservations");

        migrationBuilder.DropTable(
            name: "stock_transfer_lines");

        migrationBuilder.DropTable(
            name: "stored_file_contents");

        migrationBuilder.DropTable(
            name: "tenant_sequences");

        migrationBuilder.DropTable(
            name: "user_claims");

        migrationBuilder.DropTable(
            name: "user_logins");

        migrationBuilder.DropTable(
            name: "user_roles");

        migrationBuilder.DropTable(
            name: "user_tokens");

        migrationBuilder.DropTable(
            name: "warehouse_assignments");

        migrationBuilder.DropTable(
            name: "user_sessions");

        migrationBuilder.DropTable(
            name: "inventory_items");

        migrationBuilder.DropTable(
            name: "stock_transfers");

        migrationBuilder.DropTable(
            name: "stored_files");

        migrationBuilder.DropTable(
            name: "roles");

        migrationBuilder.DropTable(
            name: "tenant_memberships");

        migrationBuilder.DropTable(
            name: "products");

        migrationBuilder.DropTable(
            name: "warehouses");

        migrationBuilder.DropTable(
            name: "users");

        migrationBuilder.DropTable(
            name: "product_categories");

        migrationBuilder.DropTable(
            name: "units_of_measure");

        migrationBuilder.DropTable(
            name: "tenants");
    }
}
