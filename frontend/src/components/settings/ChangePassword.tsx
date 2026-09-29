import { useState, type FormEvent } from "react";
import { Lock } from "lucide-react";
import { useTranslation } from "react-i18next";
import { api, ApiError } from "@/api/client";
import Button from "@/components/ui/Button";
import Panel from "@/components/ui/Panel";

const inputClass = "mt-2 h-10 w-full rounded-md border border-red-950/45 bg-slate-950/40 px-3 text-slate-200 outline-none focus:border-[#e04444] focus:ring-2 focus:ring-[#b8282e]/25 transition-all duration-200 text-sm font-medium";

/** Lets the signed-in user change their own password. */
export default function ChangePassword() {
  const { t } = useTranslation();
  const [current, setCurrent] = useState("");
  const [next, setNext] = useState("");
  const [confirm, setConfirm] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [done, setDone] = useState(false);

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    setError(null);
    setDone(false);
    if (next !== confirm) {
      setError(t("settings.password_mismatch"));
      return;
    }
    setBusy(true);
    try {
      await api.changePassword(current, next);
      setCurrent("");
      setNext("");
      setConfirm("");
      setDone(true);
    } catch (err) {
      setError(err instanceof ApiError && err.message ? err.message : t("errors.change_password_failed"));
    } finally {
      setBusy(false);
    }
  }

  return (
    <Panel className="p-6 space-y-4">
      <div className="flex items-center gap-3">
        <div className="p-2 rounded-lg bg-[#b8282e]/10 text-[#e04444] shadow-[0_0_6px_rgba(224,68,68,0.2)] shrink-0">
          <Lock size={20} />
        </div>
        <div className="min-w-0">
          <h2 className="text-base font-bold font-serif text-slate-100 glow-text tracking-wide">{t("settings.change_password")}</h2>
          <p className="text-xs text-slate-400 mt-1">{t("settings.change_password_desc")}</p>
        </div>
      </div>

      <div className="h-px bg-red-950/20" />

      {error && <div className="p-3 rounded-lg border border-rose-500/30 bg-rose-500/10 text-sm text-rose-200">{error}</div>}
      {done && <div className="p-3 rounded-lg border border-emerald-500/30 bg-emerald-500/10 text-sm text-emerald-200">{t("settings.password_changed")}</div>}

      <form onSubmit={(e) => void handleSubmit(e)} className="grid gap-4 sm:grid-cols-3 sm:items-end">
        <label className="block text-xs font-semibold tracking-wider text-slate-300 uppercase">
          {t("settings.current_password")}
          <input type="password" autoComplete="current-password" className={inputClass} value={current} onChange={(e) => setCurrent(e.target.value)} />
        </label>
        <label className="block text-xs font-semibold tracking-wider text-slate-300 uppercase">
          {t("settings.new_password")}
          <input type="password" autoComplete="new-password" className={inputClass} value={next} onChange={(e) => setNext(e.target.value)} />
        </label>
        <label className="block text-xs font-semibold tracking-wider text-slate-300 uppercase">
          {t("settings.confirm_password")}
          <input type="password" autoComplete="new-password" className={inputClass} value={confirm} onChange={(e) => setConfirm(e.target.value)} />
        </label>
        <div className="sm:col-span-3 flex justify-end">
          <Button type="submit" variant="primary" className="text-xs" disabled={busy || !current || !next || !confirm}>
            {busy ? t("common.saving") : t("settings.change_password")}
          </Button>
        </div>
      </form>
    </Panel>
  );
}
