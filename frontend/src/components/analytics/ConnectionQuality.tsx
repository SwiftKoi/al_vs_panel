import { useEffect, useState, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import type { TFunction } from "i18next";
import DataTable, { type DataTableColumn } from "@/components/ui/DataTable";
import Panel from "@/components/ui/Panel";
import {
  connectionQualityApi,
  type ConnectionQualityResponse,
  type ConnectionQualityStats,
  type PlayerConnectionQuality,
  type PlayerConnectionHistoryResponse
} from "@/api/analytics";
import TimeSeriesChart, { DROP_COLOR, SERIES_COLOR as LINE_COLOR, TimeAxis } from "@/components/analytics/TimeSeriesChart";
import { cn } from "@/lib/cn";

const REFRESH_MS = 60_000;
const RANGES = [6, 24, 72, 168] as const;

type Level = "good" | "warn" | "bad";
const levelClass: Record<Level, string> = { good: "text-emerald-400", warn: "text-amber-400", bad: "text-rose-400" };
const pingLevel = (ms: number): Level => (ms < 100 ? "good" : ms < 180 ? "warn" : "bad");
const lossLevel = (percent: number): Level => (percent < 1 ? "good" : percent < 4 ? "warn" : "bad");
const jitterLevel = (ms: number): Level => (ms < 20 ? "good" : ms < 50 ? "warn" : "bad");

export default function ConnectionQuality({ serverId }: { serverId: string }) {
  const { t } = useTranslation();
  const [hours, setHours] = useState<(typeof RANGES)[number]>(24);
  const [overview, setOverview] = useState<ConnectionQualityResponse>();
  const [failed, setFailed] = useState(false);
  const [selected, setSelected] = useState<string>();

  useEffect(() => {
    let active = true;
    const load = () => {
      if (document.visibilityState !== "visible") return;
      void connectionQualityApi.overview(serverId, hours)
        .then((response) => { if (active) { setOverview(response); setFailed(false); } })
        .catch(() => { if (active) setFailed(true); });
    };
    load();
    const timer = window.setInterval(load, REFRESH_MS);
    return () => { active = false; window.clearInterval(timer); };
  }, [serverId, hours]);

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <p className="text-xs text-slate-400 max-w-2xl">{t("analytics.quality.hint")}</p>
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

      {failed && !overview ? (
        <Panel className="p-6 text-sm text-slate-400">{t("analytics.quality.unavailable")}</Panel>
      ) : !overview ? (
        <Panel className="p-6 text-sm text-slate-500">…</Panel>
      ) : overview.all.samples === 0 ? (
        <Panel className="p-6 text-sm text-slate-400">{t("analytics.quality.empty")}</Panel>
      ) : (
        <>
          <GroupComparison overview={overview} />
          <Panel className="p-0 overflow-hidden">
            <DataTable
              rows={overview.players}
              rowKey={(player) => player.playerName}
              searchText={(player) => player.playerName}
              defaultSort={{ id: "loss", direction: "desc" }}
              onRowClick={(player) => setSelected(player.playerName)}
              rowClassName={(player) => selected === player.playerName && "bg-[#b8282e]/10"}
              toolbar={
                <div className="text-xs font-semibold text-slate-300">
                  {t("analytics.quality.playersTitle")}
                  <span className="ml-2 font-normal text-slate-500">{t("analytics.quality.clickHint")}</span>
                </div>
              }
              columns={[
                {
                  id: "player",
                  header: t("analytics.disconnects.player"),
                  sortValue: (p) => p.playerName,
                  firstDirection: "asc",
                  cell: (p) => (
                    <span className="font-semibold text-slate-200">
                      {p.playerName}
                      {p.usesProxy && <span className="ml-2 text-[10px] font-normal text-amber-500/80">{t("server.connections.sharedAddress")}</span>}
                    </span>
                  )
                },
                ...statColumns<PlayerConnectionQuality>((p) => p.stats, t),
                {
                  id: "lastSeen",
                  header: t("analytics.quality.lastSeen"),
                  align: "right",
                  sortValue: (p) => p.lastSeenUtc,
                  cell: (p) => new Date(p.lastSeenUtc).toLocaleString()
                }
              ]}
            />
          </Panel>
          {selected && <PlayerHistory serverId={serverId} playerName={selected} hours={hours} />}
        </>
      )}
    </div>
  );
}

