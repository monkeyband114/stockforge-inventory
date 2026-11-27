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
import { PurchaseOrderForm } from "@/features/procurement/PurchaseOrderForm";
import { api } from "@/lib/api";
import { P } from "@/lib/permissions";
import { qk } from "@/lib/queries";
import type { PurchaseOrder } from "@/lib/types";

export default function EditPurchaseOrderPage() {
  return (
    <RequirePermission anyOf={[P.purchasingCreate]}>
      <EditView />
    </RequirePermission>
  );
}

function EditView() {
  const { id } = useParams<{ id: string }>();
  const query = useQuery({
    queryKey: qk.purchaseOrder(id),
    queryFn: () => api.get<PurchaseOrder>(`/api/purchase-orders/${id}`),
    refetchOnWindowFocus: false,
    staleTime: Infinity,
  });
  if (query.isPending) return <PageSpinner label="Loading purchase order…" />;
  if (query.isError) {
    return (
      <Card>
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      </Card>
    );
  }
  const po = query.data;
  return (
    <>
      <PageHeader
        title={`Edit ${po.number}`}
        breadcrumbs={[{ label: "Purchase orders", href: "/purchase-orders" }, { label: po.number, href: `/purchase-orders/${po.id}` }, { label: "Edit" }]}
      />
      {po.status !== "Draft" ? (
        <Card>
          <EmptyState
            title="This purchase order can no longer be edited"
            description="Only drafts can be changed."
            action={<ButtonLink href={`/purchase-orders/${po.id}`}>Back to purchase order</ButtonLink>}
          />
        </Card>
      ) : (
        <PurchaseOrderForm order={po} />
      )}
    </>
  );
}
