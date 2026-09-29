import type { LogKind } from "@/api/serverLogs";

export const LOG_KINDS: LogKind[] = ["main", "audit", "debug", "chat"];

export function levelClass(level: string) {
  switch (level.toLowerCase()) {
    case "error":
    case "fatal":
      return "border-rose-500/40 bg-rose-500/10 text-rose-300";
    case "warning":
      return "border-amber-500/40 bg-amber-500/10 text-amber-300";
    case "event":
      return "border-sky-500/30 bg-sky-500/10 text-sky-300";
    case "audit":
      return "border-emerald-500/30 bg-emerald-500/10 text-emerald-300";
    case "chat":
      return "border-violet-500/30 bg-violet-500/10 text-violet-300";
    case "debug":
    case "verbosedebug":
      return "border-slate-600/40 bg-slate-800/40 text-slate-300";
    default:
      return "border-slate-500/30 bg-slate-500/10 text-slate-300";
  }
}

export function messageClass(level: string) {
  switch (level.toLowerCase()) {
    case "error":
    case "fatal":
      return "text-rose-200";
    case "warning":
      return "text-amber-100";
    case "debug":
    case "verbosedebug":
      return "text-slate-300";
    default:
      return "text-slate-200";
  }
}

export const kindClass: Record<LogKind, string> = {
  main: "text-slate-300",
  audit: "text-emerald-300",
  debug: "text-slate-400",
  chat: "text-violet-300"
};

/** Time ranges offered by the pickers; "all" is capped by the server's retention anyway. */
export const RANGE_PRESETS = ["1h", "6h", "24h", "7d", "30d", "all"] as const;
export type RangePreset = (typeof RANGE_PRESETS)[number];

const PRESET_MS: Record<RangePreset, number> = {
  "1h": 3_600_000,
  "6h": 6 * 3_600_000,
  "24h": 24 * 3_600_000,
  "7d": 7 * 86_400_000,
  "30d": 30 * 86_400_000,
  all: 399 * 86_400_000
};

export type TimeRange = { preset: RangePreset } | { from: string; to: string };

export function resolveRange(range: TimeRange): { from: string; to?: string } {
  if ("preset" in range) return { from: new Date(Date.now() - PRESET_MS[range.preset]).toISOString() };
  return { from: range.from, to: range.to };
}

export function parseRange(params: URLSearchParams, fallback: RangePreset): TimeRange {
  const from = params.get("from");
  const to = params.get("to");
  if (from && to && !Number.isNaN(Date.parse(from)) && !Number.isNaN(Date.parse(to))) return { from, to };
  const preset = params.get("range");
  return { preset: (RANGE_PRESETS as readonly string[]).includes(preset ?? "") ? (preset as RangePreset) : fallback };
}

export function writeRange(params: URLSearchParams, range: TimeRange) {
  params.delete("range");
  params.delete("from");
  params.delete("to");
  if ("preset" in range) params.set("range", range.preset);
  else {
    params.set("from", range.from);
    params.set("to", range.to);
  }
}
