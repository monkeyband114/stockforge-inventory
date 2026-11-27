"use client";

import { useQueryClient } from "@tanstack/react-query";
import { CheckCircle2 } from "lucide-react";
import { useEffect, useMemo, useState } from "react";
import { Button } from "@/components/ui/Button";
import { Dialog } from "@/components/ui/Dialog";
import { FormError } from "@/components/ui/Field";
import { Input } from "@/components/ui/Input";
import { Select } from "@/components/ui/Select";
import { useToast } from "@/components/ui/Toast";
import { api, ApiError, fieldErrors } from "@/lib/api";
import type { CreatedOrganization, CreateOrganizationRequest } from "@/lib/types";
import { allTimeZones, browserTimeZone, COMMON_CURRENCIES, emailError, hasErrors, required } from "@/lib/validation";

type Errors = Partial<Record<keyof CreateOrganizationRequest, string>>;

const EMPTY = { organizationName: "", adminEmail: "", adminFirstName: "", adminLastName: "", baseCurrency: "USD", timeZone: "UTC" };

export function CreateOrganizationDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const zones = useMemo(() => allTimeZones(), []);
  const [form, setForm] = useState(EMPTY);
  const [errors, setErrors] = useState<Errors>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [created, setCreated] = useState<{ result: CreatedOrganization; adminEmail: string } | null>(null);

  useEffect(() => {
    if (open) {
      setForm({ ...EMPTY, timeZone: browserTimeZone() });
      setErrors({});
      setFormError(null);
      setCreated(null);
    }
  }, [open]);

  const set = (key: keyof typeof EMPTY) => (e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement>) => setForm((f) => ({ ...f, [key]: e.target.value }));

  const submit = async () => {
    if (created) {
      onClose();
      return;
    }
    const next: Errors = {
      organizationName: required(form.organizationName, "Organization name"),
      adminEmail: emailError(form.adminEmail),
      adminFirstName: required(form.adminFirstName, "First name"),
      adminLastName: required(form.adminLastName, "Last name"),
    };
    setErrors(next);
    if (hasErrors(next)) return;

    const body: CreateOrganizationRequest = {
      organizationName: form.organizationName.trim(),
      adminEmail: form.adminEmail.trim(),
      adminFirstName: form.adminFirstName.trim(),
      adminLastName: form.adminLastName.trim(),
      baseCurrency: form.baseCurrency,
      timeZone: form.timeZone || null,
    };
    setBusy(true);
    setFormError(null);
    try {
      const result = await api.post<CreatedOrganization>("/api/platform/tenants", body);
      await queryClient.invalidateQueries({ queryKey: ["platform"] });
      setCreated({ result, adminEmail: body.adminEmail });
      toast.success("Organization created", result.name);
    } catch (err) {
      setErrors(fieldErrors(err) as Errors);
      setFormError(err instanceof ApiError ? err.detail : "The organization could not be created.");
    } finally {
      setBusy(false);
    }
  };

  return (
    <Dialog
      open={open}
      onClose={onClose}
      busy={busy}
      size="lg"
      title={created ? "Organization created" : "Create organization"}
      description={created ? undefined : "Provision a new tenant and invite its first administrator by email."}
      onSubmit={submit}
      footer={
        created ? (
          <Button type="submit">Done</Button>
        ) : (
          <>
            <Button variant="secondary" onClick={onClose} disabled={busy}>
              Cancel
            </Button>
            <Button type="submit" loading={busy}>
              Create organization
            </Button>
          </>
        )
      }
    >
      {created ? (
        <div className="flex items-start gap-3" role="status">
          <CheckCircle2 className="mt-0.5 size-6 shrink-0 text-emerald-600" aria-hidden />
          <div className="text-sm text-slate-600">
            <p className="font-medium text-slate-900">
              {created.result.name} <span className="font-mono text-xs font-normal text-slate-500">({created.result.slug})</span> is ready.
            </p>
            <p className="mt-1">
              {created.result.adminInvited ? (
                <>
                  An invitation was sent to <span className="font-medium text-slate-800">{created.adminEmail}</span> to set a password and sign in as the
                  organization administrator.
                </>
              ) : (
                <>
                  <span className="font-medium text-slate-800">{created.adminEmail}</span> already has a StockForge account and was added as the organization
                  administrator.
                </>
              )}
            </p>
          </div>
        </div>
      ) : (
        <div className="space-y-4">
          <Input label="Organization name" required value={form.organizationName} onChange={set("organizationName")} error={errors.organizationName} maxLength={200} />
          <fieldset className="space-y-4 rounded-md border border-slate-200 p-4">
            <legend className="px-1 text-sm font-medium text-slate-700">First administrator</legend>
            <Input label="Email" type="email" required value={form.adminEmail} onChange={set("adminEmail")} error={errors.adminEmail} autoComplete="off" />
            <div className="grid gap-4 sm:grid-cols-2">
              <Input label="First name" required value={form.adminFirstName} onChange={set("adminFirstName")} error={errors.adminFirstName} />
              <Input label="Last name" required value={form.adminLastName} onChange={set("adminLastName")} error={errors.adminLastName} />
            </div>
          </fieldset>
          <div className="grid gap-4 sm:grid-cols-2">
            <Select label="Base currency" value={form.baseCurrency} onChange={set("baseCurrency")} error={errors.baseCurrency} options={COMMON_CURRENCIES.map((c) => ({ value: c, label: c }))} />
            <Select
              label="Time zone"
              value={form.timeZone}
              onChange={set("timeZone")}
              error={errors.timeZone}
              options={(zones.includes(form.timeZone) ? zones : [form.timeZone, ...zones]).map((z) => ({ value: z, label: z }))}
            />
          </div>
          <FormError message={formError} />
        </div>
      )}
    </Dialog>
  );
}
