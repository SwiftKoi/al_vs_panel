import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useSearchParams } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { Bookmark, BookmarkPlus, ChevronDown, Download, HelpCircle, Loader2, Radio, SlidersHorizontal, X } from "lucide-react";
import Panel from "@/components/ui/Panel";
import Button from "@/components/ui/Button";
import { ApiError } from "@/api/client";
import { serverLogsApi, type LogEntry, type LogFacets as Facets, type LogHistogram as Histogram, type LogSearchParams, type SavedSearch } from "@/api/serverLogs";
import { useToast } from "@/context/ToastContext";
import { highlightTerms, setFilter, toggleFilter } from "@/lib/logQuery";
import { cn } from "@/lib/cn";
import RangePicker from "@/components/server-logs/RangePicker";
import LogHistogram from "@/components/server-logs/LogHistogram";
import LogFacets from "@/components/server-logs/LogFacets";
import LogContextViewer from "@/components/server-logs/LogContextViewer";
import LogSearchBox, { rememberSearch } from "@/components/server-logs/LogSearchBox";
import { LogEntryRow } from "@/components/server-logs/LogEntryRow";
import { parseRange, resolveRange, writeRange, type RangePreset, type TimeRange } from "@/components/server-logs/logStyles";

const LIVE_INTERVAL_MS = 10_000;


type Results = { entries: LogEntry[]; nextCursor: string | null; terms: string[] };

function errorMessage(error: unknown, fallback: string) {
  return error instanceof ApiError && error.message ? error.message : fallback;
}

