import { Archive, Download, Edit, Edit3, FolderPlus, Move, Package, Trash2, Upload } from "lucide-react";
import { useTranslation } from "react-i18next";
import type { FileItem } from "@/components/file-manager/types";
import Button from "@/components/ui/Button";

export default function FileManagerActions({
  selectedItems,
  onUpload,
  onMkdir,
  onDownload,
  onEdit,
  onRename,
  onMove,
  onUnpack,
  onPack,
  onDelete
}: {
  selectedItems: FileItem[];
  onUpload: () => void;
  onMkdir: () => void;
  onDownload: () => void;
  onEdit: () => void;
  onRename: () => void;
  onMove: () => void;
  onUnpack: () => void;
  onPack: () => void;
  onDelete: () => void;
}) {
  const { t } = useTranslation();
  const secondaryClass = "flex items-center gap-1.5 bg-slate-950/30 hover:bg-red-950/15 text-slate-200 border border-red-950/45 px-3 py-1.5 rounded-md transition-all duration-200 disabled:opacity-40 disabled:cursor-not-allowed shrink-0 cursor-pointer font-semibold";

  const selectedItem = selectedItems.length === 1 ? selectedItems[0] : null;
  const hasSelection = selectedItems.length > 0;

  return (
    <div className="flex items-center gap-1.5 overflow-x-auto text-xs pb-1 select-none">
      <Button
        variant="primary"
        onClick={onUpload}
        className="h-[32px] text-xs font-bold uppercase tracking-wider shrink-0 flex items-center gap-1.5"
      >
        <Upload size={14} />
        <span>{t("fileManager.upload")}</span>
      </Button>

      <button onClick={onMkdir} className={secondaryClass}>
        <FolderPlus size={14} className="text-[#ffd8a0]" />
        <span>{t("fileManager.newFolder")}</span>
      </button>

      <button onClick={onDownload} disabled={!selectedItem || selectedItem.isFolder} className={secondaryClass}>
        <Download size={14} className="text-[#e04444]" />
        <span>{t("fileManager.download")}</span>
      </button>

      <button onClick={onEdit} disabled={!selectedItem || selectedItem.isFolder} className={secondaryClass}>
        <Edit size={14} className="text-[#e04444]" />
        <span>{t("fileManager.edit")}</span>
      </button>

      <button onClick={onRename} disabled={!selectedItem} className={secondaryClass}>
        <Edit3 size={14} className="text-[#e04444]" />
        <span>{t("fileManager.rename")}</span>
      </button>

      <button onClick={onMove} disabled={!hasSelection} className={secondaryClass}>
        <Move size={14} className="text-[#e04444]" />
        <span>{t("fileManager.move")}</span>
      </button>

      <button onClick={onUnpack} disabled={!(selectedItems.length > 0 && selectedItems.every(item => item.name.endsWith(".zip")))} className={secondaryClass}>
        <Archive size={14} className="text-[#e04444]" />
        <span>{t("fileManager.unpack")}</span>
      </button>

      <button onClick={onPack} disabled={!hasSelection} className={secondaryClass}>
        <Package size={14} className="text-[#e04444]" />
        <span>{t("fileManager.pack")}</span>
      </button>

      <Button
        variant="danger"
        onClick={onDelete}
        disabled={!hasSelection}
        className="h-[32px] text-xs font-bold uppercase tracking-wider shrink-0 ml-auto flex items-center gap-1.5"
      >
        <Trash2 size={14} />
        <span>{t("fileManager.delete")}</span>
      </Button>
    </div>
  );
}
