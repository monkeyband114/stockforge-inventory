"use client";

import { MutationCache, QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useState, type ReactNode } from "react";
import { ToastProvider } from "@/components/ui/Toast";
import { ApiError } from "@/lib/api";
import { AuthProvider } from "@/lib/auth";
import { RealtimeProvider } from "@/lib/realtime";

function makeQueryClient() {
  return new QueryClient({
    defaultOptions: {
      queries: {
        staleTime: 15_000,
        refetchOnWindowFocus: true,
        retry: (failureCount, error) => {
          // Never retry client errors (403/404/validation); auth is handled by the fetch wrapper.
          if (error instanceof ApiError && error.status >= 400 && error.status < 500 && error.status !== 429) return false;
          return failureCount < 2;
        },
      },
      mutations: { retry: false },
    },
    mutationCache: new MutationCache(),
  });
}

export function Providers({ children }: { children: ReactNode }) {
  const [client] = useState(makeQueryClient);
  return (
    <QueryClientProvider client={client}>
      <ToastProvider>
        <AuthProvider>
          <RealtimeProvider>{children}</RealtimeProvider>
        </AuthProvider>
      </ToastProvider>
    </QueryClientProvider>
  );
}
