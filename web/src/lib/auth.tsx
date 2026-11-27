"use client";

import { useQueryClient } from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import {
  api,
  applySession,
  getAccessTokenExpiry,
  onSessionChange,
  refreshSession,
  setSessionExpiredHandler,
} from "./api";
import { P, type Permission } from "./permissions";
import type {
  AcceptInvitationRequest,
  AuthResponse,
  CurrentUser,
  Guid,
  LoginRequest,
  RegisterOrganizationRequest,
} from "./types";

export type AuthStatus = "loading" | "authenticated" | "unauthenticated";

interface AuthContextValue {
  status: AuthStatus;
  user: CurrentUser | null;
  /** Increments on every new sign-in or organization switch (not on routine token refreshes). */
  sessionEpoch: number;
  /** True when the session ended unexpectedly (refresh failed) rather than by signing out. */
  sessionExpired: boolean;
  hasPermission: (permission: Permission | string) => boolean;
  hasAnyPermission: (...permissions: (Permission | string)[]) => boolean;
  /** Whether the member may operate on the given warehouse (null warehouseIds = every warehouse). */
  canAccessWarehouse: (warehouseId: Guid) => boolean;
  login: (request: LoginRequest) => Promise<CurrentUser>;
  register: (request: RegisterOrganizationRequest) => Promise<CurrentUser>;
  acceptInvitation: (request: AcceptInvitationRequest) => Promise<CurrentUser>;
  logout: () => Promise<void>;
  switchOrganization: (tenantId: Guid) => Promise<void>;
  reloadUser: () => Promise<void>;
}

const AuthContext = createContext<AuthContextValue | null>(null);

const REFRESH_LEAD_MS = 60_000;
const RETRY_MS = 15_000;
const CHANNEL = "stockforge-auth";

