"use client";

import { useQueryClient } from "@tanstack/react-query";
import { ExternalLink, Pencil, Power, PowerOff, Trash2 } from "lucide-react";
import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { useState, type ReactNode } from "react";
import { Badge } from "@/components/ui/Badge";
import { Button, ButtonLink } from "@/components/ui/Button";
import { Card, CardBody, CardHeader } from "@/components/ui/Card";
import { ConfirmDialog } from "@/components/ui/ConfirmDialog";
import { EmptyState } from "@/components/ui/EmptyState";
import { ErrorState } from "@/components/ui/ErrorState";
import { PageHeader } from "@/components/ui/PageHeader";
import { Skeleton } from "@/components/ui/Skeleton";
import { Table, TBody, TD, TH, THead, TR } from "@/components/ui/Table";
import { useToast } from "@/components/ui/Toast";
import { RequirePermission } from "@/features/common/RequirePermission";
import { ActiveBadge } from "@/features/common/StatusBadges";
import { Barcode } from "@/features/products/Barcode";
import { ProductFormDialog } from "@/features/products/ProductFormDialog";
import { ProductImagePanel } from "@/features/products/ProductImagePanel";
import { updateFromProduct } from "@/features/products/productForm";
import { api, ApiError } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { formatCurrency, formatDateTime, formatPercent, formatQty } from "@/lib/format";
import { P } from "@/lib/permissions";
import { qk, useCurrency, useProduct } from "@/lib/queries";
import type { Product } from "@/lib/types";

function Detail({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div>
      <dt className="text-xs font-medium text-slate-500">{label}</dt>
      <dd className="mt-0.5 text-sm text-slate-900">{children}</dd>
    </div>
  );
}

function DetailSkeleton() {
  return (
    <div role="status" aria-label="Loading product">
      <Skeleton className="h-3 w-40" />
      <Skeleton className="mt-3 h-8 w-80" />
      <Skeleton className="mt-2 h-4 w-56" />
      <div className="mt-8 grid gap-6 lg:grid-cols-3">
        <div className="space-y-6 lg:col-span-2">
          <Skeleton className="h-48 w-full" />
          <Skeleton className="h-56 w-full" />
        </div>
        <div className="space-y-6">
          <Skeleton className="h-64 w-full" />
          <Skeleton className="h-36 w-full" />
        </div>
      </div>
    </div>
  );
}

