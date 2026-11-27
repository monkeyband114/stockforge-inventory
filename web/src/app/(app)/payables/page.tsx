"use client";

import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { ChevronRight, Download, Plus, Wallet } from "lucide-react";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { Suspense, useState } from "react";
import { Button } from "@/components/ui/Button";
import { Card } from "@/components/ui/Card";
import { ToggleChip } from "@/components/ui/Checkbox";
import { EmptyState } from "@/components/ui/EmptyState";
import { PageHeader, Toolbar } from "@/components/ui/PageHeader";
import { Pagination } from "@/components/ui/Pagination";
import { PageSpinner } from "@/components/ui/Spinner";
import { Table, TBody, TD, TH, THead, TR } from "@/components/ui/Table";
import { useToast } from "@/components/ui/Toast";
import { FilterSelect, NoResults, QueryBoundary, SearchInput } from "@/features/common/QueryBoundary";
import { RequirePermission } from "@/features/common/RequirePermission";
import { useListParams } from "@/features/common/useListParams";
import { BILL_STATUS_LABELS, MatchBadge, SupplierInvoiceStatusBadge } from "@/features/procurement/ProcurementBadges";
import { SupplierInvoiceDialog } from "@/features/procurement/SupplierInvoiceDialog";
import { SupplierPicker, type PickedSupplier } from "@/features/procurement/SupplierPicker";
import { api, fetchAllPages } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { cn } from "@/lib/cn";
import { downloadCsv, timestampedName, toCsv } from "@/lib/csv";
import { formatCurrency, formatDate } from "@/lib/format";
import { P } from "@/lib/permissions";
import { qk } from "@/lib/queries";
import { SUPPLIER_INVOICE_STATUSES, type PagedResult, type SupplierInvoiceStatus, type SupplierInvoiceSummary } from "@/lib/types";

export default function PayablesPage() {
  return (
    <RequirePermission anyOf={[P.payablesView]}>
      <Suspense fallback={<PageSpinner />}>
        <PayablesView />
      </Suspense>
    </RequirePermission>
  );
}

