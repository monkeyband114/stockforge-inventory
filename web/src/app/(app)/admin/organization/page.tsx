"use client";

import { Card, CardBody } from "@/components/ui/Card";
import { ErrorState } from "@/components/ui/ErrorState";
import { PageHeader } from "@/components/ui/PageHeader";
import { Skeleton } from "@/components/ui/Skeleton";
import { TenantStatusBadge } from "@/features/common/StatusBadges";
import { OrganizationProfileForm, OrganizationSettingsForm } from "@/features/admin/OrganizationForms";
import { RequirePermission } from "@/features/common/RequirePermission";
import { useAuth } from "@/lib/auth";
import { formatDate } from "@/lib/format";
import { P } from "@/lib/permissions";
import { useOrganization } from "@/lib/queries";

function FormSkeleton({ fields }: { fields: number }) {
  return (
    <Card>
      <CardBody className="space-y-4">
        <Skeleton className="h-4 w-48" />
        <div className="grid gap-4 sm:grid-cols-2">
          {Array.from({ length: fields }, (_, i) => (
            <div key={i} className="space-y-2">
              <Skeleton className="h-3 w-24" />
              <Skeleton className="h-9" />
            </div>
          ))}
        </div>
      </CardBody>
    </Card>
  );
}

function OrganizationContent() {
  const { hasPermission } = useAuth();
  const canEdit = hasPermission(P.organizationManage);
  const query = useOrganization();
  const org = query.data;

  return (
    <>
      <PageHeader
        title="Organization"
        description={canEdit ? "Manage your organization's profile and operational settings." : "Your organization's profile and settings (read-only)."}
        meta={
          org && (
            <>
              <TenantStatusBadge status={org.status} />
              <span className="text-xs text-slate-500">Created {formatDate(org.createdAt)}</span>
            </>
          )
        }
      />
      {query.isPending ? (
        <div className="grid gap-6 xl:grid-cols-2">
          <FormSkeleton fields={6} />
          <FormSkeleton fields={4} />
        </div>
      ) : query.isError && !org ? (
        <Card>
          <ErrorState error={query.error} onRetry={() => void query.refetch()} />
        </Card>
      ) : org ? (
        <div className="grid items-start gap-6 xl:grid-cols-2">
          <OrganizationProfileForm
            org={org}
            canEdit={canEdit}
            onReload={async () => {
              const result = await query.refetch();
              return result.data;
            }}
          />
          <OrganizationSettingsForm org={org} canEdit={canEdit} />
        </div>
      ) : null}
    </>
  );
}

export default function OrganizationPage() {
  return (
    <RequirePermission anyOf={[P.organizationView]}>
      <OrganizationContent />
    </RequirePermission>
  );
}
