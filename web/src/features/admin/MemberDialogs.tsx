"use client";

import { useQuery, useQueryClient } from "@tanstack/react-query";
import { Info } from "lucide-react";
import { useEffect, useMemo, useState } from "react";
import { Button } from "@/components/ui/Button";
import { Checkbox } from "@/components/ui/Checkbox";
import { Dialog } from "@/components/ui/Dialog";
import { FormError } from "@/components/ui/Field";
import { Input } from "@/components/ui/Input";
import { Skeleton } from "@/components/ui/Skeleton";
import { useToast } from "@/components/ui/Toast";
import { api, ApiError, fieldErrors } from "@/lib/api";
import { fullName } from "@/lib/format";
import { P, roleLabel } from "@/lib/permissions";
import { qk, useWarehouses } from "@/lib/queries";
import type { InviteMemberRequest, Member, PermissionDefinition, Role, UpdateMemberRequest } from "@/lib/types";
import { emailError, hasErrors, required } from "@/lib/validation";
import { permissionLabel } from "./permissionLabels";

export function useRoles() {
  return useQuery({ queryKey: qk.roles, queryFn: () => api.get<Role[]>("/api/roles"), staleTime: 5 * 60_000 });
}

export function usePermissionCatalog(enabled = true) {
  return useQuery({
    queryKey: qk.permissions,
    queryFn: () => api.get<PermissionDefinition[]>("/api/permissions"),
    staleTime: 10 * 60_000,
    enabled,
  });
}

function toggle(list: string[], value: string, on: boolean): string[] {
  return on ? (list.includes(value) ? list : [...list, value]) : list.filter((v) => v !== value);
}

function SectionLabel({ children, hint }: { children: React.ReactNode; hint?: React.ReactNode }) {
  return (
    <div className="mb-2">
      <p className="text-sm font-medium text-slate-700">{children}</p>
      {hint && <p className="text-xs text-slate-500">{hint}</p>}
    </div>
  );
}

function RoleChecklist({ value, onChange, error }: { value: string[]; onChange: (roles: string[]) => void; error?: string }) {
  const roles = useRoles();
  return (
    <fieldset>
      <legend className="sr-only">Roles</legend>
      <SectionLabel hint="Roles are bundles of permissions. Select at least one.">
        Roles <span className="text-red-600" aria-hidden="true">*</span>
      </SectionLabel>
      {roles.isPending ? (
        <div className="space-y-2">
          <Skeleton className="h-10" />
          <Skeleton className="h-10" />
          <Skeleton className="h-10" />
        </div>
      ) : roles.isError ? (
        <p className="text-sm text-red-600">Could not load roles. {roles.error.message}</p>
      ) : (
        <div className="grid gap-2 sm:grid-cols-2">
          {roles.data.map((role) => (
            <div
              key={role.name}
              className={`rounded-md border px-3 py-2 ${value.includes(role.name) ? "border-brand-600/50 bg-brand-50/50" : "border-slate-200"}`}
            >
              <Checkbox
                label={<span className="font-medium">{roleLabel(role.name)}</span>}
                description={role.description ?? undefined}
                checked={value.includes(role.name)}
                onChange={(e) => onChange(toggle(value, role.name, e.target.checked))}
              />
            </div>
          ))}
        </div>
      )}
      {error && <p className="mt-1.5 text-xs font-medium text-red-600">{error}</p>}
    </fieldset>
  );
}

function WarehouseChecklist({ value, onChange, allAccess }: { value: string[]; onChange: (ids: string[]) => void; allAccess: boolean }) {
  const warehouses = useWarehouses(true);
  return (
    <fieldset>
      <legend className="sr-only">Warehouse assignment</legend>
      <SectionLabel hint="Warehouse-scoped roles (warehouse manager, staff) can only operate in the warehouses assigned here.">Warehouse assignment</SectionLabel>
      {allAccess && (
        <p className="mb-2 flex items-start gap-2 rounded-md bg-blue-50 px-3 py-2 text-xs text-blue-800">
          <Info className="mt-0.5 size-3.5 shrink-0" aria-hidden />
          The selected roles include access to all warehouses, so this assignment has no effect on stock visibility.
        </p>
      )}
      {warehouses.isPending ? (
        <Skeleton className="h-16" />
      ) : warehouses.isError ? (
        <p className="text-sm text-red-600">Could not load warehouses.</p>
      ) : warehouses.data.length === 0 ? (
        <p className="text-sm text-slate-500">No warehouses yet.</p>
      ) : (
        <div className="grid gap-2 sm:grid-cols-2">
          {warehouses.data.map((w) => (
            <Checkbox
              key={w.id}
              label={
                <>
                  <span className="font-mono text-xs text-slate-500">{w.code}</span> {w.name}
                  {!w.isActive && <span className="ml-1 text-xs text-slate-400">(inactive)</span>}
                </>
              }
              checked={value.includes(w.id)}
              onChange={(e) => onChange(toggle(value, w.id, e.target.checked))}
            />
          ))}
        </div>
      )}
    </fieldset>
  );
}

