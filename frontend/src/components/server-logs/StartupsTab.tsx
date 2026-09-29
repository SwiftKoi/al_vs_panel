import { useEffect, useMemo, useState } from "react";
import { useSearchParams } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { ChevronDown, ChevronRight, Loader2, Power } from "lucide-react";
import Panel from "@/components/ui/Panel";
import { ApiError } from "@/api/client";
import { serverLogsApi, type Boot } from "@/api/serverLogs";
import { cn } from "@/lib/cn";
import RangePicker from "@/components/server-logs/RangePicker";
import { parseRange, resolveRange, writeRange, type RangePreset } from "@/components/server-logs/logStyles";

const PRESETS: readonly RangePreset[] = ["7d", "30d", "all"];

const stateClass: Record<Boot["state"], string> = {
  running: "border-emerald-500/40 bg-emerald-500/10 text-emerald-300",
  stopped: "border-slate-500/40 bg-slate-500/10 text-slate-300",
  unclean: "border-rose-500/40 bg-rose-500/10 text-rose-300"
};

const changeClass = { added: "text-emerald-300", removed: "text-rose-300", updated: "text-amber-200" } as const;

/** One card per server start: version, mods, how long startup took, problems while starting, and mod changes. */
export default function StartupsTab({ serverId }: { serverId: string }) {
  const { t, i18n } = useTranslation();
  const [params, setParams] = useSearchParams();
  const rangeKey = `${params.get("range") ?? ""}|${params.get("from") ?? ""}|${params.get("to") ?? ""}`;
  const range = useMemo(() => parseRange(params, "30d"), [rangeKey]);
  const [boots, setBoots] = useState<Boot[]>();
  const [error, setError] = useState<string>();
  const [open, setOpen] = useState<string | null>(null);

  useEffect(() => {
    let active = true;
    setBoots(undefined);
    const { from, to } = resolveRange(range);
    serverLogsApi.boots(serverId, from, to)
      .then((result) => { if (active) { setBoots(result.boots); setError(undefined); } })
      .catch((err) => { if (active) setError(err instanceof ApiError && err.message ? err.message : t("serverLogs.errors.load")); });
    return () => { active = false; };
  }, [serverId, range, t]);

  const dateTime = useMemo(() => new Intl.DateTimeFormat(i18n.language, { dateStyle: "medium", timeStyle: "short" }), [i18n.language]);
  const duration = (from: string, to: string | null) => {
    const minutes = Math.max(0, Math.round(((to ? Date.parse(to) : Date.now()) - Date.parse(from)) / 60_000));
    const hours = Math.floor(minutes / 60);
    return hours > 0 ? t("serverLogs.startups.hoursMinutes", { hours, minutes: minutes % 60 }) : t("serverLogs.startups.minutes", { minutes });
  };

  // Problems while starting: open Search limited to the startup window.
  const openStartupProblems = (boot: Boot, level: "warning" | "error") => setParams((current) => {
    const next = new URLSearchParams(current);
    next.set("tab", "search");
    next.set("q", `log:main level:${level}`);
    writeRange(next, { from: boot.startedAt, to: boot.readyAt ?? new Date(Date.parse(boot.startedAt) + 600_000).toISOString() });
    return next;
  });

  return (
    <div className="space-y-4">
      <Panel className="p-4">
        <RangePicker value={range} onChange={(value) => setParams((current) => { const next = new URLSearchParams(current); writeRange(next, value); return next; }, { replace: true })} presets={PRESETS} />
      </Panel>

      {error ? (
        <Panel className="p-4 text-sm text-rose-300">{error}</Panel>
      ) : !boots ? (
        <Panel className="flex justify-center py-16"><Loader2 size={22} className="animate-spin text-[#e04444]" /></Panel>
      ) : boots.length === 0 ? (
        <Panel className="p-8 text-center text-sm text-slate-300">{t("serverLogs.startups.empty")}</Panel>
      ) : (
        <div className="space-y-2">
          {boots.map((boot, index) => {
            // Boots are newest first; this run lasted until the next (newer) start, or until now.
            const endedAt = boot.stoppedAt ?? (index > 0 ? boots[index - 1].startedAt : null);
            const isOpen = open === boot.startedAt;
            return (
              <Panel key={boot.startedAt} className="p-4">
                <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
                  <div className="flex items-center gap-2">
                    <Power size={15} className="text-slate-300" />
                    <span className="text-sm font-semibold text-slate-100">{dateTime.format(new Date(boot.startedAt))}</span>
                    <span className={cn("rounded border px-1.5 text-[11px] font-semibold uppercase leading-4", stateClass[boot.state])} title={t(`serverLogs.startups.stateHint.${boot.state}`)}>
                      {t(`serverLogs.startups.state.${boot.state}`)}
                    </span>
                  </div>
                  <span className="text-xs text-slate-300">{t("serverLogs.startups.ran", { duration: duration(boot.startedAt, endedAt) })}</span>
                  {boot.gameVersion && <span className="text-xs text-slate-300">{t("serverLogs.startups.version", { version: boot.gameVersion })}</span>}
                  {boot.modCount !== null && <span className="text-xs text-slate-300">{t("serverLogs.startups.mods", { count: boot.modCount })}</span>}
                  <span className="text-xs text-slate-300">
                    {boot.startupSeconds !== null
                      ? t("serverLogs.startups.startup", { seconds: boot.startupSeconds.toFixed(0) })
                      : t("serverLogs.startups.neverReady")}
                  </span>
                  <div className="ml-auto flex items-center gap-2 text-xs">
                    {boot.startupErrors > 0 && (
                      <button type="button" onClick={() => openStartupProblems(boot, "error")} className="rounded border border-rose-500/40 bg-rose-500/10 px-1.5 text-rose-300 hover:text-rose-200 cursor-pointer">
                        {t("serverLogs.startups.errors", { count: boot.startupErrors })}
                      </button>
                    )}
                    {boot.startupWarnings > 0 && (
                      <button type="button" onClick={() => openStartupProblems(boot, "warning")} className="rounded border border-amber-500/40 bg-amber-500/10 px-1.5 text-amber-300 hover:text-amber-200 cursor-pointer">
                        {t("serverLogs.startups.warnings", { count: boot.startupWarnings })}
                      </button>
                    )}
                  </div>
                </div>
                {boot.modChanges.length > 0 && (
                  <div className="mt-2">
                    <button type="button" onClick={() => setOpen(isOpen ? null : boot.startedAt)} className="flex items-center gap-1 text-xs text-sky-300 hover:text-sky-200 cursor-pointer">
                      {isOpen ? <ChevronDown size={13} /> : <ChevronRight size={13} />}
                      {t("serverLogs.startups.changes", {
                        added: boot.modChanges.filter((c) => c.change === "added").length,
                        updated: boot.modChanges.filter((c) => c.change === "updated").length,
                        removed: boot.modChanges.filter((c) => c.change === "removed").length
                      })}
                    </button>
                    {isOpen && (
                      <ul className="mt-1.5 grid gap-x-6 gap-y-0.5 pl-4 font-mono text-[11px] sm:grid-cols-2">
                        {boot.modChanges.map((change) => (
                          <li key={`${change.change}-${change.modId}`} className={changeClass[change.change]}>
                            {change.change === "added" ? "+" : change.change === "removed" ? "−" : "↑"} {change.modId}{" "}
                            <span className="text-slate-300">
                              {change.change === "updated" ? `${change.from} → ${change.to}` : change.to ?? change.from}
                            </span>
                          </li>
                        ))}
                      </ul>
                    )}
                  </div>
                )}
              </Panel>
            );
          })}
        </div>
      )}
    </div>
  );
}
