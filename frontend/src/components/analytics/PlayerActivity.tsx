import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import DataTable from "@/components/ui/DataTable";
import Panel from "@/components/ui/Panel";
import { analyticsApi, type PlayerDailyStats, type PlayerSummaryResponse } from "@/api/analytics";
import { cn } from "@/lib/cn";

// Validated categorical pair for the dark panel surface (#101013).
const DIRECT_COLOR = "#3987e5";
const PROXY_COLOR = "#d95926";
const REFRESH_MS = 5 * 60_000;
const RANGES = [14, 30, 90] as const;

export default function PlayerActivity({ serverId }: { serverId: string }) {
  const { t } = useTranslation();
  const [days, setDays] = useState<(typeof RANGES)[number]>(30);
  const [summary, setSummary] = useState<PlayerSummaryResponse>();
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    let active = true;
    const load = () => {
      void analyticsApi.players(serverId, days)
        .then((response) => { if (active) { setSummary(response); setFailed(false); } })
        .catch(() => { if (active) setFailed(true); });
    };
    load();
    const timer = window.setInterval(load, REFRESH_MS);
    return () => { active = false; window.clearInterval(timer); };
  }, [serverId, days]);

  return (
    <div>
      <div className="flex flex-wrap items-center justify-between gap-3 mb-3">
        <h2 className="text-[10px] font-bold font-serif uppercase tracking-widest text-[#e04444] select-none">
          {t("analytics.players.title")}
        </h2>
        <div className="flex rounded-md border border-red-950/40 overflow-hidden text-[11px]">
          {RANGES.map((range) => (
            <button
              key={range}
              type="button"
              onClick={() => setDays(range)}
              className={cn(
                "px-2.5 py-1 transition-colors",
                days === range ? "bg-[#b8282e]/30 text-slate-100" : "text-slate-400 hover:text-slate-200"
              )}
            >
              {t("analytics.players.days", { count: range })}
            </button>
          ))}
        </div>
      </div>

      {failed && !summary ? (
        <Panel className="p-6 text-sm text-slate-400">{t("analytics.players.unavailable")}</Panel>
      ) : !summary ? (
        <Panel className="p-6 text-sm text-slate-500">…</Panel>
      ) : (
        <div className="space-y-4">
          <div className="grid gap-4 sm:grid-cols-3">
            {summary.windows.map((window) => (
              <Panel key={window.window} className="p-4 space-y-2">
                <div className="text-xs font-semibold text-slate-400">{t(`analytics.players.window.${window.window}`)}</div>
                <div className="font-mono text-2xl font-semibold text-slate-100">{window.uniquePlayers}</div>
                <div className="text-[11px] text-slate-400 space-y-0.5">
                  <div>{t("analytics.players.uniquePlayers")}</div>
                  {summary.proxyConfigured && (
                    <div>
                      {t("analytics.players.viaProxy")}: <span className="font-mono text-slate-200">{window.proxyPlayers}</span>
                      {window.uniquePlayers > 0 && (
                        <span className="text-slate-500"> ({Math.round((window.proxyPlayers * 100) / window.uniquePlayers)}%)</span>
                      )}
                    </div>
                  )}
                  <div>{t("analytics.players.newPlayers")}: <span className="font-mono text-slate-200">{window.newPlayers}</span></div>
                  <div>{t("analytics.players.joins")}: <span className="font-mono text-slate-200">{window.joins}</span></div>
                </div>
              </Panel>
            ))}
          </div>
          <DailyChart summary={summary} />
        </div>
      )}
    </div>
  );
}

