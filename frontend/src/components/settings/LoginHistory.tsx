import { useEffect, useState } from "react";
import { ChevronLeft, ChevronRight, Clock, Globe, User } from "lucide-react";
import { useTranslation } from "react-i18next";
import { ApiError } from "@/api/client";
import { loginsApi, type LoginLogPage } from "@/api/logins";
import Panel from "@/components/ui/Panel";

const PAGE_SIZE = 10;

function formatTimestamp(value: string): string {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString();
}

/** Paged log of panel sign-in attempts (who, when, from which IP, success or failure). */
export default function LoginHistory() {
  const { t } = useTranslation();
  const [logins, setLogins] = useState<LoginLogPage | null>(null);
  const [page, setPage] = useState(1);
  const [loginsLoading, setLoginsLoading] = useState(true);
  const [loginsError, setLoginsError] = useState<string | null>(null);

  useEffect(() => {
    let mounted = true;
    setLoginsLoading(true);
    setLoginsError(null);
    loginsApi.recent(page, PAGE_SIZE)
      .then((result) => { if (mounted) setLogins(result); })
      .catch((err) => {
        if (mounted) setLoginsError(err instanceof ApiError && err.message ? err.message : t("loginHistory.lastLoginsError"));
      })
      .finally(() => { if (mounted) setLoginsLoading(false); });
    return () => { mounted = false; };
  }, [page, t]);

  const totalPages = logins ? Math.max(1, Math.ceil(logins.total / PAGE_SIZE)) : 1;

  return (
    <Panel className="p-5">
      <div className="mb-4 pb-2 border-b border-red-950/20">
        <h2 className="text-base font-bold font-serif tracking-wider text-slate-100 glow-text">{t("loginHistory.lastLogins")}</h2>
      </div>
      {loginsError ? (
        <div className="rounded-lg border border-red-900/40 bg-[#5e1215]/20 p-4 text-sm text-red-200">
          {loginsError}
        </div>
      ) : loginsLoading ? (
        <p className="py-6 text-center text-sm text-slate-400">{t("loginHistory.lastLoginsLoading")}</p>
      ) : logins && logins.items.length > 0 ? (
        <>
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm border-collapse">
              <thead>
                <tr className="border-b border-red-950/25 text-[11px] font-bold font-serif text-slate-400 uppercase tracking-widest">
                  <th className="pb-3 pr-4">
                    <div className="flex items-center gap-1.5">
                      <User size={14} className="text-[#e04444]" />
                      <span>{t("loginHistory.user")}</span>
                    </div>
                  </th>
                  <th className="pb-3 px-4">
                    <div className="flex items-center gap-1.5">
                      <Clock size={14} className="text-[#e04444]" />
                      <span>{t("loginHistory.time")}</span>
                    </div>
                  </th>
                  <th className="pb-3 px-4">
                    <div className="flex items-center gap-1.5">
                      <Globe size={14} className="text-[#e04444]" />
                      <span>{t("loginHistory.ipAddress")}</span>
                    </div>
                  </th>
                  <th className="pb-3 pl-4 text-right">{t("loginHistory.status")}</th>
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
                          {t("loginHistory.loginStatusSuccess")}
                        </span>
                      ) : (
                        <span className="inline-flex items-center gap-1 rounded-full bg-[#5e1215]/20 px-2.5 py-0.5 text-[11px] font-bold text-[#e04444] border border-red-900/30 shadow-[0_0_6px_rgba(184,40,46,0.15)]">
                          <span className="h-1.5 w-1.5 rounded-full bg-[#e04444]" />
                          {t("loginHistory.loginStatusFailed")}
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
              {t("loginHistory.pageOf", { from: (page - 1) * PAGE_SIZE + 1, to: Math.min(page * PAGE_SIZE, logins.total), total: logins.total })}
            </span>
            <div className="flex items-center gap-2">
              <button
                onClick={() => setPage((p) => Math.max(1, p - 1))}
                disabled={page <= 1}
                className="flex items-center gap-1 rounded-md border border-red-950/45 bg-slate-950/30 px-3 py-1.5 text-xs font-semibold text-slate-300 transition-colors hover:bg-red-950/15 hover:border-red-900/50 hover:text-white disabled:opacity-40 disabled:hover:bg-transparent cursor-pointer"
              >
                <ChevronLeft size={14} className="text-[#e04444]" />
                {t("loginHistory.previous")}
              </button>
              <button
                onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
                disabled={page >= totalPages}
                className="flex items-center gap-1 rounded-md border border-red-950/45 bg-slate-950/30 px-3 py-1.5 text-xs font-semibold text-slate-300 transition-colors hover:bg-red-950/15 hover:border-red-900/50 hover:text-white disabled:opacity-40 disabled:hover:bg-transparent cursor-pointer"
              >
                {t("loginHistory.next")}
                <ChevronRight size={14} className="text-[#e04444]" />
              </button>
            </div>
          </div>
        </>
      ) : (
        <p className="py-6 text-center text-sm text-slate-400">{t("loginHistory.lastLoginsEmpty")}</p>
      )}
    </Panel>
  );
}
