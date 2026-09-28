import { useState } from "react";
import { CheckCircle2, ChevronDown, ChevronUp, FolderOpen, History, Loader2, X, XCircle } from "lucide-react";
import { useTranslation } from "react-i18next";
import type { TrackedOperationDto } from "@/api/files";
import { formatDuration, formatFileSize } from "@/lib/fileManager";
import { cn } from "@/lib/cn";

/** Running zip/unzip jobs with progress and cancel, plus a collapsible history of finished ones. */
export default function BackgroundTaskBar({
  operations,
  onCancel,
  onOpenFolder
}: {
  operations: TrackedOperationDto[];
  onCancel: (taskId: string) => void;
  onOpenFolder: (operation: TrackedOperationDto) => void;
}) {
  const { t } = useTranslation();
  const [showHistory, setShowHistory] = useState(false);

  const running = operations.filter((op) => op.status === "Running");
  const finished = operations.filter((op) => op.status !== "Running");
  if (operations.length === 0) return null;

  return (
    <div className="rounded-lg border border-red-500/30 bg-red-950/20 text-xs text-red-100">
      {running.map((op) => <RunningRow key={op.taskId} op={op} onCancel={() => onCancel(op.taskId)} />)}

      {finished.length > 0 && (
        <div className={cn(running.length > 0 && "border-t border-red-500/20")}>
          <button
            type="button"
            onClick={() => setShowHistory((value) => !value)}
            className="flex w-full items-center gap-2 px-4 py-2 text-slate-300 hover:text-slate-100 cursor-pointer"
          >
            <History size={14} className="text-[#e04444]" />
            <span>{t("fileManager.jobs.history", { count: finished.length })}</span>
            {showHistory ? <ChevronUp size={14} className="ml-auto" /> : <ChevronDown size={14} className="ml-auto" />}
          </button>
          {showHistory && (
            <ul className="divide-y divide-red-500/10 border-t border-red-500/10">
              {finished.map((op) => (
                <li key={op.taskId} className="flex items-start gap-2 px-4 py-2">
                  {op.status === "Completed" ? (
                    <CheckCircle2 size={14} className="mt-0.5 shrink-0 text-emerald-400" />
                  ) : (
                    <XCircle size={14} className={cn("mt-0.5 shrink-0", op.status === "Failed" ? "text-rose-400" : "text-slate-500")} />
                  )}
                  <div className="min-w-0 flex-1">
                    <div className="truncate text-slate-200" title={op.description}>{op.description}</div>
                    <div className={cn("text-[11px]", op.status === "Failed" ? "text-rose-300" : "text-slate-400")}>
                      {op.status === "Failed"
                        ? op.errorMessage
                        : `${t(`fileManager.jobs.status.${op.status}`)} · ${new Date(op.completed ?? op.created).toLocaleTimeString()}`
                          + (op.completed ? ` · ${formatDuration((Date.parse(op.completed) - Date.parse(op.created)) / 1000)}` : "")}
                    </div>
                  </div>
                  {op.status === "Completed" && op.target && (
                    <button
                      type="button"
                      onClick={() => onOpenFolder(op)}
                      className="flex shrink-0 items-center gap-1 text-slate-300 hover:text-white cursor-pointer"
                    >
                      <FolderOpen size={13} />
                      {t("fileManager.jobs.openFolder")}
                    </button>
                  )}
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
    </div>
  );
}

function RunningRow({ op, onCancel }: { op: TrackedOperationDto; onCancel: () => void }) {
  const { t } = useTranslation();
  const progress = op.progress;
  const fraction = progress && progress.totalBytes > 0
    ? progress.bytes / progress.totalBytes
    : progress && progress.totalItems > 0 ? progress.items / progress.totalItems : null;
  const elapsed = (Date.now() - Date.parse(op.created)) / 1000;

  return (
    <div className="px-4 py-3">
      <div className="flex items-center gap-2">
        <Loader2 size={14} className="animate-spin text-[#e04444] shrink-0" />
        <span className="font-medium truncate" title={op.description}>{op.description}</span>
        <button
          type="button"
          onClick={onCancel}
          className="ml-auto flex shrink-0 items-center gap-1 text-slate-300 hover:text-rose-300 cursor-pointer"
        >
          <X size={13} />
          {t("fileManager.jobs.cancel")}
        </button>
      </div>
      <div className="mt-2 h-1.5 rounded-full bg-slate-800 overflow-hidden">
        <div
          className={cn("h-full rounded-full bg-[#e04444] transition-[width] duration-500", fraction === null && "w-1/3 animate-pulse")}
          style={fraction !== null ? { width: `${Math.min(100, fraction * 100)}%` } : undefined}
        />
      </div>
      <div className="mt-1 text-[11px] font-mono text-slate-400">
        {progress
          ? `${progress.items} / ${progress.totalItems} ${t("fileManager.jobs.files")} · ${formatFileSize(progress.bytes)} / ${formatFileSize(progress.totalBytes)}`
          : t("fileManager.jobs.preparing")}
        {` · ${formatDuration(elapsed)}`}
        {fraction !== null && fraction > 0.02 && fraction < 1 && ` · ~${formatDuration(elapsed / fraction - elapsed)} ${t("fileManager.jobs.left")}`}
      </div>
    </div>
  );
}
