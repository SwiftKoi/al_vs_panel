import { useTranslation } from "react-i18next";
import { useSearchParams } from "react-router-dom";
import { cn } from "@/lib/cn";
import PageHeader from "@/components/layout/PageHeader";
import Panel from "@/components/ui/Panel";
import ConnectionQuality from "@/components/analytics/ConnectionQuality";
import ServerHealth from "@/components/analytics/ServerHealth";
import DisconnectReport from "@/components/analytics/DisconnectReport";
import { ActivityHeatmap, PlayerDirectory } from "@/components/analytics/PlayerDirectory";
import PlayerActivity from "@/components/analytics/PlayerActivity";
import ServerConnections from "@/components/server/ServerConnections";
import ServerStatusBadge from "@/components/server/ServerStatusBadge";
import { useServer } from "@/context/ServerContext";

const TABS = ["live", "quality", "health", "players", "disconnects"] as const;
type AnalyticsTab = (typeof TABS)[number];

export default function AnalyticsPage() {
  const { t } = useTranslation();
  const { selectedServer, loading, error } = useServer();
  const [searchParams, setSearchParams] = useSearchParams();
  const requestedTab = searchParams.get("tab");
  const tab: AnalyticsTab = TABS.includes(requestedTab as AnalyticsTab) ? (requestedTab as AnalyticsTab) : "live";

  if (loading) {
    return <Panel className="p-6 text-sm text-slate-400">{t("servers.loading")}</Panel>;
  }

  if (error || !selectedServer) {
    return (
      <Panel className="p-6 text-sm text-slate-400">
        {error ? t("servers.loadFailed") : t("servers.noneConfigured")}
      </Panel>
    );
  }

  const online = selectedServer.status === "online";

  return (
    <div className="space-y-6 max-w-none">
      <PageHeader
        title={t("analytics.title")}
        description={t("analytics.description")}
        actions={
          <div className="flex items-center gap-2">
            <span className="text-xs text-slate-400 font-medium">{t("server.statusLabel")}</span>
            <ServerStatusBadge status={selectedServer.status} />
          </div>
        }
      />

      <div role="tablist" className="flex gap-1 border-b border-red-950/30 overflow-x-auto">
        {TABS.map((id) => (
          <button
            key={id}
            type="button"
            role="tab"
            aria-selected={tab === id}
            onClick={() => setSearchParams({ tab: id }, { replace: true })}
            className={cn(
              "px-4 py-2 text-xs font-semibold whitespace-nowrap border-b-2 -mb-px transition-colors",
              tab === id
                ? "border-[#e04444] text-slate-100"
                : "border-transparent text-slate-400 hover:text-slate-200"
            )}
          >
            {t(`analytics.tabs.${id}`)}
          </button>
        ))}
      </div>

      {tab === "live" && (online ? (
        <ServerConnections serverId={selectedServer.id} online />
      ) : (
        <Panel className="p-6 text-sm text-slate-400">{t("analytics.offline")}</Panel>
      ))}
      {tab === "quality" && <ConnectionQuality serverId={selectedServer.id} />}
      {tab === "health" && <ServerHealth serverId={selectedServer.id} />}
      {tab === "players" && (
        <div className="space-y-6">
          <PlayerActivity serverId={selectedServer.id} />
          <ActivityHeatmap serverId={selectedServer.id} />
          <PlayerDirectory serverId={selectedServer.id} />
        </div>
      )}
      {tab === "disconnects" && <DisconnectReport serverId={selectedServer.id} />}
    </div>
  );
}
