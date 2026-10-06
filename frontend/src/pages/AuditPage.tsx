import { useCallback, useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { ApiError } from "@/api/client";
import { auditApi, type AuditEntry, type AuditFacets, type AuditPageResult } from "@/api/audit";
import Button from "@/components/ui/Button";
import PageHeader from "@/components/layout/PageHeader";
import { CheckCircle2, ChevronLeft, ChevronRight, RefreshCw, XCircle } from "lucide-react";

const PAGE_SIZE = 50;

const inputClass =
  "h-9 rounded-md border border-red-950/45 bg-slate-950/40 px-3 text-xs text-slate-200 outline-none focus:border-[#e04444] focus:ring-2 focus:ring-[#b8282e]/25 transition-all duration-200";

const labelClass = "block text-xs font-semibold tracking-wider text-slate-300 uppercase";

function formatTimestamp(value: string): string {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString();
}

function parseDetails(json: string | null | undefined): Record<string, unknown> | null {
  if (!json) return null;
  try {
    const parsed = JSON.parse(json) as unknown;
    return parsed && typeof parsed === "object" ? (parsed as Record<string, unknown>) : null;
  } catch {
    return null;
  }
}

export default function AuditPage() {
  const { t } = useTranslation();

  const [category, setCategory] = useState("");
  const [action, setAction] = useState("");
  const [actor, setActor] = useState("");
  const [server, setServer] = useState("");
  const [outcome, setOutcome] = useState("");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [search, setSearch] = useState("");
  const [offset, setOffset] = useState(0);

  const [facets, setFacets] = useState<AuditFacets | null>(null);
  const [result, setResult] = useState<AuditPageResult | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [expandedId, setExpandedId] = useState<number | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      setResult(
        await auditApi.query({
          category: category || undefined,
          action: action || undefined,
          actor: actor || undefined,
          server: server || undefined,
          succeeded: outcome === "" ? undefined : outcome === "success",
          from: from || undefined,
          to: to || undefined,
          search: search || undefined,
          limit: PAGE_SIZE,
          offset
        })
      );
    } catch (err) {
      setError(err instanceof ApiError && err.message ? err.message : t("audit.loadFailed"));
    } finally {
      setLoading(false);
    }
  }, [category, action, actor, server, outcome, from, to, search, offset, t]);

  useEffect(() => {
    void (async () => {
      try {
        setFacets(await auditApi.facets());
      } catch {
        setFacets(null);
      }
    })();
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  // A filter change starts again from the first page.
  function changed<T>(setter: (value: T) => void) {
    return (value: T) => {
      setter(value);
      setOffset(0);
    };
  }

  const actionLabel = (entry: { category: string; action: string }) =>
    t(`audit.actions.${entry.category}.${entry.action}`, { defaultValue: `${entry.category}: ${entry.action}` });

  const categories = Array.from(new Set(facets?.actions.map((item) => item.category) ?? []));
  const actions = (facets?.actions ?? []).filter((item) => !category || item.category === category);

  const total = result?.total ?? 0;
  const visibleFrom = total === 0 ? 0 : offset + 1;
  const visibleTo = Math.min(offset + PAGE_SIZE, total);

  return (
    <div className="space-y-6">
      <PageHeader title={t("audit.title")} description={t("audit.description")} />

      <div className="rounded-xl glass-panel p-4 shadow-xl">
        <div className="flex flex-wrap items-end gap-3">
          <label className={labelClass}>
            {t("audit.category")}
            <select
              className={`${inputClass} mt-1 block w-full md:w-40 cursor-pointer`}
              value={category}
              onChange={(e) => {
                changed(setCategory)(e.target.value);
                setAction("");
              }}
            >
              <option value="" className="bg-slate-950 text-slate-200">{t("audit.any")}</option>
              {categories.map((item) => (
                <option key={item} value={item} className="bg-slate-950 text-slate-200">
                  {t(`audit.categories.${item}`, { defaultValue: item })}
                </option>
              ))}
            </select>
          </label>
          <label className={labelClass}>
            {t("audit.action")}
            <select
              className={`${inputClass} mt-1 block w-full md:w-48 cursor-pointer`}
              value={action}
              onChange={(e) => changed(setAction)(e.target.value)}
            >
              <option value="" className="bg-slate-950 text-slate-200">{t("audit.any")}</option>
              {actions.map((item) => (
                <option key={`${item.category}/${item.action}`} value={item.action} className="bg-slate-950 text-slate-200">
                  {actionLabel(item)}
                </option>
              ))}
            </select>
          </label>
          <label className={labelClass}>
            {t("audit.actor")}
            <select
              className={`${inputClass} mt-1 block w-full md:w-40 cursor-pointer`}
              value={actor}
              onChange={(e) => changed(setActor)(e.target.value)}
            >
              <option value="" className="bg-slate-950 text-slate-200">{t("audit.any")}</option>
              {facets?.actors.map((item) => (
                <option key={item} value={item} className="bg-slate-950 text-slate-200">{item}</option>
              ))}
            </select>
          </label>
          <label className={labelClass}>
            {t("audit.server")}
            <select
              className={`${inputClass} mt-1 block w-full md:w-36 cursor-pointer`}
              value={server}
              onChange={(e) => changed(setServer)(e.target.value)}
            >
              <option value="" className="bg-slate-950 text-slate-200">{t("audit.any")}</option>
              {facets?.servers.map((item) => (
                <option key={item} value={item} className="bg-slate-950 text-slate-200">{item}</option>
              ))}
            </select>
          </label>
          <label className={labelClass}>
            {t("audit.outcome")}
            <select
              className={`${inputClass} mt-1 block w-full md:w-32 cursor-pointer`}
              value={outcome}
              onChange={(e) => changed(setOutcome)(e.target.value)}
            >
              <option value="" className="bg-slate-950 text-slate-200">{t("audit.any")}</option>
              <option value="success" className="bg-slate-950 text-slate-200">{t("audit.succeeded")}</option>
              <option value="failed" className="bg-slate-950 text-slate-200">{t("audit.failed")}</option>
            </select>
          </label>
          <label className={labelClass}>
            {t("audit.from")}
            <input
              type="datetime-local"
              className={`${inputClass} mt-1 block w-full md:w-48`}
              value={from}
              onChange={(e) => changed(setFrom)(e.target.value)}
            />
          </label>
          <label className={labelClass}>
            {t("audit.to")}
            <input
              type="datetime-local"
              className={`${inputClass} mt-1 block w-full md:w-48`}
              value={to}
              onChange={(e) => changed(setTo)(e.target.value)}
            />
          </label>
          <label className={`${labelClass} flex-1 min-w-48`}>
            {t("audit.search")}
            <input
              type="text"
              className={`${inputClass} mt-1 block w-full`}
              value={search}
              onChange={(e) => changed(setSearch)(e.target.value)}
            />
          </label>
          <Button variant="secondary" onClick={() => void load()} disabled={loading} className="h-9 text-xs">
            <RefreshCw size={14} className={`text-[#e04444] ${loading ? "animate-spin" : ""}`} />
            {t("audit.refresh")}
          </Button>
        </div>
      </div>

      {error && (
        <div className="p-3 rounded-lg border border-red-900/40 bg-[#5e1215]/20 text-sm text-red-200">{error}</div>
      )}

      <div className="overflow-x-auto rounded-lg glass-panel shadow-xl">
        {loading && !result ? (
          <p className="px-4 py-10 text-center text-sm text-slate-400 font-medium">{t("audit.loading")}</p>
        ) : (
          <table className="w-full border-collapse text-left text-sm text-slate-300">
            <thead className="bg-slate-950/60 border-b border-red-950/20 text-slate-200 font-serif font-bold text-xs tracking-wider uppercase">
              <tr>
                <th className="px-4 py-3 font-bold whitespace-nowrap">{t("audit.time")}</th>
                <th className="px-4 py-3 font-bold">{t("audit.actor")}</th>
                <th className="px-4 py-3 font-bold">{t("audit.action")}</th>
                <th className="px-4 py-3 font-bold">{t("audit.server")}</th>
                <th className="px-4 py-3 font-bold">{t("audit.target")}</th>
                <th className="px-4 py-3 font-bold">{t("audit.outcome")}</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-red-950/10">
              {result?.items.map((entry) => (
                <AuditRow
                  key={entry.id}
                  entry={entry}
                  label={actionLabel(entry)}
                  expanded={expandedId === entry.id}
                  onToggle={() => setExpandedId(expandedId === entry.id ? null : entry.id)}
                />
              ))}
              {result && result.items.length === 0 && (
                <tr>
                  <td colSpan={6} className="px-4 py-10 text-center text-slate-500">{t("audit.empty")}</td>
                </tr>
              )}
            </tbody>
          </table>
        )}
        {result && total > 0 && (
          <div className="flex items-center justify-between border-t border-slate-800 px-4 py-3 text-xs text-slate-400">
            <span>{t("audit.pageOf", { from: visibleFrom, to: visibleTo, total })}</span>
            <div className="flex items-center gap-2">
              <Button
                variant="ghost"
                disabled={offset === 0}
                onClick={() => setOffset(Math.max(0, offset - PAGE_SIZE))}
                className="h-8 text-xs"
              >
                <ChevronLeft size={14} />
                {t("audit.previous")}
              </Button>
              <Button
                variant="ghost"
                disabled={offset + PAGE_SIZE >= total}
                onClick={() => setOffset(offset + PAGE_SIZE)}
                className="h-8 text-xs"
              >
                {t("audit.next")}
                <ChevronRight size={14} />
              </Button>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}

interface AuditRowProps {
  entry: AuditEntry;
  label: string;
  expanded: boolean;
  onToggle: () => void;
}

function AuditRow({ entry, label, expanded, onToggle }: AuditRowProps) {
  const { t } = useTranslation();
  const details = parseDetails(entry.detailsJson);
  return (
    <>
      <tr
        className={`cursor-pointer align-top transition-colors ${expanded ? "bg-slate-800/30" : "hover:bg-slate-800/20"}`}
        onClick={onToggle}
      >
        <td className="px-4 py-3 whitespace-nowrap text-xs text-slate-400">{formatTimestamp(entry.timestampUtc)}</td>
        <td className="px-4 py-3 whitespace-nowrap">
          <span className="text-slate-200">{entry.actor}</span>
          <span className="ml-2 text-[10px] uppercase tracking-wide text-slate-500">
            {entry.actorRole === "api" ? t("audit.api") : entry.actorRole}
          </span>
        </td>
        <td className="px-4 py-3 text-slate-200">{label}</td>
        <td className="px-4 py-3 text-xs text-slate-400">{entry.serverId ?? "—"}</td>
        <td className="px-4 py-3 text-xs text-slate-300 max-w-72 truncate font-mono" title={entry.target ?? undefined}>
          {entry.target ?? "—"}
        </td>
        <td className="px-4 py-3 whitespace-nowrap">
          {entry.succeeded ? (
            <span className="inline-flex items-center gap-1 text-xs text-emerald-300">
              <CheckCircle2 size={14} /> {t("audit.succeeded")}
            </span>
          ) : (
            <span className="inline-flex items-center gap-1 text-xs text-rose-300">
              <XCircle size={14} /> {t("audit.failed")}
            </span>
          )}
        </td>
      </tr>
      {expanded && (
        <tr className="bg-slate-900/40">
          <td colSpan={6} className="px-4 py-3 space-y-3">
            <p className="text-xs text-slate-400">
              {t("audit.ip")}: <span className="font-mono text-slate-300">{entry.ipAddress ?? "—"}</span>
            </p>
            {entry.error && <p className="text-xs text-rose-200">{entry.error}</p>}
            {details ? (
              <div>
                <p className="text-[11px] uppercase tracking-wide text-slate-400 mb-1">{t("audit.details")}</p>
                <pre className="overflow-x-auto rounded-md border border-slate-800 bg-slate-950/50 p-3 text-xs font-mono text-emerald-300">
                  {JSON.stringify(details, null, 2)}
                </pre>
              </div>
            ) : (
              !entry.error && <p className="text-xs text-slate-500">{t("audit.noDetails")}</p>
            )}
          </td>
        </tr>
      )}
    </>
  );
}
