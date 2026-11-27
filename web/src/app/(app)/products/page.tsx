"use client";

import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { Download, ImageIcon, Package, Plus } from "lucide-react";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { Suspense, useMemo, useState } from "react";
import { Button } from "@/components/ui/Button";
import { Card } from "@/components/ui/Card";
import { EmptyState } from "@/components/ui/EmptyState";
import { PageHeader, Toolbar } from "@/components/ui/PageHeader";
import { Pagination } from "@/components/ui/Pagination";
import { PageSpinner } from "@/components/ui/Spinner";
import { Table, TBody, TD, TH, THead, TR } from "@/components/ui/Table";
import { useToast } from "@/components/ui/Toast";
import { FilterSelect, NoResults, QueryBoundary, SearchInput } from "@/features/common/QueryBoundary";
import { RequirePermission } from "@/features/common/RequirePermission";
import { ActiveBadge } from "@/features/common/StatusBadges";
import { useListParams } from "@/features/common/useListParams";
import { BarcodeLookup } from "@/features/products/BarcodeLookup";
import { ProductFormDialog } from "@/features/products/ProductFormDialog";
import { api, fetchAllPages } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { cn } from "@/lib/cn";
import { downloadCsv, timestampedName, toCsv } from "@/lib/csv";
import { formatCurrency, formatQty } from "@/lib/format";
import { P } from "@/lib/permissions";
import { qk, useCategories, useCurrency } from "@/lib/queries";
import type { PagedResult, ProductListItem, ProductSort } from "@/lib/types";

const SORTS: { value: ProductSort; label: string }[] = [
  { value: "name", label: "Sort: Name" },
  { value: "sku", label: "Sort: SKU" },
  { value: "price", label: "Sort: Price (low → high)" },
  { value: "-price", label: "Sort: Price (high → low)" },
  { value: "newest", label: "Sort: Newest" },
];

const STATUS_OPTIONS = [
  { value: "active", label: "Active" },
  { value: "inactive", label: "Inactive" },
];

