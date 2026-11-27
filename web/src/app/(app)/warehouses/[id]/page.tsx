"use client";

import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { AlertTriangle, ArrowDownToLine, ArrowUpFromLine, Boxes, ClipboardCheck, DollarSign, History, Lock, PackageCheck } from "lucide-react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { useState } from "react";
import { Badge } from "@/components/ui/Badge";
import { ButtonLink } from "@/components/ui/Button";
import { Card, CardBody, CardHeader, StatCard } from "@/components/ui/Card";
import { EmptyState } from "@/components/ui/EmptyState";
import { ErrorState } from "@/components/ui/ErrorState";
import { PageHeader } from "@/components/ui/PageHeader";
import { ChartSkeleton, Skeleton, StatGridSkeleton, TableSkeleton } from "@/components/ui/Skeleton";
import { RequirePermission } from "@/features/common/RequirePermission";
import { ActiveBadge } from "@/features/common/StatusBadges";
import { MovementTrendChart, ViewToggle, useChartView } from "@/features/dashboard/Charts";
import { DaysSelector, LowStockTable } from "@/features/dashboard/LowStockTable";
import { MovementsTable } from "@/features/inventory/MovementsTable";
import { warehouseTypeLabel } from "@/features/warehouses/WarehouseFormDialog";
import { api } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { formatCurrency, formatInteger, formatQty } from "@/lib/format";
import { P } from "@/lib/permissions";
import { qk, useCurrency } from "@/lib/queries";
import type { Warehouse, WarehouseDashboard } from "@/lib/types";

export default function WarehouseDetailPage() {
  return (
    <RequirePermission anyOf={[P.warehousesView, P.inventoryView]}>
      <WarehouseDashboardView />
    </RequirePermission>
  );
}

