"use client";

import { PageHeader } from "@/components/ui/PageHeader";
import { ChangePasswordCard, ProfileCard, SessionsCard } from "@/features/account/AccountCards";

export default function AccountPage() {
  return (
    <>
      <PageHeader title="Account & security" description="Your profile, password and signed-in devices." />
      <div className="grid items-start gap-6 xl:grid-cols-2">
        <ProfileCard />
        <ChangePasswordCard />
        <div className="xl:col-span-2">
          <SessionsCard />
        </div>
      </div>
    </>
  );
}
