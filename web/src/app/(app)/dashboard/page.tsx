"use client";

import { keepPreviousData, useQuery } from "@tanstack/react-query";
import {
  AlertTriangle,
  ArrowLeftRight,
  Boxes,
  ClipboardCheck,
  Clock,
  DollarSign,
  Gauge,
  Hourglass,
  Package,
  PackageX,
  ShoppingCart,
  Truck,
  Warehouse as WarehouseIcon,
} from "lucide-react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import { Card, CardBody, CardHeader, StatCard } from "@/components/ui/Card";
import { ErrorState } from "@/components/ui/ErrorState";
import { PageHeader } from "@/components/ui/PageHeader";
import { ChartSkeleton, StatGridSkeleton, TableSkeleton } from "@/components/ui/Skeleton";
import { PageSpinner } from "@/components/ui/Spinner";
import { MovementTrendChart, ValueBarChart, ViewToggle, useChartView } from "@/features/dashboard/Charts";
import { DaysSelector, LowStockTable } from "@/features/dashboard/LowStockTable";
import { MonthlySalesChart, OrderStatusBreakdown } from "@/features/dashboard/SalesCharts";
import { api } from "@/lib/api";
import { homePathFor, useAuth } from "@/lib/auth";
import { formatCurrency, formatInteger, formatQty, formatRelative } from "@/lib/format";
import { P } from "@/lib/permissions";
import { qk, useCurrency } from "@/lib/queries";
import type { OrderMetrics, OrganizationDashboard } from "@/lib/types";

export default function DashboardPage() {
  const { hasAnyPermission, user } = useAuth();
  const router = useRouter();
  const allowed = hasAnyPermission(P.reportsOrganization, P.inventoryAnalytics);

  // Warehouse staff without analytics land on their own warehouse dashboard instead.
  useEffect(() => {
    if (allowed || !user) return;
    const assigned = user.warehouseIds?.[0];
    if (assigned) router.replace(`/warehouses/${assigned}`);
    else router.replace(homePathFor(user) === "/dashboard" ? "/account" : homePathFor(user));
  }, [allowed, user, router]);

  if (!allowed) return <PageSpinner label="Opening your workspace…" />;
  return <OrganizationDashboardView />;
}

function OrganizationDashboardView() {
  const { user, hasPermission } = useAuth();
  const currency = useCurrency();
  const [days, setDays] = useState(30);
  const [trendView, setTrendView] = useChartView();
  const [categoryView, setCategoryView] = useChartView();
  const [warehouseView, setWarehouseView] = useChartView();

  const query = useQuery({
    queryKey: qk.orgDashboard(days),
    queryFn: () => api.get<OrganizationDashboard>("/api/dashboard/organization", { days }),
    placeholderData: keepPreviousData,
    refetchInterval: 60_000,
  });

  const d = query.data;
  const scoped = !!user?.warehouseIds;

  return (
    <>
      <PageHeader
        title="Dashboard"
        description={
          scoped
            ? "Inventory health across the warehouses assigned to you."
            : `Inventory health across ${user?.tenant?.name ?? "your organization"}.`
        }
        meta={query.dataUpdatedAt ? <span className="text-xs text-slate-400">Updated {formatRelative(new Date(query.dataUpdatedAt).toISOString())}</span> : null}
      />

      {query.isError && !d ? (
        <Card>
          <ErrorState error={query.error} onRetry={() => void query.refetch()} />
        </Card>
      ) : (
        <div className="space-y-6">
          {!d ? (
            <StatGridSkeleton count={8} />
          ) : (
            <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
              <StatCard
                label="Inventory value (cost)"
                value={formatCurrency(d.inventoryCostValue, currency, { compact: d.inventoryCostValue >= 1_000_000 })}
                sub={`Retail value ${formatCurrency(d.inventoryRetailValue, currency)}`}
                icon={<DollarSign />}
                tone="info"
              />
              <StatCard
                label="Units on hand"
                value={formatQty(d.unitsOnHand)}
                sub={`${formatQty(d.unitsReserved)} reserved · ${formatQty(d.unitsOnHand - d.unitsReserved)} available`}
                icon={<Boxes />}
                href={hasPermission(P.inventoryView) ? "/inventory" : undefined}
              />
              <StatCard
                label="Products"
                value={formatInteger(d.totalProducts)}
                sub={`${formatInteger(d.activeProducts)} active`}
                icon={<Package />}
                href={hasPermission(P.productsView) ? "/products" : undefined}
              />
              <StatCard
                label="Warehouses"
                value={formatInteger(d.totalWarehouses)}
                sub="Active sites"
                icon={<WarehouseIcon />}
                href={hasPermission(P.warehousesView) ? "/warehouses" : undefined}
              />
              <StatCard
                label="Low stock"
                value={formatInteger(d.lowStockCount)}
                sub="At or below reorder level"
                icon={<AlertTriangle />}
                tone={d.lowStockCount > 0 ? "warning" : "success"}
                href={hasPermission(P.inventoryView) ? "/inventory?lowStock=true" : undefined}
              />
              <StatCard
                label="Out of stock"
                value={formatInteger(d.outOfStockCount)}
                sub="Nothing available to sell"
                icon={<PackageX />}
                tone={d.outOfStockCount > 0 ? "danger" : "success"}
              />
              <StatCard
                label="Pending adjustments"
                value={formatInteger(d.pendingAdjustments)}
                sub="Awaiting approval"
                icon={<ClipboardCheck />}
                tone={d.pendingAdjustments > 0 ? "warning" : "neutral"}
                href={hasPermission(P.inventoryView) ? "/inventory/adjustments?status=PendingApproval" : undefined}
              />
              <StatCard
                label="Open transfers"
                value={formatInteger(d.openTransfers)}
                sub="Requested or in transit"
                icon={<ArrowLeftRight />}
                href={hasPermission(P.inventoryView) ? "/inventory/transfers" : undefined}
              />
            </div>
          )}

          {d?.orders && hasPermission(P.ordersView) && <OrderSection metrics={d.orders} days={days} currency={currency} />}

          <Card>
            <CardHeader
              title="Stock movement"
              description="Units received vs. issued per day (transfers between warehouses excluded)"
              actions={
                <>
                  <DaysSelector value={days} onChange={setDays} />
                  <ViewToggle view={trendView} onChange={setTrendView} />
                </>
              }
            />
            <CardBody className={query.isFetching && d ? "opacity-70 transition-opacity" : undefined}>
              {d ? <MovementTrendChart data={d.movementTrend} view={trendView} /> : <ChartSkeleton height={280} />}
            </CardBody>
          </Card>

          <div className="grid grid-cols-1 gap-6 xl:grid-cols-2">
            <Card>
              <CardHeader title="Stock value by category" description={`At average cost, in ${currency}`} actions={<ViewToggle view={categoryView} onChange={setCategoryView} />} />
              <CardBody>
                {d ? (
                  <ValueBarChart
                    view={categoryView}
                    currency={currency}
                    data={d.valueByCategory.map((c) => ({ key: c.categoryId ?? "none", label: c.categoryName, value: c.costValue, retail: c.retailValue, units: c.units }))}
                  />
                ) : (
                  <ChartSkeleton height={220} />
                )}
              </CardBody>
            </Card>
            <Card>
              <CardHeader title="Stock value by warehouse" description={`At average cost, in ${currency}`} actions={<ViewToggle view={warehouseView} onChange={setWarehouseView} />} />
              <CardBody>
                {d ? (
                  <ValueBarChart
                    view={warehouseView}
                    currency={currency}
                    data={d.valueByWarehouse.map((w) => ({
                      key: w.warehouseId,
                      label: `${w.warehouseCode} · ${w.warehouseName}`,
                      value: w.costValue,
                      retail: w.retailValue,
                      units: w.units,
                    }))}
                  />
                ) : (
                  <ChartSkeleton height={220} />
                )}
              </CardBody>
            </Card>
          </div>

          <Card>
            <CardHeader
              title="Low-stock items"
              description="Lowest availability relative to reorder level"
              actions={
                hasPermission(P.inventoryView) ? (
                  <Link href="/inventory?lowStock=true" className="text-xs font-medium text-brand-700 hover:text-brand-600">
                    View all in inventory →
                  </Link>
                ) : null
              }
            />
            {d ? <LowStockTable items={d.lowStockItems} /> : <TableSkeleton rows={5} columns={5} />}
          </Card>
        </div>
      )}
    </>
  );
}

