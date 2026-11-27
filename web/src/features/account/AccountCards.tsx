"use client";

import { useQuery, useQueryClient } from "@tanstack/react-query";
import { KeyRound, Laptop, LogOut, ShieldCheck } from "lucide-react";
import { useState } from "react";
import { Badge } from "@/components/ui/Badge";
import { Button } from "@/components/ui/Button";
import { Card, CardBody, CardHeader } from "@/components/ui/Card";
import { ConfirmDialog } from "@/components/ui/ConfirmDialog";
import { EmptyState } from "@/components/ui/EmptyState";
import { FormError } from "@/components/ui/Field";
import { Input } from "@/components/ui/Input";
import { Table, TBody, TD, TH, THead, TR } from "@/components/ui/Table";
import { useToast } from "@/components/ui/Toast";
import { PasswordChecklist } from "@/features/auth/PasswordChecklist";
import { QueryBoundary } from "@/features/common/QueryBoundary";
import { api, ApiError } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { describeUserAgent, formatDateTime, formatRelative, fullName, initials } from "@/lib/format";
import { roleLabel } from "@/lib/permissions";
import { qk } from "@/lib/queries";
import type { Session } from "@/lib/types";
import { passwordError } from "@/lib/validation";

export function ProfileCard() {
  const { user } = useAuth();
  if (!user) return null;
  return (
    <Card>
      <CardHeader title="Profile" description="Your identity in StockForge. Contact an administrator to change your name or email." />
      <CardBody>
        <div className="flex items-center gap-4">
          <span className="flex size-14 shrink-0 items-center justify-center rounded-full bg-navy-800 text-lg font-semibold text-white">{initials(user)}</span>
          <div className="min-w-0">
            <p className="truncate text-base font-semibold text-slate-900">{fullName(user)}</p>
            <p className="truncate text-sm text-slate-500">{user.email}</p>
          </div>
        </div>
        <dl className="mt-6 grid gap-4 text-sm sm:grid-cols-2">
          <div>
            <dt className="text-xs font-medium uppercase tracking-wide text-slate-500">Current organization</dt>
            <dd className="mt-1 text-slate-800">{user.tenant?.name ?? (user.isPlatformAdmin ? "Platform administration" : "—")}</dd>
          </div>
          <div>
            <dt className="text-xs font-medium uppercase tracking-wide text-slate-500">Roles</dt>
            <dd className="mt-1 flex flex-wrap gap-1">
              {user.roles.length ? (
                user.roles.map((r) => (
                  <Badge key={r} tone="brand">
                    {roleLabel(r)}
                  </Badge>
                ))
              ) : (
                <span className="text-slate-500">—</span>
              )}
            </dd>
          </div>
          <div className="sm:col-span-2">
            <dt className="text-xs font-medium uppercase tracking-wide text-slate-500">Organizations you belong to</dt>
            <dd className="mt-1">
              {user.memberships.length === 0 ? (
                <span className="text-slate-500">None</span>
              ) : (
                <ul className="flex flex-wrap gap-1.5">
                  {user.memberships.map((m) => (
                    <li key={m.id}>
                      <Badge tone={m.id === user.tenant?.id ? "info" : "neutral"}>
                        {m.name}
                        {m.id === user.tenant?.id && <span className="sr-only"> (current)</span>}
                      </Badge>
                    </li>
                  ))}
                </ul>
              )}
            </dd>
          </div>
          {user.isPlatformAdmin && (
            <div className="sm:col-span-2">
              <Badge tone="warning" dot>
                Platform administrator
              </Badge>
            </div>
          )}
        </dl>
      </CardBody>
    </Card>
  );
}

type PwErrors = Partial<Record<"currentPassword" | "newPassword" | "confirm", string>>;

export function ChangePasswordCard() {
  const toast = useToast();
  const queryClient = useQueryClient();
  const [current, setCurrent] = useState("");
  const [next, setNext] = useState("");
  const [confirm, setConfirm] = useState("");
  const [errors, setErrors] = useState<PwErrors>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    const nextErrors: PwErrors = {
      currentPassword: current ? undefined : "Current password is required.",
      newPassword: passwordError(next) ?? (next === current ? "The new password must differ from the current one." : undefined),
      confirm: confirm !== next ? "Passwords do not match." : undefined,
    };
    setErrors(nextErrors);
    if (Object.values(nextErrors).some(Boolean)) return;

    setSaving(true);
    setFormError(null);
    try {
      await api.post("/api/auth/change-password", { currentPassword: current, newPassword: next });
      setCurrent("");
      setNext("");
      setConfirm("");
      setErrors({});
      await queryClient.invalidateQueries({ queryKey: qk.sessions });
      toast.success("Password changed", "Your other sessions were signed out.");
    } catch (err) {
      if (err instanceof ApiError) {
        setErrors({ currentPassword: err.fieldError("currentPassword"), newPassword: err.fieldError("newPassword") });
        setFormError(err.errors ? null : err.detail);
      } else {
        setFormError("The password could not be changed.");
      }
    } finally {
      setSaving(false);
    }
  };

  return (
    <Card>
      <CardHeader title="Change password" description="Changing your password signs out your other sessions." />
      <form onSubmit={submit} noValidate>
        <CardBody className="space-y-4">
          <Input
            label="Current password"
            type="password"
            autoComplete="current-password"
            required
            value={current}
            onChange={(e) => setCurrent(e.target.value)}
            error={errors.currentPassword}
          />
          <Input label="New password" type="password" autoComplete="new-password" required value={next} onChange={(e) => setNext(e.target.value)} error={errors.newPassword} />
          <PasswordChecklist password={next} />
          <Input
            label="Confirm new password"
            type="password"
            autoComplete="new-password"
            required
            value={confirm}
            onChange={(e) => setConfirm(e.target.value)}
            error={errors.confirm}
          />
          <FormError message={formError} />
        </CardBody>
        <div className="flex justify-end border-t border-slate-200 px-5 py-3">
          <Button type="submit" loading={saving} icon={<KeyRound className="size-4" />}>
            Update password
          </Button>
        </div>
      </form>
    </Card>
  );
}

