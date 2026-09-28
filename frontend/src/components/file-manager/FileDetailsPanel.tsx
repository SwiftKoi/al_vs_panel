import { useEffect, useState } from "react";
import { Calculator, Folder, Info, Loader2 } from "lucide-react";
import { useTranslation } from "react-i18next";
import Panel from "@/components/ui/Panel";
import FileIcon from "@/components/file-manager/FileIcon";
import type { FileItem } from "@/components/file-manager/types";
import { filesApi } from "@/api/files";
import { formatFileSize, isPreviewableImage } from "@/lib/fileManager";

type Measured = { bytes: number; files: number; folders: number; complete: boolean };

export default function FileDetailsPanel({
  selectedItems,
  currentPath,
  serverId,
  rootId,
  previewUrl,
  onPreview
}: {
  selectedItems: FileItem[];
  currentPath: string;
  serverId?: string;
  rootId?: string;
  previewUrl?: (item: FileItem) => string;
  onPreview?: (item: FileItem) => void;
}) {
  const { t } = useTranslation();
  const selectedItem = selectedItems.length === 1 ? selectedItems[0] : null;
  const [measured, setMeasured] = useState<Record<string, Measured | "loading" | "error">>({});

  // Measurements belong to one folder view; start fresh when it changes.
  useEffect(() => setMeasured({}), [serverId, rootId, currentPath]);

  const pathOf = (item: FileItem) => (currentPath ? `${currentPath}/${item.name}` : item.name);
  const measure = (items: FileItem[]) => {
    if (!serverId || !rootId) return;
    items.filter((item) => item.isFolder).forEach((item) => {
      const path = pathOf(item);
      setMeasured((prev) => ({ ...prev, [path]: "loading" }));
      filesApi.size(serverId, rootId, path)
        .then((result) => setMeasured((prev) => ({ ...prev, [path]: result })))
        .catch(() => setMeasured((prev) => ({ ...prev, [path]: "error" })));
    });
  };

  const folders = selectedItems.filter((item) => item.isFolder);
  const results = folders.map((item) => measured[pathOf(item)]);
  const measuring = results.some((r) => r === "loading");
  const allMeasured = folders.length > 0 && results.every((r) => r && r !== "loading" && r !== "error");
  const fileBytes = selectedItems.filter((item) => !item.isFolder).reduce((sum, item) => sum + item.sizeBytes, 0);
  const folderBytes = results.reduce((sum, r) => sum + (r && typeof r === "object" ? r.bytes : 0), 0);
  const incomplete = results.some((r) => r && typeof r === "object" && !r.complete);

  const sizeBlock = (
    <div>
      <span className="text-slate-500 block text-[10px] uppercase">{t("fileManager.detailsPanel.size")}</span>
      {folders.length === 0 || allMeasured ? (
        <span className="text-slate-300">
          {formatFileSize(fileBytes + folderBytes)}
          {incomplete && <span className="text-amber-300"> {t("fileManager.detailsPanel.atLeast")}</span>}
        </span>
      ) : (
        <span className="flex items-center gap-2 text-slate-400">
          {fileBytes > 0 && <span>{formatFileSize(fileBytes)} + </span>}
          <button
            type="button"
            onClick={() => measure(folders)}
            disabled={measuring}
            className="flex items-center gap-1 rounded border border-red-950/45 px-2 py-0.5 text-[11px] text-slate-200 hover:bg-red-950/15 disabled:opacity-50 cursor-pointer"
          >
            {measuring ? <Loader2 size={11} className="animate-spin" /> : <Calculator size={11} />}
            {t("fileManager.detailsPanel.calculate")}
          </button>
        </span>
      )}
      {selectedItem?.isFolder && allMeasured && typeof results[0] === "object" && (
        <span className="block text-[10px] text-slate-500">
          {t("fileManager.detailsPanel.contents", { files: results[0].files, folders: results[0].folders })}
        </span>
      )}
      {results.includes("error") && <span className="block text-[10px] text-rose-300">{t("fileManager.detailsPanel.sizeFailed")}</span>}
    </div>
  );

  return (
    <Panel className="p-5 lg:col-span-1 space-y-4">
      <h2 className="text-sm font-bold font-serif text-slate-100 glow-text tracking-wide border-b border-red-950/20 pb-2">{t("fileManager.details")}</h2>
      {selectedItems.length > 1 ? (
        <div className="space-y-3 text-xs">
          <div className="flex items-start gap-3">
            <Folder size={24} className="text-[#e04444] shrink-0" />
            <div className="min-w-0">
              <div className="font-semibold text-slate-200 truncate">{t("fileManager.detailsPanel.selected", { count: selectedItems.length })}</div>
              <div className="text-slate-400 text-[11px]">
                {t("fileManager.detailsPanel.breakdown", { files: selectedItems.length - folders.length, folders: folders.length })}
              </div>
            </div>
          </div>
          <div className="bg-slate-950/60 p-3 rounded border border-red-950/20 font-mono">{sizeBlock}</div>
          <div className="bg-slate-950/60 p-3 rounded border border-red-950/20 space-y-2 font-mono max-h-[160px] overflow-y-auto">
            {selectedItems.map((item) => (
              <div key={item.name} className="text-slate-300 truncate text-[11px] flex items-center gap-1.5">
                <FileIcon name={item.name} isFolder={item.isFolder} size={12} />
                <span>{item.name}</span>
              </div>
            ))}
          </div>
        </div>
      ) : selectedItem ? (
        <div className="space-y-3 text-xs">
          <div className="flex items-start gap-3">
            <FileIcon name={selectedItem.name} isFolder={selectedItem.isFolder} size={24} />
            <div className="min-w-0">
              <div className="font-semibold text-slate-200 truncate">{selectedItem.name}</div>
              <div className="text-slate-400 text-[11px]">{selectedItem.isFolder ? t("fileManager.detailsPanel.folder") : t("fileManager.detailsPanel.file")}</div>
            </div>
          </div>
          {previewUrl && !selectedItem.isFolder && isPreviewableImage(selectedItem.name) && (
            <button
              type="button"
              onClick={() => onPreview?.(selectedItem)}
              className="flex w-full items-center justify-center rounded border border-red-950/20 bg-slate-950/60 p-2 cursor-zoom-in"
              title={t("fileManager.preview.open")}
            >
              <img
                key={selectedItem.name}
                src={previewUrl(selectedItem)}
                alt={selectedItem.name}
                loading="lazy"
                className="max-h-40 max-w-full object-contain [image-rendering:pixelated]"
              />
            </button>
          )}
          <div className="bg-slate-950/60 p-3 rounded border border-red-950/20 space-y-2 font-mono">
            {sizeBlock}
            <div><span className="text-slate-500 block text-[10px] uppercase">{t("fileManager.detailsPanel.modified")}</span><span className="text-slate-300 text-[11px]">{selectedItem.modified}</span></div>
            <div><span className="text-slate-500 block text-[10px] uppercase">{t("fileManager.detailsPanel.path")}</span><span className="text-slate-400 text-[10px] break-all">{pathOf(selectedItem)}</span></div>
          </div>
        </div>
      ) : <div className="text-xs text-slate-400 leading-relaxed font-medium">{t("fileManager.selectItem")}</div>}
      <div className="pt-2 border-t border-red-950/20 text-[11px] text-slate-400 flex items-start gap-2">
        <Info size={14} className="text-[#ffd8a0] shrink-0 mt-0.5" />
        <span>{t("fileManager.sshNotice")}</span>
      </div>
    </Panel>
  );
}
