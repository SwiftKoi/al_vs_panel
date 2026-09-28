import { useCallback, useEffect, useMemo, useState, type ReactNode } from "react";
import { createPortal } from "react-dom";
import { useTranslation } from "react-i18next";
import {
  AlertTriangle, ArrowRight, CheckCircle2, ExternalLink, Loader2, Package, Pin, PinOff, RefreshCw, RotateCcw, Search, X
} from "lucide-react";
import PageHeader from "@/components/layout/PageHeader";
import Button from "@/components/ui/Button";
import Modal from "@/components/ui/Modal";
import Panel from "@/components/ui/Panel";
import { ApiError } from "@/api/client";
import { modsApi, type InstalledMod, type ModDetail, type ModOverview, type ModRelease, type ModStatus, type ModUpdateItem, type ModUpdateJob } from "@/api/mods";
import { serverApi } from "@/api/servers";
import { useServer } from "@/context/ServerContext";
import { useToast } from "@/context/ToastContext";
import { cn } from "@/lib/cn";

type Filter = "updates" | "all" | "private" | "problems";

const JOB_POLL_MS = 1_500;

const statusStyle: Record<ModStatus, string> = {
  UpdateAvailable: "border-amber-500/40 bg-amber-500/10 text-amber-300",
  UpToDate: "border-emerald-500/30 bg-emerald-500/10 text-emerald-300",
  Ahead: "border-sky-500/30 bg-sky-500/10 text-sky-300",
  NoCompatibleRelease: "border-rose-500/30 bg-rose-500/10 text-rose-300",
  NotOnModDb: "border-slate-500/30 bg-slate-500/10 text-slate-300",
  CheckFailed: "border-rose-500/30 bg-rose-500/10 text-rose-300",
  Unidentified: "border-rose-500/30 bg-rose-500/10 text-rose-300"
};

const isProblem = (mod: InstalledMod) =>
  mod.status === "NoCompatibleRelease" || mod.status === "CheckFailed" || mod.status === "Unidentified" || mod.isLoaded === false;

const canOneClickUpdate = (mod: InstalledMod) => mod.status === "UpdateAvailable" && !mod.isPinned && !!mod.modId && !!mod.updateVersion;

function errorMessage(error: unknown, fallback: string) {
  return error instanceof ApiError && error.message ? error.message : fallback;
}