function PayablesView() {
  const params = useSearchParams();
  const router = useRouter();
  const { hasPermission } = useAuth();
  const toast = useToast();
  const [initial] = useState(() => {
    const s = params.get("status") ?? "";
    return {
      status: ((SUPPLIER_INVOICE_STATUSES as readonly string[]).includes(s) ? s : "") as SupplierInvoiceStatus | "",
      supplierId: params.get("supplierId") ?? "",
      outstandingOnly: ["true", "1"].includes(params.get("outstanding") ?? ""),
    };
  });
  const list = useListParams(initial, 25, params.get("search") ?? "");
  const [supplier, setSupplier] = useState<PickedSupplier | null>(null);
  const [creating, setCreating] = useState(false);
  const [exporting, setExporting] = useState(false);
  const f = list.filters;

  const query = {
    page: list.page,
    pageSize: list.pageSize,
    search: list.debouncedSearch || undefined,
    status: f.status || undefined,
    supplierId: f.supplierId || undefined,
    outstandingOnly: f.outstandingOnly || undefined,
  };
  const bills = useQuery({
    queryKey: qk.supplierInvoices(query),
    queryFn: ({ signal }) => api.get<PagedResult<SupplierInvoiceSummary>>("/api/supplier-invoices", query, signal),
    placeholderData: keepPreviousData,
  });

  const filtered = !!(query.search || query.status || query.supplierId || query.outstandingOnly);
  const clearFilters = () => {
    list.reset();
    list.setFilter("status", "");
    list.setFilter("supplierId", "");
    list.setFilter("outstandingOnly", false);
    setSupplier(null);
  };

  const exportCsv = async () => {
    setExporting(true);
    try {
      const { page: _p, pageSize: _s, ...filters } = query;
      const rows = await fetchAllPages<SupplierInvoiceSummary>("/api/supplier-invoices", filters);
      downloadCsv(
        timestampedName("payables"),
        toCsv(rows, [
          { header: "Reference", value: (r) => r.reference },
          { header: "Supplier invoice no.", value: (r) => r.supplierInvoiceNumber },
          { header: "Supplier", value: (r) => r.supplierName },
          { header: "Purchase order", value: (r) => r.purchaseOrderNumber },
          { header: "Status", value: (r) => r.status },
          { header: "Invoice date", value: (r) => r.invoiceDate },
          { header: "Due date", value: (r) => r.dueDate },
          { header: "Currency", value: (r) => r.currency },
          { header: "Total", value: (r) => r.total },
          { header: "Paid", value: (r) => r.amountPaid },
          { header: "Balance due", value: (r) => r.balanceDue },
          { header: "Matches receipts", value: (r) => (r.purchaseOrderId ? r.matchesReceipts : null) },
        ]),
      );
      toast.success(`Exported ${rows.length} bills`);
    } catch (e) {
      toast.error(e);
    } finally {
      setExporting(false);
    }
  };

  return (
    <>
      <PageHeader
        title="Payables"
        description="Supplier invoices (bills), their three-way match against goods received, and payments."
        actions={
          <>
            <Button variant="secondary" icon={<Download className="size-4" />} onClick={exportCsv} loading={exporting} disabled={!bills.data?.totalCount}>
              Export CSV
            </Button>
            {hasPermission(P.payablesManage) && (
              <Button icon={<Plus className="size-4" />} onClick={() => setCreating(true)}>
                Record supplier invoice
              </Button>
            )}
          </>
        }
      />
      <Card>
        <Toolbar>
          <SearchInput value={list.search} onChange={list.setSearch} placeholder="Reference or supplier invoice no." label="Search bills" />
          <FilterSelect
            label="Status"
            value={f.status}
            onChange={(v) => list.setFilter("status", v as SupplierInvoiceStatus | "")}
            allLabel="Any status"
            options={SUPPLIER_INVOICE_STATUSES.map((s) => ({ value: s, label: BILL_STATUS_LABELS[s] }))}
          />
          {hasPermission(P.suppliersView) && (
            <div className="w-64">
              <SupplierPicker
                value={supplier ?? (f.supplierId ? { id: f.supplierId, code: "", name: "Selected supplier" } : null)}
                onChange={(s) => {
                  setSupplier(s);
                  list.setFilter("supplierId", s?.id ?? "");
                }}
              />
            </div>
          )}
          <ToggleChip checked={f.outstandingOnly} onChange={(v) => list.setFilter("outstandingOnly", v)}>
            Outstanding only
          </ToggleChip>
          {filtered && (
            <Button variant="ghost" size="sm" onClick={clearFilters}>
              Clear filters
            </Button>
          )}
        </Toolbar>
        <QueryBoundary
          query={bills}
          columns={8}
          isEmpty={(d) => d.items.length === 0}
          empty={
            filtered ? (
              <NoResults onClear={clearFilters} what="bills" />
            ) : (
              <EmptyState
                icon={<Wallet />}
                title="No supplier invoices yet"
                description="Record suppliers' bills to track what you owe and when it is due."
                action={
                  hasPermission(P.payablesManage) ? (
                    <Button size="sm" icon={<Plus className="size-4" />} onClick={() => setCreating(true)}>
                      Record supplier invoice
                    </Button>
                  ) : undefined
                }
              />
            )
          }
        >
          {(data) => (
            <>
              <div className={bills.isPlaceholderData ? "opacity-60 transition-opacity" : undefined}>
                <Table>
                  <THead>
                    <tr>
                      <TH>Bill</TH>
                      <TH>Supplier</TH>
                      <TH>PO</TH>
                      <TH>Match</TH>
                      <TH>Status</TH>
                      <TH>Invoice date</TH>
                      <TH>Due</TH>
                      <TH align="right">Total</TH>
                      <TH align="right">Balance</TH>
                      <TH>
                        <span className="sr-only">Open</span>
                      </TH>
                    </tr>
                  </THead>
                  <TBody>
                    {data.items.map((b) => (
                      <TR key={b.id} interactive onClick={() => router.push(`/payables/${b.id}`)}>
                        <TD>
                          <Link href={`/payables/${b.id}`} onClick={(e) => e.stopPropagation()} className="font-mono text-xs font-semibold text-slate-900 hover:text-brand-700 hover:underline">
                            {b.reference}
                          </Link>
                          <div className="font-mono text-xs text-slate-500">{b.supplierInvoiceNumber}</div>
                        </TD>
                        <TD className="max-w-56 truncate font-medium text-slate-800">{b.supplierName}</TD>
                        <TD className="font-mono text-xs">
                          {b.purchaseOrderId && b.purchaseOrderNumber ? (
                            hasPermission(P.purchasingView) ? (
                              <Link href={`/purchase-orders/${b.purchaseOrderId}`} onClick={(e) => e.stopPropagation()} className="hover:text-brand-700 hover:underline">
                                {b.purchaseOrderNumber}
                              </Link>
                            ) : (
                              b.purchaseOrderNumber
                            )
                          ) : (
                            "—"
                          )}
                        </TD>
                        <TD>
                          <MatchBadge matches={b.matchesReceipts} hasPo={!!b.purchaseOrderId} />
                        </TD>
                        <TD>
                          <SupplierInvoiceStatusBadge status={b.status} />
                        </TD>
                        <TD className="whitespace-nowrap text-xs text-slate-600">{formatDate(b.invoiceDate)}</TD>
                        <TD className={cn("whitespace-nowrap text-xs", b.status === "Overdue" ? "font-semibold text-red-600" : "text-slate-600")}>{formatDate(b.dueDate)}</TD>
                        <TD numeric>{formatCurrency(b.total, b.currency)}</TD>
                        <TD numeric className={b.balanceDue > 0 ? "font-semibold text-slate-900" : "text-slate-400"}>
                          {formatCurrency(b.balanceDue, b.currency)}
                        </TD>
                        <TD align="right">
                          <ChevronRight className="size-4 text-slate-400" aria-hidden />
                        </TD>
                      </TR>
                    ))}
                  </TBody>
                </Table>
              </div>
              <Pagination page={data.page} pageSize={data.pageSize} totalCount={data.totalCount} totalPages={data.totalPages} onPageChange={list.setPage} onPageSizeChange={list.setPageSize} />
            </>
          )}
        </QueryBoundary>
      </Card>
      <SupplierInvoiceDialog open={creating} onClose={() => setCreating(false)} presetSupplier={supplier} />
    </>
  );
}