function ProductDetail({ id }: { id: string }) {
  const router = useRouter();
  const queryClient = useQueryClient();
  const toast = useToast();
  const { hasPermission } = useAuth();
  const canManage = hasPermission(P.productsManage);
  const canViewInventory = hasPermission(P.inventoryView);
  const currency = useCurrency();
  const query = useProduct(id);
  const [editOpen, setEditOpen] = useState(false);
  const [toggleOpen, setToggleOpen] = useState(false);
  const [deleteOpen, setDeleteOpen] = useState(false);

  if (query.isPending) return <DetailSkeleton />;
  // Background refetch failures keep showing the last good data.
  if (!query.data) {
    return (
      <>
        <PageHeader title="Product" breadcrumbs={[{ label: "Products", href: "/products" }, { label: "Product" }]} />
        <Card>
          <ErrorState error={query.error} onRetry={() => void query.refetch()} title={query.error instanceof ApiError && query.error.status === 404 ? "Product not found" : undefined} />
        </Card>
      </>
    );
  }

  const product = query.data;
  const totals = product.stock.reduce(
    (acc, s) => ({
      onHand: acc.onHand + s.quantityOnHand,
      reserved: acc.reserved + s.quantityReserved,
      available: acc.available + s.quantityAvailable,
      value: acc.value + s.quantityOnHand * s.averageUnitCost,
    }),
    { onHand: 0, reserved: 0, available: 0, value: 0 },
  );
  const margin = product.sellingPrice > 0 ? (product.sellingPrice - product.costPrice) / product.sellingPrice : null;

  const reload = async (): Promise<Product | undefined> => (await query.refetch()).data;

  const afterChange = (saved: Product) => {
    queryClient.setQueryData(qk.product(saved.id), saved);
    void queryClient.invalidateQueries({ queryKey: ["products", "list"] });
  };

  const toggleActive = async () => {
    try {
      const saved = await api.put<Product>(`/api/products/${product.id}`, updateFromProduct(product, { isActive: !product.isActive }));
      afterChange(saved);
      toast.success(saved.isActive ? "Product activated" : "Product deactivated", saved.name);
    } catch (e) {
      if (e instanceof ApiError && e.code === "concurrency_conflict") {
        await reload();
        throw new Error("Someone else changed this product — the latest version has been reloaded. Confirm again to continue.");
      }
      throw e;
    }
  };

  const remove = async () => {
    await api.delete(`/api/products/${product.id}`);
    toast.success("Product deleted", product.name);
    // Only lists: refetching this (now deleted) product while still mounted would flash a 404.
    void queryClient.invalidateQueries({ queryKey: ["products", "list"] });
    void queryClient.invalidateQueries({ queryKey: qk.categories });
    router.push("/products");
  };

  return (
    <>
      <PageHeader
        title={product.name}
        breadcrumbs={[{ label: "Products", href: "/products" }, { label: product.sku }]}
        meta={
          <>
            <Badge tone="neutral" className="font-mono">
              {product.sku}
            </Badge>
            <ActiveBadge active={product.isActive} />
            {product.categoryName && <Badge tone="info">{product.categoryName}</Badge>}
          </>
        }
        description={product.description ?? undefined}
        actions={
          canManage ? (
            <>
              <Button variant="secondary" icon={<Pencil className="size-4" />} onClick={() => setEditOpen(true)}>
                Edit
              </Button>
              <Button
                variant="secondary"
                icon={product.isActive ? <PowerOff className="size-4" /> : <Power className="size-4" />}
                onClick={() => setToggleOpen(true)}
              >
                {product.isActive ? "Deactivate" : "Activate"}
              </Button>
              <Button variant="danger-ghost" icon={<Trash2 className="size-4" />} onClick={() => setDeleteOpen(true)}>
                Delete
              </Button>
            </>
          ) : undefined
        }
      />

      <div className="grid gap-6 lg:grid-cols-3">
        <div className="min-w-0 space-y-6 lg:col-span-2">
          <Card>
            <CardHeader title="Details" />
            <CardBody>
              <dl className="grid grid-cols-2 gap-x-6 gap-y-4 sm:grid-cols-3">
                <Detail label="Category">{product.categoryName ?? <span className="text-slate-400">Uncategorized</span>}</Detail>
                <Detail label="Base unit">
                  <span className="font-mono">{product.baseUnitCode}</span>
                </Detail>
                <Detail label="Barcode">
                  {product.barcode ? <span className="font-mono">{product.barcode}</span> : <span className="text-slate-400">None</span>}
                </Detail>
                <Detail label="Cost price">
                  <span className="tabular-nums">{formatCurrency(product.costPrice, currency, { precise: true })}</span>
                </Detail>
                <Detail label="Selling price">
                  <span className="tabular-nums">{formatCurrency(product.sellingPrice, currency)}</span>
                </Detail>
                <Detail label="Gross margin">
                  <span className="tabular-nums">{margin === null ? "—" : formatPercent(margin)}</span>
                </Detail>
                <Detail label="Tax rate">
                  {product.taxRate === null ? <span className="text-slate-400">Organization default</span> : formatPercent(product.taxRate)}
                </Detail>
                <Detail label="Reorder level">
                  <span className="tabular-nums">
                    {formatQty(product.reorderLevel)} {product.baseUnitCode}
                  </span>
                </Detail>
                <Detail label="Reorder quantity">
                  <span className="tabular-nums">
                    {formatQty(product.reorderQuantity)} {product.baseUnitCode}
                  </span>
                </Detail>
                <Detail label="Created">{formatDateTime(product.createdAt)}</Detail>
                <Detail label="Last updated">{formatDateTime(product.updatedAt)}</Detail>
              </dl>
            </CardBody>
          </Card>

          <Card>
            <CardHeader
              title="Stock by warehouse"
              description="Balances in the warehouses you can access."
              actions={
                canViewInventory ? (
                  <ButtonLink href={`/inventory?search=${encodeURIComponent(product.sku)}`} size="sm" variant="ghost" icon={<ExternalLink className="size-3.5" />}>
                    Open in inventory
                  </ButtonLink>
                ) : undefined
              }
            />
            {product.stock.length === 0 ? (
              <EmptyState title="No stock recorded" description="This product has not been received into any warehouse yet." />
            ) : (
              <Table>
                <THead>
                  <tr>
                    <TH>Warehouse</TH>
                    <TH align="right">On hand</TH>
                    <TH align="right">Reserved</TH>
                    <TH align="right">Available</TH>
                    <TH align="right">Avg. cost</TH>
                    <TH align="right">Value</TH>
                  </tr>
                </THead>
                <TBody>
                  {product.stock.map((s) => {
                    const low = product.isActive && s.quantityAvailable <= product.reorderLevel;
                    return (
                      <TR key={s.warehouseId}>
                        <TD>
                          {canViewInventory ? (
                            <Link
                              href={`/inventory?warehouseId=${s.warehouseId}&search=${encodeURIComponent(product.sku)}`}
                              className="font-medium text-slate-900 hover:text-brand-700 hover:underline"
                            >
                              {s.warehouseName}
                            </Link>
                          ) : (
                            <span className="font-medium text-slate-900">{s.warehouseName}</span>
                          )}
                          <span className="ml-2 font-mono text-xs text-slate-500">{s.warehouseCode}</span>
                        </TD>
                        <TD numeric>{formatQty(s.quantityOnHand)}</TD>
                        <TD numeric>{formatQty(s.quantityReserved)}</TD>
                        <TD numeric className={low ? "font-semibold text-amber-700" : "font-medium text-slate-900"}>
                          {formatQty(s.quantityAvailable)}
                          {low && (
                            <Badge tone="warning" className="ml-2">
                              Low
                            </Badge>
                          )}
                        </TD>
                        <TD numeric>{formatCurrency(s.averageUnitCost, currency, { precise: true })}</TD>
                        <TD numeric>{formatCurrency(s.quantityOnHand * s.averageUnitCost, currency)}</TD>
                      </TR>
                    );
                  })}
                  {product.stock.length > 1 && (
                    <tr className="bg-slate-50/80 font-semibold">
                      <TD>Total</TD>
                      <TD numeric>{formatQty(totals.onHand)}</TD>
                      <TD numeric>{formatQty(totals.reserved)}</TD>
                      <TD numeric>{formatQty(totals.available)}</TD>
                      <TD />
                      <TD numeric>{formatCurrency(totals.value, currency)}</TD>
                    </tr>
                  )}
                </TBody>
              </Table>
            )}
          </Card>

          <Card>
            <CardHeader title="Alternate units" description={`Conversions to the base unit (${product.baseUnitCode}).`} />
            {product.units.length === 0 ? (
              <EmptyState title="No alternate units" description="Packaging units such as boxes or cases can be added by editing the product." />
            ) : (
              <Table>
                <THead>
                  <tr>
                    <TH>Unit</TH>
                    <TH align="right">Conversion</TH>
                    <TH>Barcode</TH>
                  </tr>
                </THead>
                <TBody>
                  {product.units.map((u) => (
                    <TR key={u.unitId}>
                      <TD>
                        <span className="font-mono font-medium text-slate-900">{u.unitCode}</span>
                        <span className="ml-2 text-slate-500">{u.unitName}</span>
                      </TD>
                      <TD numeric>
                        1 {u.unitCode} = {formatQty(u.conversionFactor)} {product.baseUnitCode}
                      </TD>
                      <TD className="font-mono text-xs">{u.barcode ?? <span className="text-slate-400">—</span>}</TD>
                    </TR>
                  ))}
                </TBody>
              </Table>
            )}
          </Card>
        </div>

        <div className="min-w-0 space-y-6">
          <ProductImagePanel product={product} canManage={canManage} />

          <Card>
            <CardHeader title="Barcodes" description="Code 128 — printable and scannable." />
            <CardBody className="space-y-4">
              {product.barcode ? (
                <div>
                  <p className="mb-1.5 text-xs font-medium text-slate-500">Product ({product.baseUnitCode})</p>
                  <Barcode value={product.barcode} label={`Barcode for ${product.name}: ${product.barcode}`} />
                </div>
              ) : (
                <p className="text-sm text-slate-500">No product barcode assigned.{canManage ? " Edit the product to add one." : ""}</p>
              )}
              {product.units
                .filter((u) => u.barcode)
                .map((u) => (
                  <div key={u.unitId}>
                    <p className="mb-1.5 text-xs font-medium text-slate-500">
                      {u.unitName} ({u.unitCode} × {formatQty(u.conversionFactor)})
                    </p>
                    <Barcode value={u.barcode as string} height={48} label={`Barcode for ${u.unitName}: ${u.barcode}`} />
                  </div>
                ))}
            </CardBody>
          </Card>
        </div>
      </div>

      {canManage && (
        <>
          <ProductFormDialog open={editOpen} onClose={() => setEditOpen(false)} mode="edit" product={product} onReload={reload} onSaved={afterChange} />
          <ConfirmDialog
            open={toggleOpen}
            onClose={() => setToggleOpen(false)}
            title={product.isActive ? `Deactivate ${product.name}?` : `Activate ${product.name}?`}
            description={
              product.isActive
                ? "Inactive products are hidden from pickers and cannot be received into stock. Existing balances and history are kept."
                : "The product becomes available for stock movements, transfers and reservations again."
            }
            confirmLabel={product.isActive ? "Deactivate" : "Activate"}
            tone={product.isActive ? "danger" : "primary"}
            onConfirm={toggleActive}
          />
          <ConfirmDialog
            open={deleteOpen}
            onClose={() => setDeleteOpen(false)}
            title={`Delete ${product.name}?`}
            description="Only products that were never stocked can be deleted. Products with stock history must be deactivated instead. This cannot be undone."
            confirmLabel="Delete product"
            tone="danger"
            onConfirm={remove}
          />
        </>
      )}
    </>
  );
}

export default function ProductDetailPage() {
  const params = useParams<{ id: string }>();
  return (
    <RequirePermission anyOf={[P.productsView]}>
      <ProductDetail id={params.id} />
    </RequirePermission>
  );
}
