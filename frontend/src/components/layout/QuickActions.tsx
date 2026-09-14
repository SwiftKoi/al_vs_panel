import { useState } from "react";
import { FileText, Package, RotateCw, Settings } from "lucide-react";
import { useNavigate } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { cn } from "@/lib/cn";
import { useServer } from "@/context/ServerContext";
import { serverApi } from "@/api/servers";

const menuItemBaseClass = "flex items-center gap-2 rounded-md px-3 py-2 text-[14px] leading-5 font-normal whitespace-nowrap";
const quickActionClass = cn(menuItemBaseClass, "text-slate-300 hover:bg-red-950/10 hover:text-white hover:pl-3.5 transition-all duration-200 disabled:opacity-40 disabled:cursor-not-allowed w-full text-left cursor-pointer");

export default function QuickActions() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { selectedServer, updateServerStatus } = useServer();
  const [restarting, setRestarting] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);

  const handleRestart = async () => {
    if (!selectedServer || restarting) return;
    setRestarting(true);
    setActionError(null);
    try {
      const response = await serverApi.restart(selectedServer.id);
      updateServerStatus(selectedServer.id, response.status);
    } catch {
      setActionError(t("server.errors.restart"));
    } finally {
      setRestarting(false);
    }
  };

  const openFolder = (path: string) => {
    navigate(`/files?root=data&path=${encodeURIComponent(path)}`);
  };

  return (
    <div className="flex md:flex-col gap-1 overflow-x-auto md:overflow-x-visible">
      <div className="hidden md:block px-3 pb-1.5 text-[10px] font-bold font-serif uppercase tracking-widest text-[#e04444]">
        {t("navigation.quickActions")}
      </div>
      <button type="button" onClick={() => void handleRestart()} disabled={!selectedServer || restarting} className={quickActionClass} title={t("navigation.restartServer")}>
        <RotateCw size={16} className={restarting ? "animate-spin text-[#e04444]" : "text-[#e04444]"} />
        <span>{t("navigation.restartServer")}</span>
      </button>
      <button type="button" onClick={() => openFolder("Mods")} disabled={!selectedServer} className={quickActionClass}>
        <Package size={16} className="text-[#e04444]" />
        <span>{t("navigation.openModsFolder")}</span>
      </button>
      <button type="button" onClick={() => openFolder("Logs")} disabled={!selectedServer} className={quickActionClass}>
        <FileText size={16} className="text-[#e04444]" />
        <span>{t("navigation.openLogsFolder")}</span>
      </button>
      <button type="button" onClick={() => openFolder("ModConfig")} disabled={!selectedServer} className={quickActionClass}>
        <Settings size={16} className="text-[#e04444]" />
        <span>{t("navigation.openModConfigFolder")}</span>
      </button>
      {actionError && <div className="hidden md:block px-3 pt-1 text-[11px] text-rose-400">{actionError}</div>}
    </div>
  );
}
