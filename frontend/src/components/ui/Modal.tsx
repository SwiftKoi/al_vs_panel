import { useEffect, type PropsWithChildren, type ReactNode } from "react";
import { X } from "lucide-react";
import { useTranslation } from "react-i18next";

export interface ModalProps extends PropsWithChildren {
  isOpen: boolean;
  onClose: () => void;
  title: string;
  subtitle?: string;
  icon?: ReactNode;
  confirmLabel?: string;
  onConfirm?: () => void;
  confirmDisabled?: boolean;
  confirmVariant?: "primary" | "danger";
  isSubmitting?: boolean;
}

export default function Modal({
  isOpen,
  onClose,
  title,
  subtitle,
  icon,
  confirmLabel,
  onConfirm,
  confirmDisabled = false,
  confirmVariant = "primary",
  isSubmitting = false,
  children
}: ModalProps) {
  const { t } = useTranslation();

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
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
      {/* Backdrop overlay */}
      <div
        className="fixed inset-0 bg-black/80 backdrop-blur-sm transition-opacity animate-in fade-in duration-200"
        onClick={onClose}
      />

      {/* Modal Dialog */}
      <div className="relative z-10 w-full max-w-md glass-panel rounded-xl p-5 shadow-2xl animate-in zoom-in-95 duration-150">
        {/* Header */}
        <div className="flex items-start justify-between gap-3 pb-3 border-b border-red-950/20">
          <div className="flex items-center gap-2.5">
            {icon && <div className="text-[#e04444] shrink-0">{icon}</div>}
            <div>
              <h3 className="text-base font-bold text-slate-100 font-serif tracking-wide">{title}</h3>
              {subtitle && <p className="text-[11px] text-slate-400 mt-0.5">{subtitle}</p>}
            </div>
          </div>
          <button
            onClick={onClose}
            className="rounded-lg p-1 text-slate-400 hover:bg-red-950/20 hover:text-white transition-colors cursor-pointer"
          >
            <X size={18} />
          </button>
        </div>

        {/* Body Content */}
        <div className="py-4 text-xs text-slate-300">{children}</div>

        {/* Footer Actions */}
        <div className="flex items-center justify-end gap-2.5 pt-3 border-t border-red-950/20">
          <button
            type="button"
            onClick={onClose}
            className="rounded-md border border-red-950/40 bg-slate-950/30 px-4 py-1.5 text-xs font-semibold text-slate-300 hover:bg-red-950/15 hover:border-red-900/50 hover:text-white transition-colors cursor-pointer"
          >
            {t("fileManager.modals.cancel")}
          </button>

          {onConfirm && (
            <button
              type="button"
              onClick={onConfirm}
              disabled={confirmDisabled || isSubmitting}
              className={`rounded-md px-4 py-1.5 text-xs font-semibold text-white shadow-sm transition-all disabled:opacity-40 disabled:cursor-not-allowed cursor-pointer ${
                confirmVariant === "danger"
                  ? "bg-[#5e1215] border border-red-900/40 hover:bg-[#7a181c] shadow-[0_0_8px_rgba(184,40,46,0.2)]"
                  : "bg-gradient-to-r from-[#981d22] to-[#b8282e] hover:from-[#b8282e] hover:to-[#e04444] shadow-[0_0_8px_rgba(184,40,46,0.3)] btn-sweep"
              }`}
            >
              {confirmLabel || t("fileManager.modals.confirm")}
            </button>
          )}
        </div>
      </div>
    </div>
  );
}
