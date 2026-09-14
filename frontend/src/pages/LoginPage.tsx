import { useState, type FormEvent } from "react";
import { api } from "@/api/client";
import Button from "@/components/ui/Button";
import LanguageSwitcher from "@/components/layout/LanguageSwitcher";
import { useTranslation } from "react-i18next";
import { ApiError } from "@/api/client";

type LoginStep = "credentials" | "totp";

function FloatingEmbers() {
  const [particles] = useState(() =>
    Array.from({ length: 15 }, (_, i) => ({
      id: i,
      left: `${Math.random() * 100}%`,
      size: `${Math.random() * 3 + 2}px`,
      delay: `${Math.random() * 10}s`,
      duration: `${Math.random() * 15 + 10}s`,
    }))
  );

  return (
    <div className="absolute inset-0 overflow-hidden pointer-events-none z-0">
      {particles.map((p) => (
        <span
          key={p.id}
          className="absolute bottom-[-10px] rounded-full bg-gradient-to-t from-[#ffd8a0] to-[#b8282e] opacity-35 animate-ember-float"
          style={{
            left: p.left,
            width: p.size,
            height: p.size,
            animationDelay: p.delay,
            animationDuration: p.duration,
            boxShadow: "0 0 6px #e07a3a, 0 0 10px #b8282e",
          }}
        />
      ))}
    </div>
  );
}

export default function LoginPage({ onLogin }: { onLogin: () => void }) {
  const { t } = useTranslation();
  const [step, setStep] = useState<LoginStep>("credentials");
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [code, setCode] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function submitCredentials(event: FormEvent) {
    event.preventDefault();
    setError(null);
    setBusy(true);
    try {
      const result = await api.login(username, password);
      if (result.requiresTwoFactor) {
        setStep("totp");
        setCode("");
      } else if (result.authenticated) {
        onLogin();
      } else {
        throw new Error(t("errors.unauthenticated"));
      }
    } catch (reason) {
      setError(reason instanceof ApiError && reason.message ? reason.message : t("errors.login"));
    } finally {
      setBusy(false);
    }
  }

  async function submitTotp(event: FormEvent) {
    event.preventDefault();
    setError(null);
    setBusy(true);
    try {
      const result = await api.login2fa(code);
      if (result.authenticated) {
        onLogin();
      } else {
        throw new Error(t("errors.unauthenticated"));
      }
    } catch (reason) {
      setError(reason instanceof ApiError && reason.message ? reason.message : t("errors.invalid_totp_code"));
    } finally {
      setBusy(false);
    }
  }

  return (
    <main
      className="relative flex min-h-screen items-center justify-center bg-[#0a0a0c] p-4 text-slate-200 overflow-hidden"
      style={{ backgroundImage: "url('/bg.png')", backgroundRepeat: "repeat" }}
    >
      {/* Background radial gradient overlay */}
      <div className="absolute inset-0 bg-gradient-to-tr from-[#1c0809]/40 via-transparent to-[#1c0809]/20 pointer-events-none z-0" />
      
      <FloatingEmbers />

      {step === "credentials" ? (
        <form
          onSubmit={submitCredentials}
          className="relative z-10 w-full max-w-md rounded-2xl p-10 bg-gradient-to-b from-[#180407]/90 to-[#070203]/95 border border-[#8f1d22]/40 backdrop-blur-xl shadow-[0_25px_60px_rgba(0,0,0,0.95),_0_0_40px_rgba(184,40,46,0.25),_inset_0_0_20px_rgba(184,40,46,0.1)] space-y-6"
        >
          <div className="pb-4 border-b border-red-950/20 select-none">
            <h1 className="text-2xl font-bold font-serif tracking-widest text-slate-100 glow-text text-center uppercase">
              {t("login.title")}
            </h1>
          </div>

          <div className="space-y-4">
            <label className="block text-xs font-semibold tracking-wider text-slate-300 uppercase">
              {t("login.username")}
              <input
                type="text"
                autoComplete="username"
                className="mt-2 h-10 w-full rounded-md border border-red-950/45 bg-slate-950/40 px-3 text-slate-200 outline-none focus:border-[#e04444] focus:ring-2 focus:ring-[#b8282e]/25 transition-all duration-200 text-sm font-medium"
                value={username}
                onChange={(e) => setUsername(e.target.value)}
              />
            </label>

            <label className="block text-xs font-semibold tracking-wider text-slate-300 uppercase">
              {t("login.password")}
              <input
                type="password"
                autoComplete="current-password"
                className="mt-2 h-10 w-full rounded-md border border-red-950/45 bg-slate-950/40 px-3 text-slate-200 outline-none focus:border-[#e04444] focus:ring-2 focus:ring-[#b8282e]/25 transition-all duration-200 text-sm font-medium"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
              />
            </label>
          </div>

          {error ? (
            <p className="rounded-md border border-red-900/40 bg-[#5e1215]/20 p-3 text-xs text-red-200 shadow-[inset_0_0_8px_rgba(184,40,46,0.1)]">
              {error}
            </p>
          ) : null}

          <Button type="submit" variant="primary" className="w-full h-11 text-xs font-bold uppercase tracking-wider" disabled={busy || !username || !password}>
            {busy ? t("login.submitting") : t("login.submit")}
          </Button>
        </form>
      ) : (
        <form
          onSubmit={submitTotp}
          className="relative z-10 w-full max-w-md rounded-2xl p-10 bg-gradient-to-b from-[#180407]/90 to-[#070203]/95 border border-[#8f1d22]/40 backdrop-blur-xl shadow-[0_25px_60px_rgba(0,0,0,0.95),_0_0_40px_rgba(184,40,46,0.25),_inset_0_0_20px_rgba(184,40,46,0.1)] space-y-6"
        >
          <div className="pb-4 border-b border-red-950/20 select-none">
            <h1 className="text-2xl font-bold font-serif tracking-widest text-slate-100 glow-text text-center uppercase">
              {t("login.totp_title")}
            </h1>
          </div>

          <div className="text-center">
            <p className="text-[11px] text-slate-400 leading-relaxed font-medium">
              {t("login.totp_instructions")}
            </p>
          </div>

          <label className="block text-xs font-semibold tracking-wider text-slate-300 uppercase">
            {t("login.totp_code")}
            <input
              type="text"
              pattern="[0-9]*"
              inputMode="numeric"
              maxLength={6}
              placeholder="000000"
              className="mt-2 h-10 w-full rounded-md border border-red-950/45 bg-slate-950/40 px-3 text-center text-lg font-mono tracking-widest text-slate-200 outline-none focus:border-[#e04444] focus:ring-2 focus:ring-[#b8282e]/25 transition-all duration-200"
              value={code}
              onChange={(e) => setCode(e.target.value.replace(/\D/g, ""))}
            />
          </label>

          {error ? (
            <p className="rounded-md border border-red-900/40 bg-[#5e1215]/20 p-3 text-xs text-red-200 shadow-[inset_0_0_8px_rgba(184,40,46,0.1)]">
              {error}
            </p>
          ) : null}

          <div className="space-y-3 pt-2">
            <Button type="submit" variant="primary" className="w-full h-11 text-xs font-bold uppercase tracking-wider" disabled={busy || code.length < 6}>
              {busy ? t("login.submitting") : t("login.submit")}
            </Button>

            <Button
              type="button"
              variant="ghost"
              className="w-full text-xs text-slate-400 hover:text-slate-200 font-semibold"
              onClick={() => {
                setStep("credentials");
                setPassword("");
                setError(null);
              }}
            >
              {t("login.back_to_login")}
            </Button>
          </div>
        </form>
      )}
    </main>
  );
}
