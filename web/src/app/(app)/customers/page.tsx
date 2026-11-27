"use client";

import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { Contact, Download, Pencil, Plus, ShoppingCart } from "lucide-react";
import Link from "next/link";
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
import { CustomerFormDialog } from "@/features/sales/CustomerFormDialog";
import { api, fetchAllPages } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { downloadCsv, timestampedName, toCsv } from "@/lib/csv";
import { formatCurrency, formatInteger } from "@/lib/format";
import { P } from "@/lib/permissions";
import { qk, useCurrency } from "@/lib/queries";
import type { Customer, PagedResult } from "@/lib/types";

export default function CustomersPage() {
  return (
    <RequirePermission anyOf={[P.customersView]}>
      <CustomersView />
    </RequirePermission>
  );
}

function CustomersView() {
  const { hasPermission } = useAuth();
  const toast = useToast();
  const currency = useCurrency();
  const canManage = hasPermission(P.customersManage);
  const list = useListParams({ status: "active" as "active" | "inactive" | "" });
  const [editing, setEditing] = useState<Customer | null>(null);
  const [creating, setCreating] = useState(false);
  const [exporting, setExporting] = useState(false);

  const query = {
    page: list.page,
    pageSize: list.pageSize,
    search: list.debouncedSearch || undefined,
    isActive: list.filters.status === "" ? undefined : list.filters.status === "active",
  };

  const customers = useQuery({
    queryKey: qk.customers(query),
    queryFn: ({ signal }) => api.get<PagedResult<Customer>>("/api/customers", query, signal),
    placeholderData: keepPreviousData,
  });

  const filtered = !!query.search || list.filters.status !== "active";

  const exportCsv = async () => {
    setExporting(true);
    try {
      const { page: _p, pageSize: _s, ...filters } = query;
      const rows = await fetchAllPages<Customer>("/api/customers", filters);
      downloadCsv(
        timestampedName("customers"),
        toCsv(rows, [
          { header: "Code", value: (r) => r.code },
          { header: "Name", value: (r) => r.name },
          { header: "Email", value: (r) => r.email },
          { header: "Phone", value: (r) => r.phone },
          { header: "Tax number", value: (r) => r.taxNumber },
          { header: "Payment terms (days)", value: (r) => r.paymentTermsDays },
          { header: `Credit limit (${currency})`, value: (r) => r.creditLimit },
          { header: `Open balance (${currency})`, value: (r) => r.openBalance },
          { header: "Orders", value: (r) => r.orderCount },
          { header: "Active", value: (r) => r.isActive },
        ]),
      );
      toast.success(`Exported ${rows.length} customers`);
    } catch (e) {
      toast.error(e);
    } finally {
      setExporting(false);
    }
  };

  return (
    <>
      <PageHeader
        title="Customers"
        description="Accounts you sell to, with open invoice balances and credit limits."
        actions={
          <>
            <Button variant="secondary" icon={<Download className="size-4" />} onClick={exportCsv} loading={exporting} disabled={!customers.data?.totalCount}>
              Export CSV
            </Button>
            {canManage && (
              <Button icon={<Plus className="size-4" />} onClick={() => setCreating(true)}>
                New customer
              </Button>
            )}
          </>
        }
      />
      <Card>
        <Toolbar>
          <SearchInput value={list.search} onChange={list.setSearch} placeholder="Name, code or email" label="Search customers" />
          <FilterSelect
            label="Status"
            value={list.filters.status}
            onChange={(v) => list.setFilter("status", v as "active" | "inactive" | "")}
            options={[
              { value: "active", label: "Active" },
              { value: "inactive", label: "Inactive" },
              { value: "", label: "All customers" },
            ]}
          />
        </Toolbar>
        <QueryBoundary
          query={customers}
          columns={7}
          isEmpty={(d) => d.items.length === 0}
          empty={
            filtered ? (
              <NoResults onClear={list.reset} what="customers" />
            ) : (
              <EmptyState
                icon={<Contact />}
                title="No customers yet"
                description="Create a customer to start taking orders."
                action={
                  canManage ? (
                    <Button size="sm" icon={<Plus className="size-4" />} onClick={() => setCreating(true)}>
                      New customer
                    </Button>
                  ) : undefined
                }
              />
            )
          }
        >
          {(data) => (
            <>
              <div className={customers.isPlaceholderData ? "opacity-60 transition-opacity" : undefined}>
                <Table>
                  <THead>
                    <tr>
                      <TH>Customer</TH>
                      <TH>Contact</TH>
                      <TH>Location</TH>
                      <TH align="right">Terms</TH>
                      <TH align="right">Credit limit</TH>
                      <TH align="right">Open balance</TH>
                      <TH align="right">Orders</TH>
                      <TH>Status</TH>
                      <TH>
                        <span className="sr-only">Actions</span>
                      </TH>
                    </tr>
                  </THead>
                  <TBody>
                    {data.items.map((c) => {
                      const overLimit = c.creditLimit !== null && c.openBalance > c.creditLimit;
                      return (
                        <TR key={c.id}>
                          <TD>
                            <div className="font-medium text-slate-900">{c.name}</div>
                            <div className="font-mono text-xs text-slate-500">{c.code}</div>
                          </TD>
                          <TD className="text-xs">
                            {c.email && <div className="text-slate-700">{c.email}</div>}
                            {c.phone && <div className="text-slate-500">{c.phone}</div>}
                            {!c.email && !c.phone && <span className="text-slate-400">—</span>}
                          </TD>
                          <TD className="text-xs text-slate-600">
                            {c.billingAddress ? [c.billingAddress.city, c.billingAddress.country].filter(Boolean).join(", ") : "—"}
                          </TD>
                          <TD numeric className="text-slate-600">
                            {c.paymentTermsDays != null ? `${c.paymentTermsDays} d` : "Default"}
                          </TD>
                          <TD numeric>{c.creditLimit != null ? formatCurrency(c.creditLimit, currency) : <span className="text-slate-400">No limit</span>}</TD>
                          <TD numeric className={overLimit ? "font-semibold text-red-600" : c.openBalance > 0 ? "font-medium text-slate-900" : "text-slate-500"}>
                            {formatCurrency(c.openBalance, currency)}
                            {overLimit && <div className="text-[11px] font-medium">Over limit</div>}
                          </TD>
                          <TD numeric>
                            {c.orderCount > 0 && hasPermission(P.ordersView) ? (
                              <Link href={`/orders?customerId=${c.id}`} className="font-medium text-brand-700 hover:underline">
                                {formatInteger(c.orderCount)}
                              </Link>
                            ) : (
                              formatInteger(c.orderCount)
                            )}
                          </TD>
                          <TD>
                            <ActiveBadge active={c.isActive} />
                          </TD>
                          <TD align="right" className="whitespace-nowrap">
                            {hasPermission(P.ordersCreate) && c.isActive && (
                              <Link
                                href={`/orders/new?customerId=${c.id}`}
                                className="mr-1 inline-flex size-7 items-center justify-center rounded-md text-slate-500 hover:bg-slate-100 hover:text-slate-800"
                                aria-label={`New order for ${c.name}`}
                                title="New order"
                              >
                                <ShoppingCart className="size-4" />
                              </Link>
                            )}
                            {canManage && (
                              <Button variant="ghost" size="icon-sm" onClick={() => setEditing(c)} aria-label={`Edit ${c.name}`}>
                                <Pencil className="size-4" />
                              </Button>
                            )}
                          </TD>
                        </TR>
                      );
                    })}
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
      <CustomerFormDialog open={creating} onClose={() => setCreating(false)} />
      <CustomerFormDialog open={!!editing} onClose={() => setEditing(null)} customer={editing} />
    </>
  );
}
