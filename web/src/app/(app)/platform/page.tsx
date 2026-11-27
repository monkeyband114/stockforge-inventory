"use client";

import { keepPreviousData, useQuery, useQueryClient } from "@tanstack/react-query";
import { AlertOctagon, Building2, CircleCheck, Clock, Landmark, Plus, Users } from "lucide-react";
import { useState } from "react";
import { Button } from "@/components/ui/Button";
import { Card, StatCard } from "@/components/ui/Card";
import { ConfirmDialog } from "@/components/ui/ConfirmDialog";
import { EmptyState } from "@/components/ui/EmptyState";
import { ErrorState } from "@/components/ui/ErrorState";
import { PageHeader, Toolbar } from "@/components/ui/PageHeader";
import { Pagination } from "@/components/ui/Pagination";
import { StatGridSkeleton } from "@/components/ui/Skeleton";
import { Table, TBody, TD, TH, THead, TR } from "@/components/ui/Table";
import { useToast } from "@/components/ui/Toast";
import { FilterSelect, NoResults, QueryBoundary, SearchInput } from "@/features/common/QueryBoundary";
import { RequirePermission } from "@/features/common/RequirePermission";
import { TenantStatusBadge } from "@/features/common/StatusBadges";
import { useListParams } from "@/features/common/useListParams";
import { CreateOrganizationDialog } from "@/features/platform/CreateOrganizationDialog";
import { api } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { formatDate, formatDateTime, formatInteger } from "@/lib/format";
import { P } from "@/lib/permissions";
import { qk } from "@/lib/queries";
import { TENANT_STATUSES, type PagedResult, type PlatformStats, type TenantStatus, type TenantSummary } from "@/lib/types";

