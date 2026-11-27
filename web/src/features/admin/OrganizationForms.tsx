"use client";

import { useQueryClient } from "@tanstack/react-query";
import { AlertTriangle, RefreshCw, Save } from "lucide-react";
import { useEffect, useMemo, useRef, useState } from "react";
import { Button } from "@/components/ui/Button";
import { Card, CardBody, CardHeader } from "@/components/ui/Card";
import { Switch } from "@/components/ui/Checkbox";
import { FormError } from "@/components/ui/Field";
import { Input } from "@/components/ui/Input";
import { Select } from "@/components/ui/Select";
import { useToast } from "@/components/ui/Toast";
import { api, ApiError, fieldErrors } from "@/lib/api";
import { parseNumber } from "@/lib/format";
import { qk } from "@/lib/queries";
import type { Address, Organization, TenantSettings, UpdateOrganizationRequest } from "@/lib/types";
import { allTimeZones, COMMON_CURRENCIES, emailError, hasErrors, required } from "@/lib/validation";

// ---------------------------------------------------------------------------------------------
// Profile
// ---------------------------------------------------------------------------------------------

interface ProfileState {
  name: string;
  contactEmail: string;
  phone: string;
  line1: string;
  line2: string;
  city: string;
  state: string;
  postalCode: string;
  country: string;
  baseCurrency: string;
  timeZone: string;
}

function profileFrom(org: Organization): ProfileState {
  return {
    name: org.name,
    contactEmail: org.contactEmail,
    phone: org.phone ?? "",
    line1: org.address?.line1 ?? "",
    line2: org.address?.line2 ?? "",
    city: org.address?.city ?? "",
    state: org.address?.state ?? "",
    postalCode: org.address?.postalCode ?? "",
    country: org.address?.country ?? "",
    baseCurrency: org.baseCurrency,
    timeZone: org.timeZone,
  };
}

const same = (a: ProfileState, b: ProfileState) => JSON.stringify(a) === JSON.stringify(b);

function ConflictBanner({ onReload, reloading }: { onReload: () => void; reloading: boolean }) {
  return (
    <div role="alert" className="flex flex-wrap items-center justify-between gap-3 rounded-md border border-amber-200 bg-amber-50 px-3 py-2.5 text-sm text-amber-900">
      <span className="flex items-center gap-2">
        <AlertTriangle className="size-4 shrink-0" aria-hidden />
        Someone else changed these details while you were editing. Reload to see the latest version, then reapply your changes.
      </span>
      <Button variant="secondary" size="sm" onClick={onReload} loading={reloading} icon={<RefreshCw className="size-3.5" />}>
        Reload
      </Button>
    </div>
  );
}

