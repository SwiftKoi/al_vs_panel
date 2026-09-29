import { useEffect, useState } from "react";
import { useSearchParams } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { AlertTriangle, Database, Loader2 } from "lucide-react";
import PageHeader from "@/components/layout/PageHeader";
import Panel from "@/components/ui/Panel";
import SearchTab from "@/components/server-logs/SearchTab";
import ProblemsTab from "@/components/server-logs/ProblemsTab";
import PlayersTab from "@/components/server-logs/PlayersTab";
import StartupsTab from "@/components/server-logs/StartupsTab";
import { serverLogsApi, type LogIndexStatus } from "@/api/serverLogs";
import { useServer } from "@/context/ServerContext";
import { formatBytes } from "@/lib/format";
import { cn } from "@/lib/cn";

const TABS = ["search", "problems", "players", "startups"] as const;
type Tab = (typeof TABS)[number];
const STATUS_REFRESH_MS = 30_000;

function IndexStatus({ status }: { status: LogIndexStatus }) {
  const { t, i18n } = useTranslation();
  const number = new Intl.NumberFormat(i18n.language, { notation: "compact" });
  const date = new Intl.DateTimeFormat(i18n.language, { dateStyle: "short" });
  const backfilling = status.totalBytes > 0 && status.indexedBytes < status.totalBytes * 0.98;
  const percent = status.totalBytes > 0 ? Math.floor((status.indexedBytes / status.totalBytes) * 100) : 0;

  return (
    <div className="flex items-center gap-2 text-xs text-slate-300" title={status.lastError ?? undefined}>
      {status.lastError ? <AlertTriangle size={14} className="text-amber-300" /> : backfilling ? <Loader2 size={14} className="animate-spin text-[#e04444]" /> : <Database size={14} />}
      <span>
        {backfilling
          ? t("serverLogs.status.backfilling", { percent })
          : t("serverLogs.status.ready", {
              entries: number.format(status.entries),
              since: status.oldestEntry ? date.format(new Date(status.oldestEntry)) : "—",
              size: formatBytes(status.databaseBytes)
            })}
      </span>
    </div>
  );
}

export default function ServerLogsPage() {
  const { t } = useTranslation();
  const { selectedServer, loading, error } = useServer();
  const [params, setParams] = useSearchParams();
  const requested = params.get("tab");
  const tab: Tab = TABS.includes(requested as Tab) ? (requested as Tab) : "search";
  const serverId = selectedServer?.id;
  const [status, setStatus] = useState<LogIndexStatus>();

  useEffect(() => {
    if (!serverId) return;
    let active = true;
    setStatus(undefined);
    const run = () => {
      if (document.visibilityState !== "visible") return;
      serverLogsApi.status(serverId).then((result) => { if (active) setStatus(result); }).catch(() => undefined);
    };
    run();
    const timer = window.setInterval(run, STATUS_REFRESH_MS);
    return () => { active = false; window.clearInterval(timer); };
  }, [serverId]);

  if (loading) return <Panel className="p-6 text-sm text-slate-300">{t("servers.loading")}</Panel>;
  if (error || !selectedServer) {
    return <Panel className="p-6 text-sm text-slate-300">{error ? t("servers.loadFailed") : t("servers.noneConfigured")}</Panel>;
  }

  return (
    <div className="space-y-6 max-w-none">
      <PageHeader title={t("serverLogs.title")} description={t("serverLogs.description")} actions={status?.configured ? <IndexStatus status={status} /> : undefined} />

      {status && !status.configured ? (
        <Panel className="p-6 text-sm text-slate-300">{t("serverLogs.notConfigured")}</Panel>
      ) : (
        <>
          <div role="tablist" className="scroll-fade-x flex gap-1 border-b border-red-950/30 overflow-x-auto">
            {TABS.map((id) => (
              <button
                key={id}
                type="button"
                role="tab"
                aria-selected={tab === id}
                onClick={() => setParams((current) => { const next = new URLSearchParams(current); next.set("tab", id); return next; }, { replace: true })}
                className={cn(
                  "px-4 py-2 text-xs font-semibold whitespace-nowrap border-b-2 -mb-px transition-colors cursor-pointer",
                  tab === id ? "border-[#e04444] text-slate-100" : "border-transparent text-slate-300 hover:text-white"
                )}
              >
                {t(`serverLogs.tabs.${id}`)}
              </button>
            ))}
          </div>
          {tab === "search" && <SearchTab key={selectedServer.id} serverId={selectedServer.id} />}
          {tab === "problems" && <ProblemsTab key={selectedServer.id} serverId={selectedServer.id} />}
          {tab === "players" && <PlayersTab key={selectedServer.id} serverId={selectedServer.id} />}
          {tab === "startups" && <StartupsTab key={selectedServer.id} serverId={selectedServer.id} />}
        </>
      )}
    </div>
  );
}
