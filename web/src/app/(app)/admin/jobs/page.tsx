"use client";

import { keepPreviousData, useQuery, useQueryClient } from "@tanstack/react-query";
import { AlertOctagon, CheckCircle2, Clock, Loader, RotateCcw, ServerCog } from "lucide-react";
import { Fragment, useMemo, useState } from "react";
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
import { FilterSelect, NoResults, QueryBoundary } from "@/features/common/QueryBoundary";
import { RequirePermission } from "@/features/common/RequirePermission";
import { JobStatusBadge } from "@/features/common/StatusBadges";
import { useListParams } from "@/features/common/useListParams";
import { api } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { cn } from "@/lib/cn";
import { formatDateTime, formatInteger, formatRelative } from "@/lib/format";
import { P } from "@/lib/permissions";
import { qk } from "@/lib/queries";
import { OUTBOX_STATUSES, type Job, type JobStats, type OutboxStatus, type PagedResult, type TenantSummary } from "@/lib/types";

const JOB_TYPES = [
  { value: "email.send", label: "Email delivery" },
  { value: "inventory.low_stock_alert", label: "Low-stock alert" },
  { value: "inventory.daily_summary", label: "Daily inventory summary" },
  { value: "billing.invoice_pdf", label: "Invoice PDF" },
  { value: "billing.invoice_email", label: "Invoice email" },
  { value: "sales.order_confirmation_email", label: "Order confirmation email" },
  { value: "sales.shipment_notification", label: "Shipment notification" },
];

const REFRESH_MS = 15_000;

