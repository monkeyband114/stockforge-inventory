"use client";

import { keepPreviousData, useQuery, useQueryClient } from "@tanstack/react-query";
import { KeyRound, LogOut, MoreHorizontal, Pencil, UserCheck, UserPlus, UserX, Users } from "lucide-react";
import { useMemo, useState } from "react";
import { Badge } from "@/components/ui/Badge";
import { Button } from "@/components/ui/Button";
import { Card } from "@/components/ui/Card";
import { ConfirmDialog } from "@/components/ui/ConfirmDialog";
import { EmptyState } from "@/components/ui/EmptyState";
import { Menu } from "@/components/ui/Menu";
import { PageHeader, Toolbar } from "@/components/ui/PageHeader";
import { Pagination } from "@/components/ui/Pagination";
import { Table, TBody, TD, TH, THead, TR } from "@/components/ui/Table";
import { useToast } from "@/components/ui/Toast";
import { EditMemberDialog, InviteMemberDialog, useRoles } from "@/features/admin/MemberDialogs";
import { FilterSelect, NoResults, QueryBoundary, SearchInput } from "@/features/common/QueryBoundary";
import { RequirePermission } from "@/features/common/RequirePermission";
import { useListParams } from "@/features/common/useListParams";
import { api } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { formatDate, formatRelative, fullName, initials } from "@/lib/format";
import { P, roleLabel } from "@/lib/permissions";
import { qk, useWarehouses } from "@/lib/queries";
import type { Member, PagedResult } from "@/lib/types";

type MemberAction = "deactivate" | "activate" | "unlock" | "revoke";

const ACTIONS: Record<MemberAction, { title: string; confirm: string; tone: "primary" | "danger"; done: string; describe: (name: string) => string }> = {
  deactivate: {
    title: "Deactivate member",
    confirm: "Deactivate",
    tone: "danger",
    done: "Member deactivated",
    describe: (n) => `${n} will be signed out everywhere and can no longer access this organization. You can reactivate them later.`,
  },
  activate: {
    title: "Reactivate member",
    confirm: "Reactivate",
    tone: "primary",
    done: "Member reactivated",
    describe: (n) => `${n} will regain access to this organization with their existing roles.`,
  },
  unlock: {
    title: "Unlock account",
    confirm: "Unlock",
    tone: "primary",
    done: "Account unlocked",
    describe: (n) => `${n}'s account was locked after repeated failed sign-in attempts. Unlocking lets them sign in again immediately.`,
  },
  revoke: {
    title: "Revoke all sessions",
    confirm: "Revoke sessions",
    tone: "danger",
    done: "Sessions revoked",
    describe: (n) => `${n} will be signed out on every device and must sign in again.`,
  },
};

function MemberStatus({ member }: { member: Member }) {
  return (
    <div className="flex flex-wrap gap-1">
      {member.isActive ? (
        <Badge tone="success" dot>
          Active
        </Badge>
      ) : (
        <Badge tone="neutral" dot>
          Inactive
        </Badge>
      )}
      {member.isLockedOut && (
        <Badge tone="danger" dot>
          Locked
        </Badge>
      )}
    </div>
  );
}

