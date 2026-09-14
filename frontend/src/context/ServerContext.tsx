import { createContext, useCallback, useContext, useEffect, useMemo, useState, type PropsWithChildren } from "react";
import { serverApi, type ServerRuntimeStatus, type ServerSummary } from "@/api/servers";
import { SELECTED_SERVER_STORAGE_KEY, SERVER_STATUS_INTERVAL_MS } from "@/lib/constants";

export interface ServerInstance extends ServerSummary {
  status: ServerRuntimeStatus;
}

interface ServerContextType {
  servers: ServerInstance[];
  selectedServer?: ServerInstance;
  loading: boolean;
  error: boolean;
  selectServer: (serverId: string) => void;
  updateServerStatus: (serverId: string, status: ServerRuntimeStatus) => void;
}

const ServerContext = createContext<ServerContextType | undefined>(undefined);
export function ServerProvider({ children }: PropsWithChildren) {
  const [servers, setServers] = useState<ServerInstance[]>([]);
  const [selectedServerId, setSelectedServerId] = useState<string>();
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(false);

  useEffect(() => {
    let active = true;
    void serverApi.list()
      .then((profiles) => {
        if (!active) return;
        const loaded = profiles.map((profile) => ({ ...profile, status: "unknown" as const }));
        const savedId = localStorage.getItem(SELECTED_SERVER_STORAGE_KEY);
        const selected = loaded.find((server) => server.id === savedId) ?? loaded[0];
        setServers(loaded);
        setSelectedServerId(selected?.id);
        if (selected) localStorage.setItem(SELECTED_SERVER_STORAGE_KEY, selected.id);
        else localStorage.removeItem(SELECTED_SERVER_STORAGE_KEY);
        setError(false);
      })
      .catch(() => { if (active) setError(true); })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, []);

  const selectedServer = useMemo(
    () => servers.find((server) => server.id === selectedServerId),
    [servers, selectedServerId]
  );

  useEffect(() => {
    if (!selectedServerId) return;

    let active = true;
    const loadStatus = async () => {
      try {
        const response = await serverApi.status(selectedServerId);
        if (active) {
          setServers((current) => current.map((server) =>
            server.id === selectedServerId ? { ...server, status: response.status } : server
          ));
        }
      } catch {
        if (active) {
          setServers((current) => current.map((server) =>
            server.id === selectedServerId ? { ...server, status: "unknown" } : server
          ));
        }
      }
    };

    void loadStatus();
    const statusTimer = window.setInterval(() => void loadStatus(), SERVER_STATUS_INTERVAL_MS);
    return () => {
      active = false;
      window.clearInterval(statusTimer);
    };
  }, [selectedServerId]);

  const selectServer = useCallback((serverId: string) => {
    if (!servers.some((server) => server.id === serverId)) return;
    setSelectedServerId(serverId);
    localStorage.setItem(SELECTED_SERVER_STORAGE_KEY, serverId);
  }, [servers]);

  const updateServerStatus = useCallback((serverId: string, status: ServerRuntimeStatus) => {
    setServers((current) => current.map((server) => server.id === serverId ? { ...server, status } : server));
  }, []);

  return (
    <ServerContext.Provider value={{ servers, selectedServer, loading, error, selectServer, updateServerStatus }}>
      {children}
    </ServerContext.Provider>
  );
}

export function useServer() {
  const context = useContext(ServerContext);
  if (!context) throw new Error("useServer must be used within a ServerProvider");
  return context;
}