function PlatformContent() {
  const { hasPermission } = useAuth();
  const toast = useToast();
  const queryClient = useQueryClient();
  const list = useListParams({ status: "" as TenantStatus | "" });
  const [createOpen, setCreateOpen] = useState(false);
  const [pending, setPending] = useState<{ action: "suspend" | "activate"; tenant: TenantSummary } | null>(null);

  const stats = useQuery({ queryKey: qk.platformStats, queryFn: () => api.get<PlatformStats>("/api/platform/stats"), refetchInterval: 30_000 });

  const params = {
    page: list.page,
    pageSize: list.pageSize,
    search: list.debouncedSearch || undefined,
    status: list.filters.status || undefined,
  };
  const tenants = useQuery({
    queryKey: qk.tenants(params),
    queryFn: ({ signal }) => api.get<PagedResult<TenantSummary>>("/api/platform/tenants", params, signal),
    placeholderData: keepPreviousData,
  });

  const filtered = !!(list.debouncedSearch || list.filters.status);

  const runAction = async (notes: string) => {
    if (!pending) return;
    const { action, tenant } = pending;
    if (action === "suspend") await api.post(`/api/platform/tenants/${tenant.id}/suspend`, { reason: notes });
    else await api.post(`/api/platform/tenants/${tenant.id}/activate`);
    await queryClient.invalidateQueries({ queryKey: ["platform"] });
    toast.success(action === "suspend" ? "Organization suspended" : "Organization activated", tenant.name);
  };

  return (
    <>
      <PageHeader
        title="Organizations"
        description="Provision and manage tenant organizations across the StockForge platform."
        actions={
          <Button icon={<Plus className="size-4" />} onClick={() => setCreateOpen(true)}>
            Create organization
          </Button>
        }
      />

      <section aria-label="Platform statistics" className="mb-6">
        {stats.isPending ? (
          <StatGridSkeleton count={5} />
        ) : stats.isError ? (
          <Card>
            <ErrorState error={stats.error} onRetry={() => void stats.refetch()} />
          </Card>
        ) : (
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-5">
            <StatCard label="Organizations" value={formatInteger(stats.data.tenants)} icon={<Building2 />} tone="info" />
            <StatCard
              label="Active"
              value={formatInteger(stats.data.activeTenants)}
              icon={<CircleCheck />}
              tone="success"
              sub={stats.data.tenants - stats.data.activeTenants > 0 ? `${formatInteger(stats.data.tenants - stats.data.activeTenants)} suspended` : "None suspended"}
            />
            <StatCard label="Users" value={formatInteger(stats.data.users)} icon={<Users />} />
            <StatCard
              label="Failed jobs"
              value={formatInteger(stats.data.failedJobs)}
              icon={<AlertOctagon />}
              tone={stats.data.failedJobs > 0 ? "danger" : "neutral"}
              href={hasPermission(P.platformJobs) ? "/admin/jobs" : undefined}
              sub={hasPermission(P.platformJobs) ? "View background jobs" : undefined}
            />
            <StatCard label="Pending jobs" value={formatInteger(stats.data.pendingJobs)} icon={<Clock />} tone="warning" />
          </div>
        )}
      </section>

      <Card>
        <Toolbar>
          <SearchInput value={list.search} onChange={list.setSearch} placeholder="Search name, slug or email" label="Search organizations" />
          <FilterSelect
            label="Status"
            value={list.filters.status}
            onChange={(v) => list.setFilter("status", v as TenantStatus | "")}
            allLabel="All statuses"
            options={TENANT_STATUSES.map((s) => ({ value: s, label: s }))}
          />
        </Toolbar>
        <QueryBoundary
          query={tenants}
          columns={8}
          isEmpty={(d) => d.items.length === 0}
          empty={
            filtered ? (
              <NoResults what="organizations" onClear={list.reset} />
            ) : (
              <EmptyState
                icon={<Landmark />}
                title="No organizations yet"
                description="Create the first organization to get started."
                action={
                  <Button size="sm" icon={<Plus className="size-4" />} onClick={() => setCreateOpen(true)}>
                    Create organization
                  </Button>
                }
              />
            )
          }
        >
          {(data) => (
            <>
              <Table>
                <THead>
                  <tr>
                    <TH>Organization</TH>
                    <TH>Contact</TH>
                    <TH>Currency</TH>
                    <TH align="right">Members</TH>
                    <TH align="right">Warehouses</TH>
                    <TH align="right">Products</TH>
                    <TH>Status</TH>
                    <TH>Created</TH>
                    <TH align="right">
                      <span className="sr-only">Actions</span>
                    </TH>
                  </tr>
                </THead>
                <TBody>
                  {data.items.map((t) => (
                    <TR key={t.id}>
                      <TD>
                        <p className="font-medium text-slate-900">{t.name}</p>
                        <p className="font-mono text-xs text-slate-500">{t.slug}</p>
                      </TD>
                      <TD className="max-w-56 truncate text-slate-600">{t.contactEmail}</TD>
                      <TD className="font-mono text-xs">{t.baseCurrency}</TD>
                      <TD numeric>{formatInteger(t.memberCount)}</TD>
                      <TD numeric>{formatInteger(t.warehouseCount)}</TD>
                      <TD numeric>{formatInteger(t.productCount)}</TD>
                      <TD>
                        <TenantStatusBadge status={t.status} />
                        {t.status === "Suspended" && (
                          <p className="mt-1 max-w-48 truncate text-xs text-slate-500" title={t.suspensionReason ?? undefined}>
                            {t.suspendedAt ? `${formatDateTime(t.suspendedAt)}` : ""}
                            {t.suspensionReason ? ` · ${t.suspensionReason}` : ""}
                          </p>
                        )}
                      </TD>
                      <TD className="whitespace-nowrap text-slate-500">{formatDate(t.createdAt)}</TD>
                      <TD align="right">
                        {t.status === "Active" ? (
                          <Button variant="danger-ghost" size="sm" onClick={() => setPending({ action: "suspend", tenant: t })}>
                            Suspend
                          </Button>
                        ) : (
                          <Button variant="secondary" size="sm" onClick={() => setPending({ action: "activate", tenant: t })}>
                            Activate
                          </Button>
                        )}
                      </TD>
                    </TR>
                  ))}
                </TBody>
              </Table>
              <Pagination
                page={data.page}
                pageSize={data.pageSize}
                totalCount={data.totalCount}
                totalPages={data.totalPages}
                onPageChange={list.setPage}
                onPageSizeChange={list.setPageSize}
              />
            </>
          )}
        </QueryBoundary>
      </Card>

      <CreateOrganizationDialog open={createOpen} onClose={() => setCreateOpen(false)} />
      <ConfirmDialog
        open={!!pending}
        onClose={() => setPending(null)}
        title={pending?.action === "suspend" ? `Suspend ${pending.tenant.name}?` : `Activate ${pending?.tenant.name ?? "organization"}?`}
        description={
          pending?.action === "suspend"
            ? "All members are signed out immediately and cannot sign in until the organization is activated again. Data is retained."
            : "Members will be able to sign in and use the organization again."
        }
        confirmLabel={pending?.action === "suspend" ? "Suspend organization" : "Activate organization"}
        tone={pending?.action === "suspend" ? "danger" : "primary"}
        notes={pending?.action === "suspend" ? { label: "Reason", required: true, placeholder: "Visible to platform administrators", maxLength: 500 } : undefined}
        onConfirm={runAction}
      />
    </>
  );
}

export default function PlatformPage() {
  return (
    <RequirePermission anyOf={[P.platformTenants]} requireTenant={false}>
      <PlatformContent />
    </RequirePermission>
  );
}
