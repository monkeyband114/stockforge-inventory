"use client";

import { keepPreviousData, useQuery } from "@tanstack/react-query";
import {
  ArrowDownToLine,
  ArrowUpFromLine,
  Boxes,
  ClipboardList,
  Download,
  Lock,
  MoreHorizontal,
  PackageX,
  PencilRuler,
  Plus,
  Undo2,
} from "lucide-react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { Suspense, useMemo, useState } from "react";
import { Badge } from "@/components/ui/Badge";
import { Button } from "@/components/ui/Button";
import { Card } from "@/components/ui/Card";
import { ToggleChip } from "@/components/ui/Checkbox";
import { EmptyState } from "@/components/ui/EmptyState";
import { Menu, type MenuItem } from "@/components/ui/Menu";
import { PageHeader, Toolbar } from "@/components/ui/PageHeader";
import { Pagination } from "@/components/ui/Pagination";
import { PageSpinner } from "@/components/ui/Spinner";
import { Table, TBody, TD, TH, THead, TR } from "@/components/ui/Table";
import { useToast } from "@/components/ui/Toast";
import { FilterSelect, NoResults, QueryBoundary, SearchInput } from "@/features/common/QueryBoundary";
import { RequirePermission } from "@/features/common/RequirePermission";
import { useListParams } from "@/features/common/useListParams";
import { ACTION_ORDER, ACTIONS, InventoryActionDialog, type ActionPreset, type InventoryAction } from "@/features/inventory/InventoryActionDialog";
import { fetchAllPages, api } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { downloadCsv, timestampedName, toCsv } from "@/lib/csv";
import { formatCurrency, formatQty, formatRelative } from "@/lib/format";
import { P } from "@/lib/permissions";
import { qk, useAccessibleWarehouses, useCategories, useCurrency } from "@/lib/queries";
import type { InventoryBalance, PagedResult } from "@/lib/types";

const ACTION_ICONS: Record<InventoryAction, React.ReactNode> = {
  stockIn: <ArrowDownToLine />,
  stockOut: <ArrowUpFromLine />,
  damaged: <PackageX />,
  return: <Undo2 />,
  count: <ClipboardList />,
  correction: <PencilRuler />,
  reserve: <Lock />,
};

export default function InventoryPage() {
  return (
    <RequirePermission anyOf={[P.inventoryView]}>
      <Suspense fallback={<PageSpinner />}>
        <InventoryView />
      </Suspense>
    </RequirePermission>
  );
}

