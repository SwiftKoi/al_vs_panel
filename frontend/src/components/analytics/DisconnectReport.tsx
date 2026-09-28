import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import DataTable from "@/components/ui/DataTable";
import Panel from "@/components/ui/Panel";
import { disconnectsApi, type DisconnectReportResponse } from "@/api/analytics";
import { cn } from "@/lib/cn";

// Single-series magnitude color, validated for the dark panel surface.
const BAR_COLOR = "#3987e5";
const REFRESH_MS = 5 * 60_000;
const RANGES = [1, 7, 30] as const;

const percent = (part: number, total: number) => (total > 0 ? `${((part * 100) / total).toFixed(1)}%` : "—");

export default function DisconnectReport({ serverId }: { serverId: string }) {
  const { t } = useTranslation();
  const [days, setDays] = useState<(typeof RANGES)[number]>(7);
  const [report, setReport] = useState<DisconnectReportResponse>();
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    let active = true;
    const load = () => {
      void disconnectsApi.report(serverId, days)
        .then((response) => { if (active) { setReport(response); setFailed(false); } })
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
          {t("analytics.disconnects.title")}
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
              {range === 1 ? t("analytics.disconnects.today") : t("analytics.players.days", { count: range })}
            </button>
          ))}
        </div>
      </div>

      {failed && !report ? (
        <Panel className="p-6 text-sm text-slate-400">{t("analytics.disconnects.unavailable")}</Panel>
      ) : !report ? (
        <Panel className="p-6 text-sm text-slate-500">…</Panel>
      ) : (
        <Report report={report} />
      )}
    </div>
  );
}