export default function ModsPage() {
  const { t, i18n } = useTranslation();
  const toast = useToast();
  const { selectedServer, loading: serversLoading } = useServer();
  const serverId = selectedServer?.id;

  const [overview, setOverview] = useState<ModOverview>();
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [loadError, setLoadError] = useState<string>();
  const [filter, setFilter] = useState<Filter>();
  const [query, setQuery] = useState("");
  const [job, setJob] = useState<ModUpdateJob>();
  const [confirmItems, setConfirmItems] = useState<{ mod: InstalledMod; version: string }[]>();
  const [submitting, setSubmitting] = useState(false);
  const [detailModId, setDetailModId] = useState<string>();
  const [confirmRollback, setConfirmRollback] = useState(false);
  const [confirmRestart, setConfirmRestart] = useState(false);

  const dateFormat = useMemo(() => new Intl.DateTimeFormat(i18n.language, { dateStyle: "medium", timeStyle: "short" }), [i18n.language]);
  const formatDate = useCallback((value: string | null | undefined) => (value ? dateFormat.format(new Date(value)) : "—"), [dateFormat]);

  const load = useCallback(async (refresh = false) => {
    if (!serverId) return;
    if (refresh) setRefreshing(true);
    try {
      const result = await modsApi.overview(serverId, refresh);
      setOverview(result);
      setLoadError(undefined);
      // Only a running job matters (to poll it); finished jobs were already reported by a toast.
      setJob(result.currentJob?.state === "Running" ? result.currentJob : undefined);
    } catch (error) {
      setLoadError(errorMessage(error, t("mods.errors.load")));
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  }, [serverId, t]);

  useEffect(() => {
    setLoading(true);
    setOverview(undefined);
    setJob(undefined);
    void load();
  }, [load]);

  // Poll a running job; refresh the list once it finishes.
  useEffect(() => {
    if (!serverId || job?.state !== "Running") return;
    const timer = window.setInterval(async () => {
      try {
        const current = await modsApi.currentJob(serverId);
        if (!current) return;
        setJob(current);
        if (current.state !== "Running") {
          const installed = current.items.filter((item) => item.state === "Installed").length;
          setJob(undefined);
          if (current.state === "Succeeded") {
            toast.success(t("mods.toast.updated", { count: installed }));
          } else {
            toast.error(current.error ?? t("mods.errors.update"));
          }
          void load();
        }
      } catch {
        // The next tick retries.
      }
    }, JOB_POLL_MS);
    return () => window.clearInterval(timer);
  }, [serverId, job?.state, load, t, toast]);

  const mods = useMemo(() => overview?.mods ?? [], [overview]);
  const updatable = useMemo(() => mods.filter(canOneClickUpdate), [mods]);
  const counts = useMemo(() => ({
    updates: mods.filter((mod) => mod.status === "UpdateAvailable").length,
    all: mods.length,
    private: mods.filter((mod) => mod.status === "NotOnModDb").length,
    problems: mods.filter(isProblem).length
  }), [mods]);

  const activeFilter: Filter = filter ?? (counts.updates > 0 ? "updates" : "all");
  const visible = useMemo(() => {
    const needle = query.trim().toLowerCase();
    return mods.filter((mod) => {
      if (activeFilter === "updates" && mod.status !== "UpdateAvailable") return false;
      if (activeFilter === "private" && mod.status !== "NotOnModDb") return false;
      if (activeFilter === "problems" && !isProblem(mod)) return false;
      return !needle || [mod.name, mod.modId, mod.fileName].some((value) => value?.toLowerCase().includes(needle));
    });
  }, [mods, activeFilter, query]);

  const jobRunning = job?.state === "Running";

  const startUpdate = async () => {
    if (!serverId || !confirmItems) return;
    setSubmitting(true);
    try {
      const items: ModUpdateItem[] = confirmItems.map(({ mod, version }) => ({ modId: mod.modId!, version }));
      setJob(await modsApi.update(serverId, items));
      setConfirmItems(undefined);
      setDetailModId(undefined);
    } catch (error) {
      toast.error(errorMessage(error, t("mods.errors.update")));
    } finally {
      setSubmitting(false);
    }
  };

  const rollback = async () => {
    if (!serverId) return;
    setSubmitting(true);
    try {
      const result = await modsApi.rollback(serverId);
      toast.success(t("mods.toast.rolledBack", { count: result.items.length }));
      setConfirmRollback(false);
      setJob(undefined);
      await load();
    } catch (error) {
      toast.error(errorMessage(error, t("mods.errors.rollback")));
    } finally {
      setSubmitting(false);
    }
  };

  const restart = async () => {
    if (!serverId) return;
    setSubmitting(true);
    try {
      await serverApi.restart(serverId);
      toast.success(t("mods.toast.restarted"));
      setConfirmRestart(false);
      await load();
    } catch (error) {
      toast.error(errorMessage(error, t("mods.errors.restart")));
    } finally {
      setSubmitting(false);
    }
  };

  const togglePin = async (mod: InstalledMod) => {
    if (!serverId || !mod.modId) return;
    try {
      await modsApi.setPinned(serverId, mod.modId, !mod.isPinned);
      setOverview((current) => current && {
        ...current,
        mods: current.mods.map((item) => (item.modId === mod.modId ? { ...item, isPinned: !mod.isPinned } : item))
      });
    } catch (error) {
      toast.error(errorMessage(error, t("mods.errors.pin")));
    }
  };

  if (serversLoading || (loading && !overview)) {
    return <Panel className="p-6 text-sm text-slate-400 flex items-center gap-2"><Loader2 size={16} className="animate-spin" /> {t("mods.loading")}</Panel>;
  }

  if (!serverId) {
    return <Panel className="p-6 text-sm text-slate-400">{t("servers.noneConfigured")}</Panel>;
  }

  return (
    <div className="space-y-5 max-w-none">
      <PageHeader
        title={t("mods.title")}
        description={t("mods.description")}
        actions={
          <Button onClick={() => void load(true)} disabled={refreshing}>
            <RefreshCw size={15} className={cn(refreshing && "animate-spin")} /> {t("mods.checkNow")}
          </Button>
        }
      />

      {loadError && (
        <div className="rounded border border-rose-500/30 bg-rose-500/10 px-4 py-3 text-sm text-rose-300">{loadError}</div>
      )}

      {overview && (
        <>
          {/* Summary: one sentence and one button. */}
          <Panel className="p-4 flex flex-col md:flex-row md:items-center gap-4 justify-between">
            <div className="text-sm text-slate-300 space-y-1">
              <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
                <span className="font-semibold text-slate-100">{t("mods.summary.mods", { count: counts.all })}</span>
                {counts.updates > 0
                  ? <span className="text-amber-300 font-semibold">{t("mods.summary.updates", { count: counts.updates })}</span>
                  : <span className="text-emerald-300 inline-flex items-center gap-1"><CheckCircle2 size={14} /> {t("mods.summary.upToDate")}</span>}
                {counts.problems > 0 && <span className="text-rose-300">{t("mods.summary.problems", { count: counts.problems })}</span>}
              </div>
              <div className="text-xs text-slate-400">
                {t("mods.summary.game", { version: overview.gameVersion ?? t("mods.unknown") })}
                {" · "}
                {t("mods.summary.checked", { time: formatDate(overview.checkedUtc) })}
              </div>
            </div>
            {updatable.length > 0 && (
              <Button
                variant="primary"
                className="h-10 px-5"
                disabled={jobRunning}
                onClick={() => setConfirmItems(updatable.map((mod) => ({ mod, version: mod.updateVersion! })))}
              >
                {jobRunning ? <Loader2 size={16} className="animate-spin" /> : <Package size={16} />}
                {jobRunning ? t("mods.job.Running") : t("mods.updateAll", { count: updatable.length })}
              </Button>
            )}
          </Panel>

          {overview.restartRequired && !jobRunning && (
            <div className="rounded-lg border border-amber-500/30 bg-amber-500/10 px-4 py-3 text-sm text-amber-200 flex flex-col sm:flex-row sm:items-center gap-3 justify-between">
              <span className="flex items-center gap-2"><AlertTriangle size={16} className="shrink-0" /> {t("mods.restartRequired")}</span>
              <Button onClick={() => setConfirmRestart(true)} className="shrink-0"><RotateCcw size={15} /> {t("mods.restartNow")}</Button>
            </div>
          )}

          {overview.lastUpdate && !jobRunning && (
            <div className="rounded-lg border border-red-950/40 bg-slate-950/30 px-4 py-3 text-xs text-slate-400 flex flex-col sm:flex-row sm:items-center gap-3 justify-between">
              <span>{t("mods.lastUpdate", { count: overview.lastUpdate.items.length, time: formatDate(overview.lastUpdate.appliedUtc) })}</span>
              <Button variant="ghost" onClick={() => setConfirmRollback(true)} className="shrink-0 h-8"><RotateCcw size={14} /> {t("mods.rollback")}</Button>
            </div>
          )}

          <div className="flex flex-col sm:flex-row sm:items-center gap-3 justify-between">
            <div className="flex flex-wrap gap-2">
              {(["updates", "all", "private", "problems"] as const).map((key) => (
                <button
                  key={key}
                  type="button"
                  onClick={() => setFilter(key)}
                  className={cn(
                    "rounded-full border px-3 py-1 text-xs font-semibold transition cursor-pointer",
                    activeFilter === key ? "border-[#b8282e] bg-[#b8282e]/15 text-white" : "border-red-950/40 text-slate-400 hover:text-white"
                  )}
                >
                  {t(`mods.filters.${key}`)} <span className="opacity-80">{counts[key]}</span>
                </button>
              ))}
            </div>
            <label className="relative sm:w-64">
              <Search size={14} className="absolute left-2.5 top-1/2 -translate-y-1/2 text-slate-400" />
              <input
                value={query}
                onChange={(event) => setQuery(event.target.value)}
                placeholder={t("mods.search")}
                className="w-full rounded-md border border-red-950/40 bg-slate-950/40 pl-8 pr-3 py-1.5 text-sm text-slate-200 placeholder:text-slate-500 focus:outline-none focus:border-red-800/60"
              />
            </label>
          </div>

          <Panel className="divide-y divide-red-950/20">
            {visible.length === 0 && (
              <div className="p-6 text-center text-sm text-slate-400">
                {activeFilter === "updates" ? t("mods.emptyUpdates") : t("mods.empty")}
              </div>
            )}
            {visible.map((mod) => (
              <ModRow
                key={mod.fileName}
                mod={mod}
                disabled={jobRunning}
                onOpen={() => mod.modId && setDetailModId(mod.modId)}
                onUpdate={() => setConfirmItems([{ mod, version: mod.updateVersion! }])}
                onTogglePin={() => void togglePin(mod)}
              />
            ))}
          </Panel>
        </>
      )}

      {detailModId && (
        <ModDetailDrawer
          serverId={serverId}
          modId={detailModId}
          formatDate={formatDate}
          disabled={jobRunning}
          onClose={() => setDetailModId(undefined)}
          onInstall={(mod, version) => setConfirmItems([{ mod, version }])}
          onTogglePin={(mod) => void togglePin(mod)}
        />
      )}

      {/* Above the detail panel (z-[100]), which confirmations are opened from. */}
      {createPortal(
        <div className="relative z-[110]">
        <Modal
          isOpen={!!confirmItems}
          onClose={() => setConfirmItems(undefined)}
          title={t("mods.confirm.title", { count: confirmItems?.length ?? 0 })}
          icon={<Package size={18} />}
          confirmLabel={t("mods.confirm.action", { count: confirmItems?.length ?? 0 })}
          onConfirm={() => void startUpdate()}
          isSubmitting={submitting}
        >
          <ul className="space-y-1.5 max-h-64 overflow-y-auto">
            {confirmItems?.map(({ mod, version }) => (
              <li key={mod.fileName} className="flex items-center justify-between gap-3">
                <span className="truncate text-slate-200">{mod.name}</span>
                <span className="shrink-0 font-mono text-[11px] text-slate-400 flex items-center gap-1">
                  {mod.version} <ArrowRight size={11} /> <span className={cn(isPrereleaseVersion(version) ? "text-sky-300" : "text-amber-300")}>{version}</span>
                </span>
              </li>
            ))}
          </ul>
          <p className="mt-4 text-slate-400 leading-relaxed">{t("mods.confirm.note")}</p>
        </Modal>
  
        <Modal
          isOpen={confirmRollback}
          onClose={() => setConfirmRollback(false)}
          title={t("mods.rollbackConfirm.title")}
          icon={<RotateCcw size={18} />}
          confirmLabel={t("mods.rollback")}
          confirmVariant="danger"
          onConfirm={() => void rollback()}
          isSubmitting={submitting}
        >
          <ul className="space-y-1 font-mono text-[11px]">
            {overview?.lastUpdate?.items.map((item) => (
              <li key={item.newFileName} className="flex items-center gap-1.5 text-slate-400">
                <span className="text-rose-300 truncate">{item.newFileName}</span> <ArrowRight size={11} className="shrink-0" /> <span className="text-emerald-300 truncate">{item.oldFileName ?? "—"}</span>
              </li>
            ))}
          </ul>
          <p className="mt-4 text-slate-400">{t("mods.rollbackConfirm.note")}</p>
        </Modal>
  
        <Modal
          isOpen={confirmRestart}
          onClose={() => setConfirmRestart(false)}
          title={t("mods.restartConfirm.title")}
          icon={<RotateCcw size={18} />}
          confirmLabel={t("mods.restartNow")}
          confirmVariant="danger"
          onConfirm={() => void restart()}
          isSubmitting={submitting}
        >
          <p className="text-slate-400">{t("mods.restartConfirm.note")}</p>
        </Modal>
        </div>,
        document.body
      )}
    </div>
  );
}

function isPrereleaseVersion(version: string) {
  return version.includes("-");
}

function StatusBadge({ mod }: { mod: InstalledMod }) {
  const { t } = useTranslation();
  return (
    <span
      title={mod.statusDetail ?? undefined}
      className={cn("inline-flex items-center rounded-full border px-2 py-0.5 text-[10px] font-bold uppercase tracking-wide whitespace-nowrap", statusStyle[mod.status])}
    >
      {t(`mods.status.${mod.status}`)}
    </span>
  );
}

function ModIcon({ mod }: { mod: InstalledMod }) {
  const [failed, setFailed] = useState(false);
  return mod.logoUrl && !failed
    ? <img src={mod.logoUrl} alt="" loading="lazy" onError={() => setFailed(true)} className="h-10 w-10 rounded-md object-cover bg-slate-900 shrink-0" />
    : <div className="h-10 w-10 rounded-md bg-slate-900/80 border border-red-950/30 flex items-center justify-center text-slate-400 shrink-0"><Package size={18} /></div>;
}

function ModRow({ mod, disabled, onOpen, onUpdate, onTogglePin }: {
  mod: InstalledMod;
  disabled: boolean;
  onOpen: () => void;
  onUpdate: () => void;
  onTogglePin: () => void;
}) {
  const { t } = useTranslation();
  const clickable = !!mod.modId;
  return (
    <div
      className={cn("flex items-center gap-3 px-4 py-3 transition", clickable && "cursor-pointer hover:bg-red-950/10")}
      onClick={clickable ? onOpen : undefined}
    >
      <ModIcon mod={mod} />
      <div className="min-w-0 flex-1">
        <div className="flex flex-wrap items-center gap-2">
          <span className="font-semibold text-slate-100 truncate">{mod.name}</span>
          <StatusBadge mod={mod} />
          {mod.isPinned && <span title={t("mods.pinnedHint")} className="text-slate-400"><Pin size={12} /></span>}
          {mod.isLoaded === false && <span className="text-[10px] text-amber-300">{t("mods.notLoaded")}</span>}
        </div>
        <div className="mt-0.5 flex flex-wrap items-center gap-x-2 text-xs text-slate-400">
          <span className="font-mono">{mod.modId ?? mod.fileName}</span>
          {mod.side && <span>· {t(`mods.side.${mod.side}`, { defaultValue: mod.side })}</span>}
          {mod.status === "UpdateAvailable" && mod.newerReleaseCount > 1 && <span>· {t("mods.releasesBehind", { count: mod.newerReleaseCount })}</span>}
          {mod.prereleaseVersion && <span className="text-sky-300">· {t("mods.prereleaseAvailable", { version: mod.prereleaseVersion })}</span>}
          {mod.statusDetail && mod.status !== "UpdateAvailable" && <span className="truncate max-w-md">· {mod.statusDetail}</span>}
        </div>
      </div>
      <div className="hidden sm:flex items-center gap-1.5 font-mono text-xs shrink-0">
        <span className="text-slate-300">{mod.version ?? "?"}</span>
        {mod.updateVersion && <><ArrowRight size={12} className="text-slate-400" /><span className="text-amber-300">{mod.updateVersion}</span></>}
      </div>
      <div className="flex items-center gap-1 shrink-0" onClick={(event) => event.stopPropagation()}>
        {mod.modId && mod.status !== "NotOnModDb" && mod.status !== "Unidentified" && (
          <button
            type="button"
            title={mod.isPinned ? t("mods.unpin") : t("mods.pin")}
            onClick={onTogglePin}
            className="rounded-md p-2 text-slate-400 hover:text-white hover:bg-red-950/20 cursor-pointer"
          >
            {mod.isPinned ? <PinOff size={15} /> : <Pin size={15} />}
          </button>
        )}
        {mod.updateVersion && (
          <Button onClick={onUpdate} disabled={disabled} className="h-8">
            {t("mods.update")}
          </Button>
        )}
      </div>
    </div>
  );
}

function ModDetailDrawer({ serverId, modId, formatDate, disabled, onClose, onInstall, onTogglePin }: {
  serverId: string;
  modId: string;
  formatDate: (value: string | null | undefined) => string;
  disabled: boolean;
  onClose: () => void;
  onInstall: (mod: InstalledMod, version: string) => void;
  onTogglePin: (mod: InstalledMod) => void;
}) {
  const { t } = useTranslation();
  const [detail, setDetail] = useState<ModDetail>();
  const [error, setError] = useState<string>();
  const [showAll, setShowAll] = useState(false);

  useEffect(() => {
    let active = true;
    modsApi.detail(serverId, modId)
      .then((result) => { if (active) setDetail(result); })
      .catch((reason) => { if (active) setError(errorMessage(reason, t("mods.errors.load"))); });
    return () => { active = false; };
  }, [serverId, modId, t]);

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => { if (event.key === "Escape") onClose(); };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onClose]);

  const mod = detail?.mod;
  // Releases between the installed version and the update target: what the admin is about to get.
  const incoming = useMemo(() => {
    if (!detail || !mod?.updateVersion) return [];
    const target = detail.releases.findIndex((release) => release.version === mod.updateVersion);
    const installed = detail.releases.findIndex((release) => release.version === mod.version);
    return detail.releases.slice(target, installed === -1 ? undefined : installed).filter((release) => release.isCompatible);
  }, [detail, mod]);

  const history = showAll ? detail?.releases ?? [] : (detail?.releases ?? []).slice(0, 12);

  // Portal: the app shell's content area sits below the top bar, so an in-tree overlay would be covered by it.
  return createPortal(
    <div className="fixed inset-0 z-[100]">
      <div className="fixed inset-0 bg-black/70 backdrop-blur-sm" onClick={onClose} />
      <aside className="absolute right-0 top-0 h-full w-full max-w-2xl glass-panel border-l border-red-950/30 overflow-y-auto">
        <div className="sticky top-0 z-10 flex items-start justify-between gap-3 border-b border-red-950/20 bg-[#0b0708]/95 px-5 py-4">
          {mod ? (
            <div className="flex items-center gap-3 min-w-0">
              <ModIcon mod={mod} />
              <div className="min-w-0">
                <h2 className="text-lg font-bold text-slate-100 font-serif truncate">{mod.name}</h2>
                <div className="flex flex-wrap items-center gap-2 text-xs text-slate-400">
                  <span className="font-mono">{mod.modId}</span>
                  <span className="font-mono text-slate-300">{mod.version}</span>
                  <StatusBadge mod={mod} />
                </div>
              </div>
            </div>
          ) : <div className="text-sm text-slate-400">{modId}</div>}
          <button type="button" onClick={onClose} className="rounded-lg p-1 text-slate-400 hover:bg-red-950/20 hover:text-white cursor-pointer"><X size={18} /></button>
        </div>

        <div className="p-5 space-y-6">
          {error && <div className="text-sm text-rose-300">{error}</div>}
          {!detail && !error && <div className="flex items-center gap-2 text-sm text-slate-400"><Loader2 size={16} className="animate-spin" /> {t("mods.loading")}</div>}

          {detail && mod && (
            <>
              {mod.description && <p className="text-sm text-slate-400">{mod.description}</p>}

              <div className="flex flex-wrap gap-2">
                {mod.updateVersion && (
                  <Button variant="primary" disabled={disabled} onClick={() => onInstall(mod, mod.updateVersion!)}>
                    {t("mods.updateTo", { version: mod.updateVersion })}
                  </Button>
                )}
                {mod.status !== "NotOnModDb" && (
                  <Button onClick={() => { onTogglePin(mod); setDetail({ ...detail, mod: { ...mod, isPinned: !mod.isPinned } }); }}>
                    {mod.isPinned ? <><PinOff size={14} /> {t("mods.unpin")}</> : <><Pin size={14} /> {t("mods.pin")}</>}
                  </Button>
                )}
                {mod.modDbUrl && <LinkButton href={mod.modDbUrl} label="ModDB" />}
                {detail.sourceUrl && <LinkButton href={detail.sourceUrl} label={t("mods.links.source")} />}
                {detail.issueTrackerUrl && <LinkButton href={detail.issueTrackerUrl} label={t("mods.links.issues")} />}
              </div>

              <dl className="grid grid-cols-2 sm:grid-cols-4 gap-3 text-xs">
                <Fact label={t("mods.facts.author")} value={(detail.author ?? mod.authors.join(", ")) || "—"} />
                <Fact label={t("mods.facts.side")} value={mod.side ? t(`mods.side.${mod.side}`, { defaultValue: mod.side }) : "—"} />
                <Fact label={t("mods.facts.latest")} value={mod.latestVersion ?? "—"} />
                <Fact label={t("mods.facts.downloads")} value={detail.totalDownloads?.toLocaleString() ?? "—"} />
              </dl>

              {mod.status === "NotOnModDb" && <p className="text-sm text-slate-400">{t("mods.privateHint")}</p>}
              {mod.statusDetail && mod.status !== "NotOnModDb" && <p className="text-sm text-slate-400">{mod.statusDetail}</p>}

              {incoming.length > 0 && (
                <section>
                  <h3 className="mb-2 text-xs font-bold uppercase tracking-wider text-amber-300">{t("mods.whatsNew", { count: incoming.length })}</h3>
                  <div className="space-y-3">
                    {incoming.map((release) => <ReleaseCard key={release.version} release={release} formatDate={formatDate} />)}
                  </div>
                </section>
              )}

              {detail.releases.length > 0 && (
                <section>
                  <h3 className="mb-2 text-xs font-bold uppercase tracking-wider text-slate-400">{t("mods.history", { count: detail.releases.length })}</h3>
                  <ol className="relative border-l border-red-950/40 ml-1.5 space-y-3">
                    {history.map((release) => {
                      const installed = release.version === mod.version;
                      const target = release.version === mod.updateVersion;
                      return (
                        <li key={release.version} className="pl-4 relative">
                          <span className={cn(
                            "absolute -left-[5px] top-1.5 h-2.5 w-2.5 rounded-full border",
                            installed ? "bg-emerald-400 border-emerald-300" : target ? "bg-amber-400 border-amber-300" : "bg-slate-800 border-slate-600"
                          )} />
                          <details className={cn("group", !release.isCompatible && "opacity-75")}>
                            <summary className="flex flex-wrap items-center gap-2 cursor-pointer list-none">
                              <span className="font-mono text-sm text-slate-100">{release.version}</span>
                              {installed && <Tag className="border-emerald-500/40 text-emerald-300">{t("mods.tags.installed")}</Tag>}
                              {target && <Tag className="border-amber-500/40 text-amber-300">{t("mods.tags.target")}</Tag>}
                              {release.isPrerelease && <Tag className="border-sky-500/40 text-sky-300">{t("mods.tags.prerelease")}</Tag>}
                              {!release.isCompatible && <Tag className="border-rose-500/40 text-rose-300">{t("mods.tags.incompatible")}</Tag>}
                              <span className="text-[11px] text-slate-400">{formatDate(release.createdUtc)} · {gameRange(release.gameVersions)}</span>
                              {!installed && (
                                <button
                                  type="button"
                                  disabled={disabled}
                                  onClick={(event) => { event.preventDefault(); onInstall(mod, release.version); }}
                                  className="ml-auto rounded border border-red-950/40 px-2 py-0.5 text-[11px] text-slate-300 hover:text-white hover:border-red-800/60 disabled:opacity-40 cursor-pointer"
                                >
                                  {ModVersionIsNewer(release.version, mod.version) ? t("mods.install") : t("mods.downgrade")}
                                </button>
                              )}
                            </summary>
                            <Changelog html={release.changelogHtml} />
                          </details>
                        </li>
                      );
                    })}
                  </ol>
                  {!showAll && detail.releases.length > history.length && (
                    <button type="button" onClick={() => setShowAll(true)} className="mt-3 text-xs text-slate-400 hover:text-white cursor-pointer">
                      {t("mods.showAll", { count: detail.releases.length })}
                    </button>
                  )}
                </section>
              )}
            </>
          )}
        </div>
      </aside>
    </div>,
    document.body
  );
}

