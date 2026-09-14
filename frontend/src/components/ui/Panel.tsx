import type { PropsWithChildren } from "react";
import { cn } from "@/lib/cn";

export default function Panel({ className, children }: PropsWithChildren<{ className?: string }>) {
  return <section className={cn("glass-panel rounded-lg", className)}>{children}</section>;
}
