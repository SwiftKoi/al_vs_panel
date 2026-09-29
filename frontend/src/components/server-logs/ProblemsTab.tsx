import { useCallback, useEffect, useMemo, useState } from "react";
import { useSearchParams } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { Bell, BellOff, ChevronDown, ChevronRight, Loader2, Search } from "lucide-react";
import Panel from "@/components/ui/Panel";
import { ApiError } from "@/api/client";
import { serverLogsApi, type LogSignature, type LogSignatures } from "@/api/serverLogs";
import { useToast } from "@/context/ToastContext";
import { cn } from "@/lib/cn";
import RangePicker from "@/components/server-logs/RangePicker";
import { levelClass, parseRange, resolveRange, writeRange, type TimeRange } from "@/components/server-logs/logStyles";

function Trend({ values }: { values: number[] }) {
  const max = Math.max(1, ...values);
  return (
    <div className="flex h-6 w-24 items-end gap-px" aria-hidden>
      {values.map((value, index) => (
        <div key={index} className="flex-1 rounded-t-[1px] bg-[#b8282e]/60" style={{ height: value ? `${Math.max(8, (value / max) * 100)}%` : "0" }} />
      ))}
    </div>
  );
}

/** Warnings and errors grouped by message shape, newest problems first. */
export default function ProblemsTab({ serverId }: { serverId: string }) {
  const { t, i18n } = useTranslation();
  const toast = useToast();
  const [params, setParams] = useSearchParams();
  const rangeKey = `${params.get("range") ?? ""}|${params.get("from") ?? ""}|${params.get("to") ?? ""}`;
  const range = useMemo(() => parseRange(params, "7d"), [rangeKey]);
  const [data, setData] = useState<LogSignatures>();
  const [error, setError] = useState<string>();
  const [showMuted, setShowMuted] = useState(false);
  const [expanded, setExpanded] = useState<number | null>(null);
  const [levelFilter, setLevelFilter] = useState<"all" | "error" | "warning">("all");

  const load = useCallback(() => {
    const { from, to } = resolveRange(range);
    serverLogsApi.signatures(serverId, from, to)
      .then((result) => { setData(result); setError(undefined); })
      .catch((err) => setError(err instanceof ApiError && err.message ? err.message : t("serverLogs.errors.load")));
  }, [serverId, range, t]);

  useEffect(() => { setData(undefined); load(); }, [load]);

  const setRange = (value: TimeRange) => setParams((current) => {
    const next = new URLSearchParams(current);
    writeRange(next, value);
    return next;
  }, { replace: true });

  const openSearch = (signature: LogSignature) => setParams((current) => {
    const next = new URLSearchParams(current);
    next.set("tab", "search");
    next.set("q", `sig:${signature.id}`);
    return next;
  });

  const toggleMuted = async (signature: LogSignature) => {
    try {
      await serverLogsApi.setMuted(serverId, signature.id, !signature.muted);
      setData((current) => current && {
        ...current,
        signatures: current.signatures.map((item) => (item.id === signature.id ? { ...item, muted: !item.muted } : item))
      });
    } catch (err) {
      toast.error(err instanceof ApiError && err.message ? err.message : t("serverLogs.errors.mute"));
    }
  };

  const dateFormat = useMemo(() => new Intl.DateTimeFormat(i18n.language, { dateStyle: "short", timeStyle: "short" }), [i18n.language]);
  const number = new Intl.NumberFormat(i18n.language);
  const visible = (data?.signatures ?? []).filter((signature) =>
    (showMuted || !signature.muted) && (levelFilter === "all" || signature.level.toLowerCase() === levelFilter));
  const mutedCount = data?.signatures.filter((signature) => signature.muted).length ?? 0;
  const newCount = data?.signatures.filter((signature) => signature.isNew && !signature.muted).length ?? 0;

  return (
    <div className="space-y-4">
      <Panel className="flex flex-wrap items-center gap-3 p-4">
        <RangePicker value={range} onChange={setRange} presets={["24h", "7d", "30d", "all"]} />
        <div className="flex gap-1">
          {(["all", "error", "warning"] as const).map((level) => (
            <button
              key={level}
              type="button"
              onClick={() => setLevelFilter(level)}
              className={cn("rounded-md border px-2.5 py-1 text-xs cursor-pointer", levelFilter === level ? "border-[#b8282e]/60 bg-[#b8282e]/15 text-slate-100" : "border-slate-600/70 text-slate-300 hover:text-white")}
            >
              {t(`serverLogs.problems.level.${level}`)}
            </button>
          ))}
        </div>
        {mutedCount > 0 && (
          <label className="ml-auto flex items-center gap-1.5 text-xs text-slate-300 cursor-pointer">
            <input type="checkbox" checked={showMuted} onChange={(event) => setShowMuted(event.target.checked)} className="accent-[#b8282e]" />
            {t("serverLogs.problems.showMuted", { count: mutedCount })}
          </label>
        )}
      </Panel>

      {data && (
        <p className="px-1 text-xs text-slate-300">
          {data.lastServerStart
            ? t("serverLogs.problems.summary", { count: newCount, start: dateFormat.format(new Date(data.lastServerStart)) })
            : t("serverLogs.problems.noStart")}
        </p>
      )}

      {error ? (
        <Panel className="p-4 text-sm text-rose-300">{error}</Panel>
      ) : !data ? (
        <Panel className="flex justify-center py-16"><Loader2 size={22} className="animate-spin text-[#e04444]" /></Panel>
      ) : visible.length === 0 ? (
        <Panel className="p-8 text-center text-sm text-slate-300">{t("serverLogs.problems.empty")}</Panel>
      ) : (
        <Panel className="overflow-hidden">
          <ul className="divide-y divide-slate-800/70">
            {visible.map((signature) => (
              <li key={signature.id} className={cn("px-4 py-2.5", signature.muted && "opacity-60")}>
                <div className="flex items-start gap-3">
                  <button
                    type="button"
                    onClick={() => setExpanded((value) => (value === signature.id ? null : signature.id))}
                    className="mt-0.5 shrink-0 text-slate-400 hover:text-white cursor-pointer"
                    aria-label={t("serverLogs.problems.sample")}
                  >
                    {expanded === signature.id ? <ChevronDown size={14} /> : <ChevronRight size={14} />}
                  </button>
                  <div className="min-w-0 flex-1">
                    <div className="flex flex-wrap items-center gap-1.5">
                      {signature.isNew && !signature.muted && (
                        <span className="rounded bg-[#e04444] px-1.5 text-[11px] font-bold uppercase leading-4 text-white">{t("serverLogs.problems.new")}</span>
                      )}
                      <span className={cn("rounded border px-1 text-[11px] leading-4", levelClass(signature.level))}>{signature.level}</span>
                      {signature.source && <span className="text-[11px] text-sky-300/80">{signature.source}</span>}
                      <span className="text-[11px] text-slate-400">{t(`serverLogs.logs.${signature.log}`)}</span>
                    </div>
                    <p className="mt-1 break-words font-mono text-[12px] leading-5 text-slate-200">{signature.template}</p>
                    <p className="mt-0.5 text-[11px] text-slate-400">
                      {t("serverLogs.problems.seen", { first: dateFormat.format(new Date(signature.firstSeen)), last: dateFormat.format(new Date(signature.lastSeen)) })}
                    </p>
                    {expanded === signature.id && (
                      <pre className="mt-2 whitespace-pre-wrap break-words rounded border border-slate-800 bg-slate-950/70 p-2 text-[11px] text-slate-300">{signature.sampleMessage}</pre>
                    )}
                  </div>
                  <div className="hidden shrink-0 flex-col items-end gap-1 sm:flex">
                    <span className="text-sm font-semibold tabular-nums text-slate-100">{number.format(signature.count)}×</span>
                    <Trend values={signature.trend} />
                  </div>
                  <div className="flex shrink-0 flex-col gap-1">
                    <button
                      type="button"
                      onClick={() => openSearch(signature)}
                      title={t("serverLogs.problems.search")}
                      aria-label={t("serverLogs.problems.search")}
                      className="rounded p-1.5 text-slate-300 hover:bg-slate-800 hover:text-white cursor-pointer"
                    >
                      <Search size={14} />
                    </button>
                    <button
                      type="button"
                      onClick={() => void toggleMuted(signature)}
                      title={signature.muted ? t("serverLogs.problems.unmute") : t("serverLogs.problems.mute")}
                      aria-label={signature.muted ? t("serverLogs.problems.unmute") : t("serverLogs.problems.mute")}
                      className="rounded p-1.5 text-slate-300 hover:bg-slate-800 hover:text-white cursor-pointer"
                    >
                      {signature.muted ? <Bell size={14} /> : <BellOff size={14} />}
                    </button>
                  </div>
                </div>
              </li>
            ))}
          </ul>
        </Panel>
      )}
    </div>
  );
}
