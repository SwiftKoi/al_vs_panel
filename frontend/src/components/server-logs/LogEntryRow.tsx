import { memo, useState, type ReactNode } from "react";
import { ChevronDown, ChevronRight, Crosshair, ListTree, Swords, User } from "lucide-react";
import { useTranslation } from "react-i18next";
import type { LogEntry } from "@/api/serverLogs";
import { cn } from "@/lib/cn";
import { kindClass, levelClass, messageClass } from "@/components/server-logs/logStyles";

export function highlight(text: string, pattern: RegExp | null): ReactNode {
  if (!pattern) return text;
  return text.split(pattern).map((part, index) =>
    index % 2 === 1 ? <mark key={index} className="rounded-sm bg-amber-400/25 px-0.5 text-amber-100">{part}</mark> : part
  );
}

export const LogEntryRow = memo(function LogEntryRow({
  entry,
  pattern,
  timeFormat,
  focused,
  onFilter,
  onContext
}: {
  entry: LogEntry;
  pattern: RegExp | null;
  timeFormat: Intl.DateTimeFormat;
  focused?: boolean;
  onFilter?: (key: string, value: string) => void;
  onContext?: (entry: LogEntry) => void;
}) {
  const { t } = useTranslation();
  const [expanded, setExpanded] = useState(false);
  const extraLines = entry.extra ? entry.extra.split("\n").length : 0;
  const hasPosition = entry.x !== null && entry.z !== null;

  return (
    <div
      id={`log-entry-${entry.id}`}
      className={cn(
        "group border-b border-slate-800/60 px-3 py-1.5 font-mono text-[12px] leading-5",
        focused ? "bg-[#b8282e]/15 ring-1 ring-inset ring-[#b8282e]/50" : "hover:bg-slate-900/60"
      )}
    >
      <div className="flex items-start gap-2">
        <span className="shrink-0 tabular-nums text-slate-400" title={entry.timestamp}>{timeFormat.format(new Date(entry.timestamp))}</span>
        <span className={cn("hidden w-11 shrink-0 text-[11px] uppercase tracking-wide sm:inline pt-px", kindClass[entry.log])}>{t(`serverLogs.logs.${entry.log}`)}</span>
        <span className={cn("shrink-0 rounded border px-1 text-[11px] leading-4 mt-0.5", levelClass(entry.level))}>{entry.level}</span>
        <div className="min-w-0 flex-1">
          <div className={cn("whitespace-pre-wrap break-words", messageClass(entry.level))}>{highlight(entry.message, pattern)}</div>
          {(entry.player || entry.other || hasPosition || extraLines > 0) && (
            <div className="mt-0.5 flex flex-wrap items-center gap-x-3 gap-y-0.5 text-[11px] font-sans">
              {entry.player && onFilter && (
                <button type="button" onClick={() => onFilter("player", entry.player!)} className="flex items-center gap-1 text-emerald-300/80 hover:text-emerald-200 cursor-pointer">
                  <User size={11} /> {entry.player}
                </button>
              )}
              {entry.other && onFilter && (
                <button
                  type="button"
                  onClick={() => onFilter(entry.action === "kill" ? "killed" : entry.action === "death" ? "killedby" : "with", entry.other!)}
                  title={t(`serverLogs.search.other.${entry.action === "kill" ? "victim" : entry.action === "death" ? "killer" : entry.action === "damage" ? "attacker" : "with"}`)}
                  className="flex items-center gap-1 text-rose-300 hover:text-rose-200 cursor-pointer"
                >
                  <Swords size={11} /> {t(`serverLogs.search.other.${entry.action === "kill" ? "victim" : entry.action === "death" ? "killer" : entry.action === "damage" ? "attacker" : "with"}`)}: {entry.other}
                </button>
              )}
              {hasPosition && onFilter && (
                <button
                  type="button"
                  onClick={() => onFilter("near", `${entry.x},${entry.y ?? ""},${entry.z}`.replace(",,", ","))}
                  title={t("serverLogs.search.nearHint")}
                  className="flex items-center gap-1 text-sky-300/80 hover:text-sky-200 cursor-pointer"
                >
                  <Crosshair size={11} /> {entry.x}, {entry.y}, {entry.z}
                </button>
              )}
              {extraLines > 0 && (
                <button type="button" onClick={() => setExpanded((value) => !value)} className="flex items-center gap-1 text-slate-300 hover:text-white cursor-pointer">
                  {expanded ? <ChevronDown size={11} /> : <ChevronRight size={11} />}
                  {t("serverLogs.search.moreLines", { count: extraLines })}
                </button>
              )}
            </div>
          )}
          {expanded && entry.extra && (
            <pre className="mt-1 max-h-96 overflow-auto rounded border border-slate-800 bg-slate-950/70 p-2 text-[11px] leading-4 text-slate-300">{highlight(entry.extra, pattern)}</pre>
          )}
        </div>
        {onContext && (
          <button
            type="button"
            onClick={() => onContext(entry)}
            title={t("serverLogs.search.context")}
            aria-label={t("serverLogs.search.context")}
            className="shrink-0 rounded p-1 text-slate-400 opacity-80 hover:bg-slate-800 hover:text-white group-hover:opacity-100 cursor-pointer"
          >
            <ListTree size={14} />
          </button>
        )}
      </div>
    </div>
  );
});
