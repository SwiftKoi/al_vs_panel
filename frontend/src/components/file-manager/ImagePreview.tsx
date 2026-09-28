import { useEffect, useState } from "react";
import { ChevronLeft, ChevronRight, Download, Loader2, Maximize2, Minimize2, X } from "lucide-react";
import { useTranslation } from "react-i18next";
import { cn } from "@/lib/cn";
import { formatFileSize } from "@/lib/fileManager";
import type { FileItem } from "@/components/file-manager/types";

/**
 * Full-screen image viewer. ←/→ step through the other images in the folder, Space or
 * the button toggles fit-to-screen vs. actual size, Esc closes.
 */
export default function ImagePreview({
  images,
  index,
  urlFor,
  onIndexChange,
  onDownload,
  onClose
}: {
  images: FileItem[];
  index: number;
  urlFor: (item: FileItem) => string;
  onIndexChange: (index: number) => void;
  onDownload: (item: FileItem) => void;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const item = images[index];
  const [status, setStatus] = useState<"loading" | "ready" | "error">("loading");
  const [dimensions, setDimensions] = useState<{ width: number; height: number } | null>(null);
  const [actualSize, setActualSize] = useState(false);

  useEffect(() => {
    setStatus("loading");
    setDimensions(null);
  }, [item?.name]);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      // Capture phase: the file manager's own shortcuts must not react underneath.
      e.stopPropagation();
      if (e.key === "Escape") { e.preventDefault(); onClose(); }
      else if (e.key === "ArrowRight" && images.length > 1) { e.preventDefault(); onIndexChange((index + 1) % images.length); }
      else if (e.key === "ArrowLeft" && images.length > 1) { e.preventDefault(); onIndexChange((index - 1 + images.length) % images.length); }
      else if (e.key === " ") { e.preventDefault(); setActualSize((value) => !value); }
    };
    window.addEventListener("keydown", onKey, true);
    return () => window.removeEventListener("keydown", onKey, true);
  }, [images.length, index, onClose, onIndexChange]);

  if (!item) return null;

  const navButton = "absolute top-1/2 -translate-y-1/2 rounded-full bg-slate-950/70 p-2 text-slate-200 hover:bg-slate-800 hover:text-white cursor-pointer";

  return (
    <div className="fixed inset-0 z-50 flex flex-col bg-black/90 backdrop-blur-sm" role="dialog" aria-label={item.name}>
      <div className="flex items-center gap-3 px-4 py-3 text-xs text-slate-300">
        <div className="min-w-0 flex-1">
          <div className="truncate font-mono text-sm text-slate-100">{item.name}</div>
          <div className="text-slate-400">
            {formatFileSize(item.sizeBytes)}
            {dimensions && ` · ${dimensions.width} × ${dimensions.height}px`}
            {images.length > 1 && ` · ${index + 1} / ${images.length}`}
          </div>
        </div>
        <button
          type="button"
          onClick={() => setActualSize((value) => !value)}
          className="flex items-center gap-1.5 rounded-md border border-slate-600/60 px-2.5 py-1.5 hover:bg-slate-800 cursor-pointer"
          title={`${actualSize ? t("fileManager.preview.fit") : t("fileManager.preview.actual")} (Space)`}
        >
          {actualSize ? <Minimize2 size={14} /> : <Maximize2 size={14} />}
          <span className="hidden sm:inline">{actualSize ? t("fileManager.preview.fit") : t("fileManager.preview.actual")}</span>
        </button>
        <button
          type="button"
          onClick={() => onDownload(item)}
          className="flex items-center gap-1.5 rounded-md border border-slate-600/60 px-2.5 py-1.5 hover:bg-slate-800 cursor-pointer"
        >
          <Download size={14} />
          <span className="hidden sm:inline">{t("fileManager.download")}</span>
        </button>
        <button type="button" onClick={onClose} aria-label={t("fileManager.search.close")} className="rounded-md p-1.5 hover:bg-slate-800 cursor-pointer">
          <X size={18} />
        </button>
      </div>

      <div
        className={cn("relative flex-1 min-h-0", actualSize ? "overflow-auto" : "flex items-center justify-center overflow-hidden p-4")}
        onClick={(e) => { if (e.target === e.currentTarget) onClose(); }}
      >
        {status === "loading" && (
          <Loader2 size={32} className="absolute left-1/2 top-1/2 -translate-x-1/2 -translate-y-1/2 animate-spin text-[#e04444]" />
        )}
        {status === "error" && (
          <div className="absolute inset-0 flex items-center justify-center text-sm text-rose-300">{t("fileManager.preview.failed")}</div>
        )}
        <img
          key={item.name}
          src={urlFor(item)}
          alt={item.name}
          onLoad={(e) => {
            setStatus("ready");
            setDimensions({ width: e.currentTarget.naturalWidth, height: e.currentTarget.naturalHeight });
          }}
          onError={() => setStatus("error")}
          onClick={() => setActualSize((value) => !value)}
          className={cn(
            // Checkerboard shows transparency.
            "bg-[length:16px_16px] bg-[linear-gradient(45deg,#1e293b_25%,transparent_25%),linear-gradient(-45deg,#1e293b_25%,transparent_25%),linear-gradient(45deg,transparent_75%,#1e293b_75%),linear-gradient(-45deg,transparent_75%,#1e293b_75%)] bg-[position:0_0,0_8px,8px_-8px,-8px_0]",
            status !== "ready" && "invisible",
            actualSize ? "max-w-none cursor-zoom-out" : "max-h-full max-w-full object-contain cursor-zoom-in",
            // Small images are usually pixel-art textures: keep them crisp when enlarged.
            dimensions && dimensions.width <= 256 && dimensions.height <= 256 && "[image-rendering:pixelated]"
          )}
        />
        {images.length > 1 && (
          <>
            <button type="button" onClick={() => onIndexChange((index - 1 + images.length) % images.length)} className={cn(navButton, "left-3")} aria-label={t("fileManager.preview.previous")}>
              <ChevronLeft size={22} />
            </button>
            <button type="button" onClick={() => onIndexChange((index + 1) % images.length)} className={cn(navButton, "right-3")} aria-label={t("fileManager.preview.next")}>
              <ChevronRight size={22} />
            </button>
          </>
        )}
      </div>
    </div>
  );
}
