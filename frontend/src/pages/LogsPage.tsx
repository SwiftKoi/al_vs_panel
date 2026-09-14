import { useCallback, useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { ApiError } from "@/api/client";
import { logsApi, type LogEvent, type LogLevelCounts, type LogPageResult } from "@/api/logs";
import Button from "@/components/ui/Button";
import PageHeader from "@/components/layout/PageHeader";
import BottomSheet from "@/components/ui/BottomSheet";
import { AlertTriangle, ChevronLeft, ChevronRight, RefreshCw, ScrollText, Search, Sliders, Trash2 } from "lucide-react";

type LogsView = "all" | "errors";

const PAGE_SIZE = 100;

const inputClass =
  "h-9 rounded-md border border-red-950/45 bg-slate-950/40 px-3 text-xs text-slate-200 outline-none focus:border-[#e04444] focus:ring-2 focus:ring-[#b8282e]/25 transition-all duration-200";

function levelBadgeClass(level: string): string {
  switch (level) {
    case "Critical":
      return "bg-rose-500/15 text-rose-300 border-rose-500/30";
    case "Error":
      return "bg-red-500/15 text-red-300 border-red-500/30";
    case "Warning":
      return "bg-amber-500/15 text-amber-300 border-amber-500/30";
    case "Information":
      return "bg-sky-500/15 text-sky-300 border-sky-500/30";
    default:
      return "bg-slate-500/15 text-slate-300 border-slate-500/30";
  }
}

function formatTimestamp(value: string): string {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString();
}

function parseState(stateJson: string | null | undefined): Record<string, unknown> | null {
  if (!stateJson) return null;
  try {
    const parsed = JSON.parse(stateJson) as unknown;
    return parsed && typeof parsed === "object" ? (parsed as Record<string, unknown>) : null;
  } catch {
    return null;
  }
}

interface LogFiltersControlsProps {
  level: string;
  onLevel: (value: string) => void;
  source: string;
  onSource: (value: string) => void;
  from: string;
  onFrom: (value: string) => void;
  to: string;
  onTo: (value: string) => void;
  search: string;
  onSearch: (value: string) => void;
  sources: string[];
}

function LogFiltersControls({
  level,
  onLevel,
  source,
  onSource,
  from,
  onFrom,
  to,
  onTo,
  search,
  onSearch,
  sources
}: LogFiltersControlsProps) {
  const { t } = useTranslation();
  return (
    <>
      <label className="block text-xs font-semibold tracking-wider text-slate-300 uppercase">
        {t("logs.level")}
        <select className={`${inputClass} mt-1 block w-full md:w-36 cursor-pointer`} value={level} onChange={(e) => onLevel(e.target.value)}>
          <option value="" className="bg-slate-950 text-slate-200">{t("logs.anyLevel")}</option>
          <option value="Debug" className="bg-slate-950 text-slate-200">Debug</option>
          <option value="Information" className="bg-slate-950 text-slate-200">Information</option>
          <option value="Warning" className="bg-slate-950 text-slate-200">Warning</option>
          <option value="Error" className="bg-slate-950 text-slate-200">Error</option>
          <option value="Critical" className="bg-slate-950 text-slate-200">Critical</option>
        </select>
      </label>
      <label className="block text-xs font-semibold tracking-wider text-slate-300 uppercase">
        {t("logs.source")}
        <select className={`${inputClass} mt-1 block w-full md:w-52 cursor-pointer`} value={source} onChange={(e) => onSource(e.target.value)}>
          <option value="" className="bg-slate-950 text-slate-200">{t("logs.anySource")}</option>
          {sources.map((item) => (
            <option key={item} value={item} className="bg-slate-950 text-slate-200">
               {item}
            </option>
          ))}
        </select>
      </label>
      <label className="block text-xs font-semibold tracking-wider text-slate-300 uppercase">
        {t("logs.from")}
        <input
          type="datetime-local"
          className={`${inputClass} mt-1 block w-full md:w-48`}
          value={from}
          onChange={(e) => onFrom(e.target.value)}
        />
      </label>
      <label className="block text-xs font-semibold tracking-wider text-slate-300 uppercase">
        {t("logs.to")}
        <input
          type="datetime-local"
          className={`${inputClass} mt-1 block w-full md:w-48`}
          value={to}
          onChange={(e) => onTo(e.target.value)}
        />
      </label>
      <label className="block text-xs font-semibold tracking-wider text-slate-300 uppercase flex-1 min-w-48">
        {t("logs.search")}
        <input
          type="text"
          className={`${inputClass} mt-1 block w-full`}
          value={search}
          onChange={(e) => onSearch(e.target.value)}
        />
      </label>
    </>
  );
}

export default function LogsPage() {
  const { t } = useTranslation();

  const [view, setView] = useState<LogsView>("all");
  const [level, setLevel] = useState("");
  const [source, setSource] = useState("");
  const [search, setSearch] = useState("");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [offset, setOffset] = useState(0);

  const [sources, setSources] = useState<string[]>([]);
  const [summary, setSummary] = useState<LogLevelCounts | null>(null);
  const [result, setResult] = useState<LogPageResult | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [expandedId, setExpandedId] = useState<number | null>(null);
  const [filtersOpen, setFiltersOpen] = useState(false);

  const loadSources = useCallback(async () => {
    try {
      setSources(await logsApi.sources());
    } catch {
      setSources([]);
    }
  }, []);

  const loadSummary = useCallback(async () => {
    try {
      setSummary(await logsApi.summary(from || undefined, to || undefined));
    } catch {
      setSummary(null);
    }
  }, [from, to]);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const page =
        view === "errors"
          ? await logsApi.errors(PAGE_SIZE, offset)
          : await logsApi.query({
              level: level || undefined,
              source: source || undefined,
              from: from || undefined,
              to: to || undefined,
              search: search || undefined,
              limit: PAGE_SIZE,
              offset
            });
      setResult(page);
    } catch (err) {
      setError(err instanceof ApiError && err.message ? err.message : t("logs.loadFailed"));
    } finally {
      setLoading(false);
    }
  }, [view, level, source, from, to, search, offset, t]);

  useEffect(() => {
    void loadSources();
  }, [loadSources]);

  useEffect(() => {
    void loadSummary();
  }, [loadSummary]);

  useEffect(() => {
    setOffset(0);
    void load();
  }, [view, level, source, from, to, search, load]);

  function applyFilters(nextView: LogsView) {
    setView(nextView);
    setOffset(0);
  }

  async function handleClear() {
    if (!window.confirm(t("logs.clearConfirm"))) return;
    try {
      await logsApi.clear();
      await loadSummary();
      await load();
    } catch (err) {
      setError(err instanceof ApiError && err.message ? err.message : t("logs.clearFailed"));
    }
  }

  const total = result?.total ?? 0;
  const visibleFrom = total === 0 ? 0 : offset + 1;
  const visibleTo = Math.min(offset + PAGE_SIZE, total);
  const activeFilterCount = [level, source, from, to, search].filter(Boolean).length;

  return (
    <div className="space-y-6">
      <PageHeader
        title={t("logs.title")}
        description={t("logs.description")}
        actions={
          <div className="flex gap-1.5 p-1 rounded-lg bg-slate-950/60 border border-red-950/20 backdrop-blur-sm shadow-inner select-none">
            <button
              type="button"
              onClick={() => applyFilters("all")}
              className={`flex items-center gap-1.5 px-3 py-1.5 text-xs font-semibold rounded-md transition-all cursor-pointer ${
                view === "all"
                  ? "bg-[#b8282e]/20 text-white border-b border-[#e04444] glow-text"
                  : "text-slate-400 hover:text-slate-200 hover:bg-red-950/10"
              }`}
            >
              <ScrollText size={14} className="text-[#e04444]" />
              {t("logs.all")}
            </button>
            <button
              type="button"
              onClick={() => applyFilters("errors")}
              className={`flex items-center gap-1.5 px-3 py-1.5 text-xs font-semibold rounded-md transition-all cursor-pointer ${
                view === "errors"
                  ? "bg-[#b8282e]/20 text-white border-b border-[#e04444] glow-text"
                  : "text-slate-400 hover:text-slate-200 hover:bg-red-950/10"
              }`}
            >
              <AlertTriangle size={14} className="text-[#e04444]" />
              {t("logs.errors.title")}
              {summary && summary.error + summary.critical > 0 && (
                <span className="ml-1 text-[10px] bg-rose-500/20 text-[#ffd8a0] px-1.5 py-0.5 rounded-full font-bold">
                  {summary.error + summary.critical}
                </span>
              )}
            </button>
          </div>
        }
      />

      <div className="rounded-xl glass-panel p-4 shadow-xl space-y-3">
        {/* Desktop filter bar */}
        <div className="hidden md:flex flex-wrap items-end gap-3">
          <LogFiltersControls
            level={level}
            onLevel={setLevel}
            source={source}
            onSource={setSource}
            from={from}
            onFrom={setFrom}
            to={to}
            onTo={setTo}
            search={search}
            onSearch={setSearch}
            sources={sources}
          />
          <div className="flex items-center gap-2">
            <Button variant="secondary" onClick={() => void load()} disabled={loading} className="h-9 text-xs">
              <RefreshCw size={14} className={`text-[#e04444] ${loading ? "animate-spin" : ""}`} />
              {t("logs.refresh")}
            </Button>
            <Button variant="danger" onClick={() => void handleClear()} className="h-9 text-xs" title={t("logs.clearHint")}>
              <Trash2 size={14} />
              {t("logs.clear")}
            </Button>
          </div>
        </div>

        {/* Mobile toolbar: filters open in a bottom sheet */}
        <div className="grid md:hidden grid-cols-3 gap-2">
          <Button
            variant="secondary"
            onClick={() => setFiltersOpen(true)}
            className="h-9 text-xs flex items-center justify-center gap-1.5 min-w-0 px-2"
          >
            <Sliders size={14} className="text-[#e04444] shrink-0" />
            <span className="min-w-0 truncate">{t("logs.filters")}</span>
            {activeFilterCount > 0 && (
              <span className="text-[10px] bg-rose-500/20 text-[#ffd8a0] px-1 py-0.5 rounded-full font-bold shrink-0">
                {activeFilterCount}
              </span>
            )}
          </Button>
          <Button
            variant="secondary"
            onClick={() => void load()}
            disabled={loading}
            className="h-9 text-xs flex items-center justify-center gap-1.5 min-w-0 px-2"
          >
            <RefreshCw size={14} className={`text-[#e04444] shrink-0 ${loading ? "animate-spin" : ""}`} />
            <span className="min-w-0 truncate">{t("logs.refresh")}</span>
          </Button>
          <Button
            variant="danger"
            onClick={() => void handleClear()}
            className="h-9 text-xs flex items-center justify-center gap-1.5 min-w-0 px-2"
            title={t("logs.clearHint")}
          >
            <Trash2 size={14} className="shrink-0" />
            <span className="min-w-0 truncate">{t("logs.clear")}</span>
          </Button>
        </div>
      </div>

      {error && (
        <div className="p-3 rounded-lg border border-red-900/40 bg-[#5e1215]/20 text-sm text-red-200">{error}</div>
      )}

      <div className="overflow-x-auto rounded-lg glass-panel shadow-xl">
        {loading && !result ? (
          <p className="px-4 py-10 text-center text-sm text-slate-400 font-medium">{t("logs.loading")}</p>
        ) : (
          <table className="w-full border-collapse text-left text-sm text-slate-300">
            <thead className="bg-slate-950/60 border-b border-red-950/20 text-slate-200 font-serif font-bold text-xs tracking-wider uppercase">
              <tr>
                <th className="px-4 py-3 font-bold whitespace-nowrap">{t("logs.timestamp")}</th>
                <th className="px-4 py-3 font-bold">{t("logs.level")}</th>
                <th className="px-4 py-3 font-bold">{t("logs.category")}</th>
                <th className="px-4 py-3 font-bold">{t("logs.message")}</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-red-950/10">
              {result?.items.map((entry) => {
                const state = parseState(entry.stateJson);
                const hasDetails = state !== null || Boolean(entry.exceptionMessage || entry.stackTrace);
                const expanded = expandedId === entry.id;
                return (
                  <LogRow
                    key={entry.id}
                    entry={entry}
                    state={state}
                    expanded={expanded}
                    onToggle={() => setExpandedId(expanded ? null : entry.id)}
                    hasDetails={hasDetails}
                  />
                );
              })}
              {result && result.items.length === 0 && (
                <tr>
                  <td colSpan={4} className="px-4 py-10 text-center text-slate-500">
                    {view === "errors" ? t("logs.errors.empty") : t("logs.empty")}
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        )}
        {result && total > 0 && (
          <div className="flex items-center justify-between border-t border-slate-800 px-4 py-3 text-xs text-slate-400">
            <span>
              {t("logs.pageOf", { from: visibleFrom, to: visibleTo, total })}
            </span>
            <div className="flex items-center gap-2">
              <Button
                variant="ghost"
                disabled={offset === 0}
                onClick={() => setOffset(Math.max(0, offset - PAGE_SIZE))}
                className="h-8 text-xs"
              >
                <ChevronLeft size={14} />
                {t("logs.previous")}
              </Button>
              <Button
                variant="ghost"
                disabled={offset + PAGE_SIZE >= total}
                onClick={() => setOffset(offset + PAGE_SIZE)}
                className="h-8 text-xs"
              >
                {t("logs.next")}
                <ChevronRight size={14} />
              </Button>
            </div>
          </div>
        )}
      </div>

      {/* Mobile filters bottom sheet */}
      <BottomSheet
        isOpen={filtersOpen}
        onClose={() => setFiltersOpen(false)}
        title={t("logs.filters")}
        icon={<Sliders size={18} />}
      >
        <div className="space-y-4">
          <LogFiltersControls
            level={level}
            onLevel={setLevel}
            source={source}
            onSource={setSource}
            from={from}
            onFrom={setFrom}
            to={to}
            onTo={setTo}
            search={search}
            onSearch={setSearch}
            sources={sources}
          />
        </div>
        <div className="mt-5 flex justify-end">
          <Button variant="primary" onClick={() => setFiltersOpen(false)} className="h-9 text-xs">
            {t("common.done")}
          </Button>
        </div>
      </BottomSheet>
    </div>
  );
}

interface LogRowProps {
  entry: LogEvent;
  state: Record<string, unknown> | null;
  expanded: boolean;
  hasDetails: boolean;
  onToggle: () => void;
}

function LogRow({ entry, state, expanded, hasDetails, onToggle }: LogRowProps) {
  const { t } = useTranslation();
  return (
    <>
      <tr
        className={`cursor-pointer align-top transition-colors ${expanded ? "bg-slate-800/30" : "hover:bg-slate-800/20"}`}
        onClick={hasDetails ? onToggle : undefined}
      >
        <td className="px-4 py-3 whitespace-nowrap text-xs text-slate-400">{formatTimestamp(entry.timestampUtc)}</td>
        <td className="px-4 py-3">
          <span className={`inline-block border rounded px-2 py-0.5 text-[11px] font-medium ${levelBadgeClass(entry.level)}`}>
            {entry.level}
          </span>
        </td>
        <td className="px-4 py-3 text-xs text-slate-400 max-w-56 truncate">{entry.category}</td>
        <td className="px-4 py-3 text-slate-200">
          {entry.message}
          {hasDetails && <span className="ml-2 text-[11px] text-blue-400">{expanded ? "▾" : "▸"}</span>}
        </td>
      </tr>
      {expanded && (
        <tr className="bg-slate-900/40">
          <td colSpan={4} className="px-4 py-3 space-y-3">
            {entry.exceptionType && (
              <p className="text-xs font-mono text-rose-300">{entry.exceptionType}</p>
            )}
            {entry.exceptionMessage && (
              <p className="text-xs text-rose-200">{entry.exceptionMessage}</p>
            )}
            {state && (
              <div>
                <p className="text-[11px] uppercase tracking-wide text-slate-400 mb-1">{t("logs.state")}</p>
                <pre className="overflow-x-auto rounded-md border border-slate-800 bg-slate-950/50 p-3 text-xs font-mono text-emerald-300">
                  {JSON.stringify(state, null, 2)}
                </pre>
              </div>
            )}
            {entry.stackTrace && (
              <div>
                <p className="text-[11px] uppercase tracking-wide text-slate-400 mb-1">{t("logs.stackTrace")}</p>
                <pre className="overflow-x-auto rounded-md border border-slate-800 bg-slate-950/50 p-3 text-xs font-mono text-slate-300">
                  {entry.stackTrace}
                </pre>
              </div>
            )}
            {!entry.exceptionType && !entry.exceptionMessage && !state && !entry.stackTrace && (
              <p className="text-xs text-slate-500">{t("logs.noDetails")}</p>
            )}
          </td>
        </tr>
      )}
    </>
  );
}
