import { FileText, Folder, Info } from "lucide-react";
import { useTranslation } from "react-i18next";
import Panel from "@/components/ui/Panel";
import type { FileItem } from "@/components/file-manager/types";

export default function FileDetailsPanel({ selectedItems, currentPath }: { selectedItems: FileItem[]; currentPath: string }) {
  const { t } = useTranslation();
  const selectedItem = selectedItems.length === 1 ? selectedItems[0] : null;

  return (
    <Panel className="p-5 lg:col-span-1 space-y-4">
      <h2 className="text-sm font-bold font-serif text-slate-100 glow-text tracking-wide border-b border-red-950/20 pb-2">{t("fileManager.details")}</h2>
      {selectedItems.length > 1 ? (
        <div className="space-y-3 text-xs">
          <div className="flex items-start gap-3">
            <Folder size={24} className="text-[#e04444] shrink-0" />
            <div className="min-w-0">
              <div className="font-semibold text-slate-200 truncate">{selectedItems.length} items selected</div>
              <div className="text-slate-400 text-[11px]">Multiple Selection</div>
            </div>
          </div>
          <div className="bg-slate-950/60 p-3 rounded border border-red-950/20 space-y-2 font-mono max-h-[160px] overflow-y-auto">
            {selectedItems.map((item, idx) => (
              <div key={idx} className="text-slate-300 truncate text-[11px] flex items-center gap-1.5">
                {item.isFolder ? <Folder size={12} className="text-[#ffd8a0] shrink-0" /> : <FileText size={12} className="text-[#e04444] shrink-0" />}
                <span>{item.name}</span>
              </div>
            ))}
          </div>
        </div>
      ) : selectedItem ? (
        <div className="space-y-3 text-xs">
          <div className="flex items-start gap-3">
            {selectedItem.isFolder ? <Folder size={24} className="text-[#ffd8a0] shrink-0" /> : <FileText size={24} className="text-[#e04444] shrink-0" />}
            <div className="min-w-0">
              <div className="font-semibold text-slate-200 truncate">{selectedItem.name}</div>
              <div className="text-slate-400 text-[11px]">{selectedItem.isFolder ? "Folder" : "File"}</div>
            </div>
          </div>
          <div className="bg-slate-950/60 p-3 rounded border border-red-950/20 space-y-2 font-mono">
            <div><span className="text-slate-500 block text-[10px] uppercase">Size</span><span className="text-slate-300">{selectedItem.size}</span></div>
            <div><span className="text-slate-500 block text-[10px] uppercase">Last Modified</span><span className="text-slate-300 text-[11px]">{selectedItem.modified}</span></div>
            <div><span className="text-slate-500 block text-[10px] uppercase">Full Path</span><span className="text-slate-400 text-[10px] break-all">{currentPath ? `${currentPath}/${selectedItem.name}` : selectedItem.name}</span></div>
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
