import { createContext, useCallback, useContext, useMemo, useRef, useState, type ReactNode } from "react";
import { AlertCircle, CheckCircle2, Info, X } from "lucide-react";
import { cn } from "@/lib/cn";

type ToastKind = "success" | "error" | "info";

interface Toast {
  id: number;
  kind: ToastKind;
  message: string;
  action?: { label: string; onClick: () => void };
}

interface ToastOptions {
  action?: Toast["action"];
  /** Milliseconds before auto-dismiss; errors stay until dismissed unless set. */
  duration?: number;
}

interface ToastContextValue {
  success: (message: string, options?: ToastOptions) => void;
  error: (message: string, options?: ToastOptions) => void;
  info: (message: string, options?: ToastOptions) => void;
}

const ToastContext = createContext<ToastContextValue | null>(null);

export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<Toast[]>([]);
  const nextId = useRef(1);

  const dismiss = useCallback((id: number) => {
    setToasts((prev) => prev.filter((toast) => toast.id !== id));
  }, []);

  const show = useCallback((kind: ToastKind, message: string, options?: ToastOptions) => {
    const id = nextId.current++;
    // Keep the stack short; the oldest toasts go first.
    setToasts((prev) => [...prev.slice(-4), { id, kind, message, action: options?.action }]);
    const duration = options?.duration ?? (kind === "error" ? undefined : 4000);
    if (duration !== undefined) window.setTimeout(() => dismiss(id), duration);
  }, [dismiss]);

  const value = useMemo<ToastContextValue>(() => ({
    success: (message, options) => show("success", message, options),
    error: (message, options) => show("error", message, options),
    info: (message, options) => show("info", message, options)
  }), [show]);

  return (
    <ToastContext.Provider value={value}>
      {children}
      <div
        aria-live="polite"
        className="pointer-events-none fixed top-4 right-4 z-[60] flex w-[min(24rem,calc(100vw-2rem))] flex-col gap-2"
      >
        {toasts.map((toast) => {
          const Icon = toast.kind === "success" ? CheckCircle2 : toast.kind === "error" ? AlertCircle : Info;
          return (
            <div
              key={toast.id}
              role={toast.kind === "error" ? "alert" : "status"}
              className={cn(
                "pointer-events-auto flex items-start gap-2.5 rounded-lg border px-3.5 py-3 text-xs shadow-xl backdrop-blur-sm animate-in fade-in slide-in-from-top-2",
                toast.kind === "success" && "border-emerald-500/30 bg-emerald-950/80 text-emerald-100",
                toast.kind === "error" && "border-rose-500/40 bg-rose-950/85 text-rose-100",
                toast.kind === "info" && "border-slate-600/50 bg-slate-900/90 text-slate-100"
              )}
            >
              <Icon size={16} className="mt-0.5 shrink-0 opacity-80" />
              <div className="min-w-0 flex-1 whitespace-pre-line break-words">{toast.message}</div>
              {toast.action && (
                <button
                  type="button"
                  onClick={() => { toast.action!.onClick(); dismiss(toast.id); }}
                  className="shrink-0 font-semibold underline underline-offset-2 hover:opacity-80 cursor-pointer"
                >
                  {toast.action.label}
                </button>
              )}
              <button
                type="button"
                onClick={() => dismiss(toast.id)}
                aria-label="Dismiss"
                className="shrink-0 opacity-60 hover:opacity-100 cursor-pointer"
              >
                <X size={14} />
              </button>
            </div>
          );
        })}
      </div>
    </ToastContext.Provider>
  );
}

export function useToast(): ToastContextValue {
  const context = useContext(ToastContext);
  if (!context) throw new Error("useToast must be used inside ToastProvider");
  return context;
}
