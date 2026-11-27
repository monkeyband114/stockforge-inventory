// Locale-aware formatting helpers (user's browser locale; currency from the organization).

const numberCache = new Map<string, Intl.NumberFormat>();

function nf(key: string, options: Intl.NumberFormatOptions): Intl.NumberFormat {
  let f = numberCache.get(key);
  if (!f) {
    f = new Intl.NumberFormat(undefined, options);
    numberCache.set(key, f);
  }
  return f;
}

/** Quantities: up to 3 decimals, no trailing zeros. */
export function formatQty(value: number | null | undefined): string {
  if (value === null || value === undefined || Number.isNaN(value)) return "—";
  return nf("qty", { maximumFractionDigits: 3 }).format(value);
}

/** Signed quantity (+12, −3). */
export function formatSignedQty(value: number): string {
  if (value === 0) return "0";
  const abs = formatQty(Math.abs(value));
  return value > 0 ? `+${abs}` : `−${abs}`;
}

export function formatInteger(value: number | null | undefined): string {
  if (value === null || value === undefined) return "—";
  return nf("int", { maximumFractionDigits: 0 }).format(value);
}

export function formatCompact(value: number): string {
  return nf("compact", { notation: "compact", maximumFractionDigits: 1 }).format(value);
}

export function formatCurrency(value: number | null | undefined, currency = "USD", options?: { compact?: boolean; precise?: boolean }): string {
  if (value === null || value === undefined || Number.isNaN(value)) return "—";
  const key = `cur:${currency}:${options?.compact ? "c" : ""}${options?.precise ? "p" : ""}`;
  try {
    return nf(key, {
      style: "currency",
      currency,
      notation: options?.compact ? "compact" : "standard",
      maximumFractionDigits: options?.compact ? 1 : options?.precise ? 4 : 2,
      minimumFractionDigits: options?.compact ? 0 : 2,
    }).format(value);
  } catch {
    return `${currency} ${formatQty(value)}`;
  }
}

/** Fraction (0.075) → "7.5%". */
export function formatPercent(fraction: number | null | undefined): string {
  if (fraction === null || fraction === undefined) return "—";
  return nf("pct", { style: "percent", maximumFractionDigits: 2 }).format(fraction);
}

const dateFormat = () => new Intl.DateTimeFormat(undefined, { dateStyle: "medium" });
const dateTimeFormat = () => new Intl.DateTimeFormat(undefined, { dateStyle: "medium", timeStyle: "short" });

export function formatDate(value: string | null | undefined): string {
  if (!value) return "—";
  const d = parseDate(value);
  return Number.isNaN(d.getTime()) ? value : dateFormat().format(d);
}

export function formatDateTime(value: string | null | undefined): string {
  if (!value) return "—";
  const d = new Date(value);
  return Number.isNaN(d.getTime()) ? value : dateTimeFormat().format(d);
}

/** Short day label for chart axes from "yyyy-MM-dd". */
export function formatDayShort(value: string): string {
  const d = parseDate(value);
  return Number.isNaN(d.getTime()) ? value : new Intl.DateTimeFormat(undefined, { month: "short", day: "numeric" }).format(d);
}

/** "yyyy-MM-dd" parsed as a local calendar date (not UTC midnight). */
function parseDate(value: string): Date {
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value);
  return m ? new Date(Number(m[1]), Number(m[2]) - 1, Number(m[3])) : new Date(value);
}

export function formatRelative(value: string | null | undefined): string {
  if (!value) return "—";
  const then = new Date(value).getTime();
  if (Number.isNaN(then)) return value;
  const seconds = Math.round((then - Date.now()) / 1000);
  const rtf = new Intl.RelativeTimeFormat(undefined, { numeric: "auto" });
  const abs = Math.abs(seconds);
  if (abs < 45) return rtf.format(seconds, "second");
  if (abs < 2700) return rtf.format(Math.round(seconds / 60), "minute");
  if (abs < 79200) return rtf.format(Math.round(seconds / 3600), "hour");
  if (abs < 2_592_000) return rtf.format(Math.round(seconds / 86400), "day");
  return formatDate(value);
}

/** "StockIn" → "Stock in", "PendingApproval" → "Pending approval". */
export function humanize(value: string): string {
  const spaced = value.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/[_.]/g, " ");
  return spaced.charAt(0).toUpperCase() + spaced.slice(1).toLowerCase();
}

export function fullName(person: { firstName: string; lastName: string }): string {
  return `${person.firstName} ${person.lastName}`.trim();
}

export function initials(person: { firstName: string; lastName: string }): string {
  return `${person.firstName.charAt(0)}${person.lastName.charAt(0)}`.toUpperCase();
}

/** Local `yyyy-MM-dd` (for <input type="date">) → ISO instant at local start of that day. */
export function startOfDayIso(date: string): string | undefined {
  if (!date) return undefined;
  const [y, m, d] = date.split("-").map(Number);
  return new Date(y, m - 1, d).toISOString();
}

/** Local `yyyy-MM-dd` → ISO instant at the start of the NEXT day (exclusive upper bound). */
export function endOfDayExclusiveIso(date: string): string | undefined {
  if (!date) return undefined;
  const [y, m, d] = date.split("-").map(Number);
  return new Date(y, m - 1, d + 1).toISOString();
}

/** <input type="datetime-local"> value → ISO instant. */
export function localDateTimeToIso(value: string): string | null {
  if (!value) return null;
  const d = new Date(value);
  return Number.isNaN(d.getTime()) ? null : d.toISOString();
}

/** Parse a numeric form field; returns NaN for blank input. */
export function parseNumber(value: string): number {
  if (value.trim() === "") return Number.NaN;
  return Number(value.replace(/,/g, ""));
}

export function describeUserAgent(ua: string | null | undefined): string {
  if (!ua) return "Unknown device";
  const browser = /Edg\//.test(ua)
    ? "Edge"
    : /OPR\//.test(ua)
      ? "Opera"
      : /Firefox\//.test(ua)
        ? "Firefox"
        : /Chrome\//.test(ua)
          ? "Chrome"
          : /Safari\//.test(ua)
            ? "Safari"
            : /curl|node|undici|PostmanRuntime/i.test(ua)
              ? "API client"
              : "Browser";
  const os = /Windows/.test(ua)
    ? "Windows"
    : /Mac OS X/.test(ua)
      ? "macOS"
      : /Android/.test(ua)
        ? "Android"
        : /iPhone|iPad/.test(ua)
          ? "iOS"
          : /Linux/.test(ua)
            ? "Linux"
            : "";
  return os ? `${browser} on ${os}` : browser;
}
