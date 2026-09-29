import { useEffect, useMemo, useState, type ReactNode } from "react";
import { Link } from "react-router-dom";
import { AlertTriangle, CheckCircle2, ChevronRight, Cpu, HardDrive, Power, Users } from "lucide-react";
import { useTranslation } from "react-i18next";
import { serverApi, type ServerClientConnection } from "@/api/servers";
import { disconnectsApi, serverHealthApi, type ServerHealthResponse } from "@/api/analytics";
import { loginsApi } from "@/api/logins";
import { serverLogsApi } from "@/api/serverLogs";
import Panel from "@/components/ui/Panel";
import PageHeader from "@/components/layout/PageHeader";
import ServerStatusBadge from "@/components/server/ServerStatusBadge";
import { useServer } from "@/context/ServerContext";
import { cn } from "@/lib/cn";
import { SERVER_CONNECTIONS_INTERVAL_MS } from "@/lib/constants";
import { clampPercent, formatBytes } from "@/lib/format";

const METRICS_REFRESH_MS = 15_000;
const HISTORY_REFRESH_MS = 60_000;
const DISK_WARN_PERCENT = 85;
const LAG_SECONDS = 1;
const HOUR_MS = 3_600_000;
const DAY_MS = 24 * HOUR_MS;

type Level = "good" | "warn" | "bad";
const levelText: Record<Level, string> = { good: "text-emerald-400", warn: "text-amber-400", bad: "text-rose-400" };
const levelBar: Record<Level, string> = { good: "bg-emerald-500", warn: "bg-amber-500", bad: "bg-rose-500" };
const usageLevel = (percent: number): Level => (percent < 70 ? "good" : percent < 90 ? "warn" : "bad");
const pingLevel = (ms: number): Level => (ms < 100 ? "good" : ms < 180 ? "warn" : "bad");
const lossLevel = (percent: number): Level => (percent < 1 ? "good" : percent < 4 ? "warn" : "bad");
const worst = (...levels: Level[]): Level => (levels.includes("bad") ? "bad" : levels.includes("warn") ? "warn" : "good");

/** Polls `load` while the tab is visible; the result is dropped once the inputs change. */
function usePolling<T>(load: (() => Promise<T>) | null, intervalMs: number) {
  const [state, setState] = useState<{ data?: T; failed: boolean }>({ failed: false });
  useEffect(() => {
    setState({ failed: false });
    if (!load) return;
    let active = true;
    const run = () => {
      if (document.visibilityState !== "visible") return;
      load()
        .then((data) => { if (active) setState({ data, failed: false }); })
        .catch(() => { if (active) setState((prev) => ({ ...prev, failed: true })); });
    };
    run();
    const timer = window.setInterval(run, intervalMs);
    return () => { active = false; window.clearInterval(timer); };
  }, [load, intervalMs]);
  return state;
}

function formatDuration(ms: number, locale: string) {
  const minutes = Math.max(0, Math.floor(ms / 60_000));
  const days = Math.floor(minutes / 1440);
  const hours = Math.floor((minutes % 1440) / 60);
  const mins = minutes % 60;
  const unit = (value: number, name: "day" | "hour" | "minute") =>
    new Intl.NumberFormat(locale, { style: "unit", unit: name, unitDisplay: "narrow" }).format(value);
  if (days > 0) return `${unit(days, "day")} ${unit(hours, "hour")}`;
  if (hours > 0) return `${unit(hours, "hour")} ${unit(mins, "minute")}`;
  return unit(mins, "minute");
}

