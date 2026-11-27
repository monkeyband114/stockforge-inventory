"use client";

import { Lock } from "lucide-react";
import type { ReactNode } from "react";
import { Card } from "@/components/ui/Card";
import { EmptyState } from "@/components/ui/EmptyState";
import { useAuth } from "@/lib/auth";

/** Page-level gate: renders children only when the user holds ANY of the permissions. */
export function RequirePermission({ anyOf, children, requireTenant = true }: { anyOf: string[]; children: ReactNode; requireTenant?: boolean }) {
  const { hasAnyPermission, user } = useAuth();
  if ((requireTenant && !user?.tenant) || !hasAnyPermission(...anyOf)) {
    return (
      <Card>
        <EmptyState
          icon={<Lock />}
          title="You don't have access to this page"
          description={
            requireTenant && !user?.tenant
              ? "This page belongs to an organization workspace. Your session is not signed in to an organization."
              : "Ask an organization administrator to grant you the required permission."
          }
        />
      </Card>
    );
  }
  return <>{children}</>;
}
