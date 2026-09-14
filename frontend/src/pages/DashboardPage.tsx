import { useEffect, useState } from "react";
import { CheckCircle2, CircleAlert, Terminal, ShieldCheck, User, Clock, Globe, ChevronLeft, ChevronRight } from "lucide-react";
import { api, ApiError } from "@/api/client";
import { loginsApi, type LoginLogPage } from "@/api/logins";
import Panel from "@/components/ui/Panel";
import PageHeader from "@/components/layout/PageHeader";
import { useTranslation } from "react-i18next";

const PAGE_SIZE = 10;

function formatTimestamp(value: string): string {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString();
}

export default function DashboardPage() {
  const { t } = useTranslation();
  const [health, setHealth] = useState<"checking" | "ok" | "error">("checking");

  const [logins, setLogins] = useState<LoginLogPage | null>(null);
  const [page, setPage] = useState(1);
  const [loginsLoading, setLoginsLoading] = useState(true);
  const [loginsError, setLoginsError] = useState<string | null>(null);

  useEffect(() => {
    api.health().then(() => setHealth("ok")).catch(() => setHealth("error"));
  }, []);

  useEffect(() => {
    let mounted = true;

    async function loadLogins() {
      setLoginsLoading(true);
      setLoginsError(null);
      try {
        const result = await loginsApi.recent(page, PAGE_SIZE);
        if (mounted) setLogins(result);
      } catch (err) {
        if (mounted) {
          setLoginsError(err instanceof ApiError && err.message ? err.message : t("dashboard.lastLoginsError"));
        }
      } finally {
        if (mounted) setLoginsLoading(false);
      }
    }

    void loadLogins();
    return () => { mounted = false; };
  }, [page, t]);

  const totalPages = logins ? Math.max(1, Math.ceil(logins.total / PAGE_SIZE)) : 1;

  return (
    <div className="space-y-6 max-w-none">
      <PageHeader
        title={t("dashboard.title")}
        description={t("dashboard.description")}
      />

      <div className="grid gap-4 md:grid-cols-2">
        {/* Backend API Connection Status */}
        <Panel className="p-4">
          <div className="flex items-center gap-3.5">
            {health === "checking" ? (
              <span className="h-2 w-2 rounded-full bg-slate-500 animate-pulse shrink-0" />
            ) : health === "ok" ? (
              <span className="h-2.5 w-2.5 rounded-full bg-[#ffd8a0] shadow-[0_0_8px_#ffd8a0] animate-ember-glow shrink-0" />
            ) : (
              <span className="h-2.5 w-2.5 rounded-full bg-[#e04444] shadow-[0_0_8px_#e04444] shrink-0" />
            )}
            <div>
              <div className="text-sm font-semibold tracking-wide text-slate-100">{t("dashboard.backendApi")}</div>
              <div className="text-xs text-slate-400 mt-0.5">
                {health === "checking" ? t("dashboard.checking") : health === "ok" ? t("dashboard.available") : t("dashboard.unavailable")}
              </div>
            </div>
          </div>
        </Panel>

        {/* SSH Server Mockup Interface */}
        <Panel className="p-4">
          <div className="flex items-center justify-between gap-4">
            <div className="flex items-center gap-3">
              <Terminal className="text-[#e04444] filter drop-shadow-[0_0_4px_rgba(224,68,68,0.4)]" size={20} />
              <div>
                <div className="text-sm font-semibold tracking-wide text-slate-100">{t("dashboard.sshServer")}</div>
                <div className="text-xs text-slate-400 mt-0.5">{t("dashboard.sshHost")}</div>
              </div>
            </div>
            <div className="flex items-center gap-1.5 rounded-full bg-[#ffd8a0]/10 px-2.5 py-1 text-xs font-semibold text-[#ffd8a0] border border-[#e07a3a]/30 shadow-[0_0_6px_rgba(224,122,58,0.2)]">
              <ShieldCheck size={14} className="text-[#ffd8a0]" />
              <span>{t("dashboard.sshConnected")}</span>
            </div>
          </div>
        </Panel>
      </div>

      {/* Last Logins */}
      <Panel className="p-5">
        <div className="mb-4 pb-2 border-b border-red-950/20">
          <h2 className="text-base font-bold font-serif tracking-wider text-slate-100 glow-text">{t("dashboard.lastLogins")}</h2>
        </div>
        {loginsError ? (
          <div className="rounded-lg border border-red-900/40 bg-[#5e1215]/20 p-4 text-sm text-red-200">
            {loginsError}
          </div>
        ) : loginsLoading ? (
          <p className="py-6 text-center text-sm text-slate-400">{t("dashboard.lastLoginsLoading")}</p>
        ) : logins && logins.items.length > 0 ? (
          <>
            <div className="overflow-x-auto">
              <table className="w-full text-left text-sm border-collapse">
                <thead>
                  <tr className="border-b border-red-950/25 text-[11px] font-bold font-serif text-slate-400 uppercase tracking-widest">
                    <th className="pb-3 pr-4">
                      <div className="flex items-center gap-1.5">
                        <User size={14} className="text-[#e04444]" />
                        <span>{t("dashboard.user")}</span>
                      </div>
                    </th>
                    <th className="pb-3 px-4">
                      <div className="flex items-center gap-1.5">
                        <Clock size={14} className="text-[#e04444]" />
                        <span>{t("dashboard.time")}</span>
                      </div>
                    </th>
                    <th className="pb-3 px-4">
                      <div className="flex items-center gap-1.5">
                        <Globe size={14} className="text-[#e04444]" />
                        <span>{t("dashboard.ipAddress")}</span>
                      </div>
                    </th>
                    <th className="pb-3 pl-4 text-right">{t("dashboard.status")}</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-red-950/10">
                  {logins.items.map((log) => (
                    <tr key={log.id} className="hover:bg-red-950/10 transition-colors">
                      <td className="py-3.5 pr-4 font-semibold text-slate-100 text-xs">{log.username}</td>
                      <td className="py-3.5 px-4 text-slate-400 font-mono text-[11px]">{formatTimestamp(log.timestampUtc)}</td>
                      <td className="py-3.5 px-4 text-slate-400 font-mono text-[11px]">{log.ipAddress}</td>
                      <td className="py-3.5 pl-4 text-right">
                        {log.succeeded ? (
                          <span className="inline-flex items-center gap-1 rounded-full bg-[#ffd8a0]/10 px-2.5 py-0.5 text-[11px] font-bold text-[#ffd8a0] border border-[#e07a3a]/30 shadow-[0_0_6px_rgba(224,122,58,0.15)]">
                            <span className="h-1.5 w-1.5 rounded-full bg-[#ffd8a0] animate-pulse" />
                            {t("dashboard.loginStatusSuccess")}
                          </span>
                        ) : (
                          <span className="inline-flex items-center gap-1 rounded-full bg-[#5e1215]/20 px-2.5 py-0.5 text-[11px] font-bold text-[#e04444] border border-red-900/30 shadow-[0_0_6px_rgba(184,40,46,0.15)]">
                            <span className="h-1.5 w-1.5 rounded-full bg-[#e04444]" />
                            {t("dashboard.loginStatusFailed")}
                          </span>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <div className="mt-4 flex items-center justify-between border-t border-red-950/20 pt-4">
              <span className="text-xs text-slate-400 font-medium">
                {t("dashboard.pageOf", { from: (page - 1) * PAGE_SIZE + 1, to: Math.min(page * PAGE_SIZE, logins.total), total: logins.total })}
              </span>
              <div className="flex items-center gap-2">
                <button
                  onClick={() => setPage((p) => Math.max(1, p - 1))}
                  disabled={page <= 1}
                  className="flex items-center gap-1 rounded-md border border-red-950/45 bg-slate-950/30 px-3 py-1.5 text-xs font-semibold text-slate-300 transition-colors hover:bg-red-950/15 hover:border-red-900/50 hover:text-white disabled:opacity-40 disabled:hover:bg-transparent cursor-pointer"
                >
                  <ChevronLeft size={14} className="text-[#e04444]" />
                  {t("dashboard.previous")}
                </button>
                <button
                  onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
                  disabled={page >= totalPages}
                  className="flex items-center gap-1 rounded-md border border-red-950/45 bg-slate-950/30 px-3 py-1.5 text-xs font-semibold text-slate-300 transition-colors hover:bg-red-950/15 hover:border-red-900/50 hover:text-white disabled:opacity-40 disabled:hover:bg-transparent cursor-pointer"
                >
                  {t("dashboard.next")}
                  <ChevronRight size={14} className="text-[#e04444]" />
                </button>
              </div>
            </div>
          </>
        ) : (
          <p className="py-6 text-center text-sm text-slate-400">{t("dashboard.lastLoginsEmpty")}</p>
        )}
      </Panel>
    </div>
  );
}