function useRolePermissions(selectedRoles: string[]): Set<string> {
  const roles = useRoles();
  return useMemo(() => {
    const set = new Set<string>();
    for (const r of roles.data ?? []) if (selectedRoles.includes(r.name)) for (const p of r.permissions) set.add(p);
    return set;
  }, [roles.data, selectedRoles]);
}

// ---------------------------------------------------------------------------------------------
// Invite
// ---------------------------------------------------------------------------------------------

type InviteErrors = Partial<Record<"email" | "firstName" | "lastName" | "jobTitle" | "roles", string>>;

export function InviteMemberDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const [form, setForm] = useState({ email: "", firstName: "", lastName: "", jobTitle: "" });
  const [roles, setRoles] = useState<string[]>([]);
  const [warehouseIds, setWarehouseIds] = useState<string[]>([]);
  const [errors, setErrors] = useState<InviteErrors>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const rolePerms = useRolePermissions(roles);

  useEffect(() => {
    if (open) {
      setForm({ email: "", firstName: "", lastName: "", jobTitle: "" });
      setRoles([]);
      setWarehouseIds([]);
      setErrors({});
      setFormError(null);
    }
  }, [open]);

  const set = (key: keyof typeof form) => (e: React.ChangeEvent<HTMLInputElement>) => setForm((f) => ({ ...f, [key]: e.target.value }));

  const submit = async () => {
    const next: InviteErrors = {
      email: emailError(form.email),
      firstName: required(form.firstName, "First name"),
      lastName: required(form.lastName, "Last name"),
      jobTitle: form.jobTitle.length > 120 ? "Job title must be at most 120 characters." : undefined,
      roles: roles.length === 0 ? "Select at least one role." : undefined,
    };
    setErrors(next);
    if (hasErrors(next)) return;

    const body: InviteMemberRequest = {
      email: form.email.trim(),
      firstName: form.firstName.trim(),
      lastName: form.lastName.trim(),
      jobTitle: form.jobTitle.trim() || null,
      roles,
      warehouseIds: warehouseIds.length ? warehouseIds : null,
    };
    setBusy(true);
    setFormError(null);
    try {
      const member = await api.post<Member>("/api/users", body);
      await queryClient.invalidateQueries({ queryKey: ["users"] });
      toast.success("Invitation sent", `${fullName(member)} (${member.email}) will receive an email to join.`);
      onClose();
    } catch (err) {
      const fe = fieldErrors(err) as InviteErrors;
      if (err instanceof ApiError && err.code === "already_member") fe.email = err.detail;
      setErrors(fe);
      setFormError(err instanceof ApiError ? err.detail : "The invitation could not be sent.");
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
      title="Invite a member"
      description="They'll receive an email invitation to set a password and join this organization."
      onSubmit={submit}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Cancel
          </Button>
          <Button type="submit" loading={busy}>
            Send invitation
          </Button>
        </>
      }
    >
      <div className="space-y-5">
        <div className="grid gap-4 sm:grid-cols-2">
          <Input label="Email" type="email" required value={form.email} onChange={set("email")} error={errors.email} autoComplete="off" containerClassName="sm:col-span-2" />
          <Input label="First name" required value={form.firstName} onChange={set("firstName")} error={errors.firstName} />
          <Input label="Last name" required value={form.lastName} onChange={set("lastName")} error={errors.lastName} />
          <Input label="Job title" value={form.jobTitle} onChange={set("jobTitle")} error={errors.jobTitle} maxLength={120} containerClassName="sm:col-span-2" />
        </div>
        <RoleChecklist value={roles} onChange={setRoles} error={errors.roles} />
        <WarehouseChecklist value={warehouseIds} onChange={setWarehouseIds} allAccess={rolePerms.has(P.inventoryViewAll)} />
        <FormError message={formError} />
      </div>
    </Dialog>
  );
}

// ---------------------------------------------------------------------------------------------
// Edit
// ---------------------------------------------------------------------------------------------

