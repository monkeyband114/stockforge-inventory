"use client";

import { Eye, EyeOff, LogIn } from "lucide-react";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { Suspense, useEffect, useState } from "react";
import { Button } from "@/components/ui/Button";
import { FormError } from "@/components/ui/Field";
import { Input } from "@/components/ui/Input";
import { PageSpinner } from "@/components/ui/Spinner";
import { AuthHeading } from "@/features/auth/PasswordChecklist";
import { safeNext } from "@/features/auth/safeNext";
import { ApiError } from "@/lib/api";
import { homePathFor, useAuth } from "@/lib/auth";
import { emailError } from "@/lib/validation";

function LoginForm() {
  const { login, status, user } = useAuth();
  const router = useRouter();
  const params = useSearchParams();
  const next = safeNext(params.get("next"));
  const reason = params.get("reason");

  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [errors, setErrors] = useState<{ email?: string; password?: string }>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  // Already signed in (e.g. session restored from the refresh cookie) → go straight in.
  useEffect(() => {
    if (status === "authenticated" && user && !submitting) router.replace(next ?? homePathFor(user));
  }, [status, user, next, router, submitting]);

  const onSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    const nextErrors = { email: emailError(email), password: password ? undefined : "Password is required." };
    setErrors(nextErrors);
    if (nextErrors.email || nextErrors.password) return;

    setSubmitting(true);
    setFormError(null);
    try {
      const signedIn = await login({ email: email.trim(), password });
      router.replace(next && signedIn.tenant ? next : homePathFor(signedIn));
    } catch (err) {
      setSubmitting(false);
      if (err instanceof ApiError) {
        if (err.code === "rate_limited") setFormError("Too many sign-in attempts. Please wait a minute and try again.");
        else if (err.code === "account_locked") setFormError(err.detail || "Your account is temporarily locked after repeated failed attempts.");
        else if (err.errors) {
          setErrors({ email: err.fieldError("email"), password: err.fieldError("password") });
          setFormError(err.detail);
        } else setFormError(err.detail);
      } else {
        setFormError("Sign-in failed. Please try again.");
      }
    }
  };

  if (status === "loading") return <PageSpinner label="Checking your session…" />;

  return (
    <>
      <AuthHeading title="Sign in to StockForge" description="Use your work email and password." />
      {reason === "expired" && !formError && (
        <div className="mb-5 rounded-md border border-amber-200 bg-amber-50 px-3 py-2 text-sm text-amber-800" role="status">
          Your session ended. Please sign in again to continue.
        </div>
      )}
      <form onSubmit={onSubmit} noValidate className="space-y-5">
        <Input
          label="Email"
          type="email"
          autoComplete="username"
          inputMode="email"
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          error={errors.email}
          required
          autoFocus
        />
        <div className="space-y-1.5">
          <Input
            label="Password"
            type={showPassword ? "text" : "password"}
            autoComplete="current-password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            error={errors.password}
            required
            className="pr-10"
          />
          <div className="flex items-center justify-between">
            <button
              type="button"
              onClick={() => setShowPassword((s) => !s)}
              className="inline-flex items-center gap-1 text-xs font-medium text-slate-500 hover:text-slate-800"
              aria-pressed={showPassword}
            >
              {showPassword ? <EyeOff className="size-3.5" aria-hidden /> : <Eye className="size-3.5" aria-hidden />}
              {showPassword ? "Hide password" : "Show password"}
            </button>
            <Link href="/forgot-password" className="text-xs font-medium text-brand-700 hover:text-brand-600">
              Forgot password?
            </Link>
          </div>
        </div>
        <FormError message={formError} />
        <Button type="submit" className="w-full" loading={submitting} icon={<LogIn className="size-4" />}>
          Sign in
        </Button>
      </form>
      <p className="mt-8 text-center text-sm text-slate-500">
        New to StockForge?{" "}
        <Link href="/register" className="font-medium text-brand-700 hover:text-brand-600">
          Create an organization
        </Link>
      </p>
    </>
  );
}

export default function LoginPage() {
  return (
    <Suspense fallback={<PageSpinner />}>
      <LoginForm />
    </Suspense>
  );
}