function OrderSection({ metrics: m, days, currency }: { metrics: OrderMetrics; days: number; currency: string }) {
  const router = useRouter();
  const { hasPermission } = useAuth();
  const [salesView, setSalesView] = useChartView();
  return (
    <section aria-labelledby="orders-heading" className="space-y-4">
      <h2 id="orders-heading" className="text-sm font-semibold uppercase tracking-wide text-slate-500">
        Sales &amp; fulfillment
      </h2>
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-6">
        <StatCard label="Total orders" value={formatInteger(m.totalOrders)} sub="All time" icon={<ShoppingCart />} href="/orders" />
        <StatCard
          label="Pending approval"
          value={formatInteger(m.pendingApproval)}
          sub="Waiting for an approver"
          icon={<Hourglass />}
          tone={m.pendingApproval > 0 ? "warning" : "neutral"}
          href="/orders?status=PendingApproval"
        />
        <StatCard label="Open orders" value={formatInteger(m.openOrders)} sub="Approved, not yet fully shipped" icon={<Package />} href={hasPermission(P.ordersFulfill) ? "/fulfillment" : "/orders"} />
        <StatCard label={`Sales · last ${days} days`} value={formatCurrency(m.salesInPeriod, currency)} sub={`${formatInteger(m.shippedInPeriod)} orders shipped`} icon={<Truck />} tone="info" />
        <StatCard
          label="Fulfillment rate"
          value={`${new Intl.NumberFormat(undefined, { maximumFractionDigits: 1 }).format(m.fulfillmentRatePercent)}%`}
          sub={`Approved in the last ${days} days and fully shipped`}
          icon={<Gauge />}
          tone={m.fulfillmentRatePercent >= 90 ? "success" : m.fulfillmentRatePercent >= 60 ? "warning" : "danger"}
        />
        <StatCard
          label="Avg. time to ship"
          value={m.averageHoursToShip !== null ? `${new Intl.NumberFormat(undefined, { maximumFractionDigits: 1 }).format(m.averageHoursToShip)} h` : "—"}
          sub="Approval to shipment"
          icon={<Clock />}
        />
      </div>
      <div className="grid grid-cols-1 gap-6 xl:grid-cols-5">
        <Card className="xl:col-span-3">
          <CardHeader title="Monthly sales" description={`Invoiced sales per month, in ${currency}`} actions={<ViewToggle view={salesView} onChange={setSalesView} />} />
          <CardBody>
            <MonthlySalesChart data={m.monthlySales} currency={currency} view={salesView} />
          </CardBody>
        </Card>
        <Card className="xl:col-span-2">
          <CardHeader title="Orders by status" description="Click a status to open those orders" />
          <CardBody>
            <OrderStatusBreakdown data={m.byStatus} onSelect={(status) => router.push(`/orders?status=${status}`)} />
          </CardBody>
        </Card>
      </div>
    </section>
  );
}