function InventoryView() {
  const params = useSearchParams();
  const { hasPermission } = useAuth();
  const toast = useToast();
  const currency = useCurrency();
  const warehouses = useAccessibleWarehouses();
  const categories = useCategories();

  const initialFilters = useMemo(
    () => ({
      warehouseId: params.get("warehouseId") ?? "",
      categoryId: params.get("categoryId") ?? "",
      lowStockOnly: ["true", "1"].includes(params.get("lowStock") ?? ""),
      inStockOnly: ["true", "1"].includes(params.get("inStock") ?? ""),
    }),
    // Read once from the URL (deep links from dashboards, emails and notifications).
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [],
  );
  const list = useListParams(initialFilters, 25, params.get("search") ?? "");
  const clearFilters = () => {
    list.reset();
    list.setFilter("warehouseId", "");
    list.setFilter("categoryId", "");
    list.setFilter("lowStockOnly", false);
    list.setFilter("inStockOnly", false);
  };

  const [action, setAction] = useState<InventoryAction | null>(null);
  const [preset, setPreset] = useState<ActionPreset | null>(null);
  const [exporting, setExporting] = useState(false);

  const query = {
    page: list.page,
    pageSize: list.pageSize,
    search: list.debouncedSearch || undefined,
    warehouseId: list.filters.warehouseId || undefined,
    categoryId: list.filters.categoryId || undefined,
    lowStockOnly: list.filters.lowStockOnly || undefined,
    inStockOnly: list.filters.inStockOnly || undefined,
  };

  const balances = useQuery({
    queryKey: qk.inventory(query),
    queryFn: ({ signal }) => api.get<PagedResult<InventoryBalance>>("/api/inventory", query, signal),
    placeholderData: keepPreviousData,
  });

  const allowedActions = ACTION_ORDER.filter((a) => hasPermission(ACTIONS[a].permission));
  const filtered = !!(query.search || query.warehouseId || query.categoryId || query.lowStockOnly || query.inStockOnly);

  const openAction = (a: InventoryAction, row?: InventoryBalance) => {
    setPreset(row ? { warehouseId: row.warehouseId, product: { id: row.productId, sku: row.sku, name: row.productName, baseUnitCode: row.unitCode } } : null);
    setAction(a);
  };

  const exportCsv = async () => {
    setExporting(true);
    try {
      const { page: _p, pageSize: _s, ...filters } = query;
      const rows = await fetchAllPages<InventoryBalance>("/api/inventory", filters);
      const csv = toCsv(rows, [
        { header: "Warehouse", value: (r) => r.warehouseCode },
        { header: "Warehouse name", value: (r) => r.warehouseName },
        { header: "SKU", value: (r) => r.sku },
        { header: "Product", value: (r) => r.productName },
        { header: "Category", value: (r) => r.categoryName },
        { header: "Unit", value: (r) => r.unitCode },
        { header: "On hand", value: (r) => r.quantityOnHand },
        { header: "Reserved", value: (r) => r.quantityReserved },
        { header: "Available", value: (r) => r.quantityAvailable },
        { header: "Reorder level", value: (r) => r.reorderLevel },
        { header: "Low stock", value: (r) => r.isLowStock },
        { header: `Average unit cost (${currency})`, value: (r) => r.averageUnitCost },
        { header: `Stock value (${currency})`, value: (r) => r.stockValue },
        { header: "Last movement", value: (r) => r.lastMovementAt },
      ]);
      downloadCsv(timestampedName("inventory"), csv);
      toast.success(`Exported ${rows.length} rows`);
    } catch (e) {
      toast.error(e);
    } finally {
      setExporting(false);
    }
  };

  const otherActions: MenuItem[] = allowedActions
    .filter((a) => a !== "stockIn")
    .map((a) => ({ label: ACTIONS[a].title, icon: ACTION_ICONS[a], onSelect: () => openAction(a) }));

  return (
    <>
      <PageHeader
        title="Stock levels"
        description="On-hand, reserved and available quantities per warehouse and product."
        actions={
          <>
            <Button variant="secondary" icon={<Download className="size-4" />} onClick={exportCsv} loading={exporting} disabled={!balances.data?.totalCount}>
              Export CSV
            </Button>
            {otherActions.length > 0 && (
              <Menu
                label="Record a movement"
                items={otherActions}
                trigger={(props) => (
                  <Button variant="secondary" {...props}>
                    Record…
                  </Button>
                )}
              />
            )}
            {hasPermission(P.inventoryReceive) && (
              <Button icon={<Plus className="size-4" />} onClick={() => openAction("stockIn")}>
                Stock in
              </Button>
            )}
          </>
        }
      />

      <Card>
        <Toolbar>
          <SearchInput value={list.search} onChange={list.setSearch} placeholder="Search product, SKU or barcode" label="Search inventory" />
          <FilterSelect
            label="Warehouse"
            value={list.filters.warehouseId}
            onChange={(v) => list.setFilter("warehouseId", v)}
            allLabel="All warehouses"
            options={(warehouses.data ?? []).map((w) => ({ value: w.id, label: `${w.code} — ${w.name}` }))}
          />
          <FilterSelect
            label="Category"
            value={list.filters.categoryId}
            onChange={(v) => list.setFilter("categoryId", v)}
            allLabel="All categories"
            options={(categories.data ?? []).map((c) => ({ value: c.id, label: c.name }))}
          />
          <ToggleChip checked={list.filters.lowStockOnly} onChange={(v) => list.setFilter("lowStockOnly", v)}>
            Low stock only
          </ToggleChip>
          <ToggleChip checked={list.filters.inStockOnly} onChange={(v) => list.setFilter("inStockOnly", v)}>
            In stock only
          </ToggleChip>
        </Toolbar>

        <QueryBoundary
          query={balances}
          columns={9}
          isEmpty={(d) => d.items.length === 0}
          empty={
            filtered ? (
              <NoResults onClear={clearFilters} what="stock balances" />
            ) : (
              <EmptyState
                icon={<Boxes />}
                title="No stock recorded yet"
                description="Balances appear once a product is received into a warehouse."
                action={
                  hasPermission(P.inventoryReceive) ? (
                    <Button size="sm" icon={<Plus className="size-4" />} onClick={() => openAction("stockIn")}>
                      Receive stock
                    </Button>
                  ) : undefined
                }
              />
            )
          }
        >
          {(data) => (
            <>
              <div className={balances.isPlaceholderData ? "opacity-60 transition-opacity" : undefined}>
                <Table maxHeight="calc(100dvh - 22rem)">
                  <THead>
                    <tr>
                      <TH>Product</TH>
                      <TH>Category</TH>
                      <TH>Warehouse</TH>
                      <TH align="right">On hand</TH>
                      <TH align="right">Reserved</TH>
                      <TH align="right">Available</TH>
                      <TH align="right">Reorder at</TH>
                      <TH align="right">Avg cost</TH>
                      <TH align="right">Value</TH>
                      <TH>Last movement</TH>
                      {allowedActions.length > 0 && (
                        <TH>
                          <span className="sr-only">Actions</span>
                        </TH>
                      )}
                    </tr>
                  </THead>
                  <TBody>
                    {data.items.map((row) => {
                      const out = row.quantityAvailable <= 0;
                      return (
                        <TR key={row.id}>
                          <TD className="max-w-72">
                            <Link href={`/products/${row.productId}`} className="block truncate font-medium text-slate-900 hover:text-brand-700 hover:underline">
                              {row.productName}
                            </Link>
                            <span className="font-mono text-xs text-slate-500">{row.sku}</span>
                          </TD>
                          <TD className="text-slate-600">{row.categoryName ?? "—"}</TD>
                          <TD>
                            <Link href={`/warehouses/${row.warehouseId}`} className="font-mono text-xs font-medium text-slate-700 hover:text-brand-700 hover:underline" title={row.warehouseName}>
                              {row.warehouseCode}
                            </Link>
                          </TD>
                          <TD numeric>
                            {formatQty(row.quantityOnHand)} <span className="text-xs text-slate-400">{row.unitCode}</span>
                          </TD>
                          <TD numeric className={row.quantityReserved > 0 ? "text-slate-700" : "text-slate-400"}>
                            {formatQty(row.quantityReserved)}
                          </TD>
                          <TD numeric>
                            <span className={out ? "font-semibold text-red-600" : row.isLowStock ? "font-semibold text-amber-700" : "font-semibold text-slate-900"}>
                              {formatQty(row.quantityAvailable)}
                            </span>
                            {(row.isLowStock || out) && (
                              <div className="mt-0.5">
                                <Badge tone={out ? "danger" : "warning"}>{out ? "Out of stock" : "Low stock"}</Badge>
                              </div>
                            )}
                          </TD>
                          <TD numeric className="text-slate-500">
                            {formatQty(row.reorderLevel)}
                          </TD>
                          <TD numeric>{formatCurrency(row.averageUnitCost, currency, { precise: true })}</TD>
                          <TD numeric className="font-medium text-slate-900">
                            {formatCurrency(row.stockValue, currency)}
                          </TD>
                          <TD className="whitespace-nowrap text-xs text-slate-500">{formatRelative(row.lastMovementAt)}</TD>
                          {allowedActions.length > 0 && (
                            <TD align="right">
                              <Menu
                                label={`Actions for ${row.productName} at ${row.warehouseCode}`}
                                items={allowedActions.map((a) => ({ label: ACTIONS[a].title, icon: ACTION_ICONS[a], onSelect: () => openAction(a, row) }))}
                                trigger={(props) => (
                                  <Button variant="ghost" size="icon-sm" aria-label={`Actions for ${row.productName} at ${row.warehouseCode}`} {...props}>
                                    <MoreHorizontal className="size-4" />
                                  </Button>
                                )}
                              />
                            </TD>
                          )}
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

      <InventoryActionDialog action={action} preset={preset} onClose={() => setAction(null)} />
    </>
  );
}
