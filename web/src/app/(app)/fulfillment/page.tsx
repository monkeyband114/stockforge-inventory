"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ClipboardList, Cog, Package, PackageOpen } from "lucide-react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { Suspense, useState } from "react";
import { Button, ButtonLink } from "@/components/ui/Button";
import { Card, StatCard } from "@/components/ui/Card";
import { ConfirmDialog } from "@/components/ui/ConfirmDialog";
import { EmptyState } from "@/components/ui/EmptyState";
import { PageHeader, Toolbar } from "@/components/ui/PageHeader";
import { PageSpinner } from "@/components/ui/Spinner";
import { Table, TBody, TD, TH, THead, TR } from "@/components/ui/Table";
import { useToast } from "@/components/ui/Toast";
import { FilterSelect, QueryBoundary } from "@/features/common/QueryBoundary";
import { RequirePermission } from "@/features/common/RequirePermission";
import { ORDER_STATUS_LABELS, OrderStatusBadge } from "@/features/sales/SalesBadges";
import { api } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { cn } from "@/lib/cn";
import { formatDate, formatQty, formatRelative } from "@/lib/format";
import { P } from "@/lib/permissions";
import { qk, useAccessibleWarehouses } from "@/lib/queries";
import type { FulfillmentQueueItem, Order, SalesOrderStatus } from "@/lib/types";

export default function FulfillmentPage() {
  return (
    <RequirePermission anyOf={[P.ordersFulfill]}>
      <Suspense fallback={<PageSpinner />}>
        <FulfillmentView />
      </Suspense>
    </RequirePermission>
  );
}

function Progress({ label, value, total, tone }: { label: string; value: number; total: number; tone: string }) {
  const pct = total > 0 ? Math.min(100, (value / total) * 100) : 0;
  return (
    <div className="min-w-24">
      <div className="flex justify-between text-[11px] text-slate-500">
        <span>{label}</span>
        <span className="tabular-nums">
          {formatQty(value)}/{formatQty(total)}
        </span>
      </div>
      <div
        className="mt-1 h-1.5 overflow-hidden rounded-full bg-slate-100"
        role="progressbar"
        aria-label={`${label}: ${formatQty(value)} of ${formatQty(total)}`}
        aria-valuemin={0}
        aria-valuemax={total}
        aria-valuenow={value}
      >
        <div className={cn("h-full rounded-full", tone)} style={{ width: `${pct}%` }} />
      </div>
    </div>
  );
}

type QuickAction = { item: FulfillmentQueueItem; action: "process" | "start-picking" | "pack" };
const QUICK_LABELS: Record<QuickAction["action"], string> = { process: "Start processing", "start-picking": "Start picking", pack: "Pack" };

