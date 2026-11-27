"use client";

import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { ChevronRight, Download, ListChecks } from "lucide-react";
import { Fragment, useState } from "react";
import { Button } from "@/components/ui/Button";
import { Card } from "@/components/ui/Card";
import { EmptyState } from "@/components/ui/EmptyState";
import { controlClasses } from "@/components/ui/Field";
import { PageHeader, Toolbar } from "@/components/ui/PageHeader";
import { Pagination } from "@/components/ui/Pagination";
import { Table, TBody, TD, TH, THead, TR } from "@/components/ui/Table";
import { useToast } from "@/components/ui/Toast";
import { AuditActionBadge, AuditDiff } from "@/features/admin/AuditDiff";
import { FilterSelect, NoResults, QueryBoundary, SearchInput } from "@/features/common/QueryBoundary";
import { RequirePermission } from "@/features/common/RequirePermission";
import { useListParams } from "@/features/common/useListParams";
import { api, fetchAllPages } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { cn } from "@/lib/cn";
import { downloadCsv, timestampedName, toCsv } from "@/lib/csv";
import { endOfDayExclusiveIso, formatDateTime, fullName, startOfDayIso } from "@/lib/format";
import { P } from "@/lib/permissions";
import { qk } from "@/lib/queries";
import type { AuditLog, Member, PagedResult } from "@/lib/types";

const ENTITY_TYPES = [
  "Product",
  "ProductCategory",
  "UnitOfMeasure",
  "Warehouse",
  "InventoryItem",
  "StockAdjustment",
  "StockTransfer",
  "StockReservation",
  "Customer",
  "SalesOrder",
  "Shipment",
  "SalesReturn",
  "Invoice",
  "Supplier",
  "SupplierProduct",
  "PurchaseOrder",
  "GoodsReceipt",
  "SupplierInvoice",
  "Tenant",
  "TenantMembership",
  "User",
  "UserSession",
];

const ACTIONS = [
  "Created",
  "Updated",
  "Deleted",
  "SignedIn",
  "SignedOut",
  "SignInFailed",
  "AccountLocked",
  "AccountUnlocked",
  "PasswordChanged",
  "PasswordReset",
  "PasswordResetRequested",
  "SessionRevoked",
  "SessionsRevoked",
  "MemberInvited",
  "InvitationAccepted",
  "OrganizationRegistered",
  "OrganizationCreated",
  "TenantSuspended",
  "TenantActivated",
  "OrderRejected",
  "OrderCancelled",
  "PaymentRecorded",
  "ReturnReceived",
  "PurchaseOrderApproved",
  "PurchaseOrderRejected",
  "PurchaseOrderClosedShort",
  "GoodsReceived",
  "SupplierInvoiceRecorded",
  "SupplierPaymentRecorded",
  "RefreshTokenReuseDetected",
];

const splitWords = (s: string) => s.replace(/([a-z])([A-Z])/g, "$1 $2");

function DateFilter({ label, value, onChange, min, max }: { label: string; value: string; onChange: (v: string) => void; min?: string; max?: string }) {
  return (
    <label className="flex items-center gap-2 text-xs font-medium text-slate-500">
      {label}
      <input type="date" value={value} min={min} max={max} onChange={(e) => onChange(e.target.value)} className={cn(controlClasses, "h-9 w-40")} />
    </label>
  );
}

