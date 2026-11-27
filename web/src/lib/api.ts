// Fetch wrapper for the StockForge API.
//
// Security model:
//  - The access token lives only in this module's memory (never in Web Storage or readable cookies).
//  - The refresh token is an HttpOnly cookie (`sf_refresh`, Path=/api/auth) that the browser sends only
//    on /api/auth/* requests made with `credentials: "include"`.
//  - A 401 caused by an expired/invalid token triggers ONE shared refresh (single flight, serialised across
//    tabs with the Web Locks API when available) and the request is retried once.

import type { AuthResponse } from "./types";

export const API_URL = (process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:8080").replace(/\/+$/, "");

// ---------------------------------------------------------------------------------------------
// Errors
// ---------------------------------------------------------------------------------------------

export interface ProblemDetails {
  type?: string;
  status?: number;
  title?: string;
  detail?: string;
  code?: string;
  traceId?: string;
  errors?: Record<string, string[]>;
  [key: string]: unknown;
}

export class ApiError extends Error {
  readonly status: number;
  readonly code: string;
  readonly title: string;
  readonly detail: string;
  readonly traceId?: string;
  readonly errors?: Record<string, string[]>;
  readonly problem: ProblemDetails;

  constructor(status: number, problem: ProblemDetails) {
    const detail = problem.detail || problem.title || defaultMessage(status);
    super(detail);
    this.name = "ApiError";
    this.status = status;
    this.code = problem.code ?? (status === 0 ? "network_error" : `http_${status}`);
    this.title = problem.title ?? "Request failed";
    this.detail = detail;
    this.traceId = problem.traceId;
    this.errors = problem.errors;
    this.problem = problem;
  }

  /** Extra problem members such as `available` for insufficient_stock. */
  extra<T = unknown>(name: string): T | undefined {
    return this.problem[name] as T | undefined;
  }

  /** First validation message for a field (camelCase, case-insensitive match). */
  fieldError(field: string): string | undefined {
    if (!this.errors) return undefined;
    const key = Object.keys(this.errors).find((k) => k.toLowerCase() === field.toLowerCase());
    return key ? this.errors[key]?.[0] : undefined;
  }
}

function defaultMessage(status: number): string {
  if (status === 0) return "Cannot reach the server. Check your connection and try again.";
  if (status === 401) return "Your session has expired. Please sign in again.";
  if (status === 403) return "You do not have permission to perform this action.";
  if (status === 404) return "The requested record was not found.";
  if (status === 429) return "Too many requests. Please slow down and try again shortly.";
  if (status >= 500) return "An unexpected server error occurred. Please try again.";
  return "The request could not be completed.";
}

export function isApiError(error: unknown): error is ApiError {
  return error instanceof ApiError;
}

/** User-facing message for any thrown value. */
export function errorMessage(error: unknown): string {
  if (error instanceof ApiError) return error.detail;
  if (error instanceof Error) return error.message;
  return "Something went wrong.";
}

/** Flattens problem `errors` into `{ field: firstMessage }` for form display. */
export function fieldErrors(error: unknown): Record<string, string> {
  if (!(error instanceof ApiError) || !error.errors) return {};
  const out: Record<string, string> = {};
  for (const [key, messages] of Object.entries(error.errors)) {
    if (messages?.length) out[key.charAt(0).toLowerCase() + key.slice(1)] = messages[0];
  }
  return out;
}

async function readProblem(res: Response): Promise<ProblemDetails> {
  const text = await res.text().catch(() => "");
  if (!text) return { status: res.status };
  try {
    const parsed = JSON.parse(text) as unknown;
    return parsed && typeof parsed === "object" ? (parsed as ProblemDetails) : { status: res.status };
  } catch {
    return { status: res.status, detail: undefined };
  }
}

// ---------------------------------------------------------------------------------------------
// In-memory session
// ---------------------------------------------------------------------------------------------

let accessToken: string | null = null;
let accessTokenExpiresAt = 0;

type SessionListener = (session: AuthResponse | null) => void;
const sessionListeners = new Set<SessionListener>();
let sessionExpiredHandler: (() => void) | null = null;

export function getAccessToken(): string | null {
  return accessToken;
}

export function getAccessTokenExpiry(): number {
  return accessTokenExpiresAt;
}

/** Stores a freshly issued session and notifies subscribers (AuthProvider, realtime). */
export function applySession(session: AuthResponse | null): void {
  accessToken = session?.accessToken ?? null;
  accessTokenExpiresAt = session ? Date.parse(session.accessTokenExpiresAt) : 0;
  for (const listener of sessionListeners) listener(session);
}

export function onSessionChange(listener: SessionListener): () => void {
  sessionListeners.add(listener);
  return () => sessionListeners.delete(listener);
}

/** Called when the session cannot be recovered (refresh failed after a 401). */
export function setSessionExpiredHandler(handler: (() => void) | null): void {
  sessionExpiredHandler = handler;
}

// ---------------------------------------------------------------------------------------------
// Refresh (single flight)
// ---------------------------------------------------------------------------------------------

let refreshInFlight: Promise<AuthResponse | null> | null = null;

/**
 * Rotates the refresh cookie and obtains a new access token. Concurrent callers share one request.
 * Resolves to null when the session is gone (401); rejects with ApiError on network/server failures so
 * callers can distinguish "signed out" from "temporarily offline".
 */
export function refreshSession(): Promise<AuthResponse | null> {
  if (refreshInFlight) return refreshInFlight;
  refreshInFlight = withCrossTabLock(doRefresh).finally(() => {
    refreshInFlight = null;
  });
  return refreshInFlight;
}

async function withCrossTabLock<T>(fn: () => Promise<T>): Promise<T> {
  const locks = typeof navigator !== "undefined" ? (navigator as Navigator & { locks?: LockManager }).locks : undefined;
  if (!locks?.request) return fn();
  return locks.request("stockforge-auth-refresh", () => fn()) as Promise<T>;
}

async function doRefresh(): Promise<AuthResponse | null> {
  let res: Response;
  try {
    res = await fetch(`${API_URL}/api/auth/refresh`, {
      method: "POST",
      credentials: "include",
      headers: { "Content-Type": "application/json", Accept: "application/json" },
      body: "{}",
      cache: "no-store",
    });
  } catch {
    throw new ApiError(0, {});
  }

  if (res.ok) {
    const session = (await res.json()) as AuthResponse;
    applySession(session);
    return session;
  }

  if (res.status === 401 || res.status === 400 || res.status === 403) {
    applySession(null);
    return null;
  }

  throw new ApiError(res.status, await readProblem(res));
}

const RECOVERABLE_401_CODES = new Set(["token_expired", "session_invalid", "invalid_token", "unauthenticated"]);

// ---------------------------------------------------------------------------------------------
// Requests
// ---------------------------------------------------------------------------------------------

export type QueryValue = string | number | boolean | null | undefined;
export type QueryParams = Record<string, QueryValue>;

export interface RequestOptions {
  method?: "GET" | "POST" | "PUT" | "DELETE";
  query?: QueryParams;
  body?: unknown;
  /** Raw body (FormData for uploads). Content-Type is left to the browser. */
  rawBody?: BodyInit;
  headers?: Record<string, string>;
  idempotencyKey?: string;
  signal?: AbortSignal;
  /** Do not attach the bearer token or attempt a refresh (public auth endpoints). */
  anonymous?: boolean;
}

export interface ApiResult<T> {
  data: T;
  status: number;
  headers: Headers;
  /** True when the server replayed a stored response for the same Idempotency-Key. */
  replayed: boolean;
}

export function buildUrl(path: string, query?: QueryParams): string {
  const url = new URL(path.startsWith("http") ? path : `${API_URL}${path}`);
  if (query) {
    for (const [key, value] of Object.entries(query)) {
      if (value === undefined || value === null || value === "") continue;
      url.searchParams.set(key, String(value));
    }
  }
  return url.toString();
}

async function send(path: string, options: RequestOptions, token: string | null): Promise<Response> {
  const headers: Record<string, string> = { Accept: "application/json", ...options.headers };
  let body: BodyInit | undefined;
  if (options.rawBody !== undefined) {
    body = options.rawBody;
  } else if (options.body !== undefined) {
    headers["Content-Type"] = "application/json";
    body = JSON.stringify(options.body);
  }
  if (token && !options.anonymous) headers.Authorization = `Bearer ${token}`;
  if (options.idempotencyKey) headers["Idempotency-Key"] = options.idempotencyKey;

  try {
    return await fetch(buildUrl(path, options.query), {
      method: options.method ?? "GET",
      headers,
      body,
      signal: options.signal,
      cache: "no-store",
      // The refresh cookie is scoped to /api/auth; every auth call must carry (and accept) cookies.
      credentials: path.startsWith("/api/auth/") ? "include" : "same-origin",
    });
  } catch (error) {
    if (error instanceof DOMException && error.name === "AbortError") throw error;
    throw new ApiError(0, {});
  }
}

/** Low-level request returning the raw Response after auth handling. Throws ApiError for non-2xx. */
export async function rawRequest(path: string, options: RequestOptions = {}): Promise<Response> {
  const usedToken = accessToken;
  let res = await send(path, options, usedToken);

  if (res.status === 401 && !options.anonymous) {
    const problem = await readProblem(res.clone());
    const recoverable = !problem.code || RECOVERABLE_401_CODES.has(problem.code);
    if (recoverable) {
      // Another request may already have refreshed while this one was in flight.
      let token: string | null = accessToken && accessToken !== usedToken ? accessToken : null;
      if (!token) {
        const session = await refreshSession();
        token = session?.accessToken ?? null;
      }
      if (token) {
        res = await send(path, options, token);
      }
      if (!token || res.status === 401) {
        applySession(null);
        sessionExpiredHandler?.();
        throw new ApiError(401, await readProblem(res));
      }
    }
  }

  if (!res.ok) {
    throw new ApiError(res.status, await readProblem(res));
  }
  return res;
}

export async function apiRequest<T>(path: string, options: RequestOptions = {}): Promise<ApiResult<T>> {
  const res = await rawRequest(path, options);
  let data: T;
  if (res.status === 204 || res.status === 202) {
    data = undefined as T;
  } else {
    const text = await res.text();
    data = (text ? JSON.parse(text) : undefined) as T;
  }
  return { data, status: res.status, headers: res.headers, replayed: res.headers.get("Idempotent-Replayed") === "true" };
}

export const api = {
  get: async <T>(path: string, query?: QueryParams, signal?: AbortSignal) =>
    (await apiRequest<T>(path, { query, signal })).data,
  post: async <T>(path: string, body?: unknown, options: Omit<RequestOptions, "body" | "method"> = {}) =>
    (await apiRequest<T>(path, { ...options, method: "POST", body: body ?? {} })).data,
  put: async <T>(path: string, body?: unknown, options: Omit<RequestOptions, "body" | "method"> = {}) =>
    (await apiRequest<T>(path, { ...options, method: "PUT", body: body ?? {} })).data,
  delete: async <T = void>(path: string) => (await apiRequest<T>(path, { method: "DELETE" })).data,

  /** POST with an Idempotency-Key; returns whether the server replayed a stored response. */
  postIdempotent: <T>(path: string, body: unknown, idempotencyKey: string) =>
    apiRequest<T>(path, { method: "POST", body, idempotencyKey }),

  /** Multipart upload (e.g. product image: field name `file`). */
  upload: async (path: string, file: File, field = "file", method: "PUT" | "POST" = "PUT") => {
    const form = new FormData();
    form.append(field, file, file.name);
    await apiRequest<void>(path, { method, rawBody: form });
  },

  /** Authenticated binary download (images, files). */
  blob: async (path: string, signal?: AbortSignal): Promise<Blob> => {
    const res = await rawRequest(path, { signal, headers: { Accept: "*/*" } });
    return res.blob();
  },
};

// ---------------------------------------------------------------------------------------------
// Idempotency
// ---------------------------------------------------------------------------------------------

/** RFC 4122 v4 UUID; falls back to getRandomValues outside secure contexts. */
export function newIdempotencyKey(): string {
  const c = globalThis.crypto;
  if (c?.randomUUID) return c.randomUUID();
  const bytes = new Uint8Array(16);
  c.getRandomValues(bytes);
  bytes[6] = (bytes[6] & 0x0f) | 0x40;
  bytes[8] = (bytes[8] & 0x3f) | 0x80;
  const hex = Array.from(bytes, (b) => b.toString(16).padStart(2, "0")).join("");
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}

/**
 * Tracks one key per logical submission: retrying the same payload reuses the key (so a double-click
 * or network retry is replayed, never double-booked); changing the payload starts a new submission.
 */
export class IdempotentSubmission {
  private key: string | null = null;
  private fingerprint: string | null = null;

  keyFor(payload: unknown): string {
    const fingerprint = JSON.stringify(payload);
    if (!this.key || this.fingerprint !== fingerprint) {
      this.key = newIdempotencyKey();
      this.fingerprint = fingerprint;
    }
    return this.key;
  }

  reset(): void {
    this.key = null;
    this.fingerprint = null;
  }
}

// ---------------------------------------------------------------------------------------------
// Paging helper for exports
// ---------------------------------------------------------------------------------------------

/** Fetches every page of a paged endpoint (max page size 200), up to `limit` rows. */
export async function fetchAllPages<T>(path: string, query: QueryParams, limit = 10_000): Promise<T[]> {
  const out: T[] = [];
  let page = 1;
  for (;;) {
    const result = await api.get<{ items: T[]; totalPages: number }>(path, { ...query, page, pageSize: 200 });
    out.push(...result.items);
    if (page >= result.totalPages || out.length >= limit || result.items.length === 0) break;
    page += 1;
  }
  return out.slice(0, limit);
}
