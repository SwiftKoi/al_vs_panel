import { X } from "lucide-react";
import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import DataTable from "@/components/ui/DataTable";
import Panel from "@/components/ui/Panel";
import { playersApi, type ActivityHeatmapResponse, type PlayerPlaytime, type PlayerProfileResponse } from "@/api/analytics";
import { PlayerHistory } from "@/components/analytics/ConnectionQuality";
import { cn } from "@/lib/cn";

// Sequential single-hue ramp for the heatmap (same blue as the other charts).
const HEAT_RGB = "57, 135, 229";
const RANGES = [7, 30, 90] as const;

function formatDuration(minutes: number, t: (key: string, options?: Record<string, unknown>) => string) {
  if (minutes < 60) return t("analytics.directory.minutes", { value: Math.round(minutes) });
  return t("analytics.directory.hours", { value: (minutes / 60).toFixed(minutes < 600 ? 1 : 0) });
}

export function ActivityHeatmap({ serverId }: { serverId: string }) {
  const { t } = useTranslation();
  const [heatmap, setHeatmap] = useState<ActivityHeatmapResponse>();
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    let active = true;
    void playersApi.heatmap(serverId, 28)
      .then((response) => { if (active) setHeatmap(response); })
      .catch(() => { if (active) setFailed(true); });
    return () => { active = false; };
  }, [serverId]);

  if (failed) return <Panel className="p-6 text-sm text-slate-400">{t("analytics.heatmap.unavailable")}</Panel>;
  if (!heatmap) return <Panel className="p-6 text-sm text-slate-500">…</Panel>;

  const max = Math.max(1, ...heatmap.cells.map((cell) => cell.averagePlayers));
  const weekdays = t("analytics.heatmap.weekdays").split(",");

  return (
    <Panel className="p-4 space-y-3">
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <div className="text-xs font-semibold text-slate-300">{t("analytics.heatmap.title")}</div>
        <div className="text-[11px] text-slate-500">{t("analytics.heatmap.hint", { zone: heatmap.timeZone })}</div>
      </div>
      <div className="overflow-x-auto">
        <div className="min-w-[640px]">
          <div className="grid grid-cols-[2.5rem_repeat(24,minmax(0,1fr))] gap-[2px] text-[10px] font-mono text-slate-500">
            <span />
            {Array.from({ length: 24 }, (_, hour) => (
              <span key={hour} className="text-center">{hour % 3 === 0 ? hour : ""}</span>
            ))}
            {weekdays.map((label, weekday) => (
              <div key={label} className="contents">
                <span className="pr-2 text-right leading-6">{label}</span>
                {heatmap.cells.filter((cell) => cell.weekday === weekday).map((cell) => (
                  <div
                    key={cell.hour}
                    title={t("analytics.heatmap.cell", { day: label, hour: cell.hour, average: cell.averagePlayers, peak: cell.peakPlayers })}
                    className="h-6 rounded-[3px] border border-slate-800/40"
                    style={{ background: cell.averagePlayers > 0 ? `rgba(${HEAT_RGB}, ${0.12 + 0.88 * (cell.averagePlayers / max)})` : undefined }}
                  />
                ))}
              </div>
            ))}
          </div>
        </div>
      </div>
      <div className="flex items-center gap-2 text-[10px] font-mono text-slate-500">
        <span>0</span>
        <div className="h-2 w-32 rounded-sm" style={{ background: `linear-gradient(to right, rgba(${HEAT_RGB}, 0.12), rgba(${HEAT_RGB}, 1))` }} />
        <span>{t("analytics.heatmap.legendMax", { value: max.toFixed(1) })}</span>
      </div>
    </Panel>
  );
}

