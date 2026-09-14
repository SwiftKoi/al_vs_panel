import { useTranslation } from "react-i18next";
import PageHeader from "@/components/layout/PageHeader";
import Panel from "@/components/ui/Panel";
import ServerConsole from "@/components/server/ServerConsole";
import ServerControls from "@/components/server/ServerControls";
import ServerMetrics from "@/components/server/ServerMetrics";
import ServerStatusBadge from "@/components/server/ServerStatusBadge";
import { useServerRuntime } from "@/hooks/useServerRuntime";

export default function ServerPage() {
  const { t } = useTranslation();
  const {
    selectedServer,
    loading,
    serverListError,
    status,
    metrics,
    pendingAction,
    operationError,
    setOperationError,
    runLifecycle
  } = useServerRuntime();

  if (loading) {
    return <Panel className="p-6 text-sm text-slate-400">{t("servers.loading")}</Panel>;
  }

  if (serverListError || !selectedServer) {
    return (
      <Panel className="p-6 text-sm text-slate-400">
        {serverListError ? t("servers.loadFailed") : t("servers.noneConfigured")}
      </Panel>
    );
  }

  return (
    <div className="space-y-6 max-w-none">
      <PageHeader
        title={t("server.title")}
        description={t("server.description")}
        actions={
          <div className="flex items-center gap-2">
            <span className="text-xs text-slate-400 font-medium">{t("server.statusLabel")}</span>
            <ServerStatusBadge status={status} />
          </div>
        }
      />

      {operationError && (
        <div className="rounded border border-rose-500/30 bg-rose-500/10 px-4 py-3 text-sm text-rose-300">
          {operationError}
        </div>
      )}

      <ServerConsole
        serverId={selectedServer.id}
        status={status}
        onError={setOperationError}
      />
      <ServerControls
        status={status}
        pendingAction={pendingAction}
        onAction={(action) => void runLifecycle(action)}
      />
      <ServerMetrics metrics={metrics} />
    </div>
  );
}
