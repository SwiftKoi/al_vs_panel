import { useEffect, useMemo, useRef, useState, type CSSProperties, type KeyboardEvent, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { Bookmark, Clock, Filter, Loader2, Search, X } from "lucide-react";
import { serverLogsApi, type SavedSearch } from "@/api/serverLogs";
import { FILTER_KEYS, formatToken } from "@/lib/logQuery";
import { cn } from "@/lib/cn";

const RECENT_KEY = "serverLogs.recentSearches";
const RECENT_LIMIT = 8;
const SUGGEST_DELAY_MS = 120;
const LOGS = ["main", "audit", "debug", "chat"];
/** Keys offered in the dropdown, most useful first; `sig:` is only set by the Problems tab. */
const OFFERED_KEYS = FILTER_KEYS.filter((key) => key !== "sig");
/** Keys whose values come from the index; `near:` takes coordinates and has no list. */
const NO_VALUES = new Set(["near", "sig"]);

/**
 * The input and the highlighted copy drawn under it must use exactly the same font, or the caret drifts away from
 * the text. An inline style is used because the global `input { font: inherit }` rule overrides Tailwind classes.
 */
const TEXT_STYLE: CSSProperties = {
  fontFamily: "ui-monospace, SFMono-Regular, Menlo, Consolas, \"Liberation Mono\", monospace",
  fontSize: "14px",
  lineHeight: "34px",
  letterSpacing: "0px",
  fontWeight: 400
};

type Group = "filters" | "values" | "saved" | "recent";
type Suggestion = {
  id: string;
  group: Group;
  label: ReactNode;
  hint?: string;
  /** The value text, to know whether what was typed already is this suggestion. */
  value?: string;
  apply: () => { text: string; caret: number; reopen?: boolean; submit?: boolean };
};

export function loadRecentSearches(): string[] {
  try {
    const value = JSON.parse(window.localStorage.getItem(RECENT_KEY) ?? "[]");
    return Array.isArray(value) ? value.filter((item): item is string => typeof item === "string").slice(0, RECENT_LIMIT) : [];
  } catch {
    return [];
  }
}

export function rememberSearch(query: string) {
  const text = query.trim();
  if (!text) return;
  try {
    const next = [text, ...loadRecentSearches().filter((item) => item !== text)].slice(0, RECENT_LIMIT);
    window.localStorage.setItem(RECENT_KEY, JSON.stringify(next));
  } catch {
    // Private mode or blocked storage: recent searches are a convenience only.
  }
}

/** The token around the caret. A quote opened before the caret keeps its spaces inside the token. */
function tokenAt(text: string, caret: number) {
  const before = text.slice(0, caret);
  const insideQuotes = (before.match(/"/g) ?? []).length % 2 === 1;
  let start = insideQuotes ? before.lastIndexOf("\"") : caret;
  while (start > 0 && !/\s/.test(text[start - 1])) start--;
  let end = caret;
  if (insideQuotes) {
    const close = text.indexOf("\"", caret);
    end = close === -1 ? text.length : close + 1;
  } else {
    while (end < text.length && !/\s/.test(text[end])) end++;
  }
  const raw = text.slice(start, caret);
  const match = /^(-?)([a-z]{1,12}):"?([^"]*)"?$/i.exec(raw);
  const key = match && (FILTER_KEYS as readonly string[]).includes(match[2].toLowerCase()) ? match[2].toLowerCase() : null;
  return {
    start,
    end,
    raw,
    negated: !!match?.[1],
    key,
    value: key ? match![3] : raw.replace(/^-/, ""),
    /** The rest of the box, which narrows the suggested values. */
    context: `${text.slice(0, start)} ${text.slice(end)}`.replace(/\s+/g, " ").trim()
  };
}

/** Filters in colour: key, value, excluded (struck through) and quoted phrases. */
function Highlighted({ text }: { text: string }) {
  const parts: ReactNode[] = [];
  const pattern = /(\s+)|(-?)([a-z]{1,12}):("[^"]*"?|\S*)|("[^"]*"?)|(\S+)/gi;
  let match: RegExpExecArray | null;
  let index = 0;
  while ((match = pattern.exec(text)) !== null) {
    const [whole, space, dash, key, value, phrase] = match;
    if (space) parts.push(space);
    else if (key && (FILTER_KEYS as readonly string[]).includes(key.toLowerCase())) {
      parts.push(
        <span key={index} className={dash ? "text-rose-300 line-through decoration-rose-300/70" : undefined}>
          {dash}
          <span className={dash ? undefined : "text-[#ff8a8a]"}>{key}:</span>
          <span className={dash ? undefined : "text-amber-200"}>{value}</span>
        </span>
      );
    } else if (phrase) parts.push(<span key={index} className="text-emerald-400">{phrase}</span>);
    else parts.push(<span key={index} className="text-white">{whole}</span>);
    index++;
  }
  return <>{parts}</>;
}

export default function LogSearchBox({
  serverId,
  value,
  onChange,
  onSubmit,
  range,
  saved
}: {
  serverId: string;
  value: string;
  onChange: (value: string) => void;
  onSubmit: (value: string) => void;
  range: { from: string; to?: string };
  saved: SavedSearch[];
}) {
  const { t, i18n } = useTranslation();
  const inputRef = useRef<HTMLInputElement>(null);
  const overlayRef = useRef<HTMLDivElement>(null);
  const listRef = useRef<HTMLUListElement>(null);
  const [caret, setCaret] = useState(0);
  const [open, setOpen] = useState(false);
  const [active, setActive] = useState(0);
  const [navigated, setNavigated] = useState(false);
  const [remote, setRemote] = useState<{ id: string; values: { value: string; count: number }[] } | null>(null);
  const [loadingRemote, setLoadingRemote] = useState(false);
  const number = useMemo(() => new Intl.NumberFormat(i18n.language, { notation: "compact" }), [i18n.language]);

  const token = useMemo(() => tokenAt(value, caret), [value, caret]);
  const wantsValues = !!token.key && token.key !== "log" && !NO_VALUES.has(token.key);
  const requestId = wantsValues ? `${token.key}|${token.value}|${token.context}|${range.from}|${range.to ?? ""}` : null;

  // Values from the index, narrowed by the rest of the box (e.g. "player:Orex_ killed:" → what Orex_ killed).
  useEffect(() => {
    if (!open || !requestId || !token.key) return;
    let cancelled = false;
    setLoadingRemote(true);
    const timer = window.setTimeout(() => {
      serverLogsApi.suggest(serverId, token.key!, token.value, token.context, range.from, range.to)
        .then((result) => { if (!cancelled) setRemote({ id: requestId, values: result.values }); })
        .catch(() => { if (!cancelled) setRemote({ id: requestId, values: [] }); })
        .finally(() => { if (!cancelled) setLoadingRemote(false); });
    }, SUGGEST_DELAY_MS);
    return () => { cancelled = true; window.clearTimeout(timer); };
    // requestId already encodes every input of the request.
  }, [open, requestId, serverId]);

  const keyLabel = (key: string) => (
    <>
      <span className="font-mono font-semibold text-[#ff8a8a]">{key}:</span>{" "}
      <span className="text-slate-200">{t(`serverLogs.help.${key}.text`)}</span>
    </>
  );

  const suggestions = useMemo<Suggestion[]>(() => {
    const replaceToken = (replacement: string, reopen = false) => {
      const before = value.slice(0, token.start);
      const after = value.slice(token.end).replace(/^\s+/, "");
      const separator = reopen ? "" : " ";
      return { text: `${before}${replacement}${separator}${after}`, caret: before.length + replacement.length + separator.length, reopen };
    };
    const list: Suggestion[] = [];

    if (token.key) {
      const key = token.key;
      const valueSuggestion = (item: string, label: ReactNode, hint?: string): Suggestion => ({
        id: `${key}:${item}`,
        group: "values",
        label,
        hint,
        value: item,
        apply: () => replaceToken(formatToken(key, item, token.negated))
      });
      if (key === "log") {
        const needle = token.value.toLowerCase();
        LOGS.filter((log) => log.startsWith(needle) || t(`serverLogs.logs.${log}`).toLowerCase().includes(needle))
          .forEach((log) => list.push(valueSuggestion(log, <><span className="font-mono">{log}</span> <span className="text-slate-300">{t(`serverLogs.logs.${log}`)}</span></>)));
      } else if (remote && remote.id === requestId) {
        remote.values.forEach((item) => list.push(valueSuggestion(
          item.value,
          key === "action"
            ? <><span className="font-mono">{item.value}</span> <span className="text-slate-300">{t(`serverLogs.actions.${item.value}`, { defaultValue: "" })}</span></>
            : <span className="font-mono">{item.value}</span>,
          number.format(item.count)
        )));
      }
      return list;
    }

    const word = token.raw.replace(/^-/, "").toLowerCase();
    if (word) {
      OFFERED_KEYS.filter((key) => key.startsWith(word) && key !== word).forEach((key) => list.push({
        id: `key:${key}`,
        group: "filters",
        label: keyLabel(key),
        apply: () => replaceToken(`${token.raw.startsWith("-") ? "-" : ""}${key}:`, true)
      }));
      return list;
    }

    OFFERED_KEYS.forEach((key) => list.push({ id: `key:${key}`, group: "filters", label: keyLabel(key), apply: () => replaceToken(`${key}:`, true) }));
    if (!value.trim()) {
      saved.forEach((search) => list.push({
        id: `saved:${search.id}`,
        group: "saved",
        label: <><span className="font-semibold text-white">{search.name}</span> <span className="font-mono text-slate-300">{search.query}</span></>,
        apply: () => ({ text: search.query, caret: search.query.length, submit: true })
      }));
      loadRecentSearches().forEach((recent) => list.push({
        id: `recent:${recent}`,
        group: "recent",
        label: <span className="font-mono">{recent}</span>,
        apply: () => ({ text: recent, caret: recent.length, submit: true })
      }));
    }
    return list;
  }, [token, remote, requestId, saved, value, t, number]);

  useEffect(() => { setActive(0); setNavigated(false); }, [token.key, token.raw]);

  useEffect(() => {
    listRef.current?.querySelector<HTMLElement>(`[data-index="${active}"]`)?.scrollIntoView({ block: "nearest" });
  }, [active]);

  const syncCaret = () => {
    const input = inputRef.current;
    if (!input) return;
    setCaret(input.selectionStart ?? input.value.length);
    if (overlayRef.current) overlayRef.current.scrollLeft = input.scrollLeft;
  };

  const accept = (suggestion: Suggestion) => {
    const result = suggestion.apply();
    onChange(result.text);
    if (result.submit) {
      setOpen(false);
      onSubmit(result.text);
    } else {
      setOpen(!!result.reopen);
    }
    // Move the caret after React has rendered the new value.
    window.requestAnimationFrame(() => {
      const input = inputRef.current;
      if (!input) return;
      input.focus();
      input.setSelectionRange(result.caret, result.caret);
      syncCaret();
      if (result.reopen) setOpen(true);
    });
  };

  const showList = open && (suggestions.length > 0 || wantsValues);
  const groupTitle: Record<Group, string> = {
    filters: t("serverLogs.searchBox.filters"),
    values: token.key ? t("serverLogs.searchBox.values", { key: token.key }) : "",
    saved: t("serverLogs.searchBox.saved"),
    recent: t("serverLogs.searchBox.recent")
  };
  const groupIcon: Record<Group, ReactNode> = { filters: <Filter size={12} />, values: <Search size={12} />, saved: <Bookmark size={12} />, recent: <Clock size={12} /> };

  const onKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === "Escape") { setOpen(false); return; }
    if (!showList || suggestions.length === 0) {
      if (event.key === "ArrowDown") { event.preventDefault(); setOpen(true); }
      return;
    }
    if (event.key === "ArrowDown" || event.key === "ArrowUp") {
      event.preventDefault();
      setNavigated(true);
      setActive((i) => (event.key === "ArrowDown" ? i + 1 : i - 1 + suggestions.length) % suggestions.length);
      return;
    }
    if (event.key !== "Tab" && event.key !== "Enter") return;
    const choice = suggestions[active];
    if (!choice) return;
    // Tab always completes. Enter completes what you picked with the arrows, or a filter value you are still
    // typing (player:Or → player:Orex_); when the typed value is already complete, Enter runs the search.
    const typedComplete = choice.value !== undefined && choice.value.toLowerCase() === token.value.toLowerCase();
    const completes = event.key === "Tab" || navigated || (choice.group === "values" && !typedComplete);
    if (completes) {
      event.preventDefault();
      accept(choice);
    } else {
      setOpen(false);
    }
  };

  return (
    <div className="relative min-w-0 flex-1">
      <div className="relative h-9 overflow-hidden rounded-md border border-slate-500/60 bg-black/60 focus-within:border-[#e04444]/80 focus-within:ring-1 focus-within:ring-[#b8282e]/40">
        <Search size={15} className="pointer-events-none absolute left-3 top-1/2 z-10 -translate-y-1/2 text-slate-300" />
        <div
          ref={overlayRef}
          aria-hidden
          style={TEXT_STYLE}
          className="pointer-events-none absolute inset-0 overflow-hidden whitespace-pre pl-9 pr-9 text-white"
        >
          {value ? <Highlighted text={value} /> : <span className="font-sans text-slate-400">{t("serverLogs.search.placeholder")}</span>}
          {/* Keeps the overlay scrollable to the same width as the input's text. */}
          <span className="inline-block w-9" />
        </div>
        <input
          ref={inputRef}
          value={value}
          style={TEXT_STYLE}
          spellCheck={false}
          autoComplete="off"
          autoCorrect="off"
          autoCapitalize="off"
          role="combobox"
          aria-expanded={showList}
          aria-autocomplete="list"
          aria-label={t("serverLogs.search.placeholder")}
          onChange={(event) => { onChange(event.target.value); setOpen(true); }}
          onSelect={syncCaret}
          onScroll={syncCaret}
          onFocus={() => setOpen(true)}
          onBlur={() => setOpen(false)}
          onKeyDown={onKeyDown}
          className="absolute inset-0 h-full w-full border-0 bg-transparent p-0 pl-9 pr-9 text-transparent caret-white outline-none selection:bg-[#e04444]/35"
        />
        {value && (
          <button
            type="button"
            onClick={() => { onChange(""); onSubmit(""); inputRef.current?.focus(); }}
            aria-label={t("serverLogs.search.clear")}
            className="absolute right-2 top-1/2 z-10 -translate-y-1/2 rounded p-1 text-slate-300 hover:bg-white/10 hover:text-white cursor-pointer"
          >
            <X size={14} />
          </button>
        )}
      </div>

      {showList && (
        <ul
          ref={listRef}
          role="listbox"
          className="absolute left-0 right-0 top-full z-30 mt-1 max-h-96 overflow-y-auto rounded-md border border-[#b8282e]/40 bg-[#111113] py-1 text-sm text-slate-100 shadow-[0_12px_32px_rgba(0,0,0,0.7)]"
          onMouseDown={(event) => event.preventDefault()}
        >
          {suggestions.map((suggestion, index) => (
            <li key={suggestion.id}>
              {(index === 0 || suggestions[index - 1].group !== suggestion.group) && (
                <div className="flex items-center gap-1.5 border-t border-white/5 px-3 pb-1 pt-2 text-[11px] font-semibold uppercase tracking-wider text-[#e04444] first:border-t-0">
                  {groupIcon[suggestion.group]} {groupTitle[suggestion.group]}
                </div>
              )}
              <button
                type="button"
                role="option"
                data-index={index}
                aria-selected={index === active}
                onMouseMove={() => setActive(index)}
                onClick={() => accept(suggestion)}
                className={cn(
                  "flex w-full items-center justify-between gap-3 border-l-2 px-3 py-1.5 text-left cursor-pointer",
                  index === active ? "border-[#e04444] bg-[#b8282e]/20 text-white" : "border-transparent hover:bg-white/5"
                )}
              >
                <span className="min-w-0 truncate">{suggestion.label}</span>
                {suggestion.hint && <span className="shrink-0 tabular-nums text-xs text-slate-300">{suggestion.hint}</span>}
              </button>
            </li>
          ))}
          {wantsValues && loadingRemote && suggestions.length === 0 && (
            <li className="flex items-center gap-2 px-3 py-2 text-xs text-slate-300"><Loader2 size={12} className="animate-spin" /> {t("serverLogs.searchBox.loading")}</li>
          )}
          {wantsValues && !loadingRemote && suggestions.length === 0 && (
            <li className="px-3 py-2 text-xs text-slate-300">{t("serverLogs.searchBox.noValues")}</li>
          )}
          <li className="mt-1 border-t border-white/10 px-3 pb-0.5 pt-1.5 text-[11px] text-slate-300">{t("serverLogs.searchBox.keys")}</li>
        </ul>
      )}
    </div>
  );
}
