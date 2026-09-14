import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { serverApi, type ServerLifecycleAction, type ServerMetricsResponse, type ServerRuntimeStatus } from "@/api/servers";
import { useServer } from "@/context/ServerContext";
import { SERVER_METRICS_INTERVAL_MS } from "@/lib/constants";

export type DisplayStatus = ServerRuntimeStatus | "starting" | "stopping";

export function useServerRuntime() {
  const { t } = useTranslation();
  const { selectedServer, loading, error: serverListError, updateServerStatus } = useServer();
  const [status, setStatus] = useState<DisplayStatus>("unknown");
  const [metrics, setMetrics] = useState<ServerMetricsResponse>();
  const [pendingAction, setPendingAction] = useState<ServerLifecycleAction>();
  const [operationError, setOperationError] = useState<string>();

  useEffect(() => {
    if (!selectedServer) {
      setStatus("unknown");
      setMetrics(undefined);
      return;
    }

    if (!pendingAction) setStatus(selectedServer.status);
    setMetrics(undefined);
    setOperationError(undefined);
  }, [selectedServer?.id, selectedServer?.status, pendingAction]);

  useEffect(() => {
    if (!selectedServer || status !== "online") {
      setMetrics(undefined);
      return;
    }

    let active = true;
    const loadMetrics = () => {
      if (document.visibilityState !== "visible") return;
      void serverApi.metrics(selectedServer.id)
        .then((response) => { if (active) setMetrics(response); })
        .catch(() => { if (active) setMetrics(undefined); });
    };

    loadMetrics();
    const timer = window.setInterval(loadMetrics, SERVER_METRICS_INTERVAL_MS);
    return () => {
      active = false;
      window.clearInterval(timer);
    };
  }, [selectedServer?.id, status]);

  const runLifecycle = async (action: ServerLifecycleAction) => {
    if (!selectedServer || pendingAction) return;
    setPendingAction(action);
    setOperationError(undefined);
    setStatus(action === "stop" || action === "restart" ? "stopping" : "starting");

    try {
      const response = action === "start"
        ? await serverApi.start(selectedServer.id)
        : action === "stop"
          ? await serverApi.stop(selectedServer.id)
          : await serverApi.restart(selectedServer.id);
      setStatus(response.status);
      updateServerStatus(selectedServer.id, response.status);
    } catch {
      setOperationError(t(`server.errors.${action}`));
      try {
        const response = await serverApi.status(selectedServer.id);
        setStatus(response.status);
        updateServerStatus(selectedServer.id, response.status);
      } catch {
        setStatus("unknown");
        updateServerStatus(selectedServer.id, "unknown");
      }
    } finally {
      setPendingAction(undefined);
    }
  };

  return {
    selectedServer,
    loading,
    serverListError,
    status,
    metrics,
    pendingAction,
    operationError,
    setOperationError,
    runLifecycle
  };
}