export function PlayerDirectory({ serverId }: { serverId: string }) {
  const { t } = useTranslation();
  const [days, setDays] = useState<(typeof RANGES)[number]>(30);
  const [players, setPlayers] = useState<PlayerPlaytime[]>();
  const [failed, setFailed] = useState(false);
  const [selected, setSelected] = useState<string>();

  useEffect(() => {
    let active = true;
    setFailed(false);
    void playersApi.list(serverId, days)
      .then((response) => { if (active) setPlayers(response.players); })
      .catch(() => { if (active) setFailed(true); });
    return () => { active = false; };
  }, [serverId, days]);

  return (
    <div className="space-y-4">
      <Panel className="p-0 overflow-hidden">
        {failed && !players ? (
          <p className="px-4 py-6 text-sm text-slate-400">{t("analytics.directory.unavailable")}</p>
        ) : !players ? (
          <p className="px-4 py-6 text-sm text-slate-500">…</p>
        ) : (
          <DataTable
            rows={players}
            rowKey={(p) => p.playerName}
            searchText={(p) => p.playerName}
            defaultSort={{ id: "playtime", direction: "desc" }}
            onRowClick={(p) => setSelected(p.playerName)}
            rowClassName={(p) => selected === p.playerName && "bg-[#b8282e]/10"}
            toolbar={
              <div className="flex flex-wrap items-center gap-3">
                <div className="text-xs font-semibold text-slate-300">
                  {t("analytics.directory.title")}
                  <span className="ml-2 font-normal text-slate-500">{t("analytics.directory.clickHint")}</span>
                </div>
                <RangeSwitch value={days} onChange={setDays} />
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
              { id: "playtime", header: t("analytics.directory.playtime"), align: "right", sortValue: (p) => p.totalMinutes, cell: (p) => formatDuration(p.totalMinutes, t) },
              { id: "sessions", header: t("analytics.disconnects.sessions"), align: "right", sortValue: (p) => p.sessions, cell: (p) => p.sessions },
              { id: "avg", header: t("analytics.disconnects.avgSession"), align: "right", sortValue: (p) => p.averageSessionMinutes, cell: (p) => formatDuration(p.averageSessionMinutes, t) },
              { id: "drops", header: t("analytics.disconnects.drops"), align: "right", sortValue: (p) => p.drops, cell: (p) => <span className={p.drops > 0 ? "text-rose-400" : undefined}>{p.drops}</span> },
              { id: "quick", header: t("analytics.disconnects.quickRejoins"), align: "right", sortValue: (p) => p.quickRejoins, cell: (p) => <span className={p.quickRejoins > 0 ? "text-amber-400" : undefined}>{p.quickRejoins}</span> },
              { id: "first", header: t("analytics.directory.firstSeen"), align: "right", sortValue: (p) => p.firstSeenUtc, cell: (p) => new Date(p.firstSeenUtc).toLocaleDateString() },
              { id: "last", header: t("analytics.quality.lastSeen"), align: "right", sortValue: (p) => p.lastSeenUtc, cell: (p) => new Date(p.lastSeenUtc).toLocaleString() }
            ]}
          />
        )}
      </Panel>
      {selected && <PlayerProfile serverId={serverId} playerName={selected} days={days} onClose={() => setSelected(undefined)} />}
    </div>
  );
}

