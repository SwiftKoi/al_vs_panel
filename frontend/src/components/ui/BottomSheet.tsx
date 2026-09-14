import { useEffect, type PropsWithChildren, type ReactNode } from "react";
import { X } from "lucide-react";

export interface BottomSheetProps extends PropsWithChildren {
  isOpen: boolean;
  onClose: () => void;
  title: string;
  icon?: ReactNode;
}

export default function BottomSheet({ isOpen, onClose, title, icon, children }: BottomSheetProps) {
  // Close on Escape key
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === "Escape" && isOpen) {
        onClose();
      }
    };
    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [isOpen, onClose]);

  if (!isOpen) return null;

  return (
    <div className="fixed inset-0 z-50">
      {/* Backdrop overlay */}
      <div className="fixed inset-0 bg-black/80 backdrop-blur-sm animate-in fade-in duration-200" onClick={onClose} />

      {/* Bottom sheet panel */}
      <div className="absolute inset-x-0 bottom-0 z-10 mx-auto w-full max-w-2xl">
        <div className="glass-panel rounded-t-2xl shadow-2xl bottom-sheet-in">
          {/* Header */}
          <div className="flex items-center justify-between gap-3 border-b border-red-950/20 px-5 py-4">
            <div className="flex items-center gap-2.5">
              {icon && <div className="text-[#e04444] shrink-0">{icon}</div>}
              <h3 className="text-base font-bold text-slate-100 font-serif tracking-wide">{title}</h3>
            </div>
            <button
              onClick={onClose}
              className="rounded-lg p-1 text-slate-400 hover:bg-red-950/20 hover:text-white transition-colors cursor-pointer"
            >
              <X size={18} />
            </button>
          </div>

          {/* Body */}
          <div className="max-h-[70vh] overflow-y-auto p-5 text-xs text-slate-300">{children}</div>
        </div>
      </div>
    </div>
  );
}