function WarehouseDashboardView() {
  const { id } = useParams<{ id: string }>();
  const { canAccessWarehouse, hasPermission } = useAuth();
  const currency = useCurrency();
  const [days, setDays] = useState(14);
  const [view, setView] = useChartView();
  const canSeeInventory = hasPermission(P.inventoryView);

  const warehouse = useQuery({
    queryKey: qk.warehouse(id),
    queryFn: () => api.get<Warehouse>(`/api/warehouses/${id}`),
    enabled: hasPermission(P.warehousesView),
  });

  const dashboard = useQuery({
    queryKey: qk.warehouseDashboard(id, days),
    queryFn: () => api.get<WarehouseDashboard>(`/api/dashboard/warehouses/${id}`, { days }),
    enabled: canSeeInventory && canAccessWarehouse(id),
    placeholderData: keepPreviousData,
    refetchInterval: 60_000,
  });

  const w = warehouse.data;
  const d = dashboard.data;
  const title = w?.name ?? d?.name;

  if (warehouse.isError && !w) {
    return (
      <Card>
        <ErrorState error={warehouse.error} onRetry={() => void warehouse.refetch()} />
      </Card>
    );
  }

  const header = (
    <PageHeader
      breadcrumbs={[{ label: "Warehouses", href: "/warehouses" }, { label: w?.code ?? d?.code ?? "…" }]}
      title={title ?? <Skeleton className="h-7 w-64" />}
      description={
        w ? (
          <>
            {warehouseTypeLabel(w.type)}
            {w.address && ` · ${[w.address.line1, w.address.city, w.address.country].filter(Boolean).join(", ")}`}
          </>
        ) : undefined
      }
      meta={
        w ? (
          <>
            <Badge tone="neutral" className="font-mono">
              {w.code}
            </Badge>
            <ActiveBadge active={w.isActive} />
          </>
        ) : null
      }
      actions={
        canSeeInventory && canAccessWarehouse(id) ? (
          <>
            <ButtonLink href={`/inventory?warehouseId=${id}`} icon={<Boxes className="size-4" />}>
              Stock levels
            </ButtonLink>
            <ButtonLink href={`/inventory/movements?warehouseId=${id}`} icon={<History className="size-4" />}>
              Movements
            </ButtonLink>
          </>
        ) : null
      }
    />
  );

  if (!canSeeInventory || !canAccessWarehouse(id)) {
    return (
      <>
        {header}
        <Card>
          <EmptyState
            icon={<Lock />}
            title="Stock data is not available"
            description="You are not assigned to this warehouse, or your role does not include inventory access."
          />
        </Card>
      </>
    );
  }

  return (
    <>
      {header}
      {dashboard.isError && !d ? (
        <Card>
          <ErrorState error={dashboard.error} onRetry={() => void dashboard.refetch()} />
        </Card>
      ) : (
        <div className="space-y-6">
          {!d ? (
            <StatGridSkeleton count={8} />
          ) : (
            <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
              <StatCard label="Available" value={formatQty(d.unitsAvailable)} sub={`${formatInteger(d.skuCount)} SKUs in stock`} icon={<PackageCheck />} tone="success" />
              <StatCard label="On hand" value={formatQty(d.unitsOnHand)} sub="Physical units in the warehouse" icon={<Boxes />} />
              <StatCard
                label="Reserved"
                value={formatQty(d.unitsReserved)}
                sub="Held for transfers and orders"
                icon={<Lock />}
                href={`/inventory/reservations?warehouseId=${id}`}
              />
              <StatCard label="Stock value" value={formatCurrency(d.stockValue, currency)} sub="At average cost" icon={<DollarSign />} tone="info" />
              <StatCard
                label="Incoming transfers"
                value={formatInteger(d.incomingTransfers)}
                sub={`${formatQty(d.incomingUnits)} units in transit`}
                icon={<ArrowDownToLine />}
                href={`/inventory/transfers?warehouseId=${id}&status=InTransit`}
              />
              <StatCard
                label="Outgoing transfers"
                value={formatInteger(d.outgoingTransfers)}
                sub={`${formatQty(d.outgoingUnits)} units awaiting dispatch`}
                icon={<ArrowUpFromLine />}
                href={`/inventory/transfers?warehouseId=${id}&status=Requested`}
              />
              <StatCard
                label="Low stock"
                value={formatInteger(d.lowStockCount)}
                sub="At or below reorder level"
                icon={<AlertTriangle />}
                tone={d.lowStockCount > 0 ? "warning" : "success"}
                href={`/inventory?warehouseId=${id}&lowStock=true`}
              />
              <StatCard
                label="Pending adjustments"
                value={formatInteger(d.pendingAdjustments)}
                sub="Awaiting approval"
                icon={<ClipboardCheck />}
                tone={d.pendingAdjustments > 0 ? "warning" : "neutral"}
                href={`/inventory/adjustments?warehouseId=${id}&status=PendingApproval`}
              />
            </div>
          )}

          {d?.fulfillmentQueue && hasAnyPermission(P.ordersView, P.ordersFulfill) && (
            <Card>
              <CardHeader
                title="Fulfillment queue"
                description="Open sales orders at this warehouse by stage"
                actions={
                  hasPermission(P.ordersFulfill) ? (
                    <Link href={`/fulfillment?warehouseId=${id}`} className="text-xs font-medium text-brand-700 hover:text-brand-600">
                      Open queue →
                    </Link>
                  ) : null
                }
              />
              <CardBody>
                {d.fulfillmentQueue.length === 0 ? (
                  <p className="text-sm text-slate-500">No open orders waiting at this warehouse.</p>
                ) : (
                  <ul className="flex flex-wrap gap-3">
                    {(["Approved", "Processing", "Picking", "Packed", "PartiallyFulfilled"] as const)
                      .map((st) => ({ st, count: d.fulfillmentQueue?.find((q) => q.status === st)?.count ?? 0 }))
                      .map(({ st, count }) => (
                        <li key={st}>
                          <Link
                            href={`/orders?warehouseId=${id}&status=${st}`}
                            className="flex min-w-32 items-center justify-between gap-4 rounded-md border border-slate-200 px-3 py-2 hover:border-brand-600/40 hover:bg-brand-50/30"
                          >
                            <OrderStatusBadge status={st} />
                            <span className="text-lg font-semibold text-slate-900">{formatInteger(count)}</span>
                          </Link>
                        </li>
                      ))}
                  </ul>
                )}
              </CardBody>
            </Card>
          )}

          <Card>
            <CardHeader
              title="Stock movement"
              description="Units received vs. issued per day, including transfers"
              actions={
                <>
                  <DaysSelector value={days} onChange={setDays} options={[7, 14, 30, 90]} />
                  <ViewToggle view={view} onChange={setView} />
                </>
              }
            />
            <CardBody className={dashboard.isFetching && d ? "opacity-70 transition-opacity" : undefined}>
              {d ? <MovementTrendChart data={d.movementTrend} view={view} height={240} /> : <ChartSkeleton height={240} />}
            </CardBody>
          </Card>

          <div className="grid grid-cols-1 gap-6 2xl:grid-cols-5">
            <Card className="2xl:col-span-3">
              <CardHeader
                title="Recent movements"
                actions={
                  <Link href={`/inventory/movements?warehouseId=${id}`} className="text-xs font-medium text-brand-700 hover:text-brand-600">
                    Full ledger →
                  </Link>
                }
              />
              {!d ? (
                <TableSkeleton rows={6} columns={6} />
              ) : d.recentMovements.length === 0 ? (
                <EmptyState icon={<History />} title="No movements yet" description="Stock received, issued or transferred here will appear in real time." />
              ) : (
                <MovementsTable movements={d.recentMovements} currency={currency} showWarehouse={false} compact />
              )}
            </Card>
            <Card className="2xl:col-span-2">
              <CardHeader title="Low-stock items" />
              {d ? <LowStockTable items={d.lowStockItems} showWarehouse={false} /> : <TableSkeleton rows={5} columns={4} />}
            </Card>
          </div>
        </div>
      )}
    </>
  );
}
