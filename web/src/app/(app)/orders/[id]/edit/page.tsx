"use client";

import { useQuery } from "@tanstack/react-query";
import { useParams } from "next/navigation";
import { ButtonLink } from "@/components/ui/Button";
import { Card } from "@/components/ui/Card";
import { EmptyState } from "@/components/ui/EmptyState";
import { ErrorState } from "@/components/ui/ErrorState";
import { PageHeader } from "@/components/ui/PageHeader";
import { PageSpinner } from "@/components/ui/Spinner";
import { RequirePermission } from "@/features/common/RequirePermission";
import { OrderForm } from "@/features/sales/OrderForm";
import { api } from "@/lib/api";
import { P } from "@/lib/permissions";
import { qk } from "@/lib/queries";
import type { Order } from "@/lib/types";

export default function EditOrderPage() {
  return (
    <RequirePermission anyOf={[P.ordersCreate]}>
      <EditOrderView />
    </RequirePermission>
  );
}

function EditOrderView() {
  const { id } = useParams<{ id: string }>();
  const query = useQuery({
    queryKey: qk.order(id),
    queryFn: () => api.get<Order>(`/api/orders/${id}`),
    // Never refetch underneath an open edit form.
    refetchOnWindowFocus: false,
    staleTime: Infinity,
  });

  if (query.isPending) return <PageSpinner label="Loading order…" />;
  if (query.isError) {
    return (
      <Card>
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      </Card>
    );
  }
  const order = query.data;

  return (
    <>
      <PageHeader
        title={`Edit ${order.number}`}
        breadcrumbs={[{ label: "Orders", href: "/orders" }, { label: order.number, href: `/orders/${order.id}` }, { label: "Edit" }]}
      />
      {order.status !== "Draft" ? (
        <Card>
          <EmptyState
            title="This order can no longer be edited"
            description="Only draft orders can be changed. Cancel and re-create the order if it needs different lines."
            action={<ButtonLink href={`/orders/${order.id}`}>Back to order</ButtonLink>}
          />
        </Card>
      ) : (
        <OrderForm order={order} />
      )}
    </>
  );
}
