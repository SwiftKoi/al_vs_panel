import type { ButtonHTMLAttributes } from "react";
import { cn } from "@/lib/cn";

type Props = ButtonHTMLAttributes<HTMLButtonElement> & { variant?: "primary" | "secondary" | "ghost" | "danger" };

export default function Button({ className, variant = "secondary", ...props }: Props) {
  const styles = {
    primary: "bg-gradient-to-r from-[#981d22] to-[#b8282e] text-white hover:from-[#b8282e] hover:to-[#e04444] shadow-[0_0_12px_rgba(184,40,46,0.3)] hover:shadow-[0_0_18px_rgba(224,68,68,0.5)] btn-sweep",
    secondary: "border border-red-950/45 bg-slate-950/30 text-slate-200 hover:bg-red-950/15 hover:border-red-900/50 hover:text-white",
    ghost: "bg-transparent text-slate-300 hover:bg-red-950/10 hover:text-white",
    danger: "bg-[#5e1215] border border-red-900/40 text-red-200 hover:bg-[#7a181c] hover:text-white"
  }[variant];

  return <button className={cn("inline-flex h-9 items-center justify-center gap-2 rounded-md px-3 text-sm font-semibold transition focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-red-500/50 disabled:pointer-events-none disabled:opacity-40 cursor-pointer", styles, className)} {...props} />;
}
