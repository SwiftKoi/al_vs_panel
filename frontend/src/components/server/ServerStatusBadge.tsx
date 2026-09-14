import { useTranslation } from "react-i18next";
import type { DisplayStatus } from "@/hooks/useServerRuntime";

export default function ServerStatusBadge({ status }: { status: DisplayStatus }) {
  const { t } = useTranslation();
  
  const colors = status === "online"
    ? "bg-[#ffd8a0]/10 text-[#ffd8a0] border-[#e07a3a]/30 shadow-[0_0_8px_rgba(224,122,58,0.15)]"
    : status === "offline"
      ? "bg-[#5e1215]/20 text-[#e04444] border-red-900/30 shadow-[0_0_8px_rgba(184,40,46,0.15)]"
      : "bg-[#ffd8a0]/5 text-[#e07a3a] border-[#e07a3a]/20 animate-pulse";

  const dotColor = status === "online" 
    ? "bg-[#ffd8a0] shadow-[0_0_6px_#ffd8a0] animate-pulse" 
    : status === "offline" 
      ? "bg-[#e04444] shadow-[0_0_6px_#e04444]" 
      : "bg-[#e07a3a] shadow-[0_0_4px_#e07a3a]";

  return (
    <span className={`flex items-center gap-1.5 rounded-full px-3 py-1 text-xs font-bold border select-none ${colors}`}>
      <span className={`h-1.5 w-1.5 rounded-full ${dotColor}`} />
      <span className={status === "online" ? "glow-text" : ""}>{t(`server.status.${status}`)}</span>
    </span>
  );
}