export default function DashboardPage() {
  const { t, i18n } = useTranslation();
  const { selectedServer, loading, error } = useServer();
  const serverId = selectedServer?.id;
  const online = selectedServer?.status === "online";

  const metricsLoad = useMemo(() => (serverId && online ? () => serverApi.metrics(serverId) : null), [serverId, online]);
  const connectionsLoad = useMemo(() => (serverId && online ? () => serverApi.connections(serverId) : null), [serverId, online]);
  const healthLoad = useMemo(() => (serverId ? () => serverHealthApi.get(serverId, 24) : null), [serverId]);
  const dropsLoad = useMemo(() => (serverId ? () => disconnectsApi.report(serverId, 1) : null), [serverId]);
  const loginsLoad = useMemo(() => () => loginsApi.recent(1, 100), []);
  const problemsLoad = useMemo(() => (serverId ? () => serverLogsApi.problemSummary(serverId) : null), [serverId]);

  const metrics = usePolling(metricsLoad, METRICS_REFRESH_MS);
  const connections = usePolling(connectionsLoad, SERVER_CONNECTIONS_INTERVAL_MS);
  const health = usePolling(healthLoad, HISTORY_REFRESH_MS);
  const drops = usePolling(dropsLoad, HISTORY_REFRESH_MS);
  const logins = usePolling(loginsLoad, HISTORY_REFRESH_MS);
  const problems = usePolling(problemsLoad, HISTORY_REFRESH_MS);

  // Re-render every minute so uptime and "last hour" windows stay current between polls.
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const timer = window.setInterval(() => setNow(Date.now()), 60_000);
    return () => window.clearInterval(timer);
  }, []);

  if (loading) return <Panel className="p-6 text-sm text-slate-400">{t("servers.loading")}</Panel>;
  if (error || !selectedServer) {
    return <Panel className="p-6 text-sm text-slate-400">{error ? t("servers.loadFailed") : t("servers.noneConfigured")}</Panel>;
  }

  const m = online ? metrics.data : undefined;
  const players = online ? (connections.data?.connections ?? []) : [];
  const namedPlayers = players.filter((c) => c.playerName);

  const lastHour = (health.data?.points ?? []).filter((p) => now - Date.parse(p.sampledAtUtc) <= HOUR_MS);
  const lagSpikes = lastHour.filter((p) => (p.longestPauseSeconds ?? 0) > LAG_SECONDS || (p.slowestTickMs ?? 0) > LAG_SECONDS * 1000);
  const worstLagSeconds = Math.max(0, ...lagSpikes.map((p) => Math.max(p.longestPauseSeconds ?? 0, (p.slowestTickMs ?? 0) / 1000)));

  const failedLogins = (logins.data?.items ?? []).filter((l) => !l.succeeded && now - Date.parse(l.timestampUtc) <= DAY_MS).length;
  const dropCount = drops.data?.summary.drops ?? 0;

  const attention: { key: string; text: string; to: string; level: Level }[] = [];
  if (selectedServer.status === "offline") attention.push({ key: "offline", text: t("dashboard.attention.offline"), to: "/server", level: "bad" });
  if (m && m.diskPercent >= DISK_WARN_PERCENT) {
    attention.push({
      key: "disk",
      text: t("dashboard.attention.disk", { percent: Math.round(m.diskPercent), free: formatBytes(m.diskAvailableBytes) }),
      to: "/files",
      level: m.diskPercent >= 95 ? "bad" : "warn"
    });
  }
  if (online && metrics.failed && !metrics.data) attention.push({ key: "metrics", text: t("dashboard.attention.metricsFailed"), to: "/server", level: "warn" });
  if (lagSpikes.length > 0) {
    attention.push({
      key: "lag",
      text: t("dashboard.attention.lag", { count: lagSpikes.length, seconds: worstLagSeconds.toFixed(1) }),
      to: "/analytics?tab=health",
      level: worstLagSeconds >= 5 ? "bad" : "warn"
    });
  }
  if (dropCount > 0) attention.push({ key: "drops", text: t("dashboard.attention.drops", { count: dropCount }), to: "/analytics?tab=disconnects", level: "warn" });
  const newProblems = problems.data?.configured ? problems.data : undefined;
  if (newProblems && newProblems.newErrors + newProblems.newWarnings > 0) {
    attention.push({
      key: "logProblems",
      text: t("dashboard.attention.logProblems", { errors: newProblems.newErrors, warnings: newProblems.newWarnings }),
      to: "/server-logs?tab=problems",
      level: newProblems.newErrors > 0 ? "bad" : "warn"
    });
  }
  if (failedLogins > 0) attention.push({ key: "logins", text: t("dashboard.attention.failedLogins", { count: failedLogins }), to: "/settings", level: "warn" });

  return (
    <div className="space-y-6 max-w-none">
      <PageHeader title={t("dashboard.title")} description={t("dashboard.description")} />

      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <Tile icon={<Power size={16} />} title={t("dashboard.status.title")} to="/server">
          <div className="flex items-center gap-2">
            <ServerStatusBadge status={selectedServer.status} />
          </div>
          <div className="mt-3 text-xs text-slate-400">
            {!online ? (
              selectedServer.status === "offline" ? t("dashboard.status.offlineHint") : "—"
            ) : m?.startedAtUtc ? (
              <>
                <div className="text-lg font-semibold text-slate-100">{t("dashboard.status.upFor", { duration: formatDuration(now - Date.parse(m.startedAtUtc), i18n.language) })}</div>
                <div>{t("dashboard.status.since", { time: new Date(m.startedAtUtc).toLocaleString() })}</div>
              </>
            ) : metrics.data || metrics.failed ? t("dashboard.status.uptimeUnknown") : "…"}
          </div>
        </Tile>

        <Tile icon={<Users size={16} />} title={t("dashboard.players.title")} to="/analytics?tab=live">
          <div className="flex items-end justify-between gap-3">
            <div className="text-3xl font-bold text-slate-100 tabular-nums">{online ? (connections.data ? namedPlayers.length : "…") : 0}</div>
            {health.data && <PlayersSparkline health={health.data} />}
          </div>
          {health.data && (
            <div className="mt-2 text-xs text-slate-400">{t("dashboard.players.peak", { count: health.data.summary.peakPlayers })}</div>
          )}
        </Tile>

        <Tile icon={<Cpu size={16} />} title={t("dashboard.performance.title")} to="/analytics?tab=health">
          {m ? (
            <div className="space-y-2">
              <Meter label={t("dashboard.performance.cpu")} percent={m.cpuPercent} value={`${m.cpuPercent.toFixed(0)}%`} />
              <Meter label={t("dashboard.performance.memory")} percent={m.memoryPercent} value={m.memoryUsage || `${m.memoryPercent.toFixed(0)}%`} />
            </div>
          ) : <div className="text-xs text-slate-500">{online ? "…" : "—"}</div>}
          {health.data && (
            <div className={cn("mt-2 flex items-center gap-1.5 text-xs", lagSpikes.length ? "text-amber-300" : "text-slate-400")}>
              {lagSpikes.length ? <AlertTriangle size={12} /> : <CheckCircle2 size={12} className="text-emerald-400" />}
              {lagSpikes.length ? t("dashboard.performance.lag", { count: lagSpikes.length }) : t("dashboard.performance.lagNone")}
            </div>
          )}
        </Tile>

        <Tile icon={<HardDrive size={16} />} title={t("dashboard.disk.title")} to="/files">
          {m ? (
            <div className="space-y-2">
              <div className="text-3xl font-bold text-slate-100 tabular-nums">{Math.round(m.diskPercent)}%</div>
              <Bar percent={m.diskPercent} level={m.diskPercent >= 95 ? "bad" : m.diskPercent >= DISK_WARN_PERCENT ? "warn" : "good"} />
              <div className="flex justify-between gap-2 text-xs text-slate-400">
                <span>{t("dashboard.disk.used", { used: formatBytes(m.diskUsedBytes), total: formatBytes(m.diskTotalBytes) })}</span>
                <span>{t("dashboard.disk.free", { free: formatBytes(m.diskAvailableBytes) })}</span>
              </div>
            </div>
          ) : <div className="text-xs text-slate-500">{online ? "…" : "—"}</div>}
        </Tile>
      </div>

      <Panel className="p-5">
        <SectionTitle title={t("dashboard.online.title")} count={online ? players.length : undefined} link={{ to: "/analytics?tab=live", label: t("dashboard.online.viewAll") }} />
        {!online ? (
          <p className="py-4 text-sm text-slate-400">{t("dashboard.online.offline")}</p>
        ) : !connections.data ? (
          <p className="py-4 text-sm text-slate-500">…</p>
        ) : players.length === 0 ? (
          <p className="py-4 text-sm text-slate-400">{t("dashboard.online.empty")}</p>
        ) : (
          <ul className="grid gap-2 sm:grid-cols-2 xl:grid-cols-3">
            {players.map((c) => <PlayerRow key={`${c.remoteAddress}:${c.remotePort}`} connection={c} />)}
          </ul>
        )}
      </Panel>

      <Panel className="p-5">
        <SectionTitle title={t("dashboard.attention.title")} count={attention.length || undefined} />
        {attention.length === 0 ? (
          <p className="flex items-center gap-2 py-2 text-sm text-emerald-300">
            <CheckCircle2 size={16} /> {t("dashboard.attention.allGood")}
          </p>
        ) : (
          <ul className="divide-y divide-red-950/20">
            {attention.map((item) => (
              <li key={item.key}>
                <Link to={item.to} className="flex items-center gap-3 py-2.5 text-sm text-slate-200 hover:text-white group">
                  <AlertTriangle size={16} className={cn("shrink-0", levelText[item.level])} />
                  <span className="flex-1">{item.text}</span>
                  <span className="flex items-center text-xs text-slate-500 group-hover:text-slate-300">
                    {t("dashboard.attention.open")} <ChevronRight size={14} />
                  </span>
                </Link>
              </li>
            ))}
          </ul>
        )}
      </Panel>
    </div>
  );
}