function ProductsView() {
  const router = useRouter();
  const params = useSearchParams();
  const toast = useToast();
  const { hasPermission } = useAuth();
  const canManage = hasPermission(P.productsManage);
  const currency = useCurrency();
  const categories = useCategories();
  const [createOpen, setCreateOpen] = useState(false);
  const [exporting, setExporting] = useState(false);

  const list = useListParams({
    categoryId: params.get("categoryId") ?? "",
    status: params.get("status") ?? "",
    sort: (params.get("sort") as ProductSort | null) ?? "name",
  });
  const { filters, debouncedSearch } = list;

  const filterQuery = useMemo(
    () => ({
      search: debouncedSearch || undefined,
      categoryId: filters.categoryId || undefined,
      isActive: filters.status === "" ? undefined : filters.status === "active",
      sort: filters.sort,
    }),
    [debouncedSearch, filters],
  );
  const queryParams = { ...filterQuery, page: list.page, pageSize: list.pageSize };

  const query = useQuery({
    queryKey: qk.products(queryParams),
    queryFn: ({ signal }) => api.get<PagedResult<ProductListItem>>("/api/products", queryParams, signal),
    placeholderData: keepPreviousData,
  });

  const filtersActive = !!(debouncedSearch || filters.categoryId || filters.status);
  const clearFilters = () => {
    list.setSearch("");
    list.setFilter("categoryId", "");
    list.setFilter("status", "");
  };

  const exportCsv = async () => {
    setExporting(true);
    try {
      const rows = await fetchAllPages<ProductListItem>("/api/products", filterQuery);
      const csv = toCsv(rows, [
        { header: "SKU", value: (r) => r.sku },
        { header: "Name", value: (r) => r.name },
        { header: "Category", value: (r) => r.categoryName },
        { header: "Base unit", value: (r) => r.baseUnitCode },
        { header: "Barcode", value: (r) => r.barcode },
        { header: `Cost price (${currency})`, value: (r) => r.costPrice },
        { header: `Selling price (${currency})`, value: (r) => r.sellingPrice },
        { header: "Reorder level", value: (r) => r.reorderLevel },
        { header: "On hand", value: (r) => r.quantityOnHand },
        { header: "Available", value: (r) => r.quantityAvailable },
        { header: "Active", value: (r) => r.isActive },
      ]);
      downloadCsv(timestampedName("products"), csv);
      toast.success(`Exported ${rows.length} product${rows.length === 1 ? "" : "s"}`);
    } catch (e) {
      toast.error(e);
    } finally {
      setExporting(false);
    }
  };

  return (
    <>
      <PageHeader
        title="Products"
        description="Your product catalog with pricing, barcodes and stock across the warehouses you can access."
        actions={
          <>
            <BarcodeLookup />
            {canManage && (
              <Button icon={<Plus className="size-4" />} onClick={() => setCreateOpen(true)}>
                New product
              </Button>
            )}
          </>
        }
      />

      <Card>
        <Toolbar>
          <SearchInput value={list.search} onChange={list.setSearch} placeholder="Search name, SKU or barcode" label="Search products" />
          <FilterSelect
            label="Category"
            value={filters.categoryId}
            onChange={(v) => list.setFilter("categoryId", v)}
            allLabel="All categories"
            options={(categories.data ?? []).map((c) => ({ value: c.id, label: c.name }))}
          />
          <FilterSelect label="Status" value={filters.status} onChange={(v) => list.setFilter("status", v)} allLabel="All statuses" options={STATUS_OPTIONS} />
          <FilterSelect
            label="Sort order"
            value={filters.sort}
            onChange={(v) => list.setFilter("sort", v as ProductSort)}
            options={SORTS}
          />
          <div className="ml-auto">
            <Button
              variant="secondary"
              icon={<Download className="size-4" />}
              onClick={exportCsv}
              loading={exporting}
              disabled={!query.data || query.data.totalCount === 0}
            >
              Export CSV
            </Button>
          </div>
        </Toolbar>

        <QueryBoundary
          query={query}
          columns={9}
          isEmpty={(d) => d.items.length === 0}
          empty={
            filtersActive ? (
              <NoResults what="products" onClear={clearFilters} />
            ) : (
              <EmptyState
                icon={<Package />}
                title="No products yet"
                description="Create your first product to start tracking stock."
                action={
                  canManage ? (
                    <Button icon={<Plus className="size-4" />} onClick={() => setCreateOpen(true)}>
                      New product
                    </Button>
                  ) : undefined
                }
              />
            )
          }
        >
          {(data) => (
            <div className={cn("transition-opacity", query.isPlaceholderData && "opacity-60")}>
              <Table>
                <THead>
                  <tr>
                    <TH>SKU</TH>
                    <TH>Product</TH>
                    <TH>Category</TH>
                    <TH>Unit</TH>
                    <TH>Barcode</TH>
                    <TH align="right">Cost</TH>
                    <TH align="right">Price</TH>
                    <TH align="right">Reorder at</TH>
                    <TH align="right">On hand</TH>
                    <TH align="right">Available</TH>
                    <TH>Status</TH>
                  </tr>
                </THead>
                <TBody>
                  {data.items.map((p) => {
                    const low = p.isActive && p.quantityAvailable <= p.reorderLevel;
                    return (
                      <TR key={p.id} interactive onClick={() => router.push(`/products/${p.id}`)}>
                        <TD className="whitespace-nowrap font-mono text-xs text-slate-600">{p.sku}</TD>
                        <TD className="min-w-56">
                          <Link
                            href={`/products/${p.id}`}
                            onClick={(e) => e.stopPropagation()}
                            className="inline-flex items-center gap-1.5 font-medium text-slate-900 hover:text-brand-700 hover:underline"
                          >
                            {p.name}
                            {p.hasImage && <ImageIcon className="size-3.5 text-slate-400" aria-label="Has image" />}
                          </Link>
                        </TD>
                        <TD className="whitespace-nowrap">{p.categoryName ?? <span className="text-slate-400">—</span>}</TD>
                        <TD className="font-mono text-xs">{p.baseUnitCode}</TD>
                        <TD className="font-mono text-xs text-slate-600">{p.barcode ?? <span className="text-slate-400">—</span>}</TD>
                        <TD numeric>{formatCurrency(p.costPrice, currency, { precise: true })}</TD>
                        <TD numeric className="font-medium text-slate-900">
                          {formatCurrency(p.sellingPrice, currency)}
                        </TD>
                        <TD numeric>{formatQty(p.reorderLevel)}</TD>
                        <TD numeric>{formatQty(p.quantityOnHand)}</TD>
                        <TD numeric className={cn(low ? "font-semibold text-amber-700" : "text-slate-900")}>
                          {formatQty(p.quantityAvailable)}
                          {low && <span className="sr-only"> (at or below reorder level)</span>}
                        </TD>
                        <TD>
                          <ActiveBadge active={p.isActive} />
                        </TD>
                      </TR>
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
            </div>
          )}
        </QueryBoundary>
      </Card>

      {canManage && (
        <ProductFormDialog
          open={createOpen}
          onClose={() => setCreateOpen(false)}
          mode="create"
          onSaved={(p) => router.push(`/products/${p.id}`)}
        />
      )}
    </>
  );
}

export default function ProductsPage() {
  return (
    <RequirePermission anyOf={[P.productsView]}>
      <Suspense fallback={<PageSpinner />}>
        <ProductsView />
      </Suspense>
    </RequirePermission>
  );
}