export function SessionsCard() {
  const { logout } = useAuth();
  const toast = useToast();
  const queryClient = useQueryClient();
  const [revoking, setRevoking] = useState<Session | null>(null);
  const sessions = useQuery({ queryKey: qk.sessions, queryFn: () => api.get<Session[]>("/api/auth/sessions") });

  const revoke = async () => {
    if (!revoking) return;
    if (revoking.isCurrent) {
      // Signing out revokes this session server-side and clears the refresh cookie.
      await logout();
      return;
    }
    await api.delete(`/api/auth/sessions/${revoking.id}`);
    await queryClient.invalidateQueries({ queryKey: qk.sessions });
    toast.success("Session revoked", "That device has been signed out.");
  };

  return (
    <Card>
      <CardHeader
        title="Active sessions"
        description="Devices currently signed in to your account. Revoke any session you don't recognise."
        actions={<ShieldCheck className="size-5 text-slate-400" aria-hidden />}
      />
      <QueryBoundary
        query={sessions}
        columns={6}
        rows={3}
        isEmpty={(d) => d.length === 0}
        empty={<EmptyState icon={<Laptop />} title="No active sessions" />}
      >
        {(data) => (
          <Table>
            <THead>
              <tr>
                <TH>Device</TH>
                <TH>IP address</TH>
                <TH>Organization</TH>
                <TH>Signed in</TH>
                <TH>Last active</TH>
                <TH>Expires</TH>
                <TH align="right">
                  <span className="sr-only">Actions</span>
                </TH>
              </tr>
            </THead>
            <TBody>
              {data.map((s) => (
                <TR key={s.id}>
                  <TD>
                    <div className="flex items-center gap-2">
                      <Laptop className="size-4 shrink-0 text-slate-400" aria-hidden />
                      <span className="font-medium text-slate-800" title={s.userAgent ?? undefined}>
                        {describeUserAgent(s.userAgent)}
                      </span>
                      {s.isCurrent && (
                        <Badge tone="success" dot>
                          This device
                        </Badge>
                      )}
                    </div>
                  </TD>
                  <TD className="font-mono text-xs text-slate-600">{s.ipAddress ?? "—"}</TD>
                  <TD className="text-slate-600">{s.tenantName ?? "—"}</TD>
                  <TD className="whitespace-nowrap text-slate-500">{formatDateTime(s.createdAt)}</TD>
                  <TD className="whitespace-nowrap text-slate-500" title={formatDateTime(s.lastSeenAt)}>
                    {formatRelative(s.lastSeenAt)}
                  </TD>
                  <TD className="whitespace-nowrap text-slate-500">{formatDateTime(s.expiresAt)}</TD>
                  <TD align="right">
                    <Button
                      variant={s.isCurrent ? "secondary" : "danger-ghost"}
                      size="sm"
                      icon={<LogOut className="size-3.5" />}
                      onClick={() => setRevoking(s)}
                      aria-label={s.isCurrent ? "Sign out of this device" : `Revoke session on ${describeUserAgent(s.userAgent)}`}
                    >
                      {s.isCurrent ? "Sign out" : "Revoke"}
                    </Button>
                  </TD>
                </TR>
              ))}
            </TBody>
          </Table>
        )}
      </QueryBoundary>
      <ConfirmDialog
        open={!!revoking}
        onClose={() => setRevoking(null)}
        title={revoking?.isCurrent ? "Sign out of this device?" : "Revoke this session?"}
        description={
          revoking?.isCurrent
            ? "You will be signed out here and returned to the sign-in page."
            : `The session on ${describeUserAgent(revoking?.userAgent)}${revoking?.ipAddress ? ` (${revoking.ipAddress})` : ""} will be signed out immediately.`
        }
        confirmLabel={revoking?.isCurrent ? "Sign out" : "Revoke session"}
        tone="danger"
        onConfirm={revoke}
      />
    </Card>
  );
}