function ReleaseCard({ release, formatDate }: { release: ModRelease; formatDate: (value: string | null) => string }) {
  return (
    <div className="rounded-lg border border-red-950/30 bg-slate-950/30 p-3">
      <div className="flex flex-wrap items-center gap-2 text-xs">
        <span className="font-mono font-semibold text-slate-100">{release.version}</span>
        <span className="text-slate-400">{formatDate(release.createdUtc)}</span>
      </div>
      <Changelog html={release.changelogHtml} />
    </div>
  );
}

function Changelog({ html }: { html: string | null }) {
  const { t } = useTranslation();
  if (!html) return <p className="mt-2 text-xs italic text-slate-400">{t("mods.noChangelog")}</p>;
  // Sanitised server-side (allowlisted tags, https links only).
  return <div className="changelog mt-2 text-xs leading-relaxed text-slate-300" dangerouslySetInnerHTML={{ __html: html }} />;
}

function Tag({ className, children }: { className?: string; children: ReactNode }) {
  return <span className={cn("rounded border px-1.5 py-px text-[10px] font-semibold uppercase", className)}>{children}</span>;
}

function Fact({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-md border border-red-950/30 bg-slate-950/30 px-3 py-2">
      <dt className="text-[10px] uppercase tracking-wider text-slate-400">{label}</dt>
      <dd className="mt-0.5 text-slate-200 truncate">{value}</dd>
    </div>
  );
}