function JobsContent() {
  const { hasAnyPermission, hasPermission, user } = useAuth();
  const toast = useToast();
  const queryClient = useQueryClient();
  const canRetry = hasAnyPermission(P.jobsManage, P.platformJobs);
  const platformView = !user?.tenant;
  const list = useListParams({ status: "" as OutboxStatus | "", type: "" });
  const [retrying, setRetrying] = useState<Job | null>(null);
  const [openErrors, setOpenErrors] = useState<Set<string>>(new Set());

  const stats = useQuery({
    queryKey: qk.jobStats,
    queryFn: () => api.get<JobStats>("/api/jobs/stats"),
    refetchInterval: REFRESH_MS,
  });

  const params = {
    page: list.page,
    pageSize: list.pageSize,
    status: list.filters.status || undefined,
    type: list.filters.type || undefined,
  };
  const jobs = useQuery({
    queryKey: qk.jobs(params),
    queryFn: ({ signal }) => api.get<PagedResult<Job>>("/api/jobs", params, signal),
    placeholderData: keepPreviousData,
    refetchInterval: REFRESH_MS,
  });

  // Platform administrators see every tenant's jobs; resolve tenant names for context.
  const tenants = useQuery({
    queryKey: qk.tenants({ pageSize: 200, lookup: true }),
    queryFn: () => api.get<PagedResult<TenantSummary>>("/api/platform/tenants", { pageSize: 200 }),
    enabled: platformView && hasPermission(P.platformTenants),
    staleTime: 5 * 60_000,
  });
  const tenantNames = useMemo(() => new Map((tenants.data?.items ?? []).map((t) => [t.id, t.name])), [tenants.data]);

  const filtered = !!(list.filters.status || list.filters.type);
  const columns = platformView ? 9 : 8;

  const retry = async () => {
    if (!retrying) return;
    await api.post(`/api/jobs/${retrying.id}/retry`);
    await queryClient.invalidateQueries({ queryKey: ["jobs"] });
    toast.success("Job queued for retry", retrying.type);
  };

  const toggleError = (id: string) =>
    setOpenErrors((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });

  return (
    <>
      <PageHeader
        title="Background jobs"
        description={
          platformView
            ? "Durable jobs across all organizations (emails, alerts, scheduled summaries). Payloads are never shown."
            : "Durable jobs for this organization (emails, alerts, scheduled summaries). Payloads are never shown."
        }
        meta={<span className="text-xs text-slate-500">Refreshes automatically every 15 seconds</span>}
      />

      <section aria-label="Job statistics" className="mb-6">
        {stats.isPending ? (
          <StatGridSkeleton count={4} />
        ) : stats.isError ? (
          <Card>
            <ErrorState error={stats.error} onRetry={() => void stats.refetch()} />
          </Card>
        ) : (
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
            <StatCard label="Pending" value={formatInteger(stats.data.pending)} icon={<Clock />} tone="warning" sub="Waiting for a worker" />
            <StatCard label="Processing" value={formatInteger(stats.data.processing)} icon={<Loader />} tone="info" sub="Currently running" />
            <StatCard label="Failed" value={formatInteger(stats.data.failed)} icon={<AlertOctagon />} tone={stats.data.failed > 0 ? "danger" : "neutral"} sub="Exhausted all attempts" />
            <StatCard label="Succeeded (24h)" value={formatInteger(stats.data.succeededLast24Hours)} icon={<CheckCircle2 />} tone="success" sub="Completed in the last day" />
          </div>
        )}
      </section>

      <Card>
        <Toolbar>
          <FilterSelect
            label="Status"
            value={list.filters.status}
            onChange={(v) => list.setFilter("status", v as OutboxStatus | "")}
            allLabel="All statuses"
            options={OUTBOX_STATUSES.map((s) => ({ value: s, label: s }))}
          />
          <FilterSelect label="Job type" value={list.filters.type} onChange={(v) => list.setFilter("type", v)} allLabel="All job types" options={JOB_TYPES} />
        </Toolbar>
        <QueryBoundary
          query={jobs}
          columns={columns}
          isEmpty={(d) => d.items.length === 0}
          empty={
            filtered ? (
              <NoResults what="jobs" onClear={list.reset} />
            ) : (
              <EmptyState icon={<ServerCog />} title="No background jobs" description="Jobs appear here when emails, alerts or scheduled summaries are queued." />
            )
          }
        >
          {(data) => (
            <>
              <Table>
                <THead>
                  <tr>
                    <TH>Type</TH>
                    {platformView && <TH>Organization</TH>}
                    <TH>Status</TH>
                    <TH align="right">Attempts</TH>
                    <TH>Created</TH>
                    <TH>Next attempt</TH>
                    <TH>Processed</TH>
                    <TH>Correlation ID</TH>
                    <TH align="right">
                      <span className="sr-only">Actions</span>
                    </TH>
                  </tr>
                </THead>
                <TBody>
                  {data.items.map((job) => {
                    const errorOpen = openErrors.has(job.id);
                    return (
                      <Fragment key={job.id}>
                        <TR>
                          <TD>
                            <span className="font-mono text-xs text-slate-800">{job.type}</span>
                            {job.lastError && (
                              <button
                                type="button"
                                onClick={() => toggleError(job.id)}
                                aria-expanded={errorOpen}
                                className="mt-0.5 block max-w-xs truncate text-left text-xs text-red-600 hover:underline"
                                title={errorOpen ? "Hide error" : job.lastError}
                              >
                                {errorOpen ? "Hide error" : job.lastError}
                              </button>
                            )}
                          </TD>
                          {platformView && (
                            <TD className="max-w-48 truncate text-slate-600">
                              {job.tenantId ? (tenantNames.get(job.tenantId) ?? <span className="font-mono text-xs">{job.tenantId.slice(0, 8)}…</span>) : "Platform"}
                            </TD>
                          )}
                          <TD>
                            <JobStatusBadge status={job.status} />
                          </TD>
                          <TD numeric className={cn(job.attempts >= job.maxAttempts && "text-red-600")}>
                            {job.attempts} / {job.maxAttempts}
                          </TD>
                          <TD className="whitespace-nowrap text-slate-500" title={formatDateTime(job.createdAt)}>
                            {formatRelative(job.createdAt)}
                          </TD>
                          <TD className="whitespace-nowrap text-slate-500">
                            {job.status === "Pending" || job.status === "Processing" ? formatDateTime(job.nextAttemptAt) : "—"}
                          </TD>
                          <TD className="whitespace-nowrap text-slate-500">{formatDateTime(job.processedAt)}</TD>
                          <TD className="max-w-40 truncate font-mono text-xs text-slate-500" title={job.correlationId ?? undefined}>
                            {job.correlationId ?? "—"}
                          </TD>
                          <TD align="right">
                            {canRetry && job.status === "Failed" && (
                              <Button variant="secondary" size="sm" icon={<RotateCcw className="size-3.5" />} onClick={() => setRetrying(job)}>
                                Retry
                              </Button>
                            )}
                          </TD>
                        </TR>
                        {errorOpen && job.lastError && (
                          <tr>
                            <td colSpan={columns} className="border-b border-slate-200 bg-red-50/50 px-4 py-3">
                              <p className="mb-1 text-xs font-semibold text-red-800">Last error</p>
                              <pre className="max-h-48 overflow-auto whitespace-pre-wrap break-words font-mono text-xs text-red-900">{job.lastError}</pre>
                            </td>
                          </tr>
                        )}
                      </Fragment>
                    );
                  })}
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

      <ConfirmDialog
        open={!!retrying}
        onClose={() => setRetrying(null)}
        title="Retry failed job"
        description={
          retrying ? `The ${retrying.type} job will be queued again with a fresh set of attempts. Only retry once the cause of the failure is fixed.` : undefined
        }
        confirmLabel="Retry job"
        onConfirm={retry}
      />
    </>
  );
}

export default function JobsPage() {
  return (
    <RequirePermission anyOf={[P.jobsView, P.platformJobs]} requireTenant={false}>
      <JobsContent />
    </RequirePermission>
  );
}