export default function SearchTab({ serverId }: { serverId: string }) {
  const { t, i18n } = useTranslation();
  const [params, setParams] = useSearchParams();
  const query = params.get("q") ?? "";
  const noise = params.get("noise") === "1";
  const rangeKey = `${params.get("range") ?? ""}|${params.get("from") ?? ""}|${params.get("to") ?? ""}`;
  const range = useMemo(() => parseRange(params, "24h"), [rangeKey]);

  const [draft, setDraft] = useState(query);
  const [results, setResults] = useState<Results>();
  const [facets, setFacets] = useState<Facets>();
  const [histogram, setHistogram] = useState<Histogram>();
  const [loading, setLoading] = useState(false);
  const [loadingMore, setLoadingMore] = useState(false);
  const [error, setError] = useState<string>();
  const [live, setLive] = useState(false);
  const [showHelp, setShowHelp] = useState(false);
  // Below lg the facets would push results far down, so they start collapsed there.
  const [facetsOpen, setFacetsOpen] = useState(false);
  const [contextEntry, setContextEntry] = useState<LogEntry>();
  const [saved, setSaved] = useState<SavedSearch[]>([]);
  const [saveName, setSaveName] = useState<string | null>(null);
  const toast = useToast();
  const requestId = useRef(0);
  const pagesLoaded = useRef(1);

  useEffect(() => setDraft(query), [query]);

  useEffect(() => {
    serverLogsApi.savedSearches(serverId).then(setSaved).catch(() => setSaved([]));
  }, [serverId]);

  const saveSearch = async () => {
    const name = saveName?.trim();
    if (!name) return;
    try {
      const created = await serverLogsApi.saveSearch(serverId, { name, query, range: "preset" in range ? range.preset : "24h" });
      setSaved((current) => [...current, created].sort((a, b) => a.name.localeCompare(b.name)));
      setSaveName(null);
    } catch (err) {
      toast.error(errorMessage(err, t("serverLogs.saved.failed")));
    }
  };

  const deleteSaved = async (search: SavedSearch) => {
    try {
      await serverLogsApi.deleteSavedSearch(serverId, search.id);
      setSaved((current) => current.filter((item) => item.id !== search.id));
    } catch (err) {
      toast.error(errorMessage(err, t("serverLogs.saved.failed")));
    }
  };

  const openSaved = (search: SavedSearch) => update((next) => {
    if (search.query) next.set("q", search.query);
    else next.delete("q");
    writeRange(next, { preset: search.range as RangePreset });
  });

  const update = useCallback((change: (next: URLSearchParams) => void) => {
    setParams((current) => {
      const next = new URLSearchParams(current);
      change(next);
      return next;
    }, { replace: true });
  }, [setParams]);

  const setQuery = useCallback((value: string) => update((next) => {
    if (value.trim()) next.set("q", value.trim());
    else next.delete("q");
  }), [update]);

  const submitQuery = useCallback((value: string) => {
    rememberSearch(value);
    setQuery(value);
  }, [setQuery]);

  const setRange = useCallback((value: TimeRange) => update((next) => writeRange(next, value)), [update]);

  // Resolved once per range change, so suggestions are not refetched on every render.
  const suggestRange = useMemo(() => resolveRange(range), [range]);
  const searchParams = useCallback((): LogSearchParams => ({ q: query, noise, ...resolveRange(range) }), [query, noise, range]);

  const load = useCallback(async (quiet = false) => {
    const id = ++requestId.current;
    if (!quiet) {
      setLoading(true);
      setError(undefined);
    }

    const request = searchParams();
    try {
      const [page, facetResult, histogramResult] = await Promise.all([
        serverLogsApi.search(serverId, request),
        serverLogsApi.facets(serverId, request),
        serverLogsApi.histogram(serverId, request)
      ]);
      if (id !== requestId.current) return;
      pagesLoaded.current = 1;
      setResults(page);
      setFacets(facetResult);
      setHistogram(histogramResult);
      setError(undefined);
    } catch (err) {
      if (id === requestId.current) setError(errorMessage(err, t("serverLogs.errors.load")));
    } finally {
      if (id === requestId.current) setLoading(false);
    }
  }, [serverId, searchParams, t]);

  useEffect(() => { void load(); }, [load]);

  // Live tail: refresh the first page while the tab is visible, unless older pages were loaded.
  useEffect(() => {
    if (!live) return;
    const timer = window.setInterval(() => {
      if (document.visibilityState === "visible" && pagesLoaded.current === 1) void load(true);
    }, LIVE_INTERVAL_MS);
    return () => window.clearInterval(timer);
  }, [live, load]);

  const loadMore = async () => {
    if (!results?.nextCursor) return;
    setLoadingMore(true);
    try {
      const page = await serverLogsApi.search(serverId, searchParams(), results.nextCursor);
      pagesLoaded.current += 1;
      setResults((current) => current && { ...page, entries: [...current.entries, ...page.entries] });
    } catch (err) {
      setError(errorMessage(err, t("serverLogs.errors.load")));
    } finally {
      setLoadingMore(false);
    }
  };

  const onFilter = useCallback((key: string, value: string) => {
    setQuery(key === "near" ? setFilter(query, "near", value) : toggleFilter(query, key, value));
  }, [query, setQuery]);

  const pattern = useMemo(() => highlightTerms(results?.terms ?? []), [results?.terms]);
  // Rows show the time; a separator row marks each new day, so no range hides the date.
  const timeFormat = useMemo(() => new Intl.DateTimeFormat(i18n.language, { hour: "2-digit", minute: "2-digit", second: "2-digit" }), [i18n.language]);
  const dayFormat = useMemo(() => new Intl.DateTimeFormat(i18n.language, { weekday: "short", day: "numeric", month: "long", year: "numeric" }), [i18n.language]);
  const noMatches = results?.entries.length === 0;
  const clearSearch = () => update((next) => { next.delete("q"); next.delete("noise"); });
  const number = new Intl.NumberFormat(i18n.language);

  return (
    <div className="space-y-4">
      <Panel className="space-y-3 p-4">
        <form
          className="flex flex-col gap-2 lg:flex-row lg:items-center"
          onSubmit={(event) => { event.preventDefault(); submitQuery(draft); }}
        >
          <LogSearchBox
            serverId={serverId}
            value={draft}
            onChange={setDraft}
            onSubmit={(value) => { setDraft(value); submitQuery(value); }}
            range={suggestRange}
            saved={saved}
          />
          <button
            type="button"
            onClick={() => setShowHelp((value) => !value)}
            aria-label={t("serverLogs.search.help")}
            title={t("serverLogs.search.help")}
            className={cn("hidden h-9 w-9 shrink-0 items-center justify-center rounded-md border lg:flex cursor-pointer", showHelp ? "border-[#b8282e]/60 text-[#e04444]" : "border-slate-600/70 text-slate-300 hover:text-white")}
          >
            <HelpCircle size={16} />
          </button>
          <div className="flex shrink-0 gap-2">
            <Button type="submit" variant="primary" className="flex-1">{t("serverLogs.search.submit")}</Button>
            <Button type="button" onClick={() => setSaveName((value) => (value === null ? "" : null))} title={t("serverLogs.saved.save")} aria-label={t("serverLogs.saved.save")}>
              <BookmarkPlus size={15} />
            </Button>
          </div>
        </form>

        {saveName !== null && (
          <form className="flex flex-wrap items-center gap-2" onSubmit={(event) => { event.preventDefault(); void saveSearch(); }}>
            <input
              autoFocus
              value={saveName}
              onChange={(event) => setSaveName(event.target.value)}
              maxLength={80}
              placeholder={t("serverLogs.saved.namePlaceholder")}
              className="h-8 min-w-[14rem] flex-1 rounded-md border border-slate-600/70 bg-slate-950/60 px-2 text-sm text-slate-100 focus:border-[#b8282e]/70 focus:outline-none"
            />
            <span className="text-[11px] text-slate-400">{t("serverLogs.saved.hint")}</span>
            <Button type="submit" className="h-8" disabled={!saveName.trim()}>{t("serverLogs.saved.confirm")}</Button>
          </form>
        )}

        {saved.length > 0 && (
          <div className="flex flex-wrap items-center gap-1.5">
            <Bookmark size={13} className="text-slate-400" />
            {saved.map((search) => (
              <span key={search.id} className="group flex items-center rounded-full border border-slate-600/70 text-[11px] text-slate-200 hover:border-[#b8282e]/60">
                <button type="button" onClick={() => openSaved(search)} title={`${search.query || "*"} · ${t(`serverLogs.range.${search.range}`, { defaultValue: search.range })}`} className="py-0.5 pl-2.5 pr-1 cursor-pointer">
                  {search.name}
                </button>
                <button type="button" onClick={() => void deleteSaved(search)} aria-label={t("serverLogs.saved.delete")} title={t("serverLogs.saved.delete")} className="rounded-full p-0.5 pr-1.5 text-slate-400 opacity-80 hover:text-rose-300 group-hover:opacity-100 cursor-pointer">
                  <X size={11} />
                </button>
              </span>
            ))}
          </div>
        )}

        {showHelp && (
          <div className="rounded-md border border-slate-600/70 bg-slate-950/60 p-3 text-xs text-slate-300">
            <p className="mb-2 text-slate-300">{t("serverLogs.help.intro")}</p>
            <ul className="grid gap-1 sm:grid-cols-2">
              {(["words", "phrase", "player", "action", "command", "killed", "killedby", "took", "put", "placed", "broke", "gave", "item", "with", "level", "log", "source", "near", "exclude"] as const).map((key) => (
                <li key={key}><code className="text-amber-200">{t(`serverLogs.help.${key}.code`)}</code> — {t(`serverLogs.help.${key}.text`)}</li>
              ))}
            </ul>
          </div>
        )}


        <div className="flex flex-wrap items-center gap-2">
          <RangePicker value={range} onChange={setRange} />
          <div className="ml-auto flex flex-wrap items-center gap-2">
            <label className="flex items-center gap-1.5 text-xs text-slate-300 cursor-pointer" title={t("serverLogs.search.noiseHint")}>
              <input type="checkbox" checked={noise} onChange={(event) => update((next) => { if (event.target.checked) next.set("noise", "1"); else next.delete("noise"); })} className="accent-[#b8282e]" />
              {t("serverLogs.search.noise")}
            </label>
            <button
              type="button"
              onClick={() => setLive((value) => !value)}
              title={t("serverLogs.search.liveHint")}
              className={cn("flex items-center gap-1.5 rounded-md border px-2.5 py-1 text-xs cursor-pointer", live ? "border-emerald-500/50 bg-emerald-500/10 text-emerald-300" : "border-slate-600/70 text-slate-300 hover:text-white")}
            >
              <Radio size={12} className={live ? "animate-pulse" : undefined} /> {t("serverLogs.search.live")}
            </button>
            {(["txt", "csv"] as const).map((format) => (
              <a
                key={format}
                href={serverLogsApi.exportUrl(serverId, searchParams(), format)}
                download
                className="flex items-center gap-1.5 rounded-md border border-slate-600/70 px-2.5 py-1 text-xs text-slate-300 hover:text-white"
                title={t("serverLogs.search.exportHint")}
              >
                <Download size={12} /> {format.toUpperCase()}
              </a>
            ))}
          </div>
        </div>

        {histogram && !noMatches && (
          <LogHistogram
            histogram={histogram}
            onZoom={(from, to) => setRange({ from, to })}
          />
        )}
      </Panel>

      {error && <Panel className="p-4 text-sm text-rose-300">{error}</Panel>}

      <div className="grid gap-4 lg:grid-cols-[14rem_minmax(0,1fr)]">
        <Panel className="h-fit p-4 lg:sticky lg:top-4">
          {facets ? (
            <>
              <button
                type="button"
                onClick={() => setFacetsOpen((value) => !value)}
                aria-expanded={facetsOpen}
                className="flex w-full items-center gap-2 text-left text-xs text-slate-300 lg:pointer-events-none lg:mb-3 cursor-pointer"
              >
                <span className="flex-1">{t("serverLogs.search.matches", { count: facets.total, formatted: number.format(facets.total) })}</span>
                {facets.total > 0 && (
                  <span className="flex items-center gap-1 text-slate-400 lg:hidden">
                    <SlidersHorizontal size={13} /> {t("serverLogs.facets.title")}
                    <ChevronDown size={13} className={cn("transition-transform", facetsOpen && "rotate-180")} />
                  </span>
                )}
              </button>
              <div className={cn("mt-3 lg:mt-0 lg:block", facetsOpen ? "block" : "hidden")}>
                <LogFacets facets={facets} query={query} onToggle={(key, value, exclude) => setQuery(toggleFilter(query, key, value, exclude))} />
              </div>
            </>
          ) : (
            <div className="flex justify-center py-6"><Loader2 size={18} className="animate-spin text-[#e04444]" /></div>
          )}
        </Panel>

        <Panel className="min-w-0 overflow-hidden">
          {loading && !results ? (
            <div className="flex justify-center py-16"><Loader2 size={22} className="animate-spin text-[#e04444]" /></div>
          ) : results && noMatches ? (
            <div className="space-y-3 p-8 text-center text-sm text-slate-300">
              <p>{t("serverLogs.search.empty")}</p>
              <div className="flex flex-wrap justify-center gap-2">
                {!("preset" in range && range.preset === "all") && (
                  <Button onClick={() => setRange({ preset: "all" })}>{t("serverLogs.search.emptyAllTime")}</Button>
                )}
                {query && <Button onClick={clearSearch}>{t("serverLogs.search.emptyClear")}</Button>}
              </div>
            </div>
          ) : results ? (
            <div className={cn(loading && "opacity-60 transition-opacity")}>
              {results.entries.map((entry, index) => {
                const day = new Date(entry.timestamp).toDateString();
                const newDay = index === 0 || new Date(results.entries[index - 1].timestamp).toDateString() !== day;
                return (
                  <div key={entry.id}>
                    {newDay && (
                      <div className="border-b border-slate-800/60 bg-slate-950/95 px-3 py-1 text-[11px] font-semibold uppercase tracking-wider text-slate-400">
                        {dayFormat.format(new Date(entry.timestamp))}
                      </div>
                    )}
                    <LogEntryRow entry={entry} pattern={pattern} timeFormat={timeFormat} onFilter={onFilter} onContext={setContextEntry} />
                  </div>
                );
              })}
              {results.nextCursor && (
                <div className="flex justify-center p-3">
                  <Button onClick={() => void loadMore()} disabled={loadingMore}>
                    {loadingMore && <Loader2 size={14} className="animate-spin" />} {t("serverLogs.search.loadMore")}
                  </Button>
                </div>
              )}
            </div>
          ) : null}
        </Panel>
      </div>

      {contextEntry && (
        <LogContextViewer serverId={serverId} entry={contextEntry} pattern={pattern} onClose={() => setContextEntry(undefined)} />
      )}
    </div>
  );
}
