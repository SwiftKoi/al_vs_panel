import { useEffect, useState } from "react";
import { Navigate, Route, Routes, useNavigate } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { api, type AuthenticatedUser } from "@/api/client";
import AppShell from "@/components/layout/AppShell";
import DashboardPage from "@/pages/DashboardPage";
import LoginPage from "@/pages/LoginPage";
import ServerPage from "@/pages/ServerPage";
import FileManagerPage from "@/pages/FileManagerPage";
import EditorPage from "@/pages/EditorPage";
import AnalyticsPage from "@/pages/AnalyticsPage";
import LogsPage from "@/pages/LogsPage";
import AuditPage from "@/pages/AuditPage";
import ServerLogsPage from "@/pages/ServerLogsPage";
import ModsPage from "@/pages/ModsPage";
import SettingsPage from "@/pages/SettingsPage";
import ActionsPage from "@/pages/ActionsPage";
import { ServerProvider } from "@/context/ServerContext";
import { ToastProvider } from "@/context/ToastContext";
import { UploadProvider } from "@/context/UploadContext";
import { SessionProvider, useSession } from "@/context/SessionContext";
import UploadQueuePanel from "@/components/file-manager/UploadQueuePanel";

function AuthenticatedApp({ onLogout }: { onLogout: () => void }) {
  const { isAdmin } = useSession();

  // Moderators get the read-only Overview, the server log search and Actions; the backend rejects everything else.
  if (!isAdmin) {
    return (
      <ServerProvider>
        <ToastProvider>
          <AppShell onLogout={onLogout}>
            <Routes>
              <Route path="/" element={<DashboardPage />} />
              <Route path="/server-logs" element={<ServerLogsPage />} />
              <Route path="/actions" element={<ActionsPage />} />
              <Route path="/settings" element={<SettingsPage />} />
              <Route path="*" element={<Navigate to="/" replace />} />
            </Routes>
          </AppShell>
        </ToastProvider>
      </ServerProvider>
    );
  }

  return (
    <ServerProvider>
      <ToastProvider>
      {/* Uploads live above the routes so they keep running when you leave the file manager. */}
      <UploadProvider>
      <AppShell onLogout={onLogout}>
        <Routes>
          <Route path="/" element={<DashboardPage />} />
          <Route path="/server" element={<ServerPage />} />
          <Route path="/files" element={<FileManagerPage />} />
          <Route path="/editor" element={<EditorPage />} />
          <Route path="/mods" element={<ModsPage />} />
          <Route path="/analytics" element={<AnalyticsPage />} />
          <Route path="/server-logs" element={<ServerLogsPage />} />
          <Route path="/actions" element={<ActionsPage />} />
          <Route path="/logs" element={<LogsPage />} />
          <Route path="/audit" element={<AuditPage />} />
          <Route path="/settings" element={<SettingsPage />} />
          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </AppShell>
      <UploadQueuePanel />
      </UploadProvider>
      </ToastProvider>
    </ServerProvider>
  );
}

export default function App() {
  const { t, i18n } = useTranslation();
  const [user, setUser] = useState<AuthenticatedUser | null>(null);
  const [checkingSession, setCheckingSession] = useState(true);
  const navigate = useNavigate();

  useEffect(() => {
    document.title = t("app.name");
    document.documentElement.lang = i18n.resolvedLanguage === "ru" ? "ru" : "en";
  }, [i18n.resolvedLanguage, t]);

  useEffect(() => {
    let mounted = true;
    // Refreshing reissues the cookie, so its role claims match the account's current role.
    void api.session()
      .then((session) => api.refresh().catch(() => session))
      .then((session) => { if (mounted) setUser(session.user); })
      .catch(() => { if (mounted) setUser(null); })
      .finally(() => { if (mounted) setCheckingSession(false); });

    const handleUnauthorized = () => {
      if (mounted) {
        setUser(null);
        navigate("/");
      }
    };
    window.addEventListener("auth:unauthorized", handleUnauthorized);

    return () => {
      mounted = false;
      window.removeEventListener("auth:unauthorized", handleUnauthorized);
    };
  }, [navigate]);

  const authenticated = user !== null;
  useEffect(() => {
    if (!authenticated) return;
    const timer = window.setInterval(() => {
      void api.refresh().then((session) => setUser(session.user)).catch(() => undefined);
    }, 30 * 60 * 1000);
    return () => window.clearInterval(timer);
  }, [authenticated]);

  async function logout() {
    try { await api.logout(); } finally { setUser(null); navigate("/"); }
  }

  async function handleLogin() {
    try {
      setUser((await api.session()).user);
    } catch {
      setUser(null);
    }
  }

  if (checkingSession) return <main className="flex min-h-screen items-center justify-center bg-[#0b1220] text-slate-200">{t("login.checking")}</main>;
  if (!user) return <LoginPage onLogin={() => void handleLogin()} />;
  return (
    <SessionProvider user={user}>
      <AuthenticatedApp onLogout={() => void logout()} />
    </SessionProvider>
  );
}