function Report({ report }: { report: DisconnectReportResponse }) {
  const { t } = useTranslation();
  const s = report.summary;
  const maxReason = Math.max(1, ...report.endReasons.map((reason) => reason.count));

  return (
    <div className="space-y-4">
      <div className="grid gap-4 grid-cols-2 lg:grid-cols-6">
        <Tile label={t("analytics.disconnects.sessions")} value={s.sessions} hint={t("analytics.disconnects.medianSession", { value: s.medianSessionMinutes.toFixed(0) })} />
        <Tile label={t("analytics.disconnects.drops")} value={s.drops} hint={percent(s.drops, s.sessions)} />
        <Tile label={t("analytics.disconnects.quickRejoins")} value={s.quickRejoins} hint={t("analytics.disconnects.quickRejoinsHint")} />
        <Tile label={t("analytics.disconnects.groupDrops")} value={s.groupDrops} hint={t("analytics.disconnects.nearAutosaveCount", { count: s.dropsNearAutosave })} />
        <Tile label={t("analytics.disconnects.afterOverload")} value={s.dropsAfterOverload} hint={percent(s.dropsAfterOverload, s.drops) + " " + t("analytics.disconnects.ofDrops")} />
        <Tile label={t("analytics.disconnects.failures")} value={s.connectionFailures} hint={t("analytics.disconnects.failuresHint")} />
      </div>

      <div className="grid gap-4 lg:grid-cols-2">
        <Panel className="p-4 space-y-3">
          <div className="text-xs font-semibold text-slate-300">{t("analytics.disconnects.endReasons")}</div>
          <ul className="space-y-2">
            {report.endReasons.map((reason) => (
              <li key={reason.kind} className="grid grid-cols-[9rem_1fr_3rem] items-center gap-3 text-xs">
                <span className="text-slate-400 truncate">{t(`analytics.disconnects.kind.${reason.kind}`)}</span>
                <div className="h-2 rounded-r-[4px] bg-slate-800/40">
                  <div className="h-2 rounded-r-[4px]" style={{ width: `${(reason.count / maxReason) * 100}%`, background: BAR_COLOR }} />
                </div>
                <span className="font-mono text-slate-200 text-right">{reason.count}</span>
              </li>
            ))}
            {report.endReasons.length === 0 && <li className="text-xs text-slate-500">{t("analytics.disconnects.none")}</li>}
          </ul>
        </Panel>

        <Panel className="p-4 space-y-3">
          <div className="text-xs font-semibold text-slate-300">{t("analytics.disconnects.proxyVsDirect")}</div>
          <div className="grid grid-cols-2 gap-4 text-xs">
            <RateBlock label={t("analytics.players.viaProxy")} drops={s.proxyDrops} sessions={s.proxySessions} />
            <RateBlock label={t("analytics.players.direct")} drops={s.directDrops} sessions={s.directSessions} />
          </div>
          {report.failureReasons.length > 0 && (
            <div className="pt-2 border-t border-red-950/20 space-y-1">
              <div className="text-[11px] font-semibold text-slate-400">{t("analytics.disconnects.failureReasons")}</div>
              {report.failureReasons.slice(0, 5).map((failure) => (
                <div key={failure.reason} className="flex justify-between gap-3 text-[11px]">
                  <span className="text-slate-400 truncate" title={failure.reason}>{failure.reason}</span>
                  <span className="font-mono text-slate-200">{failure.count}</span>
                </div>
              ))}
            </div>
          )}
        </Panel>
      </div>

      {report.days > 1 && <DailyDrops report={report} />}

      <Panel className="p-0 overflow-hidden">
        <DataTable
          rows={report.players}
          rowKey={(player) => player.playerName}
          searchText={(player) => player.playerName}
          defaultSort={{ id: "problems", direction: "desc" }}
          emptyText={t("analytics.disconnects.noProblemPlayers")}
          toolbar={<div className="text-xs font-semibold text-slate-300">{t("analytics.disconnects.playersTitle")}</div>}
          columns={[
            {
              id: "player",
              header: t("analytics.disconnects.player"),
              sortValue: (player) => player.playerName,
              firstDirection: "asc",
              cell: (player) => <PlayerName name={player.playerName} proxy={player.usesProxy} />
            },
            { id: "sessions", header: t("analytics.disconnects.sessions"), align: "right", sortValue: (p) => p.sessions, cell: (p) => p.sessions },
            {
              id: "problems",
              header: t("analytics.disconnects.drops"),
              align: "right",
              sortValue: (p) => p.drops * 1000 + p.quickRejoins,
              cell: (p) => <span className={p.drops > 0 ? "text-rose-400" : undefined}>{p.drops}</span>
            },
            {
              id: "quickRejoins",
              header: t("analytics.disconnects.quickRejoins"),
              align: "right",
              sortValue: (p) => p.quickRejoins,
              cell: (p) => <span className={p.quickRejoins > 0 ? "text-amber-400" : undefined}>{p.quickRejoins}</span>
            },
            {
              id: "avgSession",
              header: t("analytics.disconnects.avgSession"),
              align: "right",
              sortValue: (p) => p.averageSessionMinutes,
              cell: (p) => `${p.averageSessionMinutes.toFixed(0)} ${t("analytics.disconnects.min")}`
            },
            {
              id: "lastDrop",
              header: t("analytics.disconnects.lastDrop"),
              align: "right",
              sortValue: (p) => p.lastDropUtc,
              cell: (p) => (p.lastDropUtc ? new Date(p.lastDropUtc).toLocaleString() : "—")
            }
          ]}
        />
      </Panel>

      <Panel className="p-0 overflow-hidden">
        <DataTable
          rows={report.recentDrops}
          rowKey={(drop) => `${drop.occurredAtUtc}-${drop.playerName}`}
          searchText={(drop) => `${drop.playerName} ${t(`analytics.disconnects.kind.${drop.kind}`)} ${drop.reason ?? ""}`}
          defaultSort={{ id: "time", direction: "desc" }}
          emptyText={t("analytics.disconnects.none")}
          toolbar={
            <div>
              <div className="text-xs font-semibold text-slate-300">{t("analytics.disconnects.recentTitle")}</div>
              <div className="text-[11px] text-slate-500 mt-1 max-w-2xl">{t("analytics.disconnects.recentHint")}</div>
            </div>
          }
          columns={[
            {
              id: "time",
              header: t("analytics.disconnects.time"),
              sortValue: (d) => d.occurredAtUtc,
              className: "font-mono text-slate-400",
              cell: (d) => new Date(d.occurredAtUtc).toLocaleString()
            },
            {
              id: "player",
              header: t("analytics.disconnects.player"),
              sortValue: (d) => d.playerName,
              firstDirection: "asc",
              cell: (d) => <PlayerName name={d.playerName} proxy={d.viaProxy} />
            },
            {
              id: "cause",
              header: t("analytics.disconnects.cause"),
              sortValue: (d) => d.kind,
              firstDirection: "asc",
              className: "text-slate-300",
              cell: (d) => <span title={d.reason ?? undefined}>{t(`analytics.disconnects.kind.${d.kind}`)}</span>
            },
            {
              id: "session",
              header: t("analytics.disconnects.sessionLength"),
              align: "right",
              sortValue: (d) => d.sessionMinutes,
              cell: (d) => `${d.sessionMinutes.toFixed(0)} ${t("analytics.disconnects.min")}`
            },
            {
              id: "autosave",
              header: t("analytics.disconnects.autosave"),
              align: "right",
              sortValue: (d) => (d.nearAutosave ? 1 : 0),
              cell: (d) => (d.nearAutosave ? <span className="text-amber-400">{t("analytics.disconnects.yes")}</span> : <span className="text-slate-600">—</span>)
            },
            {
              id: "slowTick",
              header: t("analytics.disconnects.slowTick"),
              title: t("analytics.disconnects.slowTickHint"),
              align: "right",
              sortValue: (d) => d.slowestTickMs,
              cell: (d) =>
                d.slowestTickMs === null ? (
                  <span className="text-slate-600">—</span>
                ) : (
                  <span className={d.slowestTickMs >= 2000 ? "text-rose-400" : "text-amber-400"}>{(d.slowestTickMs / 1000).toFixed(1)} s</span>
                )
            },
            {
              id: "together",
              header: t("analytics.disconnects.together"),
              align: "right",
              sortValue: (d) => d.simultaneousDrops,
              cell: (d) => (d.simultaneousDrops > 0 ? <span className="text-amber-400">+{d.simultaneousDrops}</span> : <span className="text-slate-600">—</span>)
            },
            { id: "ping", header: t("server.connections.ping"), align: "right", sortValue: (d) => d.rttMs, cell: (d) => (d.rttMs === null ? "—" : `${d.rttMs.toFixed(0)} ms`) },
            { id: "loss", header: t("server.connections.loss"), align: "right", sortValue: (d) => d.retransmitPercent, cell: (d) => (d.retransmitPercent === null ? "—" : `${d.retransmitPercent.toFixed(1)}%`) },
            { id: "lastData", header: t("server.connections.lastData"), align: "right", sortValue: (d) => d.lastReceiveMs, cell: (d) => (d.lastReceiveMs === null ? "—" : `${(d.lastReceiveMs / 1000).toFixed(1)} s`) }
          ]}
        />
      </Panel>
    </div>
  );
}

