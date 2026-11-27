import { Badge, type BadgeTone } from "@/components/ui/Badge";
import { cn } from "@/lib/cn";
import type { AuditLog, JsonValue } from "@/lib/types";

const ACTION_TONES: Record<string, BadgeTone> = {
  Created: "success",
  Updated: "info",
  Deleted: "danger",
  SignedIn: "neutral",
  SignedOut: "neutral",
  SignInFailed: "warning",
  AccountLocked: "danger",
  AccountUnlocked: "success",
  PasswordChanged: "info",
  PasswordReset: "info",
  PasswordResetRequested: "neutral",
  SessionRevoked: "warning",
  SessionsRevoked: "warning",
  MemberInvited: "brand",
  InvitationAccepted: "success",
  OrganizationRegistered: "brand",
  OrganizationCreated: "brand",
  TenantSuspended: "danger",
  TenantActivated: "success",
  RefreshTokenReuseDetected: "danger",
};

export function AuditActionBadge({ action }: { action: string }) {
  return <Badge tone={ACTION_TONES[action] ?? "neutral"}>{action.replace(/([a-z])([A-Z])/g, "$1 $2")}</Badge>;
}

function isRecord(value: JsonValue | null | undefined): value is { [key: string]: JsonValue } {
  return !!value && typeof value === "object" && !Array.isArray(value);
}

function renderValue(value: JsonValue | undefined): React.ReactNode {
  if (value === undefined) return <span className="text-slate-300">—</span>;
  if (value === null) return <span className="italic text-slate-400">null</span>;
  if (typeof value === "string") return value === "" ? <span className="italic text-slate-400">empty</span> : value;
  if (typeof value === "number" || typeof value === "boolean") return String(value);
  return <code className="whitespace-pre-wrap break-all">{JSON.stringify(value)}</code>;
}

/**
 * Resolves a changed-property name against recorded values. Keys are camelCase and may be dotted paths
 * (e.g. `settings.defaultTaxRate`) stored either flat or nested; older entries may still be PascalCase.
 */
function lookup(values: { [key: string]: JsonValue } | null, key: string): JsonValue | undefined {
  if (!values) return undefined;
  if (key in values) return values[key];
  const ci = (obj: { [key: string]: JsonValue }, k: string) => {
    const match = Object.keys(obj).find((x) => x.toLowerCase() === k.toLowerCase());
    return match === undefined ? undefined : obj[match];
  };
  const flat = ci(values, key);
  if (flat !== undefined) return flat;
  let cursor: JsonValue | undefined = values;
  for (const part of key.split(".")) {
    if (!isRecord(cursor)) return undefined;
    cursor = ci(cursor, part);
  }
  return cursor;
}

/** Property-level old → new comparison for one audit entry. */
export function AuditDiff({ log }: { log: AuditLog }) {
  const oldV = isRecord(log.oldValues) ? log.oldValues : null;
  const newV = isRecord(log.newValues) ? log.newValues : null;
  const keys = log.changedProperties?.length
    ? log.changedProperties
    : Array.from(new Set([...Object.keys(oldV ?? {}), ...Object.keys(newV ?? {})])).sort();
  const showOld = !!oldV;

  return (
    <div className="space-y-3">
      {keys.length === 0 ? (
        <p className="text-sm text-slate-500">No field-level values were recorded for this event.</p>
      ) : (
        <div className="overflow-x-auto rounded-md border border-slate-200 bg-white">
          <table className="w-full text-left text-xs">
            <thead className="bg-slate-50 text-slate-500">
              <tr>
                <th scope="col" className="px-3 py-2 font-semibold">
                  Property
                </th>
                {showOld && (
                  <th scope="col" className="px-3 py-2 font-semibold">
                    Old value
                  </th>
                )}
                <th scope="col" className="px-3 py-2 font-semibold">
                  {showOld ? "New value" : "Value"}
                </th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {keys.map((key) => {
                const before = lookup(oldV, key);
                const after = lookup(newV, key);
                const changed = showOld && JSON.stringify(before) !== JSON.stringify(after);
                return (
                  <tr key={key} className={cn(changed && "bg-amber-50/60")}>
                    <th scope="row" className="whitespace-nowrap px-3 py-1.5 font-mono font-medium text-slate-700">
                      {key}
                      {changed && <span className="sr-only"> (changed)</span>}
                    </th>
                    {showOld && <td className={cn("max-w-md px-3 py-1.5 font-mono text-slate-600", changed && "text-red-700 line-through decoration-red-300")}>{renderValue(before)}</td>}
                    <td className={cn("max-w-md px-3 py-1.5 font-mono text-slate-800", changed && "text-emerald-800")}>{renderValue(after)}</td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
      <dl className="flex flex-wrap gap-x-6 gap-y-1 text-xs text-slate-500">
        <div className="flex gap-1.5">
          <dt>Entity ID:</dt>
          <dd className="font-mono text-slate-700">{log.entityId ?? "—"}</dd>
        </div>
        <div className="flex gap-1.5">
          <dt>IP address:</dt>
          <dd className="font-mono text-slate-700">{log.ipAddress ?? "—"}</dd>
        </div>
        <div className="flex gap-1.5">
          <dt>Correlation ID:</dt>
          <dd className="font-mono text-slate-700">{log.correlationId ?? "—"}</dd>
        </div>
      </dl>
    </div>
  );
}