export function OrganizationProfileForm({ org, canEdit, onReload }: { org: Organization; canEdit: boolean; onReload: () => Promise<Organization | undefined> }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const [form, setForm] = useState<ProfileState>(() => profileFrom(org));
  const original = useRef<ProfileState>(profileFrom(org));
  const [version, setVersion] = useState(org.version);
  const [errors, setErrors] = useState<Record<string, string | undefined>>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [conflict, setConflict] = useState(false);
  const [saving, setSaving] = useState(false);
  const [reloading, setReloading] = useState(false);
  const zones = useMemo(() => allTimeZones(), []);
  const dirty = !same(form, original.current);

  // Pick up newer server data: re-initialise when clean; when only non-profile fields changed
  // (e.g. our own settings save bumped the row version) just adopt the new version.
  useEffect(() => {
    if (org.version === version) return;
    const incoming = profileFrom(org);
    if (!dirty) {
      setForm(incoming);
      original.current = incoming;
      setVersion(org.version);
    } else if (same(incoming, original.current)) {
      setVersion(org.version);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [org]);

  const set = (key: keyof ProfileState) => (e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement>) =>
    setForm((f) => ({ ...f, [key]: e.target.value }));

  const reload = async () => {
    setReloading(true);
    try {
      const latest = await onReload();
      if (latest) {
        const fresh = profileFrom(latest);
        setForm(fresh);
        original.current = fresh;
        setVersion(latest.version);
        setConflict(false);
        setErrors({});
        setFormError(null);
      }
    } finally {
      setReloading(false);
    }
  };

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    const anyAddress = [form.line1, form.line2, form.city, form.state, form.postalCode, form.country].some((v) => v.trim());
    const next: Record<string, string | undefined> = {
      name: required(form.name, "Organization name"),
      contactEmail: emailError(form.contactEmail),
      baseCurrency: /^[A-Za-z]{3}$/.test(form.baseCurrency.trim()) ? undefined : "Use a 3-letter ISO currency code.",
      timeZone: required(form.timeZone, "Time zone"),
      "address.line1": anyAddress ? required(form.line1, "Address line 1") : undefined,
      "address.city": anyAddress ? required(form.city, "City") : undefined,
      "address.country": anyAddress ? required(form.country, "Country") : undefined,
    };
    setErrors(next);
    if (hasErrors(next)) return;

    const address: Address | null = anyAddress
      ? {
          line1: form.line1.trim(),
          line2: form.line2.trim() || null,
          city: form.city.trim(),
          state: form.state.trim() || null,
          postalCode: form.postalCode.trim() || null,
          country: form.country.trim(),
        }
      : null;
    const body: UpdateOrganizationRequest = {
      name: form.name.trim(),
      contactEmail: form.contactEmail.trim(),
      phone: form.phone.trim() || null,
      address,
      baseCurrency: form.baseCurrency.trim().toUpperCase(),
      timeZone: form.timeZone,
      version,
    };

    setSaving(true);
    setFormError(null);
    try {
      const saved = await api.put<Organization>("/api/organization", body);
      queryClient.setQueryData(qk.organization, saved);
      const fresh = profileFrom(saved);
      setForm(fresh);
      original.current = fresh;
      setVersion(saved.version);
      setConflict(false);
      toast.success("Organization profile saved");
    } catch (err) {
      if (err instanceof ApiError && err.code === "concurrency_conflict") {
        setConflict(true);
      } else {
        setErrors(fieldErrors(err));
        setFormError(err instanceof ApiError ? err.detail : "The profile could not be saved.");
      }
    } finally {
      setSaving(false);
    }
  };

  const ro = !canEdit;
  const zoneOptions = (zones.includes(form.timeZone) ? zones : [form.timeZone, ...zones]).map((z) => ({ value: z, label: z }));
  const currencyOptions = (COMMON_CURRENCIES.includes(form.baseCurrency) ? COMMON_CURRENCIES : [form.baseCurrency, ...COMMON_CURRENCIES]).map((c) => ({
    value: c,
    label: c,
  }));

  return (
    <Card>
      <CardHeader title="Organization profile" description="Name, contact details, base currency and time zone used across reports." />
      <form onSubmit={submit} noValidate>
        <CardBody className="space-y-5">
          {conflict && <ConflictBanner onReload={reload} reloading={reloading} />}
          <div className="grid gap-4 sm:grid-cols-2">
            <Input label="Organization name" required value={form.name} onChange={set("name")} error={errors.name} disabled={ro} maxLength={200} />
            <Input label="Slug" value={org.slug} disabled hint="Used in links; cannot be changed." />
            <Input label="Contact email" type="email" required value={form.contactEmail} onChange={set("contactEmail")} error={errors.contactEmail} disabled={ro} />
            <Input label="Phone" type="tel" value={form.phone} onChange={set("phone")} error={errors.phone} disabled={ro} maxLength={40} />
            <Select
              label="Base currency"
              required
              value={form.baseCurrency}
              onChange={set("baseCurrency")}
              error={errors.baseCurrency}
              disabled={ro}
              options={currencyOptions}
              hint="Inventory values and prices are shown in this currency."
            />
            <Select label="Time zone" required value={form.timeZone} onChange={set("timeZone")} error={errors.timeZone} disabled={ro} options={zoneOptions} hint="Daily trends and summaries use this zone." />
          </div>
          <fieldset className="space-y-4">
            <legend className="text-sm font-semibold text-slate-900">Address</legend>
            <div className="grid gap-4 sm:grid-cols-2">
              <Input label="Address line 1" value={form.line1} onChange={set("line1")} error={errors["address.line1"]} disabled={ro} maxLength={200} />
              <Input label="Address line 2" value={form.line2} onChange={set("line2")} error={errors["address.line2"]} disabled={ro} maxLength={200} />
              <Input label="City" value={form.city} onChange={set("city")} error={errors["address.city"]} disabled={ro} maxLength={100} />
              <Input label="State / region" value={form.state} onChange={set("state")} error={errors["address.state"]} disabled={ro} maxLength={100} />
              <Input label="Postal code" value={form.postalCode} onChange={set("postalCode")} error={errors["address.postalCode"]} disabled={ro} maxLength={20} />
              <Input label="Country" value={form.country} onChange={set("country")} error={errors["address.country"]} disabled={ro} maxLength={100} />
            </div>
          </fieldset>
          <FormError message={formError} />
        </CardBody>
        {canEdit && (
          <div className="flex items-center justify-end gap-2 border-t border-slate-200 px-5 py-3">
            <Button
              variant="secondary"
              disabled={!dirty || saving}
              onClick={() => {
                setForm(original.current);
                setErrors({});
                setFormError(null);
              }}
            >
              Discard
            </Button>
            <Button type="submit" loading={saving} disabled={!dirty} icon={<Save className="size-4" />}>
              Save profile
            </Button>
          </div>
        )}
      </form>
    </Card>
  );
}

// ---------------------------------------------------------------------------------------------
// Settings
// ---------------------------------------------------------------------------------------------

interface SettingsState {
  orderApprovalAmountThreshold: string;
  orderApprovalQuantityThreshold: string;
  requireApprovalForAllOrders: boolean;
  defaultTaxRatePercent: string;
  invoiceDueDays: string;
  stockAdjustmentRequiresApproval: boolean;
  lowStockAlertsEnabled: boolean;
}

function settingsFrom(s: TenantSettings): SettingsState {
  return {
    orderApprovalAmountThreshold: String(s.orderApprovalAmountThreshold),
    orderApprovalQuantityThreshold: String(s.orderApprovalQuantityThreshold),
    requireApprovalForAllOrders: s.requireApprovalForAllOrders,
    defaultTaxRatePercent: String(Number((s.defaultTaxRate * 100).toFixed(4))),
    invoiceDueDays: String(s.invoiceDueDays),
    stockAdjustmentRequiresApproval: s.stockAdjustmentRequiresApproval,
    lowStockAlertsEnabled: s.lowStockAlertsEnabled,
  };
}

export function OrganizationSettingsForm({ org, canEdit }: { org: Organization; canEdit: boolean }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const [form, setForm] = useState<SettingsState>(() => settingsFrom(org.settings));
  const original = useRef<SettingsState>(settingsFrom(org.settings));
  const [errors, setErrors] = useState<Record<string, string | undefined>>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const dirty = JSON.stringify(form) !== JSON.stringify(original.current);

  useEffect(() => {
    const incoming = settingsFrom(org.settings);
    if (!dirty) {
      setForm(incoming);
      original.current = incoming;
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [org.settings]);

  const setText = (key: keyof SettingsState) => (e: React.ChangeEvent<HTMLInputElement>) => setForm((f) => ({ ...f, [key]: e.target.value }));
  const setFlag = (key: keyof SettingsState) => (value: boolean) => setForm((f) => ({ ...f, [key]: value }));

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    const amount = parseNumber(form.orderApprovalAmountThreshold);
    const qty = parseNumber(form.orderApprovalQuantityThreshold);
    const tax = parseNumber(form.defaultTaxRatePercent);
    const due = parseNumber(form.invoiceDueDays);
    const next: Record<string, string | undefined> = {
      orderApprovalAmountThreshold: Number.isNaN(amount) || amount < 0 ? "Enter an amount of 0 or more." : undefined,
      orderApprovalQuantityThreshold: Number.isNaN(qty) || qty < 0 ? "Enter a quantity of 0 or more." : undefined,
      defaultTaxRate: Number.isNaN(tax) || tax < 0 || tax > 100 ? "Enter a percentage between 0 and 100." : undefined,
      invoiceDueDays: Number.isNaN(due) || !Number.isInteger(due) || due < 0 || due > 365 ? "Enter whole days between 0 and 365." : undefined,
    };
    setErrors(next);
    if (hasErrors(next)) return;

    const body: TenantSettings = {
      orderApprovalAmountThreshold: amount,
      orderApprovalQuantityThreshold: qty,
      requireApprovalForAllOrders: form.requireApprovalForAllOrders,
      defaultTaxRate: Math.round((tax / 100) * 100_000) / 100_000,
      invoiceDueDays: due,
      stockAdjustmentRequiresApproval: form.stockAdjustmentRequiresApproval,
      lowStockAlertsEnabled: form.lowStockAlertsEnabled,
    };
    setSaving(true);
    setFormError(null);
    try {
      const saved = await api.put<Organization>("/api/organization/settings", body);
      const fresh = settingsFrom(saved.settings);
      original.current = fresh;
      setForm(fresh);
      queryClient.setQueryData(qk.organization, saved);
      toast.success("Settings saved");
    } catch (err) {
      setErrors(fieldErrors(err));
      setFormError(err instanceof ApiError ? err.detail : "The settings could not be saved.");
    } finally {
      setSaving(false);
    }
  };

  const ro = !canEdit;
  return (
    <Card>
      <CardHeader title="Operational settings" description="Approval rules, defaults and alerts for this organization." />
      <form onSubmit={submit} noValidate>
        <CardBody className="space-y-6">
          <section className="space-y-4">
            <h3 className="text-sm font-semibold text-slate-900">Inventory</h3>
            <Switch
              label="Stock adjustments require approval"
              description="Counts and corrections from members without approval rights wait for an inventory manager before stock changes."
              checked={form.stockAdjustmentRequiresApproval}
              onChange={setFlag("stockAdjustmentRequiresApproval")}
              disabled={ro}
            />
            <Switch
              label="Low-stock alerts"
              description="Notify inventory and warehouse managers (in-app and by email) when available stock reaches the reorder level."
              checked={form.lowStockAlertsEnabled}
              onChange={setFlag("lowStockAlertsEnabled")}
              disabled={ro}
            />
          </section>
          <section className="space-y-4 border-t border-slate-100 pt-5">
            <h3 className="text-sm font-semibold text-slate-900">Approval thresholds &amp; defaults</h3>
            <div className="grid gap-4 sm:grid-cols-2">
              <Input
                label="Order approval amount threshold"
                type="number"
                inputMode="decimal"
                min={0}
                step="0.01"
                value={form.orderApprovalAmountThreshold}
                onChange={setText("orderApprovalAmountThreshold")}
                error={errors.orderApprovalAmountThreshold}
                disabled={ro}
                trailing={org.baseCurrency}
                hint="Orders above this value require approval."
              />
              <Input
                label="Order approval quantity threshold"
                type="number"
                inputMode="decimal"
                min={0}
                step="1"
                value={form.orderApprovalQuantityThreshold}
                onChange={setText("orderApprovalQuantityThreshold")}
                error={errors.orderApprovalQuantityThreshold}
                disabled={ro}
                trailing="units"
                hint="Orders above this total quantity require approval."
              />
              <Input
                label="Default tax rate"
                type="number"
                inputMode="decimal"
                min={0}
                max={100}
                step="0.01"
                value={form.defaultTaxRatePercent}
                onChange={setText("defaultTaxRatePercent")}
                error={errors.defaultTaxRate}
                disabled={ro}
                trailing="%"
                hint="Applied when a product has no tax rate of its own."
              />
              <Input
                label="Invoice due days"
                type="number"
                inputMode="numeric"
                min={0}
                max={365}
                step="1"
                value={form.invoiceDueDays}
                onChange={setText("invoiceDueDays")}
                error={errors.invoiceDueDays}
                disabled={ro}
                trailing="days"
              />
            </div>
            <Switch
              label="Require approval for all orders"
              description="When on, every order needs approval regardless of the thresholds above."
              checked={form.requireApprovalForAllOrders}
              onChange={setFlag("requireApprovalForAllOrders")}
              disabled={ro}
            />
          </section>
          <FormError message={formError} />
        </CardBody>
        {canEdit && (
          <div className="flex items-center justify-end gap-2 border-t border-slate-200 px-5 py-3">
            <Button
              variant="secondary"
              disabled={!dirty || saving}
              onClick={() => {
                setForm(original.current);
                setErrors({});
                setFormError(null);
              }}
            >
              Discard
            </Button>
            <Button type="submit" loading={saving} disabled={!dirty} icon={<Save className="size-4" />}>
              Save settings
            </Button>
          </div>
        )}
      </form>
    </Card>
  );
}
