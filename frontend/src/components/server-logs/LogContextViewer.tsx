import { useEffect, useMemo, useState } from "react";
import { Loader2, X } from "lucide-react";
import { useTranslation } from "react-i18next";
import { serverLogsApi, type LogContext, type LogEntry } from "@/api/serverLogs";
import { ApiError } from "@/api/client";
import { cn } from "@/lib/cn";
import { LogEntryRow } from "@/components/server-logs/LogEntryRow";

const LINE_OPTIONS = [25, 100, 200];

/** What happened around one entry: neighbouring lines from all logs (or just its own), in time order. */
export default function LogContextViewer({
  serverId,
  entry,
  pattern,
  onClose
}: {
  serverId: string;
  entry: LogEntry;
  pattern: RegExp | null;
  onClose: () => void;
}) {
  const { t, i18n } = useTranslation();
  const [lines, setLines] = useState(25);
  const [sameLog, setSameLog] = useState(false);
  const [noise, setNoise] = useState(false);
  const [context, setContext] = useState<LogContext>();
  const [error, setError] = useState<string>();
  const timeFormat = useMemo(
    () => new Intl.DateTimeFormat(i18n.language, { dateStyle: "short", timeStyle: "medium" }),
    [i18n.language]
  );

  useEffect(() => {
    let active = true;
    setContext(undefined);
    setError(undefined);
    serverLogsApi.context(serverId, entry.id, lines, noise, sameLog ? entry.log : undefined)
      .then((result) => { if (active) setContext(result); })
      .catch((err) => { if (active) setError(err instanceof ApiError && err.message ? err.message : t("serverLogs.errors.load")); });
    return () => { active = false; };
  }, [serverId, entry.id, entry.log, lines, sameLog, noise, t]);

  useEffect(() => {
    if (context) document.getElementById(`log-entry-${context.focusId}`)?.scrollIntoView({ block: "center" });
  }, [context]);

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => { if (event.key === "Escape") onClose(); };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onClose]);

  const toggle = "rounded-md border px-2 py-1 text-xs cursor-pointer";

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-2 sm:p-6" role="dialog" aria-label={t("serverLogs.context.title")}>
      <div className="fixed inset-0 bg-black/80 backdrop-blur-sm" onClick={onClose} />
      <div className="glass-panel relative z-10 flex max-h-full w-full max-w-6xl flex-col rounded-xl shadow-2xl">
        <div className="flex flex-wrap items-center gap-2 border-b border-red-950/30 px-4 py-3">
          <h2 className="mr-auto font-serif text-sm font-bold text-slate-100">{t("serverLogs.context.title")}</h2>
          {LINE_OPTIONS.map((option) => (
            <button key={option} type="button" onClick={() => setLines(option)} className={cn(toggle, lines === option ? "border-[#b8282e]/60 bg-[#b8282e]/15 text-slate-100" : "border-slate-600/70 text-slate-300 hover:text-white")}>
              ±{option}
            </button>
          ))}
          <button type="button" onClick={() => setSameLog((value) => !value)} className={cn(toggle, sameLog ? "border-[#b8282e]/60 bg-[#b8282e]/15 text-slate-100" : "border-slate-600/70 text-slate-300 hover:text-white")}>
            {t("serverLogs.context.sameLog", { log: t(`serverLogs.logs.${entry.log}`) })}
          </button>
          <button type="button" onClick={() => setNoise((value) => !value)} className={cn(toggle, noise ? "border-[#b8282e]/60 bg-[#b8282e]/15 text-slate-100" : "border-slate-600/70 text-slate-300 hover:text-white")}>
            {t("serverLogs.search.noise")}
          </button>
          <button type="button" onClick={onClose} aria-label={t("serverLogs.context.close")} className="rounded-md p-1.5 text-slate-300 hover:bg-slate-800 hover:text-white cursor-pointer">
            <X size={18} />
          </button>
        </div>
        <div className="min-h-[200px] flex-1 overflow-y-auto">
          {error ? (
            <p className="p-6 text-sm text-rose-300">{error}</p>
          ) : !context ? (
            <div className="flex justify-center p-10"><Loader2 size={22} className="animate-spin text-[#e04444]" /></div>
          ) : (
            context.entries.map((item) => (
              <LogEntryRow key={item.id} entry={item} pattern={pattern} timeFormat={timeFormat} focused={item.id === context.focusId} />
            ))
          )}
        </div>
      </div>
    </div>
  );
}
