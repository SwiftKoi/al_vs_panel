import { useState } from "react";
import { AlertTriangle, Users } from "lucide-react";
import { useTranslation } from "react-i18next";
import Modal from "@/components/ui/Modal";

export interface LiveWarningRequest {
  paths: string[];
  /** Players connected right now, or null when the count could not be read. */
  players: number | null;
  resolve: (confirmed: boolean) => void;
}

/**
 * Confirmation shown before changing files the running game server uses (world saves,
 * mods, configs). Continuing requires ticking an explicit acknowledgement.
 */
export default function LiveServerWarning({ request, onDone }: { request: LiveWarningRequest | null; onDone: () => void }) {
  const { t } = useTranslation();
  const [acknowledged, setAcknowledged] = useState(false);

  const finish = (confirmed: boolean) => {
    request?.resolve(confirmed);
    setAcknowledged(false);
    onDone();
  };

  return (
    <Modal
      isOpen={request !== null}
      onClose={() => finish(false)}
      title={t("fileManager.live.title")}
      icon={<AlertTriangle size={20} className="text-amber-400" />}
      onConfirm={() => finish(true)}
      confirmDisabled={!acknowledged}
      confirmVariant="danger"
      confirmLabel={t("fileManager.live.continue")}
    >
      <div className="space-y-3 text-xs text-slate-300">
        <div className="flex items-center gap-2 rounded-md border border-amber-500/30 bg-amber-950/30 px-3 py-2 text-amber-100">
          <Users size={14} className="shrink-0" />
          {request?.players === null
            ? t("fileManager.live.onlineUnknown")
            : t("fileManager.live.online", { count: request?.players ?? 0 })}
        </div>
        <p>{t("fileManager.live.explain")}</p>
        <ul className="max-h-32 overflow-y-auto rounded border border-slate-700/50 bg-slate-950/40 px-3 py-2 font-mono text-slate-200">
          {request?.paths.map((path) => <li key={path} className="truncate">/{path}</li>)}
        </ul>
        <p className="text-slate-400">{t("fileManager.live.advice")}</p>
        <label className="flex items-start gap-2 cursor-pointer select-none text-slate-200">
          <input
            type="checkbox"
            checked={acknowledged}
            onChange={(e) => setAcknowledged(e.target.checked)}
            className="mt-0.5 accent-[#e04444]"
          />
          {t("fileManager.live.acknowledge")}
        </label>
      </div>
    </Modal>
  );
}
