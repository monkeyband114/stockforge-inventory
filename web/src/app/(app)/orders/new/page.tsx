"use client";

import { useQuery } from "@tanstack/react-query";
import { useSearchParams } from "next/navigation";
import { Suspense } from "react";
import { PageHeader } from "@/components/ui/PageHeader";
import { PageSpinner } from "@/components/ui/Spinner";
import { RequirePermission } from "@/features/common/RequirePermission";
import { toPickedCustomer } from "@/features/sales/CustomerPicker";
import { OrderForm } from "@/features/sales/OrderForm";
import { api } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { P } from "@/lib/permissions";
import { qk } from "@/lib/queries";
import type { Customer } from "@/lib/types";

export default function NewOrderPage() {
  return (
    <RequirePermission anyOf={[P.ordersCreate]}>
      <Suspense fallback={<PageSpinner />}>
        <NewOrderView />
      </Suspense>
    </RequirePermission>
  );
}

function NewOrderView() {
  const params = useSearchParams();
  const { hasPermission } = useAuth();
  const customerId = params.get("customerId");
  // Prefill from "New order" on the customers page.
  const customer = useQuery({
    queryKey: qk.customer(customerId ?? ""),
    queryFn: () => api.get<Customer>(`/api/customers/${customerId}`),
    enabled: !!customerId && hasPermission(P.customersView),
  });

  return (
    <>
      <PageHeader title="New sales order" breadcrumbs={[{ label: "Orders", href: "/orders" }, { label: "New order" }]} />
      {customerId && customer.isPending && hasPermission(P.customersView) ? (
        <PageSpinner label="Loading customer…" />
      ) : (
        <OrderForm initialCustomer={customer.data?.isActive ? toPickedCustomer(customer.data) : null} />
      )}
    </>
  );
}
