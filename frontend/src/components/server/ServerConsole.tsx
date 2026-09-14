import { useEffect, useRef, useState, type FormEvent } from "react";
import { Send, Trash2 } from "lucide-react";
import { useTranslation } from "react-i18next";
import Panel from "@/components/ui/Panel";
import { serverApi } from "@/api/servers";
import type { DisplayStatus } from "@/hooks/useServerRuntime";
import { MAX_CONSOLE_ENTRIES } from "@/lib/constants";

type ConsoleEntryKind = "line" | "command" | "error";
interface ConsoleEntry { id: number; kind: ConsoleEntryKind; text: string; }

export default function ServerConsole({
  serverId,
  status,
  onError
}: {
  serverId: string;
  status: DisplayStatus;
  onError: (message: string) => void;
}) {
  const { t } = useTranslation();
  const [entries, setEntries] = useState<ConsoleEntry[]>([]);
  const [inputCommand, setInputCommand] = useState("");
  const [sendingCommand, setSendingCommand] = useState(false);
  const [streamError, setStreamError] = useState(false);
  const nextEntryId = useRef(1);
  const consoleEndRef = useRef<HTMLDivElement>(null);

  const appendEntry = (kind: ConsoleEntryKind, text: string) => {
    setEntries((current) => [...current, { id: nextEntryId.current++, kind, text }].slice(-MAX_CONSOLE_ENTRIES));
  };

  useEffect(() => {
    setEntries([]);
    setInputCommand("");
    setStreamError(false);
    nextEntryId.current = 1;

    let active = true;
    const closeLogs = serverApi.openLogs(serverId, {
      onEvent: (event) => {
        if (!active) return;
        if (event.kind === "line") appendEntry("line", event.data);
        if (event.kind === "error") appendEntry("error", t("server.logError"));
        if (event.kind === "end") setStreamError(true);
      },
      onConnected: () => { if (active) setStreamError(false); },
      onConnectionError: () => { if (active) setStreamError(true); }
    });

    return () => {
      active = false;
      closeLogs();
    };
  }, [serverId, t]);

  useEffect(() => {
    consoleEndRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [entries]);

  const sendCommand = async (event: FormEvent) => {
    event.preventDefault();
    if (status !== "online" || !inputCommand.trim() || sendingCommand) return;
    const command = inputCommand;
    setSendingCommand(true);
    try {
      const response = await serverApi.sendCommand(serverId, command);
      if (response.accepted) {
        appendEntry("command", `> ${command}`);
        setInputCommand("");
      }
    } catch {
      onError(t("server.errors.command"));
    } finally {
      setSendingCommand(false);
    }
  };

  return (
    <Panel className="p-4 flex flex-col h-[460px]">
      <div className="flex items-center justify-between pb-3 border-b border-red-950/20 mb-3">
        <div className="flex items-center gap-2 select-none">
          <div className="flex gap-1.5">
            <span className="h-2.5 w-2.5 rounded-full bg-[#5e1215] shadow-[0_0_4px_#5e1215]" />
            <span className="h-2.5 w-2.5 rounded-full bg-[#a1551a] shadow-[0_0_4px_#a1551a]" />
            <span className="h-2.5 w-2.5 rounded-full bg-[#ffd8a0] shadow-[0_0_6px_#ffd8a0] animate-pulse" />
          </div>
          <span className="text-xs font-serif font-bold text-slate-300 ml-3 tracking-wider uppercase">{t("server.console")}</span>
          {streamError && <span className="text-[10px] text-[#e04444] animate-pulse ml-2 font-medium">{t("server.streamDisconnected")}</span>}
        </div>
        <button onClick={() => setEntries([])} className="flex items-center gap-1.5 text-xs text-slate-300 hover:text-white transition-colors bg-slate-950/30 hover:bg-red-950/15 px-2.5 py-1 rounded border border-red-950/45 cursor-pointer" title={t("server.clearLocalHint")}>
          <Trash2 size={13} className="text-[#e04444]" />
          <span>{t("server.clear")}</span>
        </button>
      </div>
      
      <div className="flex-1 overflow-y-auto font-mono text-[11px] space-y-1.5 p-3 bg-slate-950/60 rounded border border-red-950/20 select-text relative">
        {/* Subtle retro scanline backdrop */}
        <div className="absolute inset-0 pointer-events-none bg-gradient-to-b from-transparent via-black/5 to-transparent bg-[length:100%_4px] opacity-15" />
        
        {entries.length === 0 ? (
          <div className="text-slate-600 italic text-center py-8 relative z-10">{t("server.consoleEmpty")}</div>
        ) : entries.map((entry) => (
          <div key={entry.id} className={`leading-relaxed whitespace-pre-wrap break-words relative z-10 ${entry.kind === "command" ? "text-[#ffd8a0] font-semibold" : entry.kind === "error" ? "text-[#e04444]" : "text-slate-300"}`}>
            {entry.text}
          </div>
        ))}
        <div ref={consoleEndRef} />
      </div>

      <form onSubmit={sendCommand} className="mt-3 flex gap-2">
        <input
          type="text"
          value={inputCommand}
          onChange={(event) => setInputCommand(event.target.value)}
          placeholder={t("server.sendCommand")}
          disabled={status !== "online" || sendingCommand}
          className="flex-1 bg-slate-950/40 border border-red-950/45 rounded px-3 py-2 text-xs font-mono text-slate-200 placeholder-slate-500 focus:outline-none focus:border-[#e04444] focus:ring-2 focus:ring-[#b8282e]/25 transition-all duration-200 disabled:opacity-40 disabled:cursor-not-allowed"
        />
        <button
          type="submit"
          disabled={status !== "online" || sendingCommand || !inputCommand.trim()}
          className="flex items-center gap-1.5 bg-gradient-to-r from-[#981d22] to-[#b8282e] hover:from-[#b8282e] hover:to-[#e04444] text-white text-xs font-bold uppercase tracking-wider px-4 py-2 rounded shadow-[0_0_8px_rgba(184,40,46,0.3)] btn-sweep transition-all disabled:opacity-40 disabled:cursor-not-allowed cursor-pointer"
        >
          <Send size={13} />
          <span>{sendingCommand ? t("server.sending") : t("server.send")}</span>
        </button>
      </form>
    </Panel>
  );
}