function Tile({ icon, title, to, children }: { icon: ReactNode; title: string; to: string; children: ReactNode }) {
  return (
    <Link to={to} className="block group">
      <Panel className="h-full p-4 transition-colors group-hover:border-red-900/50">
        <div className="mb-3 flex items-center gap-2 text-[11px] font-bold uppercase tracking-widest text-slate-400">
          <span className="text-[#e04444]">{icon}</span>
          <span className="flex-1">{title}</span>
          <ChevronRight size={14} className="text-slate-600 group-hover:text-slate-300" />
        </div>
        {children}
      </Panel>
    </Link>
  );
}

function SectionTitle({ title, count, link }: { title: string; count?: number; link?: { to: string; label: string } }) {
  return (
    <div className="mb-3 flex items-center gap-2 border-b border-red-950/20 pb-2">
      <h2 className="text-base font-bold font-serif tracking-wider text-slate-100 glow-text">{title}</h2>
      {count !== undefined && <span className="rounded-full bg-slate-800 px-2 py-0.5 text-[11px] text-slate-300">{count}</span>}
      {link && (
        <Link to={link.to} className="ml-auto flex items-center text-xs text-slate-400 hover:text-slate-100">
          {link.label} <ChevronRight size={14} />
        </Link>
      )}
    </div>
  );
}

