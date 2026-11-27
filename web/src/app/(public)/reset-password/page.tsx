"use client";

import { CheckCircle2 } from "lucide-react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { Suspense, useState } from "react";
import { Button, ButtonLink } from "@/components/ui/Button";
import { FormError } from "@/components/ui/Field";
import { Input } from "@/components/ui/Input";
import { PageSpinner } from "@/components/ui/Spinner";
import { AuthHeading, PasswordChecklist } from "@/features/auth/PasswordChecklist";
import { api, ApiError } from "@/lib/api";
import { passwordError } from "@/lib/validation";

function ResetPasswordForm() {
  const params = useSearchParams();
  const email = params.get("email") ?? "";
  const token = params.get("token") ?? "";
  const [password, setPassword] = useState("");
  const [confirm, setConfirm] = useState("");
  const [errors, setErrors] = useState<{ password?: string; confirm?: string }>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [done, setDone] = useState(false);

  if (!email || !token) {
    return (
      <>
        <AuthHeading title="Invalid reset link" description="This password reset link is incomplete. Request a new one and use the link from the email." />
        <ButtonLink href="/forgot-password" variant="primary">
          Request a new link
        </ButtonLink>
      </>
    );
  }

  if (done) {
    return (
      <>
        <div className="mb-6 inline-flex rounded-full bg-emerald-50 p-3 text-emerald-600">
          <CheckCircle2 className="size-6" aria-hidden />
        </div>
        <AuthHeading title="Password updated" description="Your password has been reset and other sessions were signed out. Sign in with your new password." />
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
      await api.post("/api/auth/reset-password", { email, token, newPassword: password }, { anonymous: true });
      setDone(true);
    } catch (err) {
      if (err instanceof ApiError) {
        setErrors({ password: err.fieldError("newPassword") });
        setFormError(err.detail);
      } else setFormError("Reset failed. Please try again.");
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <>
      <AuthHeading
        title="Choose a new password"
        description={
          <>
            Resetting the password for <span className="font-medium text-slate-700">{email}</span>.
          </>
        }
      />
      <form onSubmit={onSubmit} noValidate className="space-y-4">
        <Input label="New password" type="password" autoComplete="new-password" value={password} onChange={(e) => setPassword(e.target.value)} error={errors.password} required autoFocus />
        <PasswordChecklist password={password} />
        <Input label="Confirm new password" type="password" autoComplete="new-password" value={confirm} onChange={(e) => setConfirm(e.target.value)} error={errors.confirm} required />
        <FormError message={formError} />
        <Button type="submit" className="w-full" loading={submitting}>
          Reset password
        </Button>
      </form>
      <p className="mt-8 text-center text-sm text-slate-500">
        <Link href="/login" className="font-medium text-brand-700 hover:text-brand-600">
          Back to sign in
        </Link>
      </p>
    </>
  );
}

export default function ResetPasswordPage() {
  return (
    <Suspense fallback={<PageSpinner />}>
      <ResetPasswordForm />
    </Suspense>
  );
}
