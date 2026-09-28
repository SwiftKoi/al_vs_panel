import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import Panel from "@/components/ui/Panel";
import { serverHealthApi, type ServerHealthPoint, type ServerHealthResponse } from "@/api/analytics";
import TimeSeriesChart, { TimeAxis } from "@/components/analytics/TimeSeriesChart";
import { cn } from "@/lib/cn";
import { formatBytes } from "@/lib/format";

const REFRESH_MS = 60_000;
const RANGES = [6, 24, 72, 168] as const;

export default function ServerHealth({ serverId }: { serverId: string }) {
  const { t } = useTranslation();
  const [hours, setHours] = useState<(typeof RANGES)[number]>(24);
  const [health, setHealth] = useState<ServerHealthResponse>();
  const [failed, setFailed] = useState(false);
  const [hovered, setHovered] = useState<number>();

  useEffect(() => {
    let active = true;
    const load = () => {
      if (document.visibilityState !== "visible") return;
      void serverHealthApi.get(serverId, hours)
        .then((response) => { if (active) { setHealth(response); setFailed(false); } })
        .catch(() => { if (active) setFailed(true); });
    };
    load();
    const timer = window.setInterval(load, REFRESH_MS);
    return () => { active = false; window.clearInterval(timer); };
  }, [serverId, hours]);

  useEffect(() => setHovered(undefined), [health]);

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <p className="text-xs text-slate-400 max-w-2xl">{t("analytics.health.hint")}</p>
        <div className="flex rounded-md border border-red-950/40 overflow-hidden text-[11px]">
          {RANGES.map((range) => (
            <button
              key={range}
              type="button"
              onClick={() => setHours(range)}
              className={cn("px-2.5 py-1 transition-colors", hours === range ? "bg-[#b8282e]/30 text-slate-100" : "text-slate-400 hover:text-slate-200")}
            >
              {range < 48 ? t("analytics.quality.hours", { count: range }) : t("analytics.players.days", { count: range / 24 })}
            </button>
          ))}
        </div>
      </div>

      {failed && !health ? (
        <Panel className="p-6 text-sm text-slate-400">{t("analytics.health.unavailable")}</Panel>
      ) : !health ? (
        <Panel className="p-6 text-sm text-slate-500">…</Panel>
      ) : health.points.length === 0 ? (
        <Panel className="p-6 text-sm text-slate-400">{t("analytics.health.empty")}</Panel>
      ) : (
        <HealthView health={health} hovered={hovered} onHover={setHovered} />
      )}
    </div>
  );
}

