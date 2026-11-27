// Client-side validation mirroring the API's FluentValidation rules (the API remains authoritative).

export const PASSWORD_RULES = [
  { id: "length", label: "At least 10 characters", test: (p: string) => p.length >= 10 },
  { id: "upper", label: "An uppercase letter", test: (p: string) => /[A-Z]/.test(p) },
  { id: "lower", label: "A lowercase letter", test: (p: string) => /[a-z]/.test(p) },
  { id: "digit", label: "A digit", test: (p: string) => /[0-9]/.test(p) },
] as const;

export function passwordError(password: string): string | undefined {
  if (!password) return "Password is required.";
  if (password.length > 128) return "Password must be at most 128 characters.";
  const failed = PASSWORD_RULES.find((r) => !r.test(password));
  return failed ? `Password needs ${failed.label.toLowerCase()}.` : undefined;
}

const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

export function emailError(email: string): string | undefined {
  if (!email.trim()) return "Email is required.";
  if (!EMAIL.test(email.trim())) return "Enter a valid email address.";
  return undefined;
}

export function required(value: string, label: string): string | undefined {
  return value.trim() ? undefined : `${label} is required.`;
}

/** Positive quantity with at most 3 decimals (API: > 0, ≤ 1e9, scale 3). */
export function quantityError(value: number, options: { allowZero?: boolean; allowNegative?: boolean; label?: string } = {}): string | undefined {
  const label = options.label ?? "Quantity";
  if (Number.isNaN(value)) return `${label} is required.`;
  if (!Number.isFinite(value)) return `${label} must be a number.`;
  if (!options.allowNegative && value < 0) return `${label} cannot be negative.`;
  if (!options.allowZero && value === 0) return options.allowNegative ? `${label} cannot be zero.` : `${label} must be greater than zero.`;
  if (Math.abs(value) > 1_000_000_000) return `${label} is too large.`;
  if (Math.round(value * 1000) / 1000 !== value) return `${label} can have at most 3 decimal places.`;
  return undefined;
}

export function hasErrors(errors: Record<string, string | undefined>): boolean {
  return Object.values(errors).some(Boolean);
}

export const COMMON_CURRENCIES = ["USD", "EUR", "GBP", "CAD", "AUD", "NGN", "ZAR", "KES", "GHS", "INR", "JPY", "CNY", "AED", "SGD", "BRL", "MXN", "CHF", "SEK"];

export function browserTimeZone(): string {
  try {
    return Intl.DateTimeFormat().resolvedOptions().timeZone || "UTC";
  } catch {
    return "UTC";
  }
}

export function allTimeZones(): string[] {
  try {
    const fn = (Intl as unknown as { supportedValuesOf?: (key: string) => string[] }).supportedValuesOf;
    return fn ? fn("timeZone") : ["UTC"];
  } catch {
    return ["UTC"];
  }
}
