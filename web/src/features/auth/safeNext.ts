/** Only same-origin absolute paths are accepted as post-login destinations (prevents open redirects). */
export function safeNext(value: string | null | undefined): string | null {
  if (!value) return null;
  if (!value.startsWith("/") || value.startsWith("//") || value.startsWith("/\\")) return null;
  if (/^\/(login|register|forgot-password|reset-password|accept-invitation)\b/.test(value)) return null;
  return value;
}
