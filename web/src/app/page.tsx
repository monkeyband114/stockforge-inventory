"use client";

import { useRouter } from "next/navigation";
import { useEffect } from "react";
import { PageSpinner } from "@/components/ui/Spinner";
import { homePathFor, useAuth } from "@/lib/auth";

export default function IndexPage() {
  const { status, user } = useAuth();
  const router = useRouter();

  useEffect(() => {
    if (status === "authenticated") router.replace(homePathFor(user));
    else if (status === "unauthenticated") router.replace("/login");
  }, [status, user, router]);

  return <PageSpinner label="Loading StockForge…" />;
}