function DailyChart({ summary }: { summary: PlayerSummaryResponse }) {
  const { t } = useTranslation();
  const [hovered, setHovered] = useState<number>();
  const max = Math.max(1, ...summary.daily.map((day) => day.uniquePlayers));
  const ticks = [max, Math.round(max / 2), 0].filter((value, index, all) => all.indexOf(value) === index);
  const active = hovered === undefined ? undefined : summary.daily[hovered];
  const showProxy = summary.proxyConfigured;

  return (
    <Panel className="p-4 space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="text-xs font-semibold text-slate-300">{t("analytics.players.dailyTitle")}</div>
        <div className="flex items-center gap-4 text-[11px] text-slate-400">
          <Legend color={DIRECT_COLOR} label={showProxy ? t("analytics.players.direct") : t("analytics.players.uniquePlayers")} />
          {showProxy && <Legend color={PROXY_COLOR} label={t("analytics.players.viaProxy")} />}
        </div>
      </div>

      <div className="relative">
        <div className="flex gap-2">
          <div className="flex flex-col justify-between h-40 text-[10px] font-mono text-slate-500 text-right w-6 shrink-0">
            {ticks.map((tick) => <span key={tick}>{tick}</span>)}
          </div>
          <div className="relative flex-1 h-40">
            {ticks.map((tick) => (
              <div
                key={tick}
                className="absolute inset-x-0 border-t border-slate-700/30"
                style={{ bottom: `${(tick / max) * 100}%` }}
              />
            ))}
            <div className="absolute inset-0 flex items-end gap-[2px]" onMouseLeave={() => setHovered(undefined)}>
              {summary.daily.map((day, index) => {
                const proxy = showProxy ? day.proxyPlayers : 0;
                const direct = day.uniquePlayers - proxy;
                return (
                  <div
                    key={day.date}
                    className="relative flex-1 h-full flex flex-col justify-end cursor-default"
                    onMouseEnter={() => setHovered(index)}
                    onClick={() => setHovered(index)}
                  >
                    <div
                      className={cn("flex flex-col justify-end gap-[2px] transition-opacity", hovered !== undefined && hovered !== index && "opacity-50")}
                      style={{ height: `${(day.uniquePlayers / max) * 100}%` }}
                    >
                      {proxy > 0 && (
                        <div className="rounded-t-[4px]" style={{ background: PROXY_COLOR, flexGrow: proxy }} />
                      )}
                      {direct > 0 && (
                        <div className={cn(proxy === 0 && "rounded-t-[4px]")} style={{ background: DIRECT_COLOR, flexGrow: direct }} />
                      )}
                    </div>
                  </div>
                );
              })}
            </div>
            {active && hovered !== undefined && (
              <Tooltip day={active} showProxy={showProxy} alignRight={hovered > summary.daily.length / 2} position={(hovered + 0.5) / summary.daily.length} />
            )}
          </div>
        </div>
        <div className="flex justify-between pl-8 pt-1 text-[10px] font-mono text-slate-500">
          <span>{formatDay(summary.daily[0]?.date)}</span>
          <span>{formatDay(summary.daily[summary.daily.length - 1]?.date)}</span>
        </div>
      </div>

      <details className="text-xs">
        <summary className="cursor-pointer text-slate-400 hover:text-slate-200 select-none">
          {t("analytics.players.table")} · {t("analytics.players.timeZone", { zone: summary.timeZone })}
          {summary.recordedSinceUtc && ` · ${t("analytics.players.since", { date: new Date(summary.recordedSinceUtc).toLocaleDateString() })}`}
        </summary>
        <div className="mt-2 -mx-4">
          <DataTable
            rows={summary.daily}
            rowKey={(day) => day.date}
            defaultSort={{ id: "date", direction: "desc" }}
            pageSize={10}
            columns={[
              { id: "date", header: t("analytics.players.date"), sortValue: (d) => d.date, className: "font-mono text-slate-300", cell: (d) => d.date },
              { id: "unique", header: t("analytics.players.uniquePlayers"), align: "right", sortValue: (d) => d.uniquePlayers, cell: (d) => d.uniquePlayers },
              ...(showProxy
                ? [{ id: "proxy", header: t("analytics.players.viaProxy"), align: "right" as const, sortValue: (d: PlayerDailyStats) => d.proxyPlayers, cell: (d: PlayerDailyStats) => d.proxyPlayers }]
                : []),
              { id: "new", header: t("analytics.players.newPlayers"), align: "right", sortValue: (d) => d.newPlayers, cell: (d) => d.newPlayers },
              { id: "joins", header: t("analytics.players.joins"), align: "right", sortValue: (d) => d.joins, cell: (d) => d.joins },
              { id: "peak", header: t("analytics.players.peak"), align: "right", sortValue: (d) => d.peakConcurrent, cell: (d) => d.peakConcurrent ?? "—" }
            ]}
          />
        </div>
      </details>
    </Panel>
  );
}

function Tooltip({ day, showProxy, alignRight, position }: { day: PlayerDailyStats; showProxy: boolean; alignRight: boolean; position: number }) {
  const { t } = useTranslation();
  return (
    <div
      className="pointer-events-none absolute top-0 z-10 rounded-md border border-slate-700/60 bg-slate-950/95 px-3 py-2 text-[11px] shadow-xl whitespace-nowrap"
      style={alignRight ? { right: `${(1 - position) * 100}%` } : { left: `${position * 100}%` }}
    >
      <div className="font-semibold text-slate-100 mb-1">{day.date}</div>
      <Row label={t("analytics.players.uniquePlayers")} value={day.uniquePlayers} />
      {showProxy && <Row label={t("analytics.players.direct")} value={day.uniquePlayers - day.proxyPlayers} swatch={DIRECT_COLOR} />}
      {showProxy && <Row label={t("analytics.players.viaProxy")} value={day.proxyPlayers} swatch={PROXY_COLOR} />}
      <Row label={t("analytics.players.newPlayers")} value={day.newPlayers} />
      <Row label={t("analytics.players.joins")} value={day.joins} />
      <Row label={t("analytics.players.peak")} value={day.peakConcurrent ?? "—"} />
    </div>
  );
}

function Row({ label, value, swatch }: { label: string; value: number | string; swatch?: string }) {
  return (
    <div className="flex items-center justify-between gap-4 text-slate-400">
      <span className="flex items-center gap-1.5">
        {swatch && <span className="inline-block h-2 w-2 rounded-sm" style={{ background: swatch }} />}
        {label}
      </span>
      <span className="font-mono text-slate-200">{value}</span>
    </div>
  );
}

function Legend({ color, label }: { color: string; label: string }) {
  return (
    <span className="flex items-center gap-1.5">
      <span className="inline-block h-2.5 w-2.5 rounded-sm" style={{ background: color }} />
      {label}
    </span>
  );
}

function formatDay(date?: string) {
  return date ? date.slice(5) : "";
}
