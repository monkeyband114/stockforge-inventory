"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Package, Pencil, Plus, Star, Trash2 } from "lucide-react";
import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { useState } from "react";
import { Badge } from "@/components/ui/Badge";
import { Button, ButtonLink } from "@/components/ui/Button";
import { Card, CardBody, CardHeader, StatCard } from "@/components/ui/Card";
import { ConfirmDialog } from "@/components/ui/ConfirmDialog";
import { EmptyState } from "@/components/ui/EmptyState";
import { ErrorState } from "@/components/ui/ErrorState";
import { PageHeader } from "@/components/ui/PageHeader";
import { Skeleton, TableSkeleton } from "@/components/ui/Skeleton";
import { Table, TBody, TD, TH, THead, TR } from "@/components/ui/Table";
import { useToast } from "@/components/ui/Toast";
import { RequirePermission } from "@/features/common/RequirePermission";
import { ActiveBadge } from "@/features/common/StatusBadges";
import { MatchBadge, PurchaseOrderStatusBadge, SupplierInvoiceStatusBadge } from "@/features/procurement/ProcurementBadges";
import { SupplierFormDialog } from "@/features/procurement/SupplierFormDialog";
import { SupplierProductDialog } from "@/features/procurement/SupplierProductDialog";
import { formatAddress } from "@/features/sales/AddressFields";
import { api } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { formatCurrency, formatDate, formatInteger, formatQty } from "@/lib/format";
import { P } from "@/lib/permissions";
import { qk, useCurrency } from "@/lib/queries";
import type { PagedResult, PurchaseOrderSummary, Supplier, SupplierInvoiceSummary, SupplierProduct } from "@/lib/types";

export default function SupplierDetailPage() {
  return (
    <RequirePermission anyOf={[P.suppliersView]}>
      <SupplierDetailView />
    </RequirePermission>
  );
}

