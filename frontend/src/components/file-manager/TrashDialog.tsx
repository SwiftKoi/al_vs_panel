import { useCallback, useEffect, useState } from "react";
import { FileText, Folder, Loader2, RotateCcw, Trash2 } from "lucide-react";
import { useTranslation } from "react-i18next";
import Modal from "@/components/ui/Modal";
import { filesApi, type TrashEntryDto } from "@/api/files";
import { formatFileSize } from "@/lib/fileManager";
import { useToast } from "@/context/ToastContext";

/** Lists the root's trash with restore / delete-forever per entry and "empty trash". */
export default function TrashDialog({
  isOpen,
  onClose,
  serverId,
  rootId,
  retentionDays,
  onRestore,
  onChanged
}: {
  isOpen: boolean;
  onClose: () => void;
  serverId: string;
  rootId: string;
  retentionDays: number;
  /** Performs the restore (the page adds the live-server check); resolves true when done. */
  onRestore: (entry: TrashEntryDto) => Promise<boolean>;
  onChanged: () => void;
}) {
  const { t } = useTranslation();
  const toast = useToast();
  const [entries, setEntries] = useState<TrashEntryDto[] | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [confirmEmpty, setConfirmEmpty] = useState(false);

  const load = useCallback(() => {
    filesApi.trash(serverId, rootId)
      .then(setEntries)
      .catch((err) => { toast.error(err instanceof Error ? err.message : String(err)); setEntries([]); });
  }, [serverId, rootId, toast]);

  useEffect(() => {
    if (!isOpen) return;
    setEntries(null);
    setConfirmEmpty(false);
    load();
  }, [isOpen, load]);

  const restore = async (entry: TrashEntryDto) => {
    setBusy(entry.id);
    try {
      if (await onRestore(entry)) {
        load();
        onChanged();
      }
    } finally {
      setBusy(null);
    }
  };

  const purge = (entry: TrashEntryDto) => {
    setBusy(entry.id);
    filesApi.purgeTrash(serverId, rootId, entry.id)
      .then(() => { toast.info(t("fileManager.trash.purged", { name: entry.name })); load(); })
      .catch((err) => toast.error(err instanceof Error ? err.message : String(err)))
      .finally(() => setBusy(null));
  };

  const emptyTrash = () => {
    setBusy("*");
    filesApi.emptyTrash(serverId, rootId)
      .then(() => { toast.info(t("fileManager.trash.emptied")); setConfirmEmpty(false); load(); })
      .catch((err) => toast.error(err instanceof Error ? err.message : String(err)))
      .finally(() => setBusy(null));
  };

  return (
    <Modal
      isOpen={isOpen}
      onClose={onClose}
      title={t("fileManager.trash.title")}
      icon={<Trash2 size={20} className="text-[#e04444]" />}
    >
      <div className="space-y-3 text-xs">
        <p className="text-slate-400">{t("fileManager.trash.explain", { days: retentionDays })}</p>

        {entries === null ? (
          <div className="flex justify-center py-6"><Loader2 size={20} className="animate-spin text-[#e04444]" /></div>
        ) : entries.length === 0 ? (
          <p className="py-6 text-center italic text-slate-500">{t("fileManager.trash.empty")}</p>
        ) : (
          <ul className="max-h-80 overflow-y-auto divide-y divide-slate-800/60 rounded border border-slate-700/40">
            {entries.map((entry) => (
              <li key={entry.id} className="flex items-center gap-2.5 px-3 py-2">
                {entry.isFolder
                  ? <Folder size={14} className="shrink-0 text-[#ffd8a0]" fill="currentColor" fillOpacity={0.2} />
                  : <FileText size={14} className="shrink-0 text-slate-400" />}
                <div className="min-w-0 flex-1">
                  <div className="truncate font-mono text-slate-200" title={`/${entry.originalPath}`}>/{entry.originalPath}</div>
                  <div className="text-[11px] text-slate-500">
                    {formatFileSize(entry.size)} · {t("fileManager.trash.deletedAt", { time: new Date(entry.deletedAt).toLocaleString() })}
                  </div>
                </div>
                {busy === entry.id ? (
                  <Loader2 size={14} className="animate-spin text-slate-400" />
                ) : (
                  <>
                    <button
                      type="button"
                      onClick={() => void restore(entry)}
                      disabled={busy !== null}
                      className="flex items-center gap-1 rounded border border-red-950/45 px-2 py-1 text-slate-200 hover:bg-red-950/15 disabled:opacity-40 cursor-pointer"
                    >
                      <RotateCcw size={12} />
                      {t("fileManager.trash.restore")}
                    </button>
                    <button
                      type="button"
                      onClick={() => purge(entry)}
                      disabled={busy !== null}
                      title={t("fileManager.trash.deleteForever")}
                      className="rounded p-1 text-slate-500 hover:text-rose-300 disabled:opacity-40 cursor-pointer"
                    >
                      <Trash2 size={13} />
                    </button>
                  </>
                )}
              </li>
            ))}
          </ul>
        )}

        {entries && entries.length > 0 && (
          <div className="flex items-center justify-end gap-2">
            {confirmEmpty ? (
              <>
                <span className="text-rose-300">{t("fileManager.trash.emptyConfirm", { count: entries.length })}</span>
                <button type="button" onClick={() => setConfirmEmpty(false)} className="text-slate-400 hover:text-slate-100 cursor-pointer">
                  {t("fileManager.modals.cancel")}
                </button>
                <button
                  type="button"
                  onClick={emptyTrash}
                  disabled={busy !== null}
                  className="rounded bg-rose-700 px-2.5 py-1 font-semibold text-white hover:bg-rose-600 disabled:opacity-40 cursor-pointer"
                >
                  {t("fileManager.trash.emptyNow")}
                </button>
              </>
            ) : (
              <button
                type="button"
                onClick={() => setConfirmEmpty(true)}
                className="text-rose-300 hover:text-rose-200 cursor-pointer"
              >
                {t("fileManager.trash.emptyAction")}
              </button>
            )}
          </div>
        )}
      </div>
    </Modal>
  );
}