/** Landing page for a user, based on what they are allowed to see. */
export function homePathFor(user: CurrentUser | null): string {
  if (!user) return "/login";
  const perms = new Set(user.permissions);
  if (!user.tenant) {
    if (perms.has(P.platformTenants)) return "/platform";
    if (perms.has(P.platformJobs)) return "/admin/jobs";
    return "/account";
  }
  if (perms.has(P.reportsOrganization) || perms.has(P.inventoryAnalytics)) return "/dashboard";
  if (perms.has(P.warehousesView) && perms.has(P.inventoryView) && user.warehouseIds?.length) {
    return `/warehouses/${user.warehouseIds[0]}`;
  }
  if (perms.has(P.ordersView)) return "/orders";
  if (perms.has(P.invoicesView)) return "/invoices";
  if (perms.has(P.purchasingView)) return "/purchase-orders";
  if (perms.has(P.inventoryView)) return "/inventory";
  if (perms.has(P.productsView)) return "/products";
  return "/account";
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient();
  const router = useRouter();
  const [status, setStatus] = useState<AuthStatus>("loading");
  const [user, setUser] = useState<CurrentUser | null>(null);
  const [expiresAt, setExpiresAt] = useState(0);
  const [sessionEpoch, setSessionEpoch] = useState(0);
  const [sessionExpired, setSessionExpired] = useState(false);
  const tenantRef = useRef<string | null | undefined>(undefined);
  const channelRef = useRef<BroadcastChannel | null>(null);
  const statusRef = useRef<AuthStatus>("loading");
  /** Set right before an intentional sign-out so it is not reported as an expired session. */
  const explicitLogoutRef = useRef(false);

  // Keep React state in sync with the in-memory token store (login, refresh, logout, failures).
  useEffect(() => {
    return onSessionChange((session) => {
      if (session) {
        const nextTenant = session.user.tenant?.id ?? null;
        if (tenantRef.current !== undefined && tenantRef.current !== nextTenant) {
          // Organization changed (here or in another tab): never show the previous tenant's data.
          void queryClient.resetQueries();
          setSessionEpoch((e) => e + 1);
        }
        tenantRef.current = nextTenant;
        statusRef.current = "authenticated";
        setUser(session.user);
        setExpiresAt(Date.parse(session.accessTokenExpiresAt));
        setStatus("authenticated");
        setSessionExpired(false);
      } else {
        const wasSignedIn = statusRef.current === "authenticated";
        // Any loss of an active session that the user did not ask for (refresh failed, revoked,
        // deactivated) is an expiry; the shell redirects to /login?reason=expired.
        setSessionExpired(wasSignedIn && !explicitLogoutRef.current);
        if (wasSignedIn) queryClient.clear();
        explicitLogoutRef.current = false;
        tenantRef.current = undefined;
        statusRef.current = "unauthenticated";
        setUser(null);
        setExpiresAt(0);
        setStatus("unauthenticated");
      }
    });
  }, [queryClient]);

  // Unrecoverable 401 → drop cached data; the authenticated layout redirects to /login.
  useEffect(() => {
    setSessionExpiredHandler(() => queryClient.clear());
    return () => setSessionExpiredHandler(null);
  }, [queryClient]);

  // Restore the session from the HttpOnly refresh cookie on first load.
  useEffect(() => {
    let cancelled = false;
    refreshSession().catch(() => {
      if (!cancelled) applySession(null);
    });
    return () => {
      cancelled = true;
    };
  }, []);

  // Proactive refresh ~60s before the access token expires (and when a background tab wakes up).
  useEffect(() => {
    if (status !== "authenticated" || !expiresAt) return;
    let timer: ReturnType<typeof setTimeout> | undefined;

    // A null result already flows through onSessionChange (expired → redirect to /login).
    const run = () => {
      refreshSession().catch(() => {
        timer = setTimeout(run, RETRY_MS);
      });
    };

    const schedule = () => {
      if (timer) clearTimeout(timer);
      const delay = Math.max(expiresAt - Date.now() - REFRESH_LEAD_MS, 1_000);
      timer = setTimeout(run, delay);
    };

    const onVisible = () => {
      if (document.visibilityState === "visible" && getAccessTokenExpiry() - Date.now() < REFRESH_LEAD_MS) run();
    };

    schedule();
    document.addEventListener("visibilitychange", onVisible);
    return () => {
      if (timer) clearTimeout(timer);
      document.removeEventListener("visibilitychange", onVisible);
    };
  }, [status, expiresAt]);

  // Cross-tab sign-out.
  useEffect(() => {
    if (typeof BroadcastChannel === "undefined") return;
    const channel = new BroadcastChannel(CHANNEL);
    channelRef.current = channel;
    channel.onmessage = (event: MessageEvent<{ type: string }>) => {
      if (event.data?.type === "logout") {
        explicitLogoutRef.current = true;
        applySession(null);
        queryClient.clear();
        router.replace("/login");
      }
    };
    return () => {
      channel.close();
      channelRef.current = null;
    };
  }, [queryClient, router]);

  const startSession = useCallback(
    (session: AuthResponse) => {
      queryClient.clear();
      tenantRef.current = session.user.tenant?.id ?? null;
      applySession(session);
      setSessionEpoch((e) => e + 1);
      return session.user;
    },
    [queryClient],
  );

  const login = useCallback(
    async (request: LoginRequest) => startSession(await api.post<AuthResponse>("/api/auth/login", request, { anonymous: true })),
    [startSession],
  );

  const register = useCallback(
    async (request: RegisterOrganizationRequest) =>
      startSession(await api.post<AuthResponse>("/api/auth/register", request, { anonymous: true })),
    [startSession],
  );

  const acceptInvitation = useCallback(
    async (request: AcceptInvitationRequest) =>
      startSession(await api.post<AuthResponse>("/api/auth/accept-invitation", request, { anonymous: true })),
    [startSession],
  );

  const logout = useCallback(async () => {
    explicitLogoutRef.current = true;
    try {
      await api.post("/api/auth/logout");
    } catch {
      // Best effort: the local session is discarded regardless.
    }
    explicitLogoutRef.current = true;
    applySession(null);
    queryClient.clear();
    channelRef.current?.postMessage({ type: "logout" });
    router.replace("/login");
  }, [queryClient, router]);

  const switchOrganization = useCallback(
    async (tenantId: Guid) => {
      const session = await api.post<AuthResponse>("/api/auth/switch-organization", { tenantId });
      await queryClient.cancelQueries();
      tenantRef.current = session.user.tenant?.id ?? null;
      applySession(session);
      await queryClient.resetQueries();
      setSessionEpoch((e) => e + 1);
      router.push(homePathFor(session.user));
    },
    [queryClient, router],
  );

  const reloadUser = useCallback(async () => {
    const me = await api.get<CurrentUser>("/api/auth/me");
    setUser(me);
  }, []);

  const value = useMemo<AuthContextValue>(() => {
    const perms = new Set(user?.permissions ?? []);
    return {
      status,
      user,
      sessionEpoch,
      sessionExpired,
      hasPermission: (permission) => perms.has(permission),
      hasAnyPermission: (...permissions) => permissions.some((p) => perms.has(p)),
      canAccessWarehouse: (warehouseId) => !user?.warehouseIds || user.warehouseIds.includes(warehouseId),
      login,
      register,
      acceptInvitation,
      logout,
      switchOrganization,
      reloadUser,
    };
  }, [status, user, sessionEpoch, sessionExpired, login, register, acceptInvitation, logout, switchOrganization, reloadUser]);

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error("useAuth must be used inside <AuthProvider>");
  return ctx;
}

/** The signed-in user; only call inside the authenticated layout. */
export function useUser(): CurrentUser {
  const { user } = useAuth();
  if (!user) throw new Error("useUser called without an authenticated user");
  return user;
}