function HealthView({ health, hovered, onHover }: { health: ServerHealthResponse; hovered?: number; onHover: (index?: number) => void }) {
  const { t } = useTranslation();
  const s = health.summary;
  const points = health.points;
  const times = points.map((point) => new Date(point.sampledAtUtc).getTime());
  const start = times[0];
  const end = Math.max(start + 1, times[times.length - 1]);
  const active = hovered === undefined ? undefined : points[hovered];
  const charts: { title: string; value: (p: ServerHealthPoint) => number | null; format: (v: number) => string; minimumMax?: number }[] = [
    { title: t("analytics.health.slowestTick"), value: (p) => (p.slowestTickMs ?? 0) / 1000, format: (v) => `${v.toFixed(1)} s`, minimumMax: 1 },
    { title: t("analytics.health.cpu"), value: (p) => p.cpuPercent, format: (v) => `${v.toFixed(0)}%` },
    { title: t("analytics.health.memory"), value: (p) => p.memoryBytes, format: (v) => formatBytes(v) },
    { title: t("analytics.health.players"), value: (p) => p.players, format: (v) => v.toFixed(0), minimumMax: 1 },
    { title: t("analytics.health.networkOut"), value: (p) => p.bytesOutPerSecond, format: (v) => `${formatBytes(v)}/s` },
    { title: t("analytics.health.pause"), value: (p) => p.longestPauseSeconds ?? 0, format: (v) => `${v.toFixed(0)} s`, minimumMax: 1 }
  ];

  return (
    <>
      <div className="grid gap-4 grid-cols-2 lg:grid-cols-5">
        <Tile
          label={t("analytics.health.overloads")}
          value={String(s.overloads)}
          hint={s.slowestTickMs === null
            ? t("analytics.health.noOverloads")
            : t("analytics.health.overloadsHint", {
                slow: s.overloadsOverTwoSeconds,
                median: ((s.medianOverloadTickMs ?? 0) / 1000).toFixed(1),
                worst: (s.slowestTickMs / 1000).toFixed(1)
              })}
        />
        <Tile label={t("analytics.health.cpu")} value={s.averageCpuPercent === null ? "—" : `${s.averageCpuPercent.toFixed(0)}%`} hint={s.peakCpuPercent === null ? "" : t("analytics.health.peak", { value: `${s.peakCpuPercent.toFixed(0)}%` })} />
        <Tile label={t("analytics.health.memory")} value={s.peakMemoryPercent === null ? "—" : `${s.peakMemoryPercent.toFixed(0)}%`} hint={t("analytics.health.peakOfLimit")} />
        <Tile label={t("analytics.health.players")} value={String(s.peakPlayers)} hint={t("analytics.health.peakOnline")} />
        <Tile
          label={t("analytics.health.autosaves")}
          value={s.longestPauseSeconds === null ? "—" : `${s.longestPauseSeconds.toFixed(0)} s`}
          hint={t("analytics.health.autosavesHint", { count: s.pauses, slow: s.pausesOverOneSecond })}
        />
      </div>

      <Panel className="p-4 space-y-4">
        {charts.map((chart) => (
          <TimeSeriesChart
            key={chart.title}
            title={chart.title}
            times={times}
            values={points.map(chart.value)}
            format={chart.format}
            start={start}
            end={end}
            hovered={hovered}
            onHover={onHover}
            minimumMax={chart.minimumMax}
          />
        ))}
        <TimeAxis start={start} end={end} />
        <div className="min-h-[2.5rem] text-[11px] text-slate-400 font-mono">
          {active ? (
            <div className="flex flex-wrap gap-x-5 gap-y-1">
              <span className="text-slate-200">{new Date(active.sampledAtUtc).toLocaleString()}</span>
              <span>{t("analytics.health.cpu")}: {active.cpuPercent === null ? "—" : `${active.cpuPercent.toFixed(0)}%`}</span>
              <span>{t("analytics.health.memory")}: {active.memoryBytes === null ? "—" : formatBytes(active.memoryBytes)}</span>
              <span>{t("analytics.health.players")}: {active.players}</span>
              <span>↓ {formatBytes(active.bytesOutPerSecond)}/s · ↑ {formatBytes(active.bytesInPerSecond)}/s</span>
              {active.longestPauseSeconds !== null && <span>{t("analytics.health.pause")}: {active.longestPauseSeconds.toFixed(0)} s</span>}
              {active.slowestTickMs !== null && <span className="text-rose-400">{t("analytics.health.slowestTick")}: {(active.slowestTickMs / 1000).toFixed(1)} s</span>}
            </div>
          ) : (
            <span>{t("analytics.quality.hoverHint")}</span>
          )}
        </div>
      </Panel>

      {health.slowestTicks.length > 0 && (
        <Panel className="p-4 space-y-2">
          <div className="text-xs font-semibold text-slate-300">{t("analytics.health.slowestTicks")}</div>
          <div className="grid gap-x-6 gap-y-1 sm:grid-cols-2 text-[11px] font-mono">
            {health.slowestTicks.map((tick) => (
              <div key={`${tick.occurredAtUtc}-${tick.tickMs}`} className="flex justify-between gap-3 text-slate-400">
                <span>{new Date(tick.occurredAtUtc).toLocaleString()}</span>
                <span className={tick.tickMs >= 2000 ? "text-rose-400" : "text-amber-400"}>{(tick.tickMs / 1000).toFixed(1)} s</span>
              </div>
            ))}
          </div>
        </Panel>
      )}

      {health.longestPauses.length > 0 && (
        <Panel className="p-4 space-y-2">
          <div className="text-xs font-semibold text-slate-300">{t("analytics.health.longestPauses")}</div>
          <div className="grid gap-x-6 gap-y-1 sm:grid-cols-2 text-[11px] font-mono">
            {health.longestPauses.map((pause) => (
              <div key={pause.startedAtUtc} className="flex justify-between gap-3 text-slate-400">
                <span>{new Date(pause.startedAtUtc).toLocaleString()}</span>
                <span className={pause.seconds >= 3 ? "text-rose-400" : pause.seconds > 1 ? "text-amber-400" : "text-slate-200"}>{pause.seconds.toFixed(0)} s</span>
              </div>
            ))}
          </div>
        </Panel>
      )}
    </>
  );
}

function Tile({ label, value, hint }: { label: string; value: string; hint: string }) {
  return (
    <Panel className="p-4 space-y-1">
      <div className="text-xs font-semibold text-slate-400">{label}</div>
      <div className="font-mono text-2xl font-semibold text-slate-100">{value}</div>
      <div className="text-[11px] text-slate-500">{hint}</div>
    </Panel>
  );
}
