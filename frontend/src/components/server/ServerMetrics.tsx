import { Cpu, HardDrive, MemoryStick } from "lucide-react";
import type { ReactNode } from "react";
import { useTranslation } from "react-i18next";
import Panel from "@/components/ui/Panel";
import type { ServerMetricsResponse } from "@/api/servers";
import { clampPercent, formatBytes } from "@/lib/format";

export default function ServerMetrics({ metrics }: { metrics?: ServerMetricsResponse }) {
  const { t } = useTranslation();
  const cpuPercent = metrics?.cpuPercent ?? 0;
  const memoryPercent = metrics?.memoryPercent ?? 0;
  const diskPercent = metrics?.diskPercent ?? 0;

  return (
    <div>
      <h2 className="text-[10px] font-bold font-serif uppercase tracking-widest text-[#e04444] mb-3 select-none">
        {t("server.metrics.title")}
      </h2>
      <div className="grid gap-4 sm:grid-cols-3">
        <MetricCard
          icon={<Cpu size={16} className="text-[#e04444] filter drop-shadow-[0_0_4px_rgba(224,68,68,0.4)]" />}
          label={t("server.metrics.cpu")}
          value={metrics ? `${metrics.cpuPercent.toFixed(1)}%` : t("server.metrics.unavailable")}
          percent={cpuPercent}
          barClass="bg-gradient-to-r from-[#981d22] to-[#b8282e] shadow-[0_0_8px_rgba(184,40,46,0.3)]"
        />
        <MetricCard
          icon={<MemoryStick size={16} className="text-[#e04444] filter drop-shadow-[0_0_4px_rgba(224,68,68,0.4)]" />}
          label={t("server.metrics.memory")}
          value={metrics ? `${metrics.memoryUsage} / ${metrics.memoryLimit}` : t("server.metrics.unavailable")}
          percent={memoryPercent}
          barClass="bg-gradient-to-r from-[#981d22] to-[#b8282e] shadow-[0_0_8px_rgba(184,40,46,0.3)]"
        />
        <MetricCard
          icon={<HardDrive size={16} className="text-[#e04444] filter drop-shadow-[0_0_4px_rgba(224,68,68,0.4)]" />}
          label={t("server.metrics.storage")}
          value={metrics ? `${formatBytes(metrics.diskUsedBytes)} / ${formatBytes(metrics.diskTotalBytes)}` : t("server.metrics.unavailable")}
          percent={diskPercent}
          barClass="bg-gradient-to-r from-[#981d22] to-[#b8282e] shadow-[0_0_8px_rgba(184,40,46,0.3)]"
        />
      </div>
    </div>
  );
}

function MetricCard({ icon, label, value, percent, barClass }: { icon: ReactNode; label: string; value: string; percent: number; barClass: string }) {
  return (
    <Panel className="p-4">
      <div className="flex items-center justify-between mb-3.5 gap-3">
        <div className="flex items-center gap-2 text-slate-300 text-xs font-semibold">{icon}<span>{label}</span></div>
        <span className="font-mono text-xs font-semibold text-slate-200 text-right">{value}</span>
      </div>
      <div className="w-full bg-slate-950/60 border border-red-950/15 rounded-full h-2 overflow-hidden shadow-inner">
        <div className={`${barClass} h-2 rounded-full transition-all duration-500`} style={{ width: `${clampPercent(percent)}%` }} />
      </div>
    </Panel>
  );
}