export function EditMemberDialog({ member, onClose }: { member: Member | null; onClose: () => void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const catalog = usePermissionCatalog(!!member);
  const [jobTitle, setJobTitle] = useState("");
  const [roles, setRoles] = useState<string[]>([]);
  const [warehouseIds, setWarehouseIds] = useState<string[]>([]);
  const [granted, setGranted] = useState<string[]>([]);
  const [errors, setErrors] = useState<{ jobTitle?: string; roles?: string }>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const rolePerms = useRolePermissions(roles);

  useEffect(() => {
    if (member) {
      setJobTitle(member.jobTitle ?? "");
      setRoles(member.roles);
      setWarehouseIds(member.warehouseIds);
      setGranted(member.grantedPermissions);
      setErrors({});
      setFormError(null);
    }
  }, [member]);

  const groups = useMemo(() => {
    const map = new Map<string, PermissionDefinition[]>();
    for (const p of catalog.data ?? []) {
      const list = map.get(p.group) ?? [];
      list.push(p);
      map.set(p.group, list);
    }
    return [...map.entries()].sort(([a], [b]) => a.localeCompare(b));
  }, [catalog.data]);

  if (!member) return null;

  const submit = async () => {
    const next = {
      jobTitle: jobTitle.length > 120 ? "Job title must be at most 120 characters." : undefined,
      roles: roles.length === 0 ? "Select at least one role." : undefined,
    };
    setErrors(next);
    if (hasErrors(next)) return;
    const body: UpdateMemberRequest = {
      jobTitle: jobTitle.trim() || null,
      roles,
      warehouseIds,
      // Only extras beyond the selected roles are stored as direct grants.
      grantedPermissions: granted.filter((p) => !rolePerms.has(p)),
    };
    setBusy(true);
    setFormError(null);
    try {
      await api.put<Member>(`/api/users/${member.userId}`, body);
      await queryClient.invalidateQueries({ queryKey: ["users"] });
      toast.success("Member updated", `${fullName(member)}'s access has been saved.`);
      onClose();
    } catch (err) {
      setErrors(fieldErrors(err));
      setFormError(err instanceof ApiError ? err.detail : "The member could not be updated.");
    } finally {
      setBusy(false);
    }
  };

  return (
    <Dialog
      open={!!member}
      onClose={onClose}
      busy={busy}
      size="xl"
      title={`Edit ${fullName(member)}`}
      description={member.email}
      onSubmit={submit}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Cancel
          </Button>
          <Button type="submit" loading={busy}>
            Save changes
          </Button>
        </>
      }
    >
      <div className="space-y-6">
        <Input label="Job title" value={jobTitle} onChange={(e) => setJobTitle(e.target.value)} error={errors.jobTitle} maxLength={120} containerClassName="sm:max-w-sm" />
        <RoleChecklist value={roles} onChange={setRoles} error={errors.roles} />
        <WarehouseChecklist value={warehouseIds} onChange={setWarehouseIds} allAccess={rolePerms.has(P.inventoryViewAll)} />
        <fieldset>
          <legend className="sr-only">Additional permissions</legend>
          <SectionLabel hint="Grant individual permissions on top of the selected roles. Permissions already included by a role are shown as granted.">
            Additional permissions
          </SectionLabel>
          {catalog.isPending ? (
            <Skeleton className="h-40" />
          ) : catalog.isError ? (
            <p className="text-sm text-red-600">Could not load the permission catalogue.</p>
          ) : (
            <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
              {groups.map(([group, perms]) => (
                <div key={group} className="rounded-md border border-slate-200 p-3">
                  <p className="mb-2 text-xs font-semibold uppercase tracking-wide text-slate-500">{group}</p>
                  <div className="space-y-2">
                    {perms.map((p) => {
                      const viaRole = rolePerms.has(p.name);
                      return (
                        <Checkbox
                          key={p.name}
                          label={
                            <>
                              {permissionLabel(p.name)}
                              {viaRole && <span className="ml-1.5 rounded bg-slate-100 px-1 py-px text-[10px] font-medium text-slate-500">via role</span>}
                            </>
                          }
                          description={<span className="font-mono">{p.name}</span>}
                          checked={viaRole || granted.includes(p.name)}
                          disabled={viaRole}
                          onChange={(e) => setGranted((g) => toggle(g, p.name, e.target.checked))}
                        />
                      );
                    })}
                  </div>
                </div>
              ))}
            </div>
          )}
        </fieldset>
        <FormError message={formError} />
      </div>
    </Dialog>
  );
}