function Bar({ percent, level }: { percent: number; level: Level }) {
  return (
    <div className="h-1.5 overflow-hidden rounded-full bg-slate-800">
      <div className={cn("h-full rounded-full", levelBar[level])} style={{ width: `${clampPercent(percent)}%` }} />
    </div>
  );
}

function Meter({ label, percent, value }: { label: string; percent: number; value: string }) {
  return (
    <div>
      <div className="mb-1 flex justify-between text-xs">
        <span className="text-slate-400">{label}</span>
        <span className="font-mono text-slate-200">{value}</span>
      </div>
      <Bar percent={percent} level={usageLevel(percent)} />
    </div>
  );
}

function PlayerRow({ connection: c }: { connection: ServerClientConnection }) {
  const { t } = useTranslation();
  const level = worst(pingLevel(c.rttMs), lossLevel(c.retransmitPercent));
  return (
    <li className="flex items-center gap-2.5 rounded-md border border-red-950/20 bg-slate-950/40 px-3 py-2">
      <span className={cn("h-2 w-2 shrink-0 rounded-full", levelBar[level])} />
      <span className="min-w-0 flex-1 truncate text-sm text-slate-100">
        {c.playerName ?? <span className="text-slate-500">{t("dashboard.online.unknownPlayer")}</span>}
      </span>
      <span className="font-mono text-[11px] text-slate-400">
        <span className={levelText[pingLevel(c.rttMs)]}>{c.rttMs.toFixed(0)} ms</span> {t("dashboard.online.ping")}
        {c.retransmitPercent >= 1 && (
          <> · <span className={levelText[lossLevel(c.retransmitPercent)]}>{c.retransmitPercent.toFixed(1)}%</span> {t("dashboard.online.loss")}</>
        )}
      </span>
    </li>
  );
}

/** Players online over the last 24 h; hover shows the value at that moment. */
function PlayersSparkline({ health }: { health: ServerHealthResponse }) {
  const { t } = useTranslation();
  const [hovered, setHovered] = useState<number>();
  const points = health.points;
  if (points.length < 2) return null;

  const width = 120;
  const height = 36;
  const max = Math.max(1, ...points.map((p) => p.players));
  const x = (i: number) => (i / (points.length - 1)) * width;
  const y = (v: number) => height - 2 - (v / max) * (height - 4);
  const line = points.map((p, i) => `${i ? "L" : "M"}${x(i).toFixed(1)},${y(p.players).toFixed(1)}`).join("");
  const hoveredPoint = hovered !== undefined ? points[hovered] : undefined;

  return (
    <div className="relative" onClick={(e) => e.preventDefault()}>
      <svg
        width={width}
        height={height}
        role="img"
        aria-label={t("dashboard.players.chart")}
        className="overflow-visible"
        onMouseMove={(e) => {
          const rect = e.currentTarget.getBoundingClientRect();
          setHovered(Math.round(((e.clientX - rect.left) / rect.width) * (points.length - 1)));
        }}
        onMouseLeave={() => setHovered(undefined)}
      >
        <title>{t("dashboard.players.chart")}</title>
        <path d={`${line}L${width},${height}L0,${height}Z`} fill="#3987e5" fillOpacity={0.15} />
        <path d={line} fill="none" stroke="#3987e5" strokeWidth={2} strokeLinejoin="round" />
        {hoveredPoint && (
          <>
            <line x1={x(hovered!)} x2={x(hovered!)} y1={0} y2={height} stroke="#64748b" strokeWidth={1} />
            <circle cx={x(hovered!)} cy={y(hoveredPoint.players)} r={3} fill="#3987e5" stroke="#0f172a" strokeWidth={2} />
          </>
        )}
      </svg>
      {hoveredPoint && (
        <div className="pointer-events-none absolute bottom-full right-0 mb-1 whitespace-nowrap rounded bg-slate-900 px-2 py-1 text-[11px] text-slate-200 shadow">
          {t("dashboard.players.point", {
            time: new Date(hoveredPoint.sampledAtUtc).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" }),
            count: hoveredPoint.players
          })}
        </div>
      )}
    </div>
  );
}