function SupplierDetailView() {
  const { id } = useParams<{ id: string }>();
  const { hasPermission } = useAuth();
  const router = useRouter();
  const toast = useToast();
  const queryClient = useQueryClient();
  const currency = useCurrency();
  const canManage = hasPermission(P.suppliersManage);
  const [editing, setEditing] = useState(false);
  const [productDialog, setProductDialog] = useState<{ existing: SupplierProduct | null } | null>(null);
  const [removing, setRemoving] = useState<SupplierProduct | null>(null);

  const supplier = useQuery({ queryKey: qk.supplier(id), queryFn: () => api.get<Supplier>(`/api/suppliers/${id}`) });
  const products = useQuery({ queryKey: qk.supplierProducts(id), queryFn: () => api.get<SupplierProduct[]>(`/api/suppliers/${id}/products`) });
  const poParams = { supplierId: id, pageSize: 10 };
  const orders = useQuery({
    queryKey: qk.purchaseOrders(poParams),
    queryFn: () => api.get<PagedResult<PurchaseOrderSummary>>("/api/purchase-orders", poParams),
    enabled: hasPermission(P.purchasingView),
  });
  const billParams = { supplierId: id, pageSize: 10 };
  const bills = useQuery({
    queryKey: qk.supplierInvoices(billParams),
    queryFn: () => api.get<PagedResult<SupplierInvoiceSummary>>("/api/supplier-invoices", billParams),
    enabled: hasPermission(P.payablesView),
  });

  const remove = useMutation({
    mutationFn: (productId: string) => api.delete(`/api/suppliers/${id}/products/${productId}`),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["suppliers"] });
      void queryClient.invalidateQueries({ queryKey: ["purchase-orders", "suggestions"] });
      toast.success("Product removed from supplier");
    },
  });

  if (supplier.isPending) {
    return (
      <div className="space-y-4">
        <Skeleton className="h-8 w-72" />
        <Skeleton className="h-40 w-full" />
      </div>
    );
  }
  if (supplier.isError || !supplier.data) {
    return (
      <Card>
        <ErrorState error={supplier.error} onRetry={() => void supplier.refetch()} />
      </Card>
    );
  }
  const s = supplier.data;

  return (
    <>
      <PageHeader
        breadcrumbs={[{ label: "Suppliers", href: "/suppliers" }, { label: s.code }]}
        title={s.name}
        meta={
          <>
            <Badge tone="neutral" className="font-mono">
              {s.code}
            </Badge>
            <ActiveBadge active={s.isActive} />
          </>
        }
        actions={
          <>
            {hasPermission(P.purchasingCreate) && s.isActive && (
              <ButtonLink href={`/purchase-orders/new?supplierId=${s.id}`} variant="primary" icon={<Plus className="size-4" />}>
                New purchase order
              </ButtonLink>
            )}
            {canManage && (
              <Button variant="secondary" icon={<Pencil className="size-4" />} onClick={() => setEditing(true)}>
                Edit
              </Button>
            )}
          </>
        }
      />

      <div className="mb-6 grid grid-cols-1 gap-4 sm:grid-cols-3">
        <StatCard label="Products supplied" value={formatInteger(s.productCount)} icon={<Package />} />
        <StatCard label="Open purchase orders" value={formatInteger(s.openPurchaseOrders)} href={hasPermission(P.purchasingView) ? `/purchase-orders?supplierId=${s.id}` : undefined} />
        <StatCard
          label="Outstanding payables"
          value={formatCurrency(s.outstandingPayables, currency)}
          tone={s.outstandingPayables > 0 ? "warning" : "neutral"}
          href={hasPermission(P.payablesView) ? `/payables?supplierId=${s.id}` : undefined}
        />
      </div>

      <div className="grid gap-6 xl:grid-cols-[minmax(0,1fr)_22rem]">
        <div className="min-w-0 space-y-6">
          <Card>
            <CardHeader
              title="Supplier products"
              description="Negotiated terms used as defaults on purchase orders"
              actions={
                canManage ? (
                  <Button size="sm" icon={<Plus className="size-3.5" />} onClick={() => setProductDialog({ existing: null })}>
                    Add product
                  </Button>
                ) : null
              }
            />
            {products.isPending ? (
              <TableSkeleton rows={4} columns={6} />
            ) : products.isError ? (
              <ErrorState error={products.error} onRetry={() => void products.refetch()} />
            ) : products.data.length === 0 ? (
              <EmptyState icon={<Package />} title="No products yet" description="Add the products this supplier sells, with their cost and minimum order quantity." />
            ) : (
              <Table>
                <THead>
                  <tr>
                    <TH>Product</TH>
                    <TH>Supplier SKU</TH>
                    <TH align="right">Unit cost</TH>
                    <TH align="right">MOQ</TH>
                    <TH align="right">Lead time</TH>
                    <TH>Preferred</TH>
                    {canManage && (
                      <TH>
                        <span className="sr-only">Actions</span>
                      </TH>
                    )}
                  </tr>
                </THead>
                <TBody>
                  {products.data.map((p) => (
                    <TR key={p.productId}>
                      <TD>
                        <Link href={`/products/${p.productId}`} className="font-medium text-slate-900 hover:text-brand-700 hover:underline">
                          {p.productName}
                        </Link>
                        <div className="font-mono text-xs text-slate-500">{p.sku}</div>
                      </TD>
                      <TD className="font-mono text-xs">{p.supplierSku ?? "—"}</TD>
                      <TD numeric>{formatCurrency(p.unitCost, currency, { precise: true })}</TD>
                      <TD numeric>{p.minimumOrderQuantity ? formatQty(p.minimumOrderQuantity) : "—"}</TD>
                      <TD numeric>{p.leadTimeDays != null ? `${p.leadTimeDays} d` : s.leadTimeDays != null ? <span className="text-slate-400">{s.leadTimeDays} d</span> : "—"}</TD>
                      <TD>
                        {p.isPreferred ? (
                          <Badge tone="brand">
                            <Star className="size-3" aria-hidden /> Preferred
                          </Badge>
                        ) : (
                          <span className="text-xs text-slate-400">—</span>
                        )}
                      </TD>
                      {canManage && (
                        <TD align="right" className="whitespace-nowrap">
                          <Button variant="ghost" size="icon-sm" onClick={() => setProductDialog({ existing: p })} aria-label={`Edit terms for ${p.productName}`}>
                            <Pencil className="size-4" />
                          </Button>
                          <Button variant="ghost" size="icon-sm" onClick={() => setRemoving(p)} aria-label={`Remove ${p.productName}`}>
                            <Trash2 className="size-4" />
                          </Button>
                        </TD>
                      )}
                    </TR>
                  ))}
                </TBody>
              </Table>
            )}
          </Card>

          {hasPermission(P.purchasingView) && (
            <Card>
              <CardHeader
                title="Recent purchase orders"
                actions={
                  <Link href={`/purchase-orders?supplierId=${s.id}`} className="text-xs font-medium text-brand-700 hover:text-brand-600">
                    All orders →
                  </Link>
                }
              />
              {orders.isPending ? (
                <TableSkeleton rows={3} columns={5} />
              ) : orders.isError ? (
                <ErrorState error={orders.error} onRetry={() => void orders.refetch()} />
              ) : orders.data.items.length === 0 ? (
                <EmptyState title="No purchase orders" className="py-8" />
              ) : (
                <Table>
                  <THead>
                    <tr>
                      <TH>Order</TH>
                      <TH>Status</TH>
                      <TH>Ordered</TH>
                      <TH>Expected</TH>
                      <TH align="right">Received</TH>
                      <TH align="right">Total</TH>
                    </tr>
                  </THead>
                  <TBody>
                    {orders.data.items.map((po) => (
                      <TR key={po.id} interactive onClick={() => router.push(`/purchase-orders/${po.id}`)}>
                        <TD className="font-mono text-xs font-semibold text-slate-900">{po.number}</TD>
                        <TD>
                          <PurchaseOrderStatusBadge status={po.status} />
                        </TD>
                        <TD className="text-xs text-slate-600">{formatDate(po.orderDate)}</TD>
                        <TD className="text-xs text-slate-600">{po.expectedDate ? formatDate(po.expectedDate) : "—"}</TD>
                        <TD numeric>
                          {formatQty(po.quantityReceived)}/{formatQty(po.quantityOrdered)}
                        </TD>
                        <TD numeric className="font-medium text-slate-900">
                          {formatCurrency(po.total, po.currency)}
                        </TD>
                      </TR>
                    ))}
                  </TBody>
                </Table>
              )}
            </Card>
          )}

          {hasPermission(P.payablesView) && (
            <Card>
              <CardHeader
                title="Bills"
                actions={
                  <Link href={`/payables?supplierId=${s.id}`} className="text-xs font-medium text-brand-700 hover:text-brand-600">
                    All bills →
                  </Link>
                }
              />
              {bills.isPending ? (
                <TableSkeleton rows={3} columns={5} />
              ) : bills.isError ? (
                <ErrorState error={bills.error} onRetry={() => void bills.refetch()} />
              ) : bills.data.items.length === 0 ? (
                <EmptyState title="No supplier invoices" className="py-8" />
              ) : (
                <Table>
                  <THead>
                    <tr>
                      <TH>Bill</TH>
                      <TH>Status</TH>
                      <TH>Match</TH>
                      <TH>Due</TH>
                      <TH align="right">Total</TH>
                      <TH align="right">Balance</TH>
                    </tr>
                  </THead>
                  <TBody>
                    {bills.data.items.map((b) => (
                      <TR key={b.id} interactive onClick={() => router.push(`/payables/${b.id}`)}>
                        <TD>
                          <div className="font-mono text-xs font-semibold text-slate-900">{b.reference}</div>
                          <div className="font-mono text-xs text-slate-500">{b.supplierInvoiceNumber}</div>
                        </TD>
                        <TD>
                          <SupplierInvoiceStatusBadge status={b.status} />
                        </TD>
                        <TD>
                          <MatchBadge matches={b.matchesReceipts} hasPo={!!b.purchaseOrderId} />
                        </TD>
                        <TD className="text-xs text-slate-600">{formatDate(b.dueDate)}</TD>
                        <TD numeric>{formatCurrency(b.total, b.currency)}</TD>
                        <TD numeric className={b.balanceDue > 0 ? "font-semibold text-slate-900" : "text-slate-400"}>
                          {formatCurrency(b.balanceDue, b.currency)}
                        </TD>
                      </TR>
                    ))}
                  </TBody>
                </Table>
              )}
            </Card>
          )}
        </div>

        <Card>
          <CardHeader title="Details" />
          <CardBody>
            <dl className="space-y-3 text-sm">
              {[
                ["Contact", s.contactName],
                ["Email", s.email],
                ["Phone", s.phone],
                ["Tax number", s.taxNumber],
                ["Address", formatAddress(s.address) || null],
                ["Payment terms", s.paymentTermsDays != null ? `${s.paymentTermsDays} days` : null],
                ["Lead time", s.leadTimeDays != null ? `${s.leadTimeDays} days` : null],
                ["Supplier since", formatDate(s.createdAt)],
              ].map(([label, value]) => (
                <div key={label}>
                  <dt className="text-xs font-medium uppercase tracking-wide text-slate-500">{label}</dt>
                  <dd className="mt-0.5 text-slate-800">{value || <span className="text-slate-400">—</span>}</dd>
                </div>
              ))}
              {s.notes && (
                <div>
                  <dt className="text-xs font-medium uppercase tracking-wide text-slate-500">Notes</dt>
                  <dd className="mt-0.5 whitespace-pre-wrap text-slate-800">{s.notes}</dd>
                </div>
              )}
            </dl>
          </CardBody>
        </Card>
      </div>

      <SupplierFormDialog open={editing} onClose={() => setEditing(false)} supplier={s} />
      <SupplierProductDialog
        open={!!productDialog}
        onClose={() => setProductDialog(null)}
        supplierId={s.id}
        existing={productDialog?.existing}
        excludeIds={(products.data ?? []).map((p) => p.productId)}
      />
      <ConfirmDialog
        open={!!removing}
        onClose={() => setRemoving(null)}
        title={`Remove ${removing?.productName ?? "product"}?`}
        description="The supplier's negotiated terms for this product are deleted. Existing purchase orders are not affected."
        confirmLabel="Remove"
        tone="danger"
        onConfirm={() => (removing ? remove.mutateAsync(removing.productId) : undefined)}
      />
    </>
  );
}