function DailyDrops({ report }: { report: DisconnectReportResponse }) {
  const { t } = useTranslation();
  const [hovered, setHovered] = useState<number>();
  const max = Math.max(1, ...report.daily.map((day) => day.drops));
  const active = hovered === undefined ? undefined : report.daily[hovered];

  return (
    <Panel className="p-4 space-y-3">
      <div className="text-xs font-semibold text-slate-300">{t("analytics.disconnects.dailyTitle")}</div>
      <div className="flex gap-2">
        <div className="flex flex-col justify-between h-28 text-[10px] font-mono text-slate-500 text-right w-6 shrink-0">
          <span>{max}</span>
          <span>0</span>
        </div>
        <div className="relative flex-1 h-28 border-b border-slate-700/40">
          <div className="absolute inset-0 flex items-end gap-[2px]" onMouseLeave={() => setHovered(undefined)}>
            {report.daily.map((day, index) => (
              <div
                key={day.date}
                className="flex-1 h-full flex items-end"
                onMouseEnter={() => setHovered(index)}
                onClick={() => setHovered(index)}
              >
                <div
                  className={cn("w-full rounded-t-[4px] transition-opacity", hovered !== undefined && hovered !== index && "opacity-50")}
                  style={{ height: `${(day.drops / max) * 100}%`, background: BAR_COLOR }}
                />
              </div>
            ))}
          </div>
          {active && hovered !== undefined && (
            <div
              className="pointer-events-none absolute top-0 z-10 rounded-md border border-slate-700/60 bg-slate-950/95 px-3 py-2 text-[11px] shadow-xl whitespace-nowrap"
              style={hovered > report.daily.length / 2
                ? { right: `${(1 - (hovered + 0.5) / report.daily.length) * 100}%` }
                : { left: `${((hovered + 0.5) / report.daily.length) * 100}%` }}
            >
              <div className="font-semibold text-slate-100 mb-1">{active.date}</div>
              <TipRow label={t("analytics.disconnects.drops")} value={active.drops} />
              <TipRow label={t("analytics.disconnects.quickRejoins")} value={active.quickRejoins} />
              <TipRow label={t("analytics.disconnects.sessions")} value={active.sessions} />
            </div>
          )}
        </div>
      </div>
      <div className="flex justify-between pl-8 text-[10px] font-mono text-slate-500">
        <span>{report.daily[0]?.date.slice(5)}</span>
        <span>{report.daily[report.daily.length - 1]?.date.slice(5)}</span>
      </div>
    </Panel>
  );
}

function Tile({ label, value, hint }: { label: string; value: number; hint: string }) {
  return (
    <Panel className="p-4 space-y-1">
      <div className="text-xs font-semibold text-slate-400">{label}</div>
      <div className="font-mono text-2xl font-semibold text-slate-100">{value}</div>
      <div className="text-[11px] text-slate-500">{hint}</div>
    </Panel>
  );
}

function RateBlock({ label, drops, sessions }: { label: string; drops: number; sessions: number }) {
  const { t } = useTranslation();
  return (
    <div className="space-y-1">
      <div className="text-slate-400">{label}</div>
      <div className="font-mono text-xl text-slate-100">{percent(drops, sessions)}</div>
      <div className="text-[11px] text-slate-500">{t("analytics.disconnects.dropsOfSessions", { drops, sessions })}</div>
    </div>
  );
}

function ProxyBadge() {
  const { t } = useTranslation();
  return <span className="ml-2 font-sans text-[10px] font-normal text-amber-500/80">{t("server.connections.sharedAddress")}</span>;
}

function TipRow({ label, value }: { label: string; value: number }) {
  return (
    <div className="flex justify-between gap-4 text-slate-400">
      <span>{label}</span>
      <span className="font-mono text-slate-200">{value}</span>
    </div>
  );
}

function PlayerName({ name, proxy }: { name: string; proxy: boolean }) {
  return (
    <span className="font-semibold text-slate-200">
      {name}
      {proxy && <ProxyBadge />}
    </span>
  );
}
