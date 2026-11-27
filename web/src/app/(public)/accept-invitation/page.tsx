"use client";

import { useRouter, useSearchParams } from "next/navigation";
import { Suspense, useState } from "react";
import { Button, ButtonLink } from "@/components/ui/Button";
import { FormError } from "@/components/ui/Field";
import { Input } from "@/components/ui/Input";
import { PageSpinner } from "@/components/ui/Spinner";
import { AuthHeading, PasswordChecklist } from "@/features/auth/PasswordChecklist";
import { ApiError } from "@/lib/api";
import { homePathFor, useAuth } from "@/lib/auth";
import { passwordError } from "@/lib/validation";

function AcceptInvitationForm() {
  const params = useSearchParams();
  const router = useRouter();
  const { acceptInvitation } = useAuth();
  const email = params.get("email") ?? "";
  const token = params.get("token") ?? "";
  const [password, setPassword] = useState("");
  const [confirm, setConfirm] = useState("");
  const [errors, setErrors] = useState<{ password?: string; confirm?: string }>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  if (!email || !token) {
    return (
      <>
        <AuthHeading title="Invalid invitation link" description="This invitation link is incomplete. Open the link from your invitation email, or ask your administrator to resend it." />
        <ButtonLink href="/login" variant="primary">
          Go to sign in
        </ButtonLink>
      </>
    );
  }

  const onSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    const next = { password: passwordError(password), confirm: confirm !== password ? "Passwords do not match." : undefined };
    setErrors(next);
    if (next.password || next.confirm) return;
    setSubmitting(true);
    setFormError(null);
    try {
      const user = await acceptInvitation({ email, token, password });
      router.replace(homePathFor(user));
    } catch (err) {
      setSubmitting(false);
      if (err instanceof ApiError) {
        setErrors({ password: err.fieldError("password") });
        setFormError(err.detail);
      } else setFormError("Could not accept the invitation. Please try again.");
    }
  };

  return (
    <>
      <AuthHeading
        title="Join your team on StockForge"
        description={
          <>
            Set a password for <span className="font-medium text-slate-700">{email}</span> to accept the invitation.
          </>
        }
      />
      <form onSubmit={onSubmit} noValidate className="space-y-4">
        <Input label="Email" type="email" value={email} readOnly disabled autoComplete="username" />
        <Input label="Password" type="password" autoComplete="new-password" value={password} onChange={(e) => setPassword(e.target.value)} error={errors.password} required autoFocus />
        <PasswordChecklist password={password} />
        <Input label="Confirm password" type="password" autoComplete="new-password" value={confirm} onChange={(e) => setConfirm(e.target.value)} error={errors.confirm} required />
        <FormError message={formError} />
        <Button type="submit" className="w-full" loading={submitting}>
          Accept invitation
        </Button>
      </form>
    </>
  );
}

export default function AcceptInvitationPage() {
  return (
    <Suspense fallback={<PageSpinner />}>
      <AcceptInvitationForm />
    </Suspense>
  );
}
