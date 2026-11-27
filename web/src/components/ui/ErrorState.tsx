import { AlertTriangle, Lock, RefreshCw } from "lucide-react";
import { ApiError } from "@/lib/api";
import { cn } from "@/lib/cn";
import { Button } from "./Button";

export function ErrorState({
  error,
  onRetry,
  title,
  className,
}: {
  error: unknown;
  onRetry?: () => void;
  title?: string;
  className?: string;
}) {
  const forbidden = error instanceof ApiError && error.status === 403;
  const notFound = error instanceof ApiError && error.status === 404;
  const message =
    error instanceof ApiError ? error.detail : error instanceof Error ? error.message : "Something went wrong while loading this data.";
  return (
    <div role="alert" className={cn("flex flex-col items-center justify-center px-6 py-14 text-center", className)}>
      <div className={cn("mb-3 rounded-full p-3 [&>svg]:size-6", forbidden ? "bg-slate-100 text-slate-500" : "bg-red-50 text-red-600")}>
        {forbidden ? <Lock /> : <AlertTriangle />}
      </div>
      <p className="text-sm font-semibold text-slate-900">
        {title ?? (forbidden ? "Access denied" : notFound ? "Not found" : "Could not load data")}
      </p>
      <p className="mt-1 max-w-md text-sm text-slate-500">{message}</p>
      {error instanceof ApiError && error.traceId && (
        <p className="mt-1 font-mono text-[11px] text-slate-400">Trace ID: {error.traceId}</p>
      )}
      {onRetry && !forbidden && !notFound && (
        <Button variant="secondary" size="sm" className="mt-4" onClick={onRetry} icon={<RefreshCw className="size-3.5" />}>
          Try again
        </Button>
      )}
    </div>
  );
}
