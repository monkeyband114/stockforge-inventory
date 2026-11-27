"use client";

import { useQueryClient } from "@tanstack/react-query";
import type { HubConnection } from "@microsoft/signalr";
import { createContext, useCallback, useContext, useEffect, useMemo, useReducer, useRef, useState, type ReactNode } from "react";
import { API_URL, getAccessToken, getAccessTokenExpiry, refreshSession } from "./api";
import { useAuth } from "./auth";
import { formatQty, humanize } from "./format";
import { P } from "./permissions";
import type {
  AdjustmentUpdatedPayload,
  InventoryUpdatedPayload,
  LowStockPayload,
  RealtimeEnvelope,
  TransferUpdatedPayload,
  WarehouseActivityPayload,
} from "./types";

export type ConnectionState = "offline" | "connecting" | "connected" | "reconnecting" | "disconnected";

export type NotificationTone = "info" | "success" | "warning" | "danger";

export interface AppNotification {
  id: string;
  /** Dedup key: the same logical event delivered through several groups is shown once. */
  key: string;
  title: string;
  message: string;
  href?: string;
  tone: NotificationTone;
  at: string;
  read: boolean;
}

interface RealtimeContextValue {
  state: ConnectionState;
  notifications: AppNotification[];
  unreadCount: number;
  markAllRead: () => void;
  markRead: (id: string) => void;
  clearAll: () => void;
}

const RealtimeContext = createContext<RealtimeContextValue | null>(null);

const MAX_NOTIFICATIONS = 50;

type Action =
  | { type: "add"; notification: AppNotification }
  | { type: "markAll" }
  | { type: "mark"; id: string }
  | { type: "clear" };

function reducer(state: AppNotification[], action: Action): AppNotification[] {
  switch (action.type) {
    case "add":
      if (state.some((n) => n.key === action.notification.key)) return state;
      return [action.notification, ...state].slice(0, MAX_NOTIFICATIONS);
    case "markAll":
      return state.map((n) => (n.read ? n : { ...n, read: true }));
    case "mark":
      return state.map((n) => (n.id === action.id ? { ...n, read: true } : n));
    case "clear":
      return [];
  }
}

const HUB_PATH = "/hubs/notifications";

