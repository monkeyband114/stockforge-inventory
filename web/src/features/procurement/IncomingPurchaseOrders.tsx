"use client";

import { useQuery } from "@tanstack/react-query";
import { Truck } from "lucide-react";
import Link from "next/link";
import { useState } from "react";
import { Button } from "@/components/ui/Button";
import { Card, CardHeader } from "@/components/ui/Card";
import { EmptyState } from "@/components/ui/EmptyState";
import { ErrorState } from "@/components/ui/ErrorState";
import { TableSkeleton } from "@/components/ui/Skeleton";
import { Table, TBody, TD, TH, THead, TR } from "@/components/ui/Table";
import { api } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { cn } from "@/lib/cn";
import { formatDate, formatQty } from "@/lib/format";
import { P } from "@/lib/permissions";
import { qk } from "@/lib/queries";
import type { PagedResult, PurchaseOrderSummary } from "@/lib/types";
import { PurchaseOrderStatusBadge, RECEIVABLE_PO_STATUSES } from "./ProcurementBadges";
import { ReceiveGoodsDialog } from "./ReceiveGoodsDialog";

/**
 * Purchase orders still expecting deliveries (optionally for one warehouse), with a quick Receive action
 * for members holding purchasing.receive and access to the receiving warehouse.
 */
export function IncomingPurchaseOrders({ warehouseId, title = "Incoming purchase orders" }: { warehouseId?: string; title?: string }) {
  const { hasPermission, canAccessWarehouse } = useAuth();
  const [receiving, setReceiving] = useState<string | null>(null);
  const canView = hasPermission(P.purchasingView);
  const params = { warehouseId: warehouseId || undefined, outstandingOnly: true, pageSize: 10 };
  const query = useQuery({
    queryKey: qk.purchaseOrders(params),
    queryFn: () => api.get<PagedResult<PurchaseOrderSummary>>("/api/purchase-orders", params),
    enabled: canView,
    refetchInterval: 60_000,
  });

  if (!canView) {
    if (!hasPermission(P.purchasingReceive)) return null;
    // Receivers without purchasing.view cannot list or open purchase orders (the API returns 403).
    return (
      <Card>
        <CardHeader title={title} />
        <p className="px-5 py-4 text-sm text-slate-500">
          Your role can receive goods but cannot view purchase orders. Ask an administrator for the <span className="font-mono text-xs">purchasing.view</span> permission to see incoming deliveries here.
        </p>
      </Card>
    );
  }

  const today = new Date().toISOString().slice(0, 10);
  return (
    <Card>
      <CardHeader
        title={title}
        description="Approved orders awaiting delivery"
        actions={
          <Link href={`/purchase-orders?outstanding=true${warehouseId ? `&warehouseId=${warehouseId}` : ""}`} className="text-xs font-medium text-brand-700 hover:text-brand-600">
            All incoming →
          </Link>
        }
      />
      {query.isPending ? (
        <TableSkeleton rows={3} columns={6} />
      ) : query.isError ? (
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      ) : query.data.items.length === 0 ? (
        <EmptyState icon={<Truck />} title="No deliveries expected" description="Approved purchase orders appear here until they are fully received." className="py-8" />
      ) : (
        <Table>
          <THead>
            <tr>
              <TH>Order</TH>
              <TH>Supplier</TH>
              {!warehouseId && <TH>Warehouse</TH>}
              <TH>Status</TH>
              <TH>Expected</TH>
              <TH align="right">Outstanding</TH>
              <TH>
                <span className="sr-only">Actions</span>
              </TH>
            </tr>
          </THead>
          <TBody>
            {query.data.items.map((po) => {
              const late = !!po.expectedDate && po.expectedDate < today;
              const receivable = hasPermission(P.purchasingReceive) && canAccessWarehouse(po.warehouseId) && RECEIVABLE_PO_STATUSES.includes(po.status);
              return (
                <TR key={po.id}>
                  <TD>
                    <Link href={`/purchase-orders/${po.id}`} className="font-mono text-xs font-semibold text-slate-900 hover:text-brand-700 hover:underline">
                      {po.number}
                    </Link>
                  </TD>
                  <TD className="max-w-48 truncate">{po.supplierName}</TD>
                  {!warehouseId && <TD className="font-mono text-xs">{po.warehouseCode}</TD>}
                  <TD>
                    <PurchaseOrderStatusBadge status={po.status} />
                  </TD>
                  <TD className={cn("whitespace-nowrap text-xs", late ? "font-semibold text-red-600" : "text-slate-600")}>
                    {po.expectedDate ? formatDate(po.expectedDate) : "—"}
                    {late && " · late"}
                  </TD>
                  <TD numeric className="font-medium text-slate-900">
                    {formatQty(po.quantityOrdered - po.quantityReceived)}
                  </TD>
                  <TD align="right">
                    {receivable && (
                      <Button size="sm" variant="secondary" icon={<Truck className="size-3.5" />} onClick={() => setReceiving(po.id)}>
                        Receive
                      </Button>
                    )}
                  </TD>
                </TR>
              );
            })}
          </TBody>
        </Table>
      )}
      <ReceiveGoodsDialog purchaseOrderId={receiving} open={!!receiving} onClose={() => setReceiving(null)} />
    </Card>
  );
}
