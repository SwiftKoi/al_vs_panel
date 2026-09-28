import { Users } from "lucide-react";
import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import DataTable from "@/components/ui/DataTable";
import Panel from "@/components/ui/Panel";
import { serverApi, type ServerClientConnection } from "@/api/servers";
import { cn } from "@/lib/cn";
import { SERVER_CONNECTIONS_INTERVAL_MS } from "@/lib/constants";
import { formatBytes } from "@/lib/format";

type Level = "good" | "warn" | "bad";

const levelClass: Record<Level, string> = {
  good: "text-emerald-400",
  warn: "text-amber-400",
  bad: "text-rose-400"
};

const pingLevel = (ms: number): Level => (ms < 100 ? "good" : ms < 180 ? "warn" : "bad");
const jitterLevel = (ms: number): Level => (ms < 20 ? "good" : ms < 50 ? "warn" : "bad");
const lossLevel = (percent: number): Level => (percent < 1 ? "good" : percent < 4 ? "warn" : "bad");
const queueLevel = (bytes: number): Level => (bytes < 64_000 ? "good" : bytes < 512_000 ? "warn" : "bad");
const lastDataLevel = (ms: number): Level => (ms < 2_000 ? "good" : ms < 8_000 ? "warn" : "bad");

// A connection is stalling when the player has sent nothing for 5 s, or when
// data queued for them has grown on consecutive polls and is already large:
// the server is sending faster than their connection accepts it.
const STALL_RECEIVE_MS = 5_000;
const BACKLOG_BYTES = 64_000;
const BACKLOG_GROWTH_POLLS = 2;

type StallReason = "silent" | "backlog";

interface ConnectionTrend {
  sendQueueBytes: number;
  growthStreak: number;
}

function connectionKey(c: ServerClientConnection) {
  return `${c.remoteAddress}:${c.remotePort}`;
}

function formatAge(ms: number) {
  return ms < 1_000 ? `${ms} ms` : `${(ms / 1_000).toFixed(1)} s`;
}

