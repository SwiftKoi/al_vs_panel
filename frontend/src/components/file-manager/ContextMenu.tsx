import { useEffect, useLayoutEffect, useRef, useState, type ReactNode } from "react";
import { cn } from "@/lib/cn";

export interface ContextMenuItem {
  label: string;
  icon?: ReactNode;
  shortcut?: string;
  onSelect: () => void;
  danger?: boolean;
  disabled?: boolean;
}

/** `null` entries render as separators. */
export type ContextMenuEntry = ContextMenuItem | null;

/** Menu at a screen position; closes on outside click, Escape, scroll or resize. Arrow keys move, Enter selects. */
export default function ContextMenu({
  x,
  y,
  entries,
  onClose
}: {
  x: number;
  y: number;
  entries: ContextMenuEntry[];
  onClose: () => void;
}) {
  const ref = useRef<HTMLDivElement>(null);
  const [position, setPosition] = useState({ left: x, top: y });
  const items = entries.filter((e): e is ContextMenuItem => e !== null && !e.disabled);
  const [active, setActive] = useState(-1);

  // Keep the menu inside the viewport.
  useLayoutEffect(() => {
    const menu = ref.current;
    if (!menu) return;
    const { width, height } = menu.getBoundingClientRect();
    setPosition({
      left: Math.max(8, Math.min(x, window.innerWidth - width - 8)),
      top: Math.max(8, Math.min(y, window.innerHeight - height - 8))
    });
  }, [x, y]);

  useEffect(() => {
    const close = (e: Event) => {
      if (e.type === "mousedown" && ref.current?.contains(e.target as Node)) return;
      onClose();
    };
    const onKey = (e: KeyboardEvent) => {
      // Capture phase + stopPropagation keeps the page's own shortcuts out of it.
      e.stopPropagation();
      if (e.key === "Escape") { e.preventDefault(); onClose(); }
      else if (e.key === "ArrowDown") { e.preventDefault(); setActive((i) => (i + 1) % items.length); }
      else if (e.key === "ArrowUp") { e.preventDefault(); setActive((i) => (i <= 0 ? items.length - 1 : i - 1)); }
      else if (e.key === "Enter" && active >= 0) { e.preventDefault(); items[active].onSelect(); onClose(); }
    };
    window.addEventListener("mousedown", close);
    window.addEventListener("scroll", close, true);
    window.addEventListener("resize", close);
    window.addEventListener("keydown", onKey, true);
    return () => {
      window.removeEventListener("mousedown", close);
      window.removeEventListener("scroll", close, true);
      window.removeEventListener("resize", close);
      window.removeEventListener("keydown", onKey, true);
    };
  }, [onClose, items, active]);

  return (
    <div
      ref={ref}
      role="menu"
      style={position}
      className="fixed z-[70] min-w-52 rounded-lg border border-red-950/50 bg-slate-950/95 py-1 text-xs shadow-2xl backdrop-blur-sm"
      onContextMenu={(e) => e.preventDefault()}
    >
      {entries.map((entry, index) => {
        if (entry === null) return <div key={`sep-${index}`} className="my-1 h-px bg-slate-800" />;
        const isActive = !entry.disabled && items[active] === entry;
        return (
          <button
            key={entry.label}
            type="button"
            role="menuitem"
            disabled={entry.disabled}
            onMouseEnter={() => setActive(items.indexOf(entry))}
            onClick={() => { entry.onSelect(); onClose(); }}
            className={cn(
              "flex w-full items-center gap-2.5 px-3 py-1.5 text-left cursor-pointer disabled:cursor-default disabled:opacity-40",
              entry.danger ? "text-rose-300" : "text-slate-200",
              isActive && (entry.danger ? "bg-rose-950/50" : "bg-slate-800/70")
            )}
          >
            <span className="flex w-4 justify-center shrink-0">{entry.icon}</span>
            <span className="flex-1">{entry.label}</span>
            {entry.shortcut && <span className="text-[10px] text-slate-500 font-mono">{entry.shortcut}</span>}
          </button>
        );
      })}
    </div>
  );
}
