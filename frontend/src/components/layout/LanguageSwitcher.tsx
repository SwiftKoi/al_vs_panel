import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { ChevronDown, Languages } from "lucide-react";
import i18n, { type Language } from "@/i18n";

export default function LanguageSwitcher() {
  const { t } = useTranslation();
  const currentLanguage = i18n.resolvedLanguage === "ru" ? "ru" : "en";
  const [open, setOpen] = useState(false);
  const containerRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    function closeOnOutsideClick(event: MouseEvent) {
      if (containerRef.current && !containerRef.current.contains(event.target as Node)) setOpen(false);
    }

    document.addEventListener("mousedown", closeOnOutsideClick);
    return () => document.removeEventListener("mousedown", closeOnOutsideClick);
  }, []);

  function changeLanguage(language: Language) {
    void i18n.changeLanguage(language);
    setOpen(false);
  }

  return (
    <div ref={containerRef} className="relative">
      <button
        type="button"
        aria-label={t("common.language")}
        aria-expanded={open}
        aria-haspopup="menu"
        onClick={() => setOpen((isOpen) => !isOpen)}
        className="flex h-8 items-center gap-1.5 rounded-md px-2 text-xs text-slate-300 transition hover:bg-red-950/15 hover:text-white focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-red-500/50 cursor-pointer"
      >
        <Languages size={15} className="text-[#e04444]" aria-hidden="true" />
        <span>{currentLanguage === "ru" ? t("common.russian") : t("common.english")}</span>
        <ChevronDown size={14} className={`text-slate-500 transition-transform ${open ? "rotate-180" : ""}`} aria-hidden="true" />
      </button>
      {open ? (
        <div role="menu" aria-label={t("common.language")} className="absolute right-0 z-20 mt-2 min-w-36 overflow-hidden rounded-md glass-panel p-1 shadow-2xl">
          <button type="button" role="menuitem" onClick={() => changeLanguage("ru")} className={`block w-full rounded px-3 py-2 text-left text-xs transition hover:bg-[#b8282e]/15 cursor-pointer ${currentLanguage === "ru" ? "bg-[#b8282e]/25 text-white glow-text font-semibold" : "text-slate-300"}`}>
            {t("common.russian")}
          </button>
          <button type="button" role="menuitem" onClick={() => changeLanguage("en")} className={`block w-full rounded px-3 py-2 text-left text-xs transition hover:bg-[#b8282e]/15 cursor-pointer ${currentLanguage === "en" ? "bg-[#b8282e]/25 text-white glow-text font-semibold" : "text-slate-300"}`}>
            {t("common.english")}
          </button>
        </div>
      ) : null}
    </div>
  );
}
