"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useMemo, useState } from "react";
import { Button } from "@/components/ui/Button";
import { FormError } from "@/components/ui/Field";
import { Input } from "@/components/ui/Input";
import { Select } from "@/components/ui/Select";
import { AuthHeading, PasswordChecklist } from "@/features/auth/PasswordChecklist";
import { ApiError, fieldErrors } from "@/lib/api";
import { homePathFor, useAuth } from "@/lib/auth";
import { allTimeZones, browserTimeZone, COMMON_CURRENCIES, emailError, hasErrors, passwordError, required } from "@/lib/validation";

type Errors = Partial<Record<"organizationName" | "firstName" | "lastName" | "email" | "password" | "confirm" | "baseCurrency" | "timeZone", string>>;

export default function RegisterPage() {
  const { register, status, user } = useAuth();
  const router = useRouter();
  const [form, setForm] = useState({
    organizationName: "",
    firstName: "",
    lastName: "",
    email: "",
    password: "",
    confirm: "",
    baseCurrency: "USD",
    timeZone: "UTC",
  });
  const [errors, setErrors] = useState<Errors>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const zones = useMemo(() => allTimeZones(), []);

  useEffect(() => {
    setForm((f) => ({ ...f, timeZone: browserTimeZone() }));
  }, []);

  useEffect(() => {
    if (status === "authenticated" && user && !submitting) router.replace(homePathFor(user));
  }, [status, user, router, submitting]);

  const set = (key: keyof typeof form) => (e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement>) =>
    setForm((f) => ({ ...f, [key]: e.target.value }));

  const onSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    const next: Errors = {
      organizationName: required(form.organizationName, "Organization name"),
      firstName: required(form.firstName, "First name"),
      lastName: required(form.lastName, "Last name"),
      email: emailError(form.email),
      password: passwordError(form.password),
      confirm: form.confirm !== form.password ? "Passwords do not match." : undefined,
      baseCurrency: /^[A-Za-z]{3}$/.test(form.baseCurrency) ? undefined : "Use a 3-letter ISO currency code.",
    };
    setErrors(next);
    if (hasErrors(next)) return;

    setSubmitting(true);
    setFormError(null);
    try {
      const created = await register({
        organizationName: form.organizationName.trim(),
        firstName: form.firstName.trim(),
        lastName: form.lastName.trim(),
        email: form.email.trim(),
        password: form.password,
        baseCurrency: form.baseCurrency.toUpperCase(),
        timeZone: form.timeZone || null,
      });
      router.replace(homePathFor(created));
    } catch (err) {
      setSubmitting(false);
      setErrors(fieldErrors(err) as Errors);
      setFormError(err instanceof ApiError ? err.detail : "Registration failed. Please try again.");
    }
  };

  return (
    <>
      <AuthHeading title="Create your organization" description="Set up a new StockForge workspace. You'll be its administrator." />
      <form onSubmit={onSubmit} noValidate className="space-y-4">
        <Input label="Organization name" value={form.organizationName} onChange={set("organizationName")} error={errors.organizationName} required autoFocus autoComplete="organization" />
        <div className="grid grid-cols-2 gap-3">
          <Input label="First name" value={form.firstName} onChange={set("firstName")} error={errors.firstName} required autoComplete="given-name" />
          <Input label="Last name" value={form.lastName} onChange={set("lastName")} error={errors.lastName} required autoComplete="family-name" />
        </div>
        <Input label="Work email" type="email" value={form.email} onChange={set("email")} error={errors.email} required autoComplete="email" />
        <Input label="Password" type="password" value={form.password} onChange={set("password")} error={errors.password} required autoComplete="new-password" />
        <PasswordChecklist password={form.password} />
        <Input label="Confirm password" type="password" value={form.confirm} onChange={set("confirm")} error={errors.confirm} required autoComplete="new-password" />
        <div className="grid grid-cols-2 gap-3">
          <Select
            label="Base currency"
            value={form.baseCurrency}
            onChange={set("baseCurrency")}
            error={errors.baseCurrency}
            options={COMMON_CURRENCIES.map((c) => ({ value: c, label: c }))}
          />
          <Select
            label="Time zone"
            value={form.timeZone}
            onChange={set("timeZone")}
            error={errors.timeZone}
            options={(zones.includes(form.timeZone) ? zones : [form.timeZone, ...zones]).map((z) => ({ value: z, label: z }))}
          />
        </div>
        <FormError message={formError} />
        <Button type="submit" className="w-full" loading={submitting}>
          Create organization
        </Button>
      </form>
      <p className="mt-8 text-center text-sm text-slate-500">
        Already have an account?{" "}
        <Link href="/login" className="font-medium text-brand-700 hover:text-brand-600">
          Sign in
        </Link>
      </p>
    </>
  );
}
