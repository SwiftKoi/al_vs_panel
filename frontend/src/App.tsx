import { useEffect, useState } from "react";
import { Navigate, Route, Routes, useNavigate } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { api } from "@/api/client";
import AppShell from "@/components/layout/AppShell";
import DashboardPage from "@/pages/DashboardPage";
import LoginPage from "@/pages/LoginPage";
import ServerPage from "@/pages/ServerPage";
import FileManagerPage from "@/pages/FileManagerPage";
import EditorPage from "@/pages/EditorPage";
import LogsPage from "@/pages/LogsPage";
import SettingsPage from "@/pages/SettingsPage";
import { ServerProvider } from "@/context/ServerContext";

function AuthenticatedApp({ onLogout }: { onLogout: () => void }) {
  return (
    <ServerProvider>
      <AppShell onLogout={onLogout}>
        <Routes>
          <Route path="/" element={<DashboardPage />} />
          <Route path="/server" element={<ServerPage />} />
          <Route path="/files" element={<FileManagerPage />} />
          <Route path="/editor" element={<EditorPage />} />
          <Route path="/logs" element={<LogsPage />} />
          <Route path="/settings" element={<SettingsPage />} />
          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </AppShell>
    </ServerProvider>
  );
}

export default function App() {
  const { t, i18n } = useTranslation();
  const [authenticated, setAuthenticated] = useState(false);
  const [checkingSession, setCheckingSession] = useState(true);
  const navigate = useNavigate();

  useEffect(() => {
    document.title = t("app.name");
    document.documentElement.lang = i18n.resolvedLanguage === "ru" ? "ru" : "en";
  }, [i18n.resolvedLanguage, t]);

  useEffect(() => {
    let mounted = true;
    void api.session()
      .then(() => { if (mounted) setAuthenticated(true); })
      .catch(() => { if (mounted) setAuthenticated(false); })
      .finally(() => { if (mounted) setCheckingSession(false); });

    const handleUnauthorized = () => {
      if (mounted) {
        setAuthenticated(false);
        navigate("/");
      }
    };
    window.addEventListener("auth:unauthorized", handleUnauthorized);

    return () => {
      mounted = false;
      window.removeEventListener("auth:unauthorized", handleUnauthorized);
    };
  }, [navigate]);

  useEffect(() => {
    if (!authenticated) return;
    const timer = window.setInterval(() => { void api.refresh().catch(() => undefined); }, 30 * 60 * 1000);
    return () => window.clearInterval(timer);
  }, [authenticated]);

  async function logout() {
    try { await api.logout(); } finally { setAuthenticated(false); navigate("/"); }
  }

  if (checkingSession) return <main className="flex min-h-screen items-center justify-center bg-[#0b1220] text-slate-200">{t("login.checking")}</main>;
  if (!authenticated) return <LoginPage onLogin={() => setAuthenticated(true)} />;
  return <AuthenticatedApp onLogout={() => void logout()} />;
}