function LinkButton({ href, label }: { href: string; label: string }) {
  return (
    <a href={href} target="_blank" rel="noopener noreferrer"
      className="inline-flex h-9 items-center gap-1.5 rounded-md border border-red-950/45 bg-slate-950/30 px-3 text-sm font-semibold text-slate-200 hover:bg-red-950/15 hover:text-white">
      {label} <ExternalLink size={13} />
    </a>
  );
}

function gameRange(versions: string[]) {
  if (versions.length === 0) return "—";
  return versions.length === 1 ? versions[0] : `${versions[0]} – ${versions[versions.length - 1]}`;
}

// Mirrors the server's comparator closely enough for labelling install vs downgrade.
function ModVersionIsNewer(candidate: string, installed: string | null) {
  if (!installed) return true;
  const parse = (value: string) => {
    const [core, pre] = value.replace(/^v/i, "").split("+")[0].split(/-(.*)/s);
    return { numbers: core.split(".").map((part) => Number(part) || 0), pre: pre ?? "" };
  };
  const a = parse(candidate);
  const b = parse(installed);
  for (let index = 0; index < Math.max(a.numbers.length, b.numbers.length); index++) {
    const diff = (a.numbers[index] ?? 0) - (b.numbers[index] ?? 0);
    if (diff !== 0) return diff > 0;
  }
  if (!a.pre !== !b.pre) return !a.pre;
  return a.pre.localeCompare(b.pre, undefined, { numeric: true }) > 0;
}