function PlayerProfile({ serverId, playerName, days, onClose }: { serverId: string; playerName: string; days: number; onClose: () => void }) {
  const { t } = useTranslation();
  const [profile, setProfile] = useState<PlayerProfileResponse>();
  const [failed, setFailed] = useState(false);
  const [showTimeline, setShowTimeline] = useState(false);

  useEffect(() => {
    let active = true;
    setProfile(undefined);
    setFailed(false);
    setShowTimeline(false);
    void playersApi.profile(serverId, playerName, days)
      .then((response) => { if (active) setProfile(response); })
      .catch(() => { if (active) setFailed(true); });
    return () => { active = false; };
  }, [serverId, playerName, days]);

  const q = profile?.quality;
  const isDrop = (kind: string) => kind === "lostConnection" || kind === "clientCrash";

  return (
    <Panel className="p-4 space-y-4">
      <div className="flex items-start justify-between gap-3">
        <div>
          <div className="text-base font-semibold text-slate-100">{playerName}</div>
          {profile && (
            <div className="text-[11px] text-slate-500 mt-0.5">
              {t("analytics.directory.profileSubtitle", {
                first: new Date(profile.firstSeenUtc).toLocaleDateString(),
                last: new Date(profile.lastSeenUtc).toLocaleString(),
                days
              })}
            </div>
          )}
        </div>
        <button type="button" onClick={onClose} aria-label={t("analytics.directory.close")} className="text-slate-400 hover:text-slate-100">
          <X size={16} />
        </button>
      </div>

      {failed ? (
        <p className="text-sm text-slate-400">{t("analytics.directory.profileUnavailable")}</p>
      ) : !profile ? (
        <p className="text-sm text-slate-500">…</p>
      ) : (
        <>
          <div className="grid gap-3 grid-cols-2 md:grid-cols-4 lg:grid-cols-7">
            <Fact label={t("analytics.directory.playtime")} value={formatDuration(profile.totalMinutes, t)} />
            <Fact label={t("analytics.disconnects.sessions")} value={String(profile.sessions)} />
            <Fact label={t("analytics.disconnects.avgSession")} value={formatDuration(profile.averageSessionMinutes, t)} />
            <Fact label={t("analytics.disconnects.drops")} value={String(profile.drops)} tone={profile.drops > 0 ? "text-rose-400" : undefined} />
            <Fact label={t("analytics.disconnects.quickRejoins")} value={String(profile.quickRejoins)} tone={profile.quickRejoins > 0 ? "text-amber-400" : undefined} />
            <Fact label={t("analytics.players.viaProxy")} value={`${profile.proxySessionPercent}%`} />
            <Fact
              label={t("analytics.quality.medianPing")}
              value={q?.medianRttMs == null ? "—" : `${q.medianRttMs.toFixed(0)} ms`}
              hint={q?.lossPercent == null ? undefined : `${t("server.connections.loss")} ${q.lossPercent.toFixed(2)}% · ${t("analytics.quality.stalls")} ${q.stalls}`}
            />
          </div>

          <div className="flex flex-wrap gap-x-4 gap-y-1 text-[11px] text-slate-400">
            <span className="font-semibold text-slate-300">{t("analytics.disconnects.endReasons")}:</span>
            {profile.endReasons.map((reason) => (
              <span key={reason.kind}>
                {t(`analytics.disconnects.kind.${reason.kind}`)} <span className="font-mono text-slate-200">{reason.count}</span>
              </span>
            ))}
          </div>

          {q && q.samples > 0 && (
            <div>
              <button
                type="button"
                onClick={() => setShowTimeline((value) => !value)}
                className="text-xs text-[#e04444] hover:text-[#ff6b6b]"
              >
                {showTimeline ? t("analytics.directory.hideTimeline") : t("analytics.directory.showTimeline")}
              </button>
              {showTimeline && (
                <div className="mt-3">
                  <PlayerHistory serverId={serverId} playerName={playerName} hours={Math.min(days * 24, 720)} />
                </div>
              )}
            </div>
          )}

          <div className="-mx-4">
            <DataTable
              rows={profile.recentSessions}
              rowKey={(s) => s.startedAtUtc}
              defaultSort={{ id: "start", direction: "desc" }}
              pageSize={10}
              toolbar={<div className="text-xs font-semibold text-slate-300">{t("analytics.directory.sessionsTitle")}</div>}
              rowClassName={(s) => isDrop(s.endKind) && "bg-rose-500/5"}
              columns={[
                { id: "start", header: t("analytics.directory.started"), sortValue: (s) => s.startedAtUtc, className: "font-mono text-slate-300", cell: (s) => new Date(s.startedAtUtc).toLocaleString() },
                { id: "length", header: t("analytics.disconnects.sessionLength"), align: "right", sortValue: (s) => s.minutes, cell: (s) => formatDuration(s.minutes, t) },
                {
                  id: "end",
                  header: t("analytics.directory.ended"),
                  sortValue: (s) => s.endKind,
                  firstDirection: "asc",
                  cell: (s) => (
                    <span title={s.endReason ?? undefined} className={isDrop(s.endKind) ? "text-rose-400" : "text-slate-300"}>
                      {t(`analytics.disconnects.kind.${s.endKind}`)}
                    </span>
                  )
                },
                {
                  id: "rejoin",
                  header: t("analytics.disconnects.quickRejoins"),
                  align: "right",
                  sortValue: (s) => (s.quickRejoin ? 1 : 0),
                  cell: (s) => (s.quickRejoin ? <span className="text-amber-400">{t("analytics.disconnects.yes")}</span> : "—")
                },
                {
                  id: "proxy",
                  header: t("server.connections.sharedAddress"),
                  align: "right",
                  sortValue: (s) => (s.viaProxy ? 1 : 0),
                  cell: (s) => (s.viaProxy ? t("analytics.disconnects.yes") : "—")
                }
              ]}
            />
          </div>
        </>
      )}
    </Panel>
  );
}

function RangeSwitch({ value, onChange }: { value: number; onChange: (value: (typeof RANGES)[number]) => void }) {
  const { t } = useTranslation();
  return (
    <div className="flex rounded-md border border-red-950/40 overflow-hidden text-[11px]">
      {RANGES.map((range) => (
        <button
          key={range}
          type="button"
          onClick={() => onChange(range)}
          className={cn("px-2.5 py-1 transition-colors", value === range ? "bg-[#b8282e]/30 text-slate-100" : "text-slate-400 hover:text-slate-200")}
        >
          {t("analytics.players.days", { count: range })}
        </button>
      ))}
    </div>
  );
}

function Fact({ label, value, hint, tone }: { label: string; value: string; hint?: string; tone?: string }) {
  return (
    <div className="rounded-md border border-red-950/20 bg-slate-950/30 px-3 py-2">
      <div className="text-[10px] uppercase tracking-wider text-slate-500">{label}</div>
      <div className={cn("font-mono text-base font-semibold text-slate-100", tone)}>{value}</div>
      {hint && <div className="text-[10px] text-slate-500 mt-0.5">{hint}</div>}
    </div>
  );
}
