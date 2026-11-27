"use client";

import { MailCheck } from "lucide-react";
import Link from "next/link";
import { useState } from "react";
import { Button } from "@/components/ui/Button";
import { FormError } from "@/components/ui/Field";
import { Input } from "@/components/ui/Input";
import { AuthHeading } from "@/features/auth/PasswordChecklist";
import { api, ApiError } from "@/lib/api";
import { emailError } from "@/lib/validation";

export default function ForgotPasswordPage() {
  const [email, setEmail] = useState("");
  const [error, setError] = useState<string | undefined>();
  const [formError, setFormError] = useState<string | null>(null);
  const [sent, setSent] = useState(false);
  const [submitting, setSubmitting] = useState(false);

  const onSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    const err = emailError(email);
    setError(err);
    if (err) return;
    setSubmitting(true);
    setFormError(null);
    try {
      // Always 202: the response never reveals whether the account exists.
      await api.post("/api/auth/forgot-password", { email: email.trim() }, { anonymous: true });
      setSent(true);
    } catch (e) {
      setFormError(e instanceof ApiError && e.code === "rate_limited" ? "Too many requests. Please wait a minute and try again." : e instanceof Error ? e.message : "Request failed.");
    } finally {
      setSubmitting(false);
    }
  };

  if (sent) {
    return (
      <div>
        <div className="mb-6 inline-flex rounded-full bg-emerald-50 p-3 text-emerald-600">
          <MailCheck className="size-6" aria-hidden />
        </div>
        <AuthHeading
          title="Check your inbox"
          description={
            <>
              If an account exists for <span className="font-medium text-slate-700">{email.trim()}</span>, we&apos;ve sent a link to reset your
              password. The link expires shortly, so use it soon.
            </>
          }
        />
        <Link href="/login" className="text-sm font-medium text-brand-700 hover:text-brand-600">
          ← Back to sign in
        </Link>
      </div>
    );
  }

  return (
    <>
      <AuthHeading title="Reset your password" description="Enter your account email and we'll send you a reset link." />
      <form onSubmit={onSubmit} noValidate className="space-y-5">
        <Input label="Email" type="email" autoComplete="email" value={email} onChange={(e) => setEmail(e.target.value)} error={error} required autoFocus />
        <FormError message={formError} />
        <Button type="submit" className="w-full" loading={submitting}>
          Send reset link
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