export function RealtimeProvider({ children }: { children: ReactNode }) {
  const { status, user, sessionEpoch, hasPermission } = useAuth();
  const queryClient = useQueryClient();
  const [state, setState] = useState<ConnectionState>("offline");
  const [notifications, dispatch] = useReducer(reducer, []);
  const hasPermissionRef = useRef(hasPermission);
  hasPermissionRef.current = hasPermission;

  // Coalesce bursts of events (e.g. a 20-line transfer) into one invalidation per query family.
  const pending = useRef(new Set<string>());
  const flushTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const invalidate = useCallback(
    (...roots: string[]) => {
      for (const r of roots) pending.current.add(r);
      if (flushTimer.current) return;
      flushTimer.current = setTimeout(() => {
        flushTimer.current = null;
        const keys = [...pending.current];
        pending.current.clear();
        for (const root of keys) void queryClient.invalidateQueries({ queryKey: [root] });
      }, 300);
    },
    [queryClient],
  );

  const notify = useCallback((n: Omit<AppNotification, "id" | "read" | "at"> & { at?: string }) => {
    dispatch({
      type: "add",
      notification: { ...n, id: `${n.key}:${Date.now()}`, at: n.at ?? new Date().toISOString(), read: false },
    });
  }, []);

  const tenantId = user?.tenant?.id ?? null;
  const userId = user?.id ?? null;

  // New tenant or new sign-in → fresh notification list.
  useEffect(() => {
    dispatch({ type: "clear" });
  }, [tenantId, userId]);

  useEffect(() => {
    if (status !== "authenticated" || !tenantId) {
      setState("offline");
      return;
    }

    let disposed = false;
    let connection: HubConnection | null = null;
    let retryTimer: ReturnType<typeof setTimeout> | undefined;
    let attempt = 0;

    const accessTokenFactory = async (): Promise<string> => {
      if (getAccessTokenExpiry() - Date.now() < 30_000) {
        const session = await refreshSession().catch(() => null);
        if (session) return session.accessToken;
      }
      return getAccessToken() ?? "";
    };

    const start = async () => {
      if (disposed) return;
      setState(attempt === 0 ? "connecting" : "reconnecting");
      const signalR = await import("@microsoft/signalr");
      if (disposed) return;

      const conn = new signalR.HubConnectionBuilder()
        .withUrl(`${API_URL}${HUB_PATH}`, { accessTokenFactory, withCredentials: false })
        .withAutomaticReconnect([0, 2_000, 5_000, 10_000, 20_000, 30_000])
        .configureLogging(signalR.LogLevel.Warning)
        .build();
      connection = conn;

      conn.on("inventory.updated", (_e: RealtimeEnvelope<InventoryUpdatedPayload>) => {
        invalidate("inventory", "products", "dashboard", "reservations");
      });

      conn.on("warehouse.activity", (_e: RealtimeEnvelope<WarehouseActivityPayload>) => {
        invalidate("movements", "dashboard");
      });

      conn.on("transfer.updated", (e: RealtimeEnvelope<TransferUpdatedPayload>) => {
        invalidate("transfers", "dashboard", "inventory", "reservations");
        const p = e.payload;
        notify({
          key: `transfer:${p.transferId}:${p.status}`,
          title: `Transfer ${p.number}`,
          message:
            p.status === "Requested"
              ? "New transfer requested."
              : p.status === "InTransit"
                ? "Dispatched and now in transit."
                : p.status === "Completed"
                  ? "Received at the destination."
                  : `Status changed to ${humanize(p.status).toLowerCase()}.`,
          tone: p.status === "Completed" ? "success" : p.status === "Cancelled" ? "danger" : "info",
          href: `/inventory/transfers?id=${p.transferId}`,
          at: e.at,
        });
      });

      conn.on("adjustment.updated", (e: RealtimeEnvelope<AdjustmentUpdatedPayload>) => {
        invalidate("adjustments", "dashboard", "inventory");
        const p = e.payload;
        if (p.status === "PendingApproval") {
          if (!hasPermissionRef.current(P.inventoryAdjustApprove)) return;
          notify({
            key: `adjustment:${p.adjustmentId}:${p.status}`,
            title: `Adjustment ${p.number} awaits approval`,
            message: `Change of ${formatQty(p.quantityChange)} units needs review.`,
            tone: "warning",
            href: "/inventory/adjustments?status=PendingApproval",
            at: e.at,
          });
        } else {
          notify({
            key: `adjustment:${p.adjustmentId}:${p.status}`,
            title: `Adjustment ${p.number} ${p.status === "Applied" ? "applied" : "rejected"}`,
            message: `Quantity change ${formatQty(p.quantityChange)}.`,
            tone: p.status === "Applied" ? "success" : "danger",
            href: "/inventory/adjustments",
            at: e.at,
          });
        }
      });

      conn.on("inventory.low_stock", (e: RealtimeEnvelope<LowStockPayload>) => {
        invalidate("inventory", "dashboard");
        const p = e.payload;
        notify({
          key: `low:${p.warehouseId}:${p.productId}:${p.quantityAvailable}`,
          title: `Low stock: ${p.name}`,
          message: `${p.sku} at ${p.warehouseName} — ${formatQty(p.quantityAvailable)} available (reorder level ${formatQty(p.reorderLevel)}).`,
          tone: "warning",
          href: `/inventory?warehouseId=${p.warehouseId}&search=${encodeURIComponent(p.sku)}`,
          at: e.at,
        });
      });

      conn.onreconnecting(() => {
        if (!disposed) setState("reconnecting");
      });
      conn.onreconnected(() => {
        if (disposed) return;
        setState("connected");
        // Events may have been missed while offline: re-sync everything visible.
        void queryClient.invalidateQueries();
      });
      conn.onclose(() => {
        if (disposed) return;
        setState("disconnected");
        scheduleRestart();
      });

      try {
        await conn.start();
        if (disposed) {
          void conn.stop();
          return;
        }
        attempt = 0;
        setState("connected");
      } catch {
        if (disposed) return;
        setState("disconnected");
        scheduleRestart();
      }
    };

    const scheduleRestart = () => {
      if (disposed) return;
      attempt += 1;
      const delay = Math.min(60_000, 2_000 * 2 ** Math.min(attempt, 5));
      retryTimer = setTimeout(() => void start(), delay);
    };

    void start();

    return () => {
      disposed = true;
      if (retryTimer) clearTimeout(retryTimer);
      if (connection) void connection.stop();
      connection = null;
      setState("offline");
    };
    // Restart only on sign-in / organization switch / user change — not on routine token refreshes,
    // since accessTokenFactory always supplies the current token on (re)connect.
  }, [status, tenantId, userId, sessionEpoch, invalidate, notify, queryClient]);

  const value = useMemo<RealtimeContextValue>(
    () => ({
      state,
      notifications,
      unreadCount: notifications.filter((n) => !n.read).length,
      markAllRead: () => dispatch({ type: "markAll" }),
      markRead: (id) => dispatch({ type: "mark", id }),
      clearAll: () => dispatch({ type: "clear" }),
    }),
    [state, notifications],
  );

  return <RealtimeContext.Provider value={value}>{children}</RealtimeContext.Provider>;
}

export function useRealtime(): RealtimeContextValue {
  const ctx = useContext(RealtimeContext);
  if (!ctx) throw new Error("useRealtime must be used inside <RealtimeProvider>");
  return ctx;
}
