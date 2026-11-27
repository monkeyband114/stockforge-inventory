"use client";

import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { Building2, ChevronRight, Download, Plus } from "lucide-react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState } from "react";
import { Button } from "@/components/ui/Button";
import { Card } from "@/components/ui/Card";
import { EmptyState } from "@/components/ui/EmptyState";
import { PageHeader, Toolbar } from "@/components/ui/PageHeader";
import { Pagination } from "@/components/ui/Pagination";
import { Table, TBody, TD, TH, THead, TR } from "@/components/ui/Table";
import { useToast } from "@/components/ui/Toast";
import { FilterSelect, NoResults, QueryBoundary, SearchInput } from "@/features/common/QueryBoundary";
import { RequirePermission } from "@/features/common/RequirePermission";
import { ActiveBadge } from "@/features/common/StatusBadges";
import { useListParams } from "@/features/common/useListParams";
import { SupplierFormDialog } from "@/features/procurement/SupplierFormDialog";
import { api, fetchAllPages } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { downloadCsv, timestampedName, toCsv } from "@/lib/csv";
import { formatCurrency, formatInteger } from "@/lib/format";
import { P } from "@/lib/permissions";
import { qk, useCurrency } from "@/lib/queries";
import type { PagedResult, Supplier } from "@/lib/types";

export default function SuppliersPage() {
  return (
    <RequirePermission anyOf={[P.suppliersView]}>
      <SuppliersView />
    </RequirePermission>
  );
}

function SuppliersView() {
  const { hasPermission } = useAuth();
  const router = useRouter();
  const toast = useToast();
  const currency = useCurrency();
  const canManage = hasPermission(P.suppliersManage);
  const list = useListParams({ status: "active" as "active" | "inactive" | "" });
  const [creating, setCreating] = useState(false);
  const [exporting, setExporting] = useState(false);

  const query = {
    page: list.page,
    pageSize: list.pageSize,
    search: list.debouncedSearch || undefined,
    isActive: list.filters.status === "" ? undefined : list.filters.status === "active",
  };
  const suppliers = useQuery({
    queryKey: qk.suppliers(query),
    queryFn: ({ signal }) => api.get<PagedResult<Supplier>>("/api/suppliers", query, signal),
    placeholderData: keepPreviousData,
  });
  const filtered = !!query.search || list.filters.status !== "active";

  const exportCsv = async () => {
    setExporting(true);
    try {
      const { page: _p, pageSize: _s, ...filters } = query;
      const rows = await fetchAllPages<Supplier>("/api/suppliers", filters);
      downloadCsv(
        timestampedName("suppliers"),
        toCsv(rows, [
          { header: "Code", value: (r) => r.code },
          { header: "Name", value: (r) => r.name },
          { header: "Contact", value: (r) => r.contactName },
          { header: "Email", value: (r) => r.email },
          { header: "Phone", value: (r) => r.phone },
          { header: "Payment terms (days)", value: (r) => r.paymentTermsDays },
          { header: "Lead time (days)", value: (r) => r.leadTimeDays },
          { header: "Products", value: (r) => r.productCount },
          { header: "Open POs", value: (r) => r.openPurchaseOrders },
          { header: `Outstanding payables (${currency})`, value: (r) => r.outstandingPayables },
          { header: "Active", value: (r) => r.isActive },
        ]),
      );
      toast.success(`Exported ${rows.length} suppliers`);
    } catch (e) {
      toast.error(e);
    } finally {
      setExporting(false);
    }
  };

  return (
    <>
      <PageHeader
        title="Suppliers"
        description="Vendors you buy from, with negotiated product terms, open purchase orders and unpaid bills."
        actions={
          <>
            <Button variant="secondary" icon={<Download className="size-4" />} onClick={exportCsv} loading={exporting} disabled={!suppliers.data?.totalCount}>
              Export CSV
            </Button>
            {canManage && (
              <Button icon={<Plus className="size-4" />} onClick={() => setCreating(true)}>
                New supplier
              </Button>
            )}
          </>
        }
      />
      <Card>
        <Toolbar>
          <SearchInput value={list.search} onChange={list.setSearch} placeholder="Name, code or email" label="Search suppliers" />
          <FilterSelect
            label="Status"
            value={list.filters.status}
            onChange={(v) => list.setFilter("status", v as "active" | "inactive" | "")}
            options={[
              { value: "active", label: "Active" },
              { value: "inactive", label: "Inactive" },
              { value: "", label: "All suppliers" },
            ]}
          />
        </Toolbar>
        <QueryBoundary
          query={suppliers}
          columns={7}
          isEmpty={(d) => d.items.length === 0}
          empty={
            filtered ? (
              <NoResults onClear={list.reset} what="suppliers" />
            ) : (
              <EmptyState
                icon={<Building2 />}
                title="No suppliers yet"
                description="Add suppliers to raise purchase orders and record their bills."
                action={
                  canManage ? (
                    <Button size="sm" icon={<Plus className="size-4" />} onClick={() => setCreating(true)}>
                      New supplier
                    </Button>
                  ) : undefined
                }
              />
            )
          }
        >
          {(data) => (
            <>
              <div className={suppliers.isPlaceholderData ? "opacity-60 transition-opacity" : undefined}>
                <Table>
                  <THead>
                    <tr>
                      <TH>Supplier</TH>
                      <TH>Contact</TH>
                      <TH align="right">Terms</TH>
                      <TH align="right">Lead time</TH>
                      <TH align="right">Products</TH>
                      <TH align="right">Open POs</TH>
                      <TH align="right">Outstanding payables</TH>
                      <TH>Status</TH>
                      <TH>
                        <span className="sr-only">Open</span>
                      </TH>
                    </tr>
                  </THead>
                  <TBody>
                    {data.items.map((s) => (
                      <TR key={s.id} interactive onClick={() => router.push(`/suppliers/${s.id}`)}>
                        <TD>
                          <Link href={`/suppliers/${s.id}`} onClick={(e) => e.stopPropagation()} className="font-medium text-slate-900 hover:text-brand-700 hover:underline">
                            {s.name}
                          </Link>
                          <div className="font-mono text-xs text-slate-500">{s.code}</div>
                        </TD>
                        <TD className="text-xs">
                          {s.contactName && <div className="text-slate-700">{s.contactName}</div>}
                          {s.email && <div className="text-slate-500">{s.email}</div>}
                          {!s.contactName && !s.email && <span className="text-slate-400">—</span>}
                        </TD>
                        <TD numeric className="text-slate-600">
                          {s.paymentTermsDays != null ? `${s.paymentTermsDays} d` : "—"}
                        </TD>
                        <TD numeric className="text-slate-600">
                          {s.leadTimeDays != null ? `${s.leadTimeDays} d` : "—"}
                        </TD>
                        <TD numeric>{formatInteger(s.productCount)}</TD>
                        <TD numeric className={s.openPurchaseOrders ? "font-medium text-slate-900" : "text-slate-400"}>
                          {formatInteger(s.openPurchaseOrders)}
                        </TD>
                        <TD numeric className={s.outstandingPayables > 0 ? "font-semibold text-slate-900" : "text-slate-400"}>
                          {formatCurrency(s.outstandingPayables, currency)}
                        </TD>
                        <TD>
                          <ActiveBadge active={s.isActive} />
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
      <SupplierFormDialog open={creating} onClose={() => setCreating(false)} />
    </>
  );
}
