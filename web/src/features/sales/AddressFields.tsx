"use client";

import { Input } from "@/components/ui/Input";
import type { Address } from "@/lib/types";

export interface AddressForm {
  line1: string;
  line2: string;
  city: string;
  state: string;
  postalCode: string;
  country: string;
}

export const emptyAddress = (): AddressForm => ({ line1: "", line2: "", city: "", state: "", postalCode: "", country: "" });

export function addressToForm(a: Address | null | undefined): AddressForm {
  return {
    line1: a?.line1 ?? "",
    line2: a?.line2 ?? "",
    city: a?.city ?? "",
    state: a?.state ?? "",
    postalCode: a?.postalCode ?? "",
    country: a?.country ?? "",
  };
}

const nullable = (v: string) => (v.trim() ? v.trim() : null);

export function isAddressBlank(f: AddressForm): boolean {
  return !Object.values(f).some((v) => v.trim());
}

/** Null when every field is blank (the API treats the address as optional). */
export function formToAddress(f: AddressForm): Address | null {
  if (isAddressBlank(f)) return null;
  return {
    line1: f.line1.trim(),
    line2: nullable(f.line2),
    city: f.city.trim(),
    state: nullable(f.state),
    postalCode: nullable(f.postalCode),
    country: f.country.trim(),
  };
}

/** Mirrors AddressDtoValidator: when any field is given, line 1, city and country are required. */
export function addressErrors(f: AddressForm): Partial<Record<keyof AddressForm, string>> {
  if (isAddressBlank(f)) return {};
  return {
    line1: f.line1.trim() ? undefined : "Address line 1 is required.",
    city: f.city.trim() ? undefined : "City is required.",
    country: f.country.trim() ? undefined : "Country is required.",
  };
}

export function formatAddress(a: Address | null | undefined): string {
  if (!a) return "";
  return [a.line1, a.line2, [a.postalCode, a.city].filter(Boolean).join(" "), a.state, a.country].filter(Boolean).join(", ");
}

export function AddressFields({
  value,
  onChange,
  errors = {},
  disabled,
}: {
  value: AddressForm;
  onChange: (next: AddressForm) => void;
  errors?: Partial<Record<keyof AddressForm, string>>;
  disabled?: boolean;
}) {
  const set = (key: keyof AddressForm) => (e: React.ChangeEvent<HTMLInputElement>) => onChange({ ...value, [key]: e.target.value });
  return (
    <div className="space-y-3">
      <Input label="Address line 1" value={value.line1} onChange={set("line1")} error={errors.line1} maxLength={200} disabled={disabled} />
      <Input label="Address line 2" value={value.line2} onChange={set("line2")} maxLength={200} disabled={disabled} />
      <div className="grid gap-3 sm:grid-cols-2">
        <Input label="City" value={value.city} onChange={set("city")} error={errors.city} maxLength={100} disabled={disabled} />
        <Input label="State / region" value={value.state} onChange={set("state")} maxLength={100} disabled={disabled} />
        <Input label="Postal code" value={value.postalCode} onChange={set("postalCode")} maxLength={20} disabled={disabled} />
        <Input label="Country" value={value.country} onChange={set("country")} error={errors.country} maxLength={100} disabled={disabled} />
      </div>
    </div>
  );
}
