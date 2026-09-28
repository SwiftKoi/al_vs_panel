import { useState } from "react";
import { CheckCircle2, ChevronDown, ChevronUp, Loader2, RotateCw, Upload, X, XCircle } from "lucide-react";
import { useTranslation } from "react-i18next";
import { cn } from "@/lib/cn";
import { formatDuration, formatFileSize } from "@/lib/fileManager";
import { isActiveUpload, useUploads, type UploadItem } from "@/context/UploadContext";

/** Floating panel (bottom-right) listing every upload with progress, speed and ETA. */
export default function UploadQueuePanel() {
  const { t } = useTranslation();
  const { uploads, cancel, cancelAll, retry, clearFinished } = useUploads();
  const [collapsed, setCollapsed] = useState(false);

  if (uploads.length === 0) return null;

  const active = uploads.filter(isActiveUpload);
  const failed = uploads.filter((item) => item.status === "failed").length;
  const totalBytes = active.reduce((sum, item) => sum + item.total, 0);
  const loadedBytes = active.reduce((sum, item) => sum + item.loaded, 0);
  const speed = active.reduce((sum, item) => sum + (item.status === "uploading" ? item.speed : 0), 0);
  const overall = totalBytes > 0 ? loadedBytes / totalBytes : 1;

  const title = active.length > 0
    ? t("fileManager.uploads.activeTitle", { count: active.length })
    : failed > 0
      ? t("fileManager.uploads.failedTitle", { count: failed })
      : t("fileManager.uploads.doneTitle");

  return (
    <div className="fixed bottom-4 right-4 z-50 w-[min(26rem,calc(100vw-2rem))] rounded-lg border border-red-950/50 bg-slate-950/95 shadow-2xl backdrop-blur-sm text-xs">
      <div className="flex items-center gap-2 px-3.5 py-2.5 border-b border-red-950/40">
        <Upload size={14} className="text-[#e04444] shrink-0" />
        <div className="min-w-0 flex-1">
          <div className="font-semibold text-slate-100 truncate">{title}</div>
          {active.length > 0 && (
            <div className="text-[11px] text-slate-400 font-mono">
              {formatFileSize(loadedBytes)} / {formatFileSize(totalBytes)}
              {speed > 0 && <> · {formatFileSize(speed)}/s · {formatDuration((totalBytes - loadedBytes) / speed)}</>}
            </div>
          )}
        </div>
        {active.length > 0 ? (
          <button type="button" onClick={cancelAll} className="text-slate-400 hover:text-rose-300 cursor-pointer px-1">
            {t("fileManager.uploads.cancelAll")}
          </button>
        ) : (
          <button type="button" onClick={clearFinished} className="text-slate-400 hover:text-slate-100 cursor-pointer px-1">
            {t("fileManager.uploads.clear")}
          </button>
        )}
        <button
          type="button"
          onClick={() => setCollapsed((value) => !value)}
          aria-label={collapsed ? t("fileManager.uploads.expand") : t("fileManager.uploads.collapse")}
          className="text-slate-400 hover:text-slate-100 cursor-pointer"
        >
          {collapsed ? <ChevronUp size={16} /> : <ChevronDown size={16} />}
        </button>
      </div>

      {active.length > 0 && (
        <div className="h-1 bg-slate-800">
          <div className="h-full bg-[#e04444] transition-[width] duration-300" style={{ width: `${overall * 100}%` }} />
        </div>
      )}

      {!collapsed && (
        <ul className="max-h-72 overflow-y-auto divide-y divide-slate-800/70">
          {uploads.map((item) => (
            <UploadRow key={item.id} item={item} onCancel={() => cancel(item.id)} onRetry={() => retry(item.id)} />
          ))}
        </ul>
      )}
    </div>
  );
}

function UploadRow({ item, onCancel, onRetry }: { item: UploadItem; onCancel: () => void; onRetry: () => void }) {
  const { t } = useTranslation();
  const percent = item.total > 0 ? Math.min(100, (item.loaded / item.total) * 100) : 0;
  const active = isActiveUpload(item);
  const destination = `/${item.folder}`;

  let detail: string;
  switch (item.status) {
    case "queued":
      detail = t("fileManager.uploads.queued");
      break;
    case "uploading":
      detail = `${percent.toFixed(0)}% · ${formatFileSize(item.loaded)} / ${formatFileSize(item.total)}`
        + (item.speed > 0 ? ` · ${formatFileSize(item.speed)}/s · ${formatDuration((item.total - item.loaded) / item.speed)}` : "");
      break;
    case "saving":
      detail = t("fileManager.uploads.saving");
      break;
    case "done": {
      const seconds = item.startedAt && item.finishedAt ? (item.finishedAt - item.startedAt) / 1000 : 0;
      detail = t("fileManager.uploads.doneIn", { size: formatFileSize(item.total), time: formatDuration(seconds) });
      break;
    }
    case "cancelled":
      detail = t("fileManager.uploads.cancelled");
      break;
    default:
      detail = item.error ?? t("fileManager.uploads.failed");
  }

  return (
    <li className="px-3.5 py-2.5">
      <div className="flex items-center gap-2">
        {item.status === "done" ? (
          <CheckCircle2 size={14} className="text-emerald-400 shrink-0" />
        ) : item.status === "failed" ? (
          <XCircle size={14} className="text-rose-400 shrink-0" />
        ) : item.status === "cancelled" ? (
          <XCircle size={14} className="text-slate-500 shrink-0" />
        ) : (
          <Loader2 size={14} className={cn("text-[#e04444] shrink-0", item.status !== "queued" && "animate-spin")} />
        )}
        <div className="min-w-0 flex-1">
          <div className="truncate font-mono text-slate-200" title={`${item.file.name} → ${destination}`}>
            {item.file.name}
          </div>
          <div
            className={cn("truncate text-[11px]", item.status === "failed" ? "text-rose-300" : "text-slate-400")}
            title={detail}
          >
            {detail}
          </div>
        </div>
        {active ? (
          <button type="button" onClick={onCancel} aria-label={t("fileManager.uploads.cancel")} className="text-slate-500 hover:text-rose-300 cursor-pointer">
            <X size={14} />
          </button>
        ) : (item.status === "failed" || item.status === "cancelled") ? (
          <button type="button" onClick={onRetry} aria-label={t("fileManager.uploads.retry")} className="text-slate-400 hover:text-slate-100 cursor-pointer">
            <RotateCw size={14} />
          </button>
        ) : null}
      </div>
      {(item.status === "uploading" || item.status === "saving") && (
        <div className="mt-1.5 h-1 rounded-full bg-slate-800 overflow-hidden">
          <div
            className={cn("h-full rounded-full bg-[#e04444] transition-[width] duration-300", item.status === "saving" && "animate-pulse")}
            style={{ width: `${percent}%` }}
          />
        </div>
      )}
    </li>
  );
}
