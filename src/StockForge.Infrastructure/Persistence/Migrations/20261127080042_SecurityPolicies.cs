using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockForge.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Database-level security that does not depend on application code:
    /// <list type="bullet">
    /// <item>a least-privileged runtime role (<c>stockforge_app</c>) that does not own the tables, so RLS applies to it;</item>
    /// <item>append-only ledger and audit tables (privileges revoked + a trigger that rejects UPDATE/DELETE for everyone);</item>
    /// <item>row-level security on every tenant-owned table, keyed on the <c>app.tenant_id</c> session setting;</item>
    /// <item>an aggregate-only SECURITY DEFINER function for the platform administrator's cross-tenant counts.</item>
    /// </list>
    /// Any later migration that adds a tenant-owned table must end with <c>SELECT stockforge_apply_tenant_rls();</c>
    /// (an integration test fails if a table with a tenant_id column lacks RLS).
    /// </summary>
    public partial class _20261127080042_SecurityPolicies : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'stockforge_app') THEN
                        CREATE ROLE stockforge_app NOLOGIN;
                    END IF;
                END $$;

                GRANT USAGE ON SCHEMA public TO stockforge_app;
                GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO stockforge_app;
                GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO stockforge_app;
                ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO stockforge_app;
                ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO stockforge_app;
                REVOKE ALL ON TABLE __ef_migrations_history FROM stockforge_app;
                """);

            // ---- Append-only tables ----
            migrationBuilder.Sql("""
                REVOKE UPDATE, DELETE, TRUNCATE ON stock_movements, audit_logs FROM stockforge_app;

                CREATE OR REPLACE FUNCTION stockforge_reject_mutation() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Table % is append-only', TG_TABLE_NAME USING ERRCODE = 'insufficient_privilege';
                END $$;

                CREATE TRIGGER stock_movements_append_only BEFORE UPDATE OR DELETE ON stock_movements
                    FOR EACH ROW EXECUTE FUNCTION stockforge_reject_mutation();
                CREATE TRIGGER stock_movements_no_truncate BEFORE TRUNCATE ON stock_movements
                    FOR EACH STATEMENT EXECUTE FUNCTION stockforge_reject_mutation();
                CREATE TRIGGER audit_logs_append_only BEFORE UPDATE OR DELETE ON audit_logs
                    FOR EACH ROW EXECUTE FUNCTION stockforge_reject_mutation();
                CREATE TRIGGER audit_logs_no_truncate BEFORE TRUNCATE ON audit_logs
                    FOR EACH STATEMENT EXECUTE FUNCTION stockforge_reject_mutation();
                """);

            // ---- Row-level security ----
            migrationBuilder.Sql($"""
                CREATE OR REPLACE FUNCTION stockforge_current_tenant() RETURNS uuid
                LANGUAGE sql STABLE AS $$
                    SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid
                $$;

                CREATE OR REPLACE FUNCTION stockforge_apply_tenant_rls() RETURNS void
                LANGUAGE plpgsql AS $$
                DECLARE t record;
                BEGIN
                    FOR t IN
                        SELECT c.table_name
                        FROM information_schema.columns c
                        JOIN information_schema.tables tb ON tb.table_schema = c.table_schema AND tb.table_name = c.table_name
                        WHERE c.table_schema = 'public' AND c.column_name = 'tenant_id' AND tb.table_type = 'BASE TABLE'
                          AND c.table_name NOT IN ({TenantRls.ExclusionSqlList})
                    LOOP
                        EXECUTE format('ALTER TABLE %I ENABLE ROW LEVEL SECURITY', t.table_name);
                        IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE schemaname = 'public' AND tablename = t.table_name AND policyname = 'tenant_isolation') THEN
                            EXECUTE format(
                                'CREATE POLICY tenant_isolation ON %I USING (tenant_id = stockforge_current_tenant()) WITH CHECK (tenant_id = stockforge_current_tenant())',
                                t.table_name);
                        END IF;
                    END LOOP;
                END $$;

                SELECT stockforge_apply_tenant_rls();

                -- Audit log: reads are tenant-isolated; inserts are always allowed (sign-in events are written
                -- before a tenant is selected). UPDATE/DELETE are impossible (see above).
                ALTER TABLE audit_logs ENABLE ROW LEVEL SECURITY;
                CREATE POLICY audit_read ON audit_logs FOR SELECT USING (tenant_id = stockforge_current_tenant());
                CREATE POLICY audit_insert ON audit_logs FOR INSERT WITH CHECK (true);
                """);

            // ---- Platform aggregates (bypass RLS as the owner, but expose counts only) ----
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION platform_tenant_stats()
                RETURNS TABLE (tenant_id uuid, warehouses int, products int)
                LANGUAGE sql STABLE SECURITY DEFINER SET search_path = public AS $$
                    SELECT t.id,
                           (SELECT count(*)::int FROM warehouses w WHERE w.tenant_id = t.id),
                           (SELECT count(*)::int FROM products p WHERE p.tenant_id = t.id)
                    FROM tenants t
                $$;
                REVOKE ALL ON FUNCTION platform_tenant_stats() FROM PUBLIC;
                GRANT EXECUTE ON FUNCTION platform_tenant_stats() TO stockforge_app;
                GRANT EXECUTE ON FUNCTION stockforge_current_tenant() TO stockforge_app;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP FUNCTION IF EXISTS platform_tenant_stats();
                DROP POLICY IF EXISTS audit_read ON audit_logs;
                DROP POLICY IF EXISTS audit_insert ON audit_logs;
                ALTER TABLE audit_logs DISABLE ROW LEVEL SECURITY;
                DO $$
                DECLARE t record;
                BEGIN
                    FOR t IN SELECT tablename FROM pg_policies WHERE schemaname = 'public' AND policyname = 'tenant_isolation' LOOP
                        EXECUTE format('DROP POLICY tenant_isolation ON %I', t.tablename);
                        EXECUTE format('ALTER TABLE %I DISABLE ROW LEVEL SECURITY', t.tablename);
                    END LOOP;
                END $$;
                DROP FUNCTION IF EXISTS stockforge_apply_tenant_rls();
                DROP FUNCTION IF EXISTS stockforge_current_tenant();
                DROP TRIGGER IF EXISTS stock_movements_append_only ON stock_movements;
                DROP TRIGGER IF EXISTS stock_movements_no_truncate ON stock_movements;
                DROP TRIGGER IF EXISTS audit_logs_append_only ON audit_logs;
                DROP TRIGGER IF EXISTS audit_logs_no_truncate ON audit_logs;
                DROP FUNCTION IF EXISTS stockforge_reject_mutation();
                GRANT UPDATE, DELETE, TRUNCATE ON stock_movements, audit_logs TO stockforge_app;
                """);
        }
    }
}
