"use client";

import { useEffect, useRef, useState } from "react";
import { IdempotentSubmission } from "./api";

export function useDebouncedValue<T>(value: T, delay = 300): T {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const t = setTimeout(() => setDebounced(value), delay);
    return () => clearTimeout(t);
  }, [value, delay]);
  return debounced;
}

/** One Idempotency-Key per logical form submission (stable across retries of the same payload). */
export function useIdempotentSubmission(): IdempotentSubmission {
  const ref = useRef<IdempotentSubmission | null>(null);
  if (!ref.current) ref.current = new IdempotentSubmission();
  return ref.current;
}

/** Object URL for an authenticated image blob; revoked automatically. */
export function useObjectUrl(blob: Blob | null | undefined): string | null {
  const [url, setUrl] = useState<string | null>(null);
  useEffect(() => {
    if (!blob) {
      setUrl(null);
      return;
    }
    const next = URL.createObjectURL(blob);
    setUrl(next);
    return () => URL.revokeObjectURL(next);
  }, [blob]);
  return url;
}