function FulfillmentView() {
  const params = useSearchParams();
  const { canAccessWarehouse } = useAuth();
  const toast = useToast();
  const queryClient = useQueryClient();
  const warehouses = useAccessibleWarehouses();
  const [warehouseId, setWarehouseId] = useState(params.get("warehouseId") ?? "");
  const [status, setStatus] = useState<SalesOrderStatus | "">("");
  const [quick, setQuick] = useState<QuickAction | null>(null);

  const queue = useQuery({
    queryKey: qk.fulfillmentQueue(warehouseId),
    queryFn: () => api.get<FulfillmentQueueItem[]>("/api/fulfillment/queue", { warehouseId: warehouseId || undefined }),
    refetchInterval: 30_000,
  });

  const mutation = useMutation({
    mutationFn: ({ item, action }: QuickAction) => api.post<Order>(`/api/orders/${item.orderId}/${action}`),
    onSuccess: (order) => {
      queryClient.setQueryData(qk.order(order.id), order);
      for (const root of ["fulfillment", "orders", "dashboard"]) void queryClient.invalidateQueries({ queryKey: [root] });
    },
  });

  const items = (queue.data ?? []).filter((i) => !status || i.status === status);
  const counts = (queue.data ?? []).reduce<Record<string, number>>((acc, i) => ({ ...acc, [i.status]: (acc[i.status] ?? 0) + 1 }), {});
  const statuses = [...new Set((queue.data ?? []).map((i) => i.status))];

  return (
    <>
      <PageHeader title="Fulfillment queue" description="Approved orders waiting to be processed, picked, packed and shipped — oldest approvals first." />
      <div className="mb-6 grid grid-cols-2 gap-4 lg:grid-cols-5">
        {(["Approved", "Processing", "Picking", "Packed", "PartiallyFulfilled"] as SalesOrderStatus[]).map((st) => (
          <StatCard key={st} label={ORDER_STATUS_LABELS[st]} value={queue.data ? String(counts[st] ?? 0) : "—"} />
        ))}
      </div>
      <Card>
        <Toolbar>
          <FilterSelect
            label="Warehouse"
            value={warehouseId}
            onChange={setWarehouseId}
            allLabel="All my warehouses"
            options={(warehouses.data ?? []).map((w) => ({ value: w.id, label: `${w.code} — ${w.name}` }))}
          />
          <FilterSelect
            label="Status"
            value={status}
            onChange={(v) => setStatus(v as SalesOrderStatus | "")}
            allLabel="All statuses"
            options={statuses.map((s) => ({ value: s, label: ORDER_STATUS_LABELS[s] ?? s }))}
          />
        </Toolbar>
        <QueryBoundary
          query={queue}
          columns={7}
          isEmpty={() => items.length === 0}
          empty={
            <EmptyState
              icon={<PackageOpen />}
              title={status ? "No orders in this status" : "The queue is empty"}
              description="Newly approved orders for your warehouses appear here in real time."
            />
          }
        >
          {() => (
            <Table>
              <THead>
                <tr>
                  <TH>Order</TH>
                  <TH>Customer</TH>
                  <TH>Warehouse</TH>
                  <TH>Status</TH>
                  <TH>Required</TH>
                  <TH>Approved</TH>
                  <TH>Progress</TH>
                  <TH>
                    <span className="sr-only">Actions</span>
                  </TH>
                </tr>
              </THead>
              <TBody>
                {items.map((i) => {
                  const late = !!i.requiredDate && i.requiredDate < new Date().toISOString().slice(0, 10);
                  const operable = canAccessWarehouse(i.warehouseId);
                  const next: QuickAction["action"] | null =
                    i.status === "Approved" ? "process" : i.status === "Processing" ? "start-picking" : i.status === "Picking" && i.quantityPicked > i.quantityPacked ? "pack" : null;
                  return (
                    <TR key={i.orderId}>
                      <TD>
                        <Link href={`/orders/${i.orderId}`} className="font-mono text-xs font-semibold text-slate-900 hover:text-brand-700 hover:underline">
                          {i.number}
                        </Link>
                      </TD>
                      <TD className="max-w-48 truncate">{i.customerName}</TD>
                      <TD className="font-mono text-xs">{i.warehouseCode}</TD>
                      <TD>
                        <OrderStatusBadge status={i.status} />
                      </TD>
                      <TD className={cn("whitespace-nowrap text-xs", late ? "font-semibold text-red-600" : "text-slate-600")}>
                        {i.requiredDate ? formatDate(i.requiredDate) : "—"}
                        {late && " · late"}
                      </TD>
                      <TD className="whitespace-nowrap text-xs text-slate-500">{formatRelative(i.approvedAt)}</TD>
                      <TD>
                        <div className="flex gap-3">
                          <Progress label="Picked" value={i.quantityPicked} total={i.totalQuantity} tone="bg-blue-500" />
                          <Progress label="Packed" value={i.quantityPacked} total={i.totalQuantity} tone="bg-indigo-500" />
                          <Progress label="Shipped" value={i.quantityShipped} total={i.totalQuantity} tone="bg-emerald-500" />
                        </div>
                      </TD>
                      <TD align="right" className="whitespace-nowrap">
                        {operable && next && (
                          <Button
                            size="sm"
                            variant="secondary"
                            icon={next === "process" ? <Cog className="size-3.5" /> : next === "pack" ? <Package className="size-3.5" /> : <ClipboardList className="size-3.5" />}
                            onClick={() => setQuick({ item: i, action: next })}
                            className="mr-1.5"
                          >
                            {QUICK_LABELS[next]}
                          </Button>
                        )}
                        <ButtonLink href={`/orders/${i.orderId}`} size="sm" variant="ghost">
                          Open
                        </ButtonLink>
                      </TD>
                    </TR>
                  );
                })}
              </TBody>
            </Table>
          )}
        </QueryBoundary>
      </Card>
      <ConfirmDialog
        open={!!quick}
        onClose={() => setQuick(null)}
        title={quick ? `${QUICK_LABELS[quick.action]} — ${quick.item.number}?` : ""}
        description={
          quick?.action === "pack"
            ? `${formatQty(quick.item.quantityPicked - quick.item.quantityPacked)} picked unit(s) will be packed.`
            : quick?.action === "start-picking"
              ? "Pickers can then record quantities on the order page."
              : "The order moves into processing at the warehouse."
        }
        confirmLabel={quick ? QUICK_LABELS[quick.action] : "Confirm"}
        onConfirm={async () => {
          if (!quick) return;
          const order = await mutation.mutateAsync(quick);
          toast.success(`${order.number}: ${ORDER_STATUS_LABELS[order.status].toLowerCase()}`);
        }}
      />
    </>
  );
}
