import { useState, useRef, useEffect } from "react";
import { Server, ChevronDown, Check } from "lucide-react";
import { useServer } from "@/context/ServerContext";
import { useTranslation } from "react-i18next";

export default function ServerSelector() {
  const { t } = useTranslation();
  const { servers, selectedServer, loading, error, selectServer } = useServer();
  const [isOpen, setIsOpen] = useState(false);
  const dropdownRef = useRef<HTMLDivElement>(null);

  // Close dropdown on outside click
  useEffect(() => {
    const handleClickOutside = (e: MouseEvent) => {
      if (dropdownRef.current && !dropdownRef.current.contains(e.target as Node)) {
        setIsOpen(false);
      }
    };
    document.addEventListener("mousedown", handleClickOutside);
    return () => document.removeEventListener("mousedown", handleClickOutside);
  }, []);

  return (
    <div className="relative" ref={dropdownRef}>
      {/* Selector Trigger Button */}
      <button
        onClick={() => setIsOpen(!isOpen)}
        disabled={loading || servers.length === 0}
        className="-ml-0.5 flex h-8 items-center gap-2 rounded-md px-2.5 text-xs text-slate-300 transition hover:bg-red-950/15 hover:text-white focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-red-500/50 cursor-pointer"
      >
        <Server
          size={16}
          className={`shrink-0 transition-all duration-200 ${
            selectedServer?.status === "online"
              ? "text-[#ffd8a0] filter drop-shadow-[0_0_4px_rgba(255,216,160,0.5)]"
              : selectedServer?.status === "offline"
                ? "text-[#e04444] filter drop-shadow-[0_0_4px_rgba(224,68,68,0.5)]"
                : "text-slate-500"
          }`}
        />
        <span className="font-semibold text-slate-200 truncate max-w-[180px]">
          {loading
            ? t("servers.loading")
            : error
              ? t("servers.loadFailed")
              : selectedServer?.name ?? t("servers.noneConfigured")}
        </span>
        <ChevronDown
          size={14}
          className={`text-slate-500 transition-transform ${isOpen ? "rotate-180" : ""}`}
        />
      </button>

      {/* Dropdown Menu */}
      {isOpen && (
        <div className="absolute left-0 z-30 mt-1.5 w-72 rounded-md glass-panel p-1.5 shadow-2xl animate-in fade-in zoom-in-95 duration-100">
          <div className="px-2.5 py-1.5 text-[10px] font-bold text-[#e04444] font-serif uppercase tracking-wider border-b border-red-950/20 mb-1">
            {t("servers.selectServer")}
          </div>

          <div className="space-y-0.5">
            {servers.map((srv) => {
              const isSelected = srv.id === selectedServer?.id;
              return (
                <button
                  key={srv.id}
                  onClick={() => {
                    selectServer(srv.id);
                    setIsOpen(false);
                  }}
                  className={`w-full flex items-center justify-between gap-3 px-2.5 py-2 rounded text-left text-xs transition-all duration-200 cursor-pointer ${
                    isSelected
                      ? "bg-[#b8282e]/15 font-semibold text-white border-l-2 border-[#b8282e] rounded-l-none pl-2 shadow-[inset_4px_0_12px_rgba(184,40,46,0.08)] glow-text"
                      : "hover:bg-red-950/10 text-slate-300 hover:text-white"
                  }`}
                >
                  <div className="min-w-0">
                    <div className="flex items-center gap-2">
                      <span className="font-semibold truncate text-slate-100">{srv.name}</span>
                      <span
                        className={`h-1.5 w-1.5 rounded-full shrink-0 ${
                          srv.status === "online"
                            ? "bg-[#ffd8a0] shadow-[0_0_6px_#ffd8a0]"
                            : srv.status === "offline"
                              ? "bg-[#e04444] shadow-[0_0_6px_#e04444]"
                              : "bg-slate-500"
                        }`}
                      />
                    </div>
                    <div className="text-[10px] font-mono text-slate-400 mt-0.5">
                      {srv.host}:{srv.port} • {srv.location}
                    </div>
                  </div>

                  {isSelected && <Check size={14} className="text-[#e04444] shrink-0" />}
                </button>
              );
            })}
          </div>
        </div>
      )}
    </div>
  );
}