function UsersContent() {
  const { user, hasPermission } = useAuth();
  const toast = useToast();
  const queryClient = useQueryClient();
  const canManage = hasPermission(P.usersManage);
  const list = useListParams({ role: "", status: "" });
  const [inviteOpen, setInviteOpen] = useState(false);
  const [editing, setEditing] = useState<Member | null>(null);
  const [pending, setPending] = useState<{ action: MemberAction; member: Member } | null>(null);

  const roles = useRoles();
  const warehouses = useWarehouses(true);
  const warehouseCodes = useMemo(() => new Map((warehouses.data ?? []).map((w) => [w.id, w.code])), [warehouses.data]);

  const params = {
    page: list.page,
    pageSize: list.pageSize,
    search: list.debouncedSearch || undefined,
    role: list.filters.role || undefined,
    isActive: list.filters.status === "" ? undefined : list.filters.status === "active",
  };
  const query = useQuery({
    queryKey: qk.users(params),
    queryFn: ({ signal }) => api.get<PagedResult<Member>>("/api/users", params, signal),
    placeholderData: keepPreviousData,
  });

  const filtered = !!(list.debouncedSearch || list.filters.role || list.filters.status);

  const runAction = async () => {
    if (!pending) return;
    const { action, member } = pending;
    const path = action === "revoke" ? "revoke-sessions" : action;
    await api.post(`/api/users/${member.userId}/${path}`);
    await queryClient.invalidateQueries({ queryKey: ["users"] });
    toast.success(ACTIONS[action].done, fullName(member));
  };

  return (
    <>
      <PageHeader
        title="Users & roles"
        description="Members of this organization, their roles, warehouse assignments and account status."
        actions={
          canManage && (
            <Button icon={<UserPlus className="size-4" />} onClick={() => setInviteOpen(true)}>
              Invite member
            </Button>
          )
        }
      />
      <Card>
        <Toolbar>
          <SearchInput value={list.search} onChange={list.setSearch} placeholder="Search name or email" label="Search members" />
          <FilterSelect
            label="Role"
            value={list.filters.role}
            onChange={(v) => list.setFilter("role", v)}
            allLabel="All roles"
            options={(roles.data ?? []).map((r) => ({ value: r.name, label: roleLabel(r.name) }))}
          />
          <FilterSelect
            label="Status"
            value={list.filters.status}
            onChange={(v) => list.setFilter("status", v)}
            allLabel="All statuses"
            options={[
              { value: "active", label: "Active" },
              { value: "inactive", label: "Inactive" },
            ]}
          />
        </Toolbar>
        <QueryBoundary
          query={query}
          columns={7}
          isEmpty={(d) => d.items.length === 0}
          empty={
            filtered ? (
              <NoResults what="members" onClear={list.reset} />
            ) : (
              <EmptyState icon={<Users />} title="No members yet" description="Invite colleagues to collaborate in this organization." />
            )
          }
        >
          {(data) => (
            <>
              <Table>
                <THead>
                  <tr>
                    <TH>Member</TH>
                    <TH>Roles</TH>
                    <TH>Warehouses</TH>
                    <TH>Status</TH>
                    <TH>Joined</TH>
                    <TH>Last sign-in</TH>
                    {canManage && (
                      <TH align="right">
                        <span className="sr-only">Actions</span>
                      </TH>
                    )}
                  </tr>
                </THead>
                <TBody>
                  {data.items.map((m) => {
                    const self = m.userId === user?.id;
                    return (
                      <TR key={m.membershipId}>
                        <TD>
                          <div className="flex items-center gap-3">
                            <span className="flex size-8 shrink-0 items-center justify-center rounded-full bg-slate-100 text-xs font-semibold text-slate-600">
                              {initials(m)}
                            </span>
                            <div className="min-w-0">
                              <p className="truncate font-medium text-slate-900">
                                {fullName(m)}
                                {self && <span className="ml-1.5 text-xs font-normal text-slate-500">(you)</span>}
                              </p>
                              <p className="truncate text-xs text-slate-500">
                                {m.email}
                                {m.jobTitle && <> · {m.jobTitle}</>}
                              </p>
                            </div>
                          </div>
                        </TD>
                        <TD>
                          <div className="flex max-w-xs flex-wrap gap-1">
                            {m.roles.map((r) => (
                              <Badge key={r} tone="brand">
                                {roleLabel(r)}
                              </Badge>
                            ))}
                            {m.grantedPermissions.length > 0 && (
                              <Badge tone="neutral">
                                +{m.grantedPermissions.length} permission{m.grantedPermissions.length === 1 ? "" : "s"}
                              </Badge>
                            )}
                          </div>
                        </TD>
                        <TD>
                          {m.warehouseIds.length === 0 ? (
                            <span className="text-xs text-slate-400">—</span>
                          ) : (
                            <span className="font-mono text-xs text-slate-600">{m.warehouseIds.map((id) => warehouseCodes.get(id) ?? "…").join(", ")}</span>
                          )}
                        </TD>
                        <TD>
                          <MemberStatus member={m} />
                        </TD>
                        <TD className="whitespace-nowrap text-slate-500">{formatDate(m.joinedAt)}</TD>
                        <TD className="whitespace-nowrap text-slate-500" title={m.lastLoginAt ?? undefined}>
                          {m.lastLoginAt ? formatRelative(m.lastLoginAt) : "Never"}
                        </TD>
                        {canManage && (
                          <TD align="right">
                            <Menu
                              label={`Actions for ${fullName(m)}`}
                              trigger={(props) => (
                                <Button {...props} variant="ghost" size="icon-sm" aria-label={`Actions for ${fullName(m)}`}>
                                  <MoreHorizontal className="size-4" />
                                </Button>
                              )}
                              items={[
                                { label: "Edit access", icon: <Pencil />, onSelect: () => setEditing(m) },
                                { label: "Unlock account", icon: <KeyRound />, hidden: !m.isLockedOut, onSelect: () => setPending({ action: "unlock", member: m }) },
                                { label: "Revoke sessions", icon: <LogOut />, hidden: self, onSelect: () => setPending({ action: "revoke", member: m }) },
                                { label: "Reactivate", icon: <UserCheck />, hidden: m.isActive, onSelect: () => setPending({ action: "activate", member: m }) },
                                {
                                  label: "Deactivate",
                                  icon: <UserX />,
                                  tone: "danger",
                                  hidden: !m.isActive || self,
                                  onSelect: () => setPending({ action: "deactivate", member: m }),
                                },
                              ]}
                            />
                          </TD>
                        )}
                      </TR>
                    );
                  })}
                </TBody>
              </Table>
              <Pagination
                page={data.page}
                pageSize={data.pageSize}
                totalCount={data.totalCount}
                totalPages={data.totalPages}
                onPageChange={list.setPage}
                onPageSizeChange={list.setPageSize}
              />
            </>
          )}
        </QueryBoundary>
      </Card>

      <InviteMemberDialog open={inviteOpen} onClose={() => setInviteOpen(false)} />
      <EditMemberDialog member={editing} onClose={() => setEditing(null)} />
      <ConfirmDialog
        open={!!pending}
        onClose={() => setPending(null)}
        title={pending ? ACTIONS[pending.action].title : ""}
        description={pending ? ACTIONS[pending.action].describe(fullName(pending.member)) : undefined}
        confirmLabel={pending ? ACTIONS[pending.action].confirm : "Confirm"}
        tone={pending ? ACTIONS[pending.action].tone : "primary"}
        onConfirm={runAction}
      />
    </>
  );
}

export default function UsersPage() {
  return (
    <RequirePermission anyOf={[P.usersView]}>
      <UsersContent />
    </RequirePermission>
  );
}
