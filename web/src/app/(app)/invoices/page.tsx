"use client";

import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { ChevronRight, Download, Receipt } from "lucide-react";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { Suspense, useState } from "react";
import { Button } from "@/components/ui/Button";
import { Card } from "@/components/ui/Card";
import { ToggleChip } from "@/components/ui/Checkbox";
import { controlClasses } from "@/components/ui/Field";
import { EmptyState } from "@/components/ui/EmptyState";
import { PageHeader, Toolbar } from "@/components/ui/PageHeader";
import { Pagination } from "@/components/ui/Pagination";
import { PageSpinner } from "@/components/ui/Spinner";
import { Table, TBody, TD, TH, THead, TR } from "@/components/ui/Table";
import { useToast } from "@/components/ui/Toast";
import { FilterSelect, NoResults, QueryBoundary, SearchInput } from "@/features/common/QueryBoundary";
import { RequirePermission } from "@/features/common/RequirePermission";
import { useListParams } from "@/features/common/useListParams";
import { CustomerPicker, type PickedCustomer } from "@/features/sales/CustomerPicker";
import { INVOICE_STATUS_LABELS, InvoiceStatusBadge } from "@/features/sales/SalesBadges";
import { api, fetchAllPages } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { cn } from "@/lib/cn";
import { downloadCsv, timestampedName, toCsv } from "@/lib/csv";
import { formatCurrency, formatDate } from "@/lib/format";
import { P } from "@/lib/permissions";
import { qk } from "@/lib/queries";
import { INVOICE_STATUSES, type InvoiceStatus, type InvoiceSummary, type PagedResult } from "@/lib/types";

export default function InvoicesPage() {
  return (
    <RequirePermission anyOf={[P.invoicesView]}>
      <Suspense fallback={<PageSpinner />}>
        <InvoicesView />
      </Suspense>
    </RequirePermission>
  );
}

