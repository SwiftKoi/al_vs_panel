import { useEffect, useRef, useState, type KeyboardEvent } from "react";
import { Check, ChevronDown } from "lucide-react";
import { cn } from "@/lib/cn";

export interface SelectOption<T extends string | number> {
  value: T;
  label: string;
}

interface SelectProps<T extends string | number> {
  value: T | null;
  options: SelectOption<T>[];
  onChange: (value: T) => void;
  placeholder: string;
  /** Lets the user type to filter the list; names starting with the text come first. */
  searchable?: boolean;
  /** With `searchable`: the typed text itself is the value, so names not in the list can be used. */
  allowCustom?: boolean;
}

// Themed replacement for <select>: the native option list can't be coloured consistently across browsers.
export default function Select<T extends string | number>({ value, options: allOptions, onChange, placeholder, searchable = false, allowCustom = false }: SelectProps<T>) {
  const [open, setOpen] = useState(false);
  const [active, setActive] = useState(0);
  const rootRef = useRef<HTMLDivElement>(null);
  const listRef = useRef<HTMLUListElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);
  const [query, setQuery] = useState("");
  const selected = allOptions.find((o) => o.value === value)
    ?? (allowCustom && value !== null && value !== "" ? { value, label: String(value) } : undefined);
  const q = query.trim().toLowerCase();
  const options = !q ? allOptions : [
    ...allOptions.filter((o) => o.label.toLowerCase().startsWith(q)),
    ...allOptions.filter((o) => !o.label.toLowerCase().startsWith(q) && o.label.toLowerCase().includes(q))
  ];

  useEffect(() => {
    if (!open) return;
    const close = (e: MouseEvent) => {
      if (!rootRef.current?.contains(e.target as Node)) { setOpen(false); setQuery(""); }
    };
    document.addEventListener("mousedown", close);
    return () => document.removeEventListener("mousedown", close);
  }, [open]);

  useEffect(() => {
    if (open) listRef.current?.children[active]?.scrollIntoView({ block: "nearest" });
  }, [open, active]);

  const toggle = () => {
    setActive(Math.max(0, options.findIndex((o) => o.value === value)));
    setOpen((o) => !o);
  };

  const pick = (option: SelectOption<T>) => {
    onChange(option.value);
    setOpen(false);
    setQuery("");
  };

  const onKeyDown = (e: KeyboardEvent) => {
    if (e.key === "Escape" && open) { e.stopPropagation(); setOpen(false); setQuery(""); return; }
    if (!open && (e.key === "ArrowDown" || e.key === "Enter" || (e.key === " " && !searchable))) { e.preventDefault(); toggle(); return; }
    if (!open) return;
    if (e.key === "ArrowDown") { e.preventDefault(); setActive((i) => Math.min(options.length - 1, i + 1)); }
    else if (e.key === "ArrowUp") { e.preventDefault(); setActive((i) => Math.max(0, i - 1)); }
    else if (e.key === "Enter" || (e.key === " " && !searchable)) { e.preventDefault(); if (options[active]) pick(options[active]); }
  };

  return (
    <div ref={rootRef} className="relative" onKeyDown={onKeyDown}>
      {searchable ? (
        <div
          onClick={() => { inputRef.current?.focus(); if (!open) toggle(); }}
          className={cn(
            "flex h-10 w-full items-center justify-between gap-2 rounded-md border bg-slate-950/40 px-3 text-sm font-medium transition-all cursor-text",
            open ? "border-[#e04444] ring-2 ring-[#b8282e]/25" : "border-red-950/45 hover:border-red-900/60"
          )}
        >
          <input
            ref={inputRef}
            role="combobox"
            aria-expanded={open}
            value={open && !(allowCustom && !query) ? query : selected?.label ?? ""}
            placeholder={open && selected ? selected.label : placeholder}
            onChange={(e) => {
              setQuery(e.target.value);
              setActive(0);
              setOpen(true);
              if (allowCustom) onChange(e.target.value.trim() as T);
            }}
            className="min-w-0 flex-1 bg-transparent text-slate-100 outline-none placeholder:text-slate-500"
          />
          <ChevronDown size={16} className={cn("shrink-0 text-slate-400 transition-transform", open && "rotate-180 text-[#e04444]")} />
        </div>
      ) : (
        <button
          type="button"
          onClick={toggle}
          aria-haspopup="listbox"
          aria-expanded={open}
          className={cn(
            "flex h-10 w-full items-center justify-between gap-2 rounded-md border bg-slate-950/40 px-3 text-left text-sm font-medium outline-none transition-all cursor-pointer",
            open ? "border-[#e04444] ring-2 ring-[#b8282e]/25" : "border-red-950/45 hover:border-red-900/60 focus:border-[#e04444] focus:ring-2 focus:ring-[#b8282e]/25"
          )}
        >
          <span className={cn("truncate", selected ? "text-slate-100" : "text-slate-500")}>{selected?.label ?? placeholder}</span>
          <ChevronDown size={16} className={cn("shrink-0 text-slate-400 transition-transform", open && "rotate-180 text-[#e04444]")} />
        </button>
      )}

      {open && (options.length > 0 || !allowCustom) && (
        <ul
          ref={listRef}
          role="listbox"
          className="absolute z-20 mt-1 max-h-60 w-full overflow-y-auto rounded-md border border-red-950/60 bg-[#120a0b] py-1 shadow-[0_8px_24px_rgba(0,0,0,0.6)]"
        >
          {options.length === 0 && <li className="px-3 py-2 text-sm text-slate-500">—</li>}
          {options.map((option, i) => {
            const isSelected = option.value === value;
            return (
              <li
                key={option.value}
                role="option"
                aria-selected={isSelected}
                onMouseEnter={() => setActive(i)}
                onClick={() => pick(option)}
                className={cn(
                  "flex cursor-pointer items-center justify-between gap-2 px-3 py-2 text-sm",
                  i === active ? "bg-[#b8282e]/20 text-white" : "text-slate-300",
                  isSelected && "text-[#e04444] font-semibold"
                )}
              >
                <span className="truncate">{option.label}</span>
                {isSelected && <Check size={14} className="shrink-0" />}
              </li>
            );
          })}
        </ul>
      )}
    </div>
  );
}