export default function ServerConnections({ serverId, online }: { serverId: string; online: boolean }) {
  const { t } = useTranslation();
  const [connections, setConnections] = useState<ServerClientConnection[]>();
  const [failed, setFailed] = useState(false);
  const [updatedAt, setUpdatedAt] = useState<Date>();
  const trends = useRef(new Map<string, ConnectionTrend>());
  const [stalls, setStalls] = useState(new Map<string, StallReason>());

  useEffect(() => {
    setConnections(undefined);
    setFailed(false);
    if (!online) return;

    let active = true;
    const load = () => {
      if (document.visibilityState !== "visible") return;
      void serverApi.connections(serverId)
        .then((response) => {
          if (!active) return;
          const nextTrends = new Map<string, ConnectionTrend>();
          const nextStalls = new Map<string, StallReason>();
          for (const c of response.connections) {
            const key = connectionKey(c);
            const previous = trends.current.get(key);
            const growthStreak = previous && c.sendQueueBytes > previous.sendQueueBytes ? previous.growthStreak + 1 : 0;
            nextTrends.set(key, { sendQueueBytes: c.sendQueueBytes, growthStreak });
            if (c.lastReceiveMs >= STALL_RECEIVE_MS) nextStalls.set(key, "silent");
            else if (growthStreak >= BACKLOG_GROWTH_POLLS && c.sendQueueBytes >= BACKLOG_BYTES) nextStalls.set(key, "backlog");
          }
          trends.current = nextTrends;
          setStalls(nextStalls);
          setConnections(response.connections);
          setFailed(false);
          setUpdatedAt(new Date());
        })
        .catch(() => { if (active) setFailed(true); });
    };

    load();
    const timer = window.setInterval(load, SERVER_CONNECTIONS_INTERVAL_MS);
    return () => {
      active = false;
      window.clearInterval(timer);
    };
  }, [serverId, online]);

  if (!online) return null;

  const addressCounts = new Map<string, number>();
  connections?.forEach((c) => addressCounts.set(c.remoteAddress, (addressCounts.get(c.remoteAddress) ?? 0) + 1));

  return (
    <div>
      <div className="flex items-baseline justify-between gap-3 mb-3">
        <h2 className="flex items-center gap-2 text-[10px] font-bold font-serif uppercase tracking-widest text-[#e04444] select-none">
          <Users size={12} />
          {t("server.connections.title")}
          {connections && <span className="text-slate-400">({connections.length})</span>}
        </h2>
        {updatedAt && (
          <span className="text-[10px] text-slate-500">
            {t("server.connections.updated", { time: updatedAt.toLocaleTimeString() })}
          </span>
        )}
      </div>
      {stalls.size > 0 && connections && (
        <div className="mb-3 rounded border border-rose-500/30 bg-rose-500/10 px-4 py-3 text-sm text-rose-300">
          <div className="font-semibold">{t("server.connections.stallBanner", { count: stalls.size })}</div>
          <div className="text-xs mt-1">
            {connections.filter((c) => stalls.has(connectionKey(c))).map((c) =>
              `${c.playerName ?? c.remoteAddress} — ${t(`server.connections.stall.${stalls.get(connectionKey(c))}`)}`
            ).join(" · ")}
          </div>
        </div>
      )}
      <Panel className="p-0 overflow-hidden">
        {(!connections || connections.length === 0) && (
          <p className="px-4 py-3 text-xs text-slate-400 border-b border-red-950/20">{t("server.connections.hint")}</p>
        )}
        {failed && !connections ? (
          <p className="px-4 py-6 text-sm text-slate-400">{t("server.connections.unavailable")}</p>
        ) : !connections ? (
          <p className="px-4 py-6 text-sm text-slate-500">…</p>
        ) : connections.length === 0 ? (
          <p className="px-4 py-6 text-sm text-slate-400">{t("server.connections.empty")}</p>
        ) : (
          <DataTable
            rows={connections}
            rowKey={connectionKey}
            searchText={(c) => `${c.playerName ?? ""} ${c.remoteAddress}`}
            defaultSort={{ id: "player", direction: "asc" }}
            rowClassName={(c) => stalls.has(connectionKey(c)) && "bg-rose-500/10"}
            toolbar={<p className="text-xs text-slate-400 max-w-3xl">{t("server.connections.hint")}</p>}
            renderCard={(c) => (
              <div className="px-4 py-3 space-y-2">
                <div className="flex items-baseline justify-between gap-3">
                  <span className="font-semibold text-sm text-slate-200 truncate">
                    {c.playerName ?? <span className="text-slate-500">{t("server.connections.unknownPlayer")}</span>}
                    <StallBadge reason={stalls.get(connectionKey(c))} />
                  </span>
                  <span className={cn("font-mono text-sm", levelClass[pingLevel(c.rttMs)])}>{c.rttMs.toFixed(0)} ms</span>
                </div>
                <div className="font-mono text-[11px] text-slate-500 break-all">{c.remoteAddress}:{c.remotePort}</div>
                <div className="grid grid-cols-3 gap-2 text-[11px]">
                  <Stat label={t("server.connections.jitter")} value={`±${c.rttVarianceMs.toFixed(0)} ms`} level={jitterLevel(c.rttVarianceMs)} />
                  <Stat label={t("server.connections.loss")} value={`${c.retransmitPercent.toFixed(2)}%`} level={lossLevel(c.retransmitPercent)} />
                  <Stat label={t("server.connections.lastData")} value={formatAge(c.lastReceiveMs)} level={lastDataLevel(c.lastReceiveMs)} />
                  <Stat label={t("server.connections.queue")} value={formatBytes(c.sendQueueBytes)} level={queueLevel(c.sendQueueBytes)} />
                  <Stat label={t("server.connections.joins")} value={String(c.joinCount)} />
                  <Stat label={t("server.connections.traffic")} value={`↓${formatBytes(c.bytesSent)}`} />
                </div>
              </div>
            )}
            columns={[
              {
                id: "player",
                header: t("server.connections.player"),
                sortValue: (c) => c.playerName,
                firstDirection: "asc",
                cell: (c) => (
                  <span className="font-semibold text-slate-200">
                    {c.playerName ?? <span className="text-slate-500">{t("server.connections.unknownPlayer")}</span>}
                    <StallBadge reason={stalls.get(connectionKey(c))} />
                  </span>
                )
              },
              {
                id: "address",
                header: t("server.connections.address"),
                sortValue: (c) => c.remoteAddress,
                firstDirection: "asc",
                className: "font-mono text-slate-400",
                cell: (c) => (
                  <>
                    {c.remoteAddress}:{c.remotePort}
                    {(addressCounts.get(c.remoteAddress) ?? 0) > 1 && (
                      <span className="ml-2 font-sans text-[10px] text-amber-500/80">{t("server.connections.sharedAddress")}</span>
                    )}
                  </>
                )
              },
              {
                id: "ping",
                header: t("server.connections.ping"),
                align: "right",
                sortValue: (c) => c.rttMs,
                cell: (c) => (
                  <>
                    <span className={levelClass[pingLevel(c.rttMs)]}>{c.rttMs.toFixed(0)} ms</span>
                    <div className="text-[10px] text-slate-500">{t("server.connections.minPing", { value: c.minRttMs.toFixed(0) })}</div>
                  </>
                )
              },
              {
                id: "jitter",
                header: t("server.connections.jitter"),
                align: "right",
                sortValue: (c) => c.rttVarianceMs,
                cell: (c) => <span className={levelClass[jitterLevel(c.rttVarianceMs)]}>±{c.rttVarianceMs.toFixed(0)} ms</span>
              },
              {
                id: "loss",
                header: t("server.connections.loss"),
                align: "right",
                sortValue: (c) => c.retransmitPercent,
                cell: (c) => (
                  <>
                    <span className={levelClass[lossLevel(c.retransmitPercent)]}>{c.retransmitPercent.toFixed(2)}%</span>
                    <div className="text-[10px] text-slate-500">{c.retransmitsTotal}×</div>
                  </>
                )
              },
              {
                id: "queue",
                header: t("server.connections.queue"),
                align: "right",
                sortValue: (c) => c.sendQueueBytes,
                cell: (c) => <span className={levelClass[queueLevel(c.sendQueueBytes)]}>{formatBytes(c.sendQueueBytes)}</span>
              },
              {
                id: "lastData",
                header: t("server.connections.lastData"),
                align: "right",
                sortValue: (c) => c.lastReceiveMs,
                cell: (c) => <span className={levelClass[lastDataLevel(c.lastReceiveMs)]}>{formatAge(c.lastReceiveMs)}</span>
              },
              {
                id: "joins",
                header: t("server.connections.joins"),
                title: t("server.connections.joinsHint"),
                align: "right",
                sortValue: (c) => c.joinCount,
                cell: (c) => c.joinCount
              },
              {
                id: "traffic",
                header: t("server.connections.traffic"),
                align: "right",
                sortValue: (c) => c.bytesSent,
                cell: (c) => (
                  <span className="text-slate-400">
                    ↓{formatBytes(c.bytesSent)}
                    <div className="text-[10px] text-slate-500">↑{formatBytes(c.bytesReceived)}</div>
                  </span>
                )
              }
            ]}
          />
        )}
      </Panel>
    </div>
  );
}

function Stat({ label, value, level }: { label: string; value: string; level?: Level }) {
  return (
    <div>
      <div className="text-[10px] uppercase tracking-wider text-slate-500">{label}</div>
      <div className={cn("font-mono", level ? levelClass[level] : "text-slate-300")}>{value}</div>
    </div>
  );
}

function StallBadge({ reason }: { reason?: StallReason }) {
  const { t } = useTranslation();
  if (!reason) return null;
  return (
    <span className="ml-2 rounded border border-rose-500/40 px-1.5 py-0.5 text-[10px] font-semibold text-rose-300" title={t(`server.connections.stall.${reason}`)}>
      ⚠ {t("server.connections.stalling")}
    </span>
  );
}