function InvoicesView() {
  const params = useSearchParams();
  const router = useRouter();
  const { hasPermission } = useAuth();
  const toast = useToast();
  const [initial] = useState(() => {
    const s = params.get("status") ?? "";
    return {
      status: ((INVOICE_STATUSES as readonly string[]).includes(s) ? s : "") as InvoiceStatus | "",
      outstandingOnly: ["true", "1"].includes(params.get("outstanding") ?? ""),
      customerId: params.get("customerId") ?? "",
      from: "",
      to: "",
    };
  });
  const list = useListParams(initial, 25, params.get("search") ?? "");
  const [customer, setCustomer] = useState<PickedCustomer | null>(null);
  const [exporting, setExporting] = useState(false);
  const f = list.filters;

  const query = {
    page: list.page,
    pageSize: list.pageSize,
    search: list.debouncedSearch || undefined,
    status: f.status || undefined,
    customerId: f.customerId || undefined,
    outstandingOnly: f.outstandingOnly || undefined,
    from: f.from || undefined,
    to: f.to || undefined,
  };

  const invoices = useQuery({
    queryKey: qk.invoices(query),
    queryFn: ({ signal }) => api.get<PagedResult<InvoiceSummary>>("/api/invoices", query, signal),
    placeholderData: keepPreviousData,
  });

  const filtered = !!(query.search || query.status || query.customerId || query.outstandingOnly || query.from || query.to);
  const clearFilters = () => {
    list.reset();
    list.setFilter("status", "");
    list.setFilter("customerId", "");
    list.setFilter("outstandingOnly", false);
    list.setFilter("from", "");
    list.setFilter("to", "");
    setCustomer(null);
  };

  const exportCsv = async () => {
    setExporting(true);
    try {
      const { page: _p, pageSize: _s, ...filters } = query;
      const rows = await fetchAllPages<InvoiceSummary>("/api/invoices", filters);
      downloadCsv(
        timestampedName("invoices"),
        toCsv(rows, [
          { header: "Number", value: (r) => r.number },
          { header: "Issue date", value: (r) => r.issueDate },
          { header: "Due date", value: (r) => r.dueDate },
          { header: "Customer", value: (r) => r.customerName },
          { header: "Order", value: (r) => r.orderNumber },
          { header: "Status", value: (r) => r.status },
          { header: "Currency", value: (r) => r.currency },
          { header: "Total", value: (r) => r.total },
          { header: "Paid", value: (r) => r.amountPaid },
          { header: "Balance due", value: (r) => r.balanceDue },
        ]),
      );
      toast.success(`Exported ${rows.length} invoices`);
    } catch (e) {
      toast.error(e);
    } finally {
      setExporting(false);
    }
  };

  return (
    <>
      <PageHeader
        title="Invoices"
        description="Issued automatically when an order is approved. Payments and return credits reduce the balance."
        actions={
          <Button variant="secondary" icon={<Download className="size-4" />} onClick={exportCsv} loading={exporting} disabled={!invoices.data?.totalCount}>
            Export CSV
          </Button>
        }
      />
      <Card>
        <Toolbar>
          <SearchInput value={list.search} onChange={list.setSearch} placeholder="Invoice number or customer" label="Search invoices" />
          <FilterSelect
            label="Status"
            value={f.status}
            onChange={(v) => list.setFilter("status", v as InvoiceStatus | "")}
            allLabel="Any status"
            options={INVOICE_STATUSES.map((s) => ({ value: s, label: INVOICE_STATUS_LABELS[s] }))}
          />
          <ToggleChip checked={f.outstandingOnly} onChange={(v) => list.setFilter("outstandingOnly", v)}>
            Outstanding only
          </ToggleChip>
          {hasPermission(P.customersView) && (
            <div className="w-64">
              <CustomerPicker
                label="Customer"
                value={customer ?? (f.customerId ? { id: f.customerId, code: "", name: "Selected customer" } : null)}
                onChange={(c) => {
                  setCustomer(c);
                  list.setFilter("customerId", c?.id ?? "");
                }}
              />
            </div>
          )}
          <label className="flex flex-col gap-1">
            <span className="text-xs font-medium text-slate-500">Issued from</span>
            <input type="date" value={f.from} max={f.to || undefined} onChange={(e) => list.setFilter("from", e.target.value)} className={cn(controlClasses, "h-9 w-40")} />
          </label>
          <label className="flex flex-col gap-1">
            <span className="text-xs font-medium text-slate-500">to</span>
            <input type="date" value={f.to} min={f.from || undefined} onChange={(e) => list.setFilter("to", e.target.value)} className={cn(controlClasses, "h-9 w-40")} />
          </label>
          {filtered && (
            <Button variant="ghost" size="sm" onClick={clearFilters}>
              Clear filters
            </Button>
          )}
        </Toolbar>
        <QueryBoundary
          query={invoices}
          columns={8}
          isEmpty={(d) => d.items.length === 0}
          empty={
            filtered ? (
              <NoResults onClear={clearFilters} what="invoices" />
            ) : (
              <EmptyState icon={<Receipt />} title="No invoices yet" description="Invoices are created when sales orders are approved." />
            )
          }
        >
          {(data) => (
            <>
              <div className={invoices.isPlaceholderData ? "opacity-60 transition-opacity" : undefined}>
                <Table>
                  <THead>
                    <tr>
                      <TH>Invoice</TH>
                      <TH>Customer</TH>
                      <TH>Order</TH>
                      <TH>Status</TH>
                      <TH>Issued</TH>
                      <TH>Due</TH>
                      <TH align="right">Total</TH>
                      <TH align="right">Paid</TH>
                      <TH align="right">Balance</TH>
                      <TH>
                        <span className="sr-only">Open</span>
                      </TH>
                    </tr>
                  </THead>
                  <TBody>
                    {data.items.map((i) => (
                      <TR key={i.id} interactive onClick={() => router.push(`/invoices/${i.id}`)}>
                        <TD>
                          <Link href={`/invoices/${i.id}`} onClick={(e) => e.stopPropagation()} className="font-mono text-xs font-semibold text-slate-900 hover:text-brand-700 hover:underline">
                            {i.number}
                          </Link>
                        </TD>
                        <TD className="max-w-56 truncate font-medium text-slate-800">{i.customerName}</TD>
                        <TD className="font-mono text-xs">
                          {i.salesOrderId && i.orderNumber ? (
                            hasPermission(P.ordersView) ? (
                              <Link href={`/orders/${i.salesOrderId}`} onClick={(e) => e.stopPropagation()} className="hover:text-brand-700 hover:underline">
                                {i.orderNumber}
                              </Link>
                            ) : (
                              i.orderNumber
                            )
                          ) : (
                            "—"
                          )}
                        </TD>
                        <TD>
                          <InvoiceStatusBadge status={i.status} />
                        </TD>
                        <TD className="whitespace-nowrap text-xs text-slate-600">{formatDate(i.issueDate)}</TD>
                        <TD className={cn("whitespace-nowrap text-xs", i.status === "Overdue" ? "font-semibold text-red-600" : "text-slate-600")}>{formatDate(i.dueDate)}</TD>
                        <TD numeric>{formatCurrency(i.total, i.currency)}</TD>
                        <TD numeric className="text-slate-500">
                          {formatCurrency(i.amountPaid, i.currency)}
                        </TD>
                        <TD numeric className={i.balanceDue > 0 ? "font-semibold text-slate-900" : "text-slate-400"}>
                          {formatCurrency(i.balanceDue, i.currency)}
                        </TD>
                        <TD align="right">
                          <ChevronRight className="size-4 text-slate-400" aria-hidden />
                        </TD>
                      </TR>
                    ))}
                  </TBody>
                </Table>
              </div>
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