function AuditContent() {
  const { hasPermission } = useAuth();
  const toast = useToast();
  const canListUsers = hasPermission(P.usersView);
  const list = useListParams({ entityType: "", action: "", userId: "", from: "", to: "" });
  const [expanded, setExpanded] = useState<Set<string>>(new Set());
  const [exporting, setExporting] = useState(false);

  const members = useQuery({
    queryKey: qk.users({ pageSize: 200, all: true }),
    queryFn: () => api.get<PagedResult<Member>>("/api/users", { pageSize: 200 }),
    enabled: canListUsers,
    staleTime: 5 * 60_000,
  });

  const f = list.filters;
  const params = {
    page: list.page,
    pageSize: list.pageSize,
    search: list.debouncedSearch || undefined,
    entityType: f.entityType || undefined,
    action: f.action || undefined,
    userId: f.userId || undefined,
    from: f.from ? startOfDayIso(f.from) : undefined,
    to: f.to ? endOfDayExclusiveIso(f.to) : undefined,
  };
  const query = useQuery({
    queryKey: qk.audit(params),
    queryFn: ({ signal }) => api.get<PagedResult<AuditLog>>("/api/audit-logs", params, signal),
    placeholderData: keepPreviousData,
  });

  const filtered = !!(list.debouncedSearch || f.entityType || f.action || f.userId || f.from || f.to);

  const toggle = (id: string) =>
    setExpanded((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });

  const exportCsv = async () => {
    setExporting(true);
    try {
      const { page: _page, pageSize: _size, ...filters } = params;
      const rows = await fetchAllPages<AuditLog>("/api/audit-logs", filters, 5_000);
      const csv = toCsv(rows, [
        { header: "Timestamp", value: (r) => r.timestamp },
        { header: "User", value: (r) => r.userEmail },
        { header: "Action", value: (r) => r.action },
        { header: "Entity type", value: (r) => r.entityType },
        { header: "Entity ID", value: (r) => r.entityId },
        { header: "Changed properties", value: (r) => r.changedProperties?.join("; ") },
        { header: "Old values", value: (r) => (r.oldValues ? JSON.stringify(r.oldValues) : "") },
        { header: "New values", value: (r) => (r.newValues ? JSON.stringify(r.newValues) : "") },
        { header: "IP address", value: (r) => r.ipAddress },
        { header: "Correlation ID", value: (r) => r.correlationId },
      ]);
      downloadCsv(timestampedName("audit-log"), csv);
      toast.success("Export ready", `${rows.length} audit entries exported.`);
    } catch (err) {
      toast.error(err);
    } finally {
      setExporting(false);
    }
  };

  return (
    <>
      <PageHeader
        title="Audit log"
        description="An immutable record of changes and security events in this organization."
        actions={
          <Button variant="secondary" icon={<Download className="size-4" />} onClick={exportCsv} loading={exporting} disabled={!query.data?.totalCount}>
            Export CSV
          </Button>
        }
      />
      <Card>
        <Toolbar>
          <SearchInput value={list.search} onChange={list.setSearch} placeholder="Search user email or entity" label="Search audit log" />
          <FilterSelect
            label="Entity type"
            value={f.entityType}
            onChange={(v) => list.setFilter("entityType", v)}
            allLabel="All entities"
            options={ENTITY_TYPES.map((t) => ({ value: t, label: splitWords(t) }))}
          />
          <FilterSelect
            label="Action"
            value={f.action}
            onChange={(v) => list.setFilter("action", v)}
            allLabel="All actions"
            options={ACTIONS.map((a) => ({ value: a, label: splitWords(a) }))}
          />
          {canListUsers && (
            <FilterSelect
              label="User"
              value={f.userId}
              onChange={(v) => list.setFilter("userId", v)}
              allLabel="All users"
              options={(members.data?.items ?? []).map((m) => ({ value: m.userId, label: `${fullName(m)} (${m.email})` }))}
            />
          )}
          <DateFilter label="From" value={f.from} max={f.to || undefined} onChange={(v) => list.setFilter("from", v)} />
          <DateFilter label="To" value={f.to} min={f.from || undefined} onChange={(v) => list.setFilter("to", v)} />
        </Toolbar>
        <QueryBoundary
          query={query}
          columns={6}
          isEmpty={(d) => d.items.length === 0}
          empty={
            filtered ? (
              <NoResults what="audit entries" onClear={list.reset} />
            ) : (
              <EmptyState icon={<ListChecks />} title="No audit entries yet" description="Changes to records and sign-in events will appear here." />
            )
          }
        >
          {(data) => (
            <>
              <Table>
                <THead>
                  <tr>
                    <TH className="w-10">
                      <span className="sr-only">Expand</span>
                    </TH>
                    <TH>Timestamp</TH>
                    <TH>User</TH>
                    <TH>Action</TH>
                    <TH>Entity</TH>
                    <TH>Changes</TH>
                  </tr>
                </THead>
                <TBody>
                  {data.items.map((log) => {
                    const open = expanded.has(log.id);
                    const detailId = `audit-detail-${log.id}`;
                    return (
                      <Fragment key={log.id}>
                        <TR interactive onClick={() => toggle(log.id)} className={cn(open && "bg-slate-50")}>
                          <TD className="pr-0">
                            <button
                              type="button"
                              onClick={(e) => {
                                e.stopPropagation();
                                toggle(log.id);
                              }}
                              aria-expanded={open}
                              aria-controls={detailId}
                              aria-label={open ? "Hide details" : "Show details"}
                              className="rounded p-1 text-slate-400 hover:bg-slate-100 hover:text-slate-700"
                            >
                              <ChevronRight className={cn("size-4 transition-transform", open && "rotate-90")} />
                            </button>
                          </TD>
                          <TD className="whitespace-nowrap tabular-nums text-slate-600">{formatDateTime(log.timestamp)}</TD>
                          <TD className="max-w-56 truncate">{log.userEmail ?? <span className="text-slate-400">System</span>}</TD>
                          <TD>
                            <AuditActionBadge action={log.action} />
                          </TD>
                          <TD>
                            <span className="font-medium text-slate-800">{splitWords(log.entityType)}</span>
                            {log.entityId && (
                              <span className="ml-2 font-mono text-xs text-slate-400" title={log.entityId}>
                                {log.entityId.length > 13 ? `${log.entityId.slice(0, 8)}…${log.entityId.slice(-4)}` : log.entityId}
                              </span>
                            )}
                          </TD>
                          <TD className="max-w-sm truncate text-xs text-slate-500">
                            {log.changedProperties?.length
                              ? log.changedProperties.join(", ")
                              : log.newValues && typeof log.newValues === "object" && !Array.isArray(log.newValues)
                                ? `${Object.keys(log.newValues).length} field${Object.keys(log.newValues).length === 1 ? "" : "s"}`
                                : "—"}
                          </TD>
                        </TR>
                        {open && (
                          <tr id={detailId}>
                            <td colSpan={6} className="border-b border-slate-200 bg-slate-50 px-4 py-3 sm:pl-14">
                              <AuditDiff log={log} />
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
    </>
  );
}

export default function AuditPage() {
  return (
    <RequirePermission anyOf={[P.auditView]}>
      <AuditContent />
    </RequirePermission>
  );
}