function GroupComparison({ overview }: { overview: ConnectionQualityResponse }) {
  const { t } = useTranslation();
  const groups: [string, ConnectionQualityStats][] = overview.proxyConfigured
    ? [[t("analytics.quality.everyone"), overview.all], [t("analytics.players.viaProxy"), overview.proxy], [t("analytics.players.direct"), overview.direct]]
    : [[t("analytics.quality.everyone"), overview.all]];
  return (
    <Panel className="p-0 overflow-hidden">
      <div className="px-4 py-3 text-xs font-semibold text-slate-300 border-b border-red-950/20">{t("analytics.quality.groupsTitle")}</div>
      <div className="overflow-x-auto">
        <table className="w-full text-xs">
          <thead className="text-[10px] uppercase tracking-wider text-slate-500">
            <tr className="border-b border-red-950/20">
              <Th left>{t("analytics.quality.group")}</Th>
              <Th>{t("analytics.quality.medianPing")}</Th>
              <Th>{t("analytics.quality.p95Ping")}</Th>
              <Th>{t("server.connections.jitter")}</Th>
              <Th>{t("server.connections.loss")}</Th>
              <Th>{t("analytics.quality.stalls")}</Th>
              <Th>{t("analytics.quality.samples")}</Th>
            </tr>
          </thead>
          <tbody className="font-mono">
            {groups.map(([label, stats]) => (
              <tr key={label} className="border-b border-red-950/10 last:border-0">
                <td className="px-4 py-2 font-sans font-semibold text-slate-200">{label}</td>
                <StatCells stats={stats} />
                <td className="px-4 py-2 text-right text-slate-400">{stats.samples}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </Panel>
  );
}

function StatCells({ stats }: { stats: ConnectionQualityStats }) {
  const ms = (value: number | null, level: (v: number) => Level) =>
    value === null ? <Td>—</Td> : <Td className={levelClass[level(value)]}>{value.toFixed(0)} ms</Td>;
  return (
    <>
      {ms(stats.medianRttMs, pingLevel)}
      {ms(stats.p95RttMs, pingLevel)}
      {stats.averageJitterMs === null ? <Td>—</Td> : <Td className={levelClass[jitterLevel(stats.averageJitterMs)]}>±{stats.averageJitterMs.toFixed(0)} ms</Td>}
      {stats.lossPercent === null ? <Td>—</Td> : <Td className={levelClass[lossLevel(stats.lossPercent)]}>{stats.lossPercent.toFixed(2)}%</Td>}
      <Td className={stats.stalls > 0 ? "text-amber-400" : undefined}>{stats.stalls}</Td>
    </>
  );
}

function statColumns<T>(stats: (row: T) => ConnectionQualityStats, t: TFunction): DataTableColumn<T>[] {
  const ms = (value: number | null, level: (v: number) => Level, prefix = "") =>
    value === null ? "—" : <span className={levelClass[level(value)]}>{prefix}{value.toFixed(0)} ms</span>;
  return [
    { id: "median", header: t("analytics.quality.medianPing"), align: "right", sortValue: (r) => stats(r).medianRttMs, cell: (r) => ms(stats(r).medianRttMs, pingLevel) },
    { id: "p95", header: t("analytics.quality.p95Ping"), align: "right", sortValue: (r) => stats(r).p95RttMs, cell: (r) => ms(stats(r).p95RttMs, pingLevel) },
    { id: "jitter", header: t("server.connections.jitter"), align: "right", sortValue: (r) => stats(r).averageJitterMs, cell: (r) => ms(stats(r).averageJitterMs, jitterLevel, "±") },
    {
      id: "loss",
      header: t("server.connections.loss"),
      align: "right",
      sortValue: (r) => stats(r).lossPercent,
      cell: (r) => {
        const loss = stats(r).lossPercent;
        return loss === null ? "—" : <span className={levelClass[lossLevel(loss)]}>{loss.toFixed(2)}%</span>;
      }
    },
    {
      id: "stalls",
      header: t("analytics.quality.stalls"),
      align: "right",
      sortValue: (r) => stats(r).stalls,
      cell: (r) => <span className={stats(r).stalls > 0 ? "text-amber-400" : undefined}>{stats(r).stalls}</span>
    }
  ];
}

export function PlayerHistory({ serverId, playerName, hours }: { serverId: string; playerName: string; hours: number }) {
  const { t } = useTranslation();
  const [history, setHistory] = useState<PlayerConnectionHistoryResponse>();
  const [failed, setFailed] = useState(false);
  const [hovered, setHovered] = useState<number>();

  useEffect(() => {
    let active = true;
    setHistory(undefined);
    setFailed(false);
    setHovered(undefined);
    void connectionQualityApi.player(serverId, playerName, hours)
      .then((response) => { if (active) setHistory(response); })
      .catch(() => { if (active) setFailed(true); });
    return () => { active = false; };
  }, [serverId, playerName, hours]);

  if (failed) return <Panel className="p-6 text-sm text-slate-400">{t("analytics.quality.playerUnavailable")}</Panel>;
  if (!history) return <Panel className="p-6 text-sm text-slate-500">…</Panel>;

  const points = history.points;
  const start = new Date(points[0].sampledAtUtc).getTime();
  const end = Math.max(start + 1, new Date(points[points.length - 1].sampledAtUtc).getTime());
  const drops = history.sessions
    .filter((session) => session.endedAtUtc && (session.endKind === "lostConnection" || session.endKind === "clientCrash"))
    .map((session) => ({ at: new Date(session.endedAtUtc!).getTime(), kind: session.endKind }))
    .filter((drop) => drop.at >= start && drop.at <= end);
  const rejoins = history.sessions.map((session) => new Date(session.startedAtUtc).getTime()).filter((at) => at > start && at <= end);
  const active = hovered === undefined ? undefined : points[hovered];
  const times = points.map((point) => new Date(point.sampledAtUtc).getTime());
  const charts = [
    { title: t("server.connections.ping"), value: (p: (typeof points)[number]) => p.rttMs, format: (v: number) => `${v.toFixed(0)} ms` },
    { title: t("server.connections.loss"), value: (p: (typeof points)[number]) => p.lossPercent, format: (v: number) => `${v.toFixed(1)}%` },
    { title: t("server.connections.lastData"), value: (p: (typeof points)[number]) => p.lastReceiveMs / 1000, format: (v: number) => `${v.toFixed(1)} s` }
  ];

  return (
    <Panel className="p-4 space-y-4">
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <div className="text-sm font-semibold text-slate-100">
          {history.playerName}
          {history.usesProxy && <span className="ml-2 text-[10px] font-normal text-amber-500/80">{t("server.connections.sharedAddress")}</span>}
        </div>
        <div className="flex items-center gap-4 text-[11px] text-slate-400">
          <span className="flex items-center gap-1.5"><span className="inline-block h-0.5 w-4" style={{ background: LINE_COLOR }} />{t("analytics.quality.measured")}</span>
          <span className="flex items-center gap-1.5"><span className="inline-block h-3 w-0.5" style={{ background: DROP_COLOR }} />{t("analytics.quality.dropMarker")}</span>
          <span className="flex items-center gap-1.5"><span className="inline-block h-3 border-l border-dashed border-slate-400" />{t("analytics.quality.joinMarker")}</span>
        </div>
      </div>

      {charts.map((chart) => (
        <TimeSeriesChart
          key={chart.title}
          title={chart.title}
          times={times}
          values={points.map(chart.value)}
          format={chart.format}
          start={start}
          end={end}
          markers={drops.map((drop) => drop.at)}
          dashedMarkers={rejoins}
          hovered={hovered}
          onHover={setHovered}
        />
      ))}
      <TimeAxis start={start} end={end} />

      <div className="min-h-[2.5rem] text-[11px] text-slate-400 font-mono">
        {active ? (
          <div className="flex flex-wrap gap-x-5 gap-y-1">
            <span className="text-slate-200">{new Date(active.sampledAtUtc).toLocaleString()}</span>
            <span>{t("server.connections.ping")}: <span className={levelClass[pingLevel(active.rttMs)]}>{active.rttMs.toFixed(0)} ms</span></span>
            <span>{t("server.connections.jitter")}: ±{active.jitterMs.toFixed(0)} ms</span>
            <span>{t("server.connections.loss")}: {active.lossPercent === null ? "—" : `${active.lossPercent.toFixed(2)}%`}</span>
            <span>{t("server.connections.lastData")}: {(active.lastReceiveMs / 1000).toFixed(1)} s</span>
          </div>
        ) : (
          <span>{t("analytics.quality.hoverHint")}</span>
        )}
      </div>
    </Panel>
  );
}

function Th({ children, left }: { children: ReactNode; left?: boolean }) {
  return <th className={cn("px-4 py-2 font-semibold whitespace-nowrap", left ? "text-left" : "text-right")}>{children}</th>;
}

function Td({ children, className }: { children: ReactNode; className?: string }) {
  return <td className={cn("px-4 py-2 text-right text-slate-300 whitespace-nowrap", className)}>{children}</td>;
}
