import { Play, RotateCw, Square } from "lucide-react";
import { useTranslation } from "react-i18next";
import Panel from "@/components/ui/Panel";
import type { ServerLifecycleAction } from "@/api/servers";
import type { DisplayStatus } from "@/hooks/useServerRuntime";

export default function ServerControls({ status, pendingAction, onAction }: { status: DisplayStatus; pendingAction?: ServerLifecycleAction; onAction: (action: ServerLifecycleAction) => void }) {
  const { t } = useTranslation();
  const busy = pendingAction !== undefined;

  return (
    <Panel className="p-4">
      <div className="text-[10px] font-bold font-serif uppercase tracking-widest text-[#e04444] mb-3 select-none">{t("server.controls")}</div>
      <div className="flex flex-wrap gap-3">
        {/* Start Button */}
        <button
          onClick={() => onAction("start")}
          disabled={status !== "offline" || busy}
          className="flex items-center gap-2 bg-[#ffd8a0]/10 hover:bg-[#ffd8a0]/15 text-[#ffd8a0] border border-[#e07a3a]/30 hover:border-[#ffd8a0] text-xs font-bold uppercase tracking-wider px-5 py-2.5 rounded-md shadow-[0_0_8px_rgba(224,122,58,0.15)] transition-all disabled:opacity-40 disabled:cursor-not-allowed cursor-pointer"
        >
          <Play size={15} fill="currentColor" className="text-[#ffd8a0]" />
          <span className="glow-text">{t("server.start")}</span>
        </button>

        {/* Stop Button */}
        <button
          onClick={() => onAction("stop")}
          disabled={status !== "online" || busy}
          className="flex items-center gap-2 bg-[#5e1215] hover:bg-[#7a181c] text-white border border-red-900/40 text-xs font-bold uppercase tracking-wider px-5 py-2.5 rounded-md shadow-[0_0_8px_rgba(184,40,46,0.2)] transition-all disabled:opacity-40 disabled:cursor-not-allowed cursor-pointer"
        >
          <Square size={14} fill="currentColor" className="text-red-200" />
          <span>{t("server.stop")}</span>
        </button>

        {/* Restart Button */}
        <button
          onClick={() => onAction("restart")}
          disabled={status === "unknown" || busy}
          className="flex items-center gap-2 border border-red-950/45 bg-slate-950/30 hover:bg-red-950/15 hover:border-red-900/50 text-slate-300 hover:text-white text-xs font-bold uppercase tracking-wider px-5 py-2.5 rounded-md transition-all disabled:opacity-40 disabled:cursor-not-allowed cursor-pointer"
        >
          <RotateCw size={15} className={`text-[#e04444] ${pendingAction === "restart" ? "animate-spin" : ""}`} />
          <span>{t("server.restart")}</span>
        </button>
      </div>
    </Panel>
  );
}
