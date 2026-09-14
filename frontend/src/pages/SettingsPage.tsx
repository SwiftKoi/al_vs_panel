import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { QRCodeSVG } from "qrcode.react";
import { api, type UserResponse, type TwoFactorSetupResponse, ApiError } from "@/api/client";
import Button from "@/components/ui/Button";
import { Shield, Users, UserPlus, Trash2, Key, CheckCircle, AlertTriangle } from "lucide-react";

type SettingsTab = "security" | "users";

export default function SettingsPage() {
  const { t } = useTranslation();
  const [activeTab, setActiveTab] = useState<SettingsTab>("security");

  // Session & Current User
  const [currentUserId, setCurrentUserId] = useState<string | null>(null);

  // Users List State
  const [users, setUsers] = useState<UserResponse[]>([]);
  const [loadingUsers, setLoadingUsers] = useState(false);
  const [usersError, setUsersError] = useState<string | null>(null);

  // Create User State
  const [isCreateModalOpen, setIsCreateModalOpen] = useState(false);
  const [newUsername, setNewUsername] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [createError, setCreateError] = useState<string | null>(null);
  const [createBusy, setCreateBusy] = useState(false);

  // 2FA Security State
  const [twoFactorEnabled, setTwoFactorEnabled] = useState(false);
  const [checking2Fa, setChecking2Fa] = useState(true);
  const [setupData, setSetupData] = useState<TwoFactorSetupResponse | null>(null);
  const [verificationCode, setVerificationCode] = useState("");
  const [securityError, setSecurityError] = useState<string | null>(null);
  const [securityBusy, setSecurityBusy] = useState(false);

  useEffect(() => {
    // Get current logged-in user profile
    void api.session()
      .then((session) => {
        if (session.authenticated && session.user) {
          setCurrentUserId(session.user.id);
        }
      })
      .catch(() => undefined);
  }, []);

  // Fetch users & 2FA status
  useEffect(() => {
    let mounted = true;

    async function loadData() {
      if (activeTab === "users") {
        setLoadingUsers(true);
        setUsersError(null);
        try {
          const list = await api.listUsers();
          if (mounted) setUsers(list);
        } catch (err) {
          if (mounted) {
            setUsersError(err instanceof ApiError && err.message ? err.message : t("errors.load_users"));
          }
        } finally {
          if (mounted) setLoadingUsers(false);
        }
      } else {
        setChecking2Fa(true);
        setSecurityError(null);
        try {
          const list = await api.listUsers();
          // Find current user's 2FA status
          const current = list.find(u => u.id === currentUserId);
          if (mounted && current) {
            setTwoFactorEnabled(current.twoFactorEnabled);
          }
        } catch {
          // Fallback or ignore
        } finally {
          if (mounted) setChecking2Fa(false);
        }
      }
    }

    if (currentUserId) {
      void loadData();
    }

    return () => { mounted = false; };
  }, [activeTab, currentUserId, t]);

  async function handleSetup2Fa() {
    setSecurityError(null);
    setSecurityBusy(true);
    try {
      const data = await api.setup2fa();
      setSetupData(data);
    } catch (err) {
      setSecurityError(err instanceof ApiError && err.message ? err.message : t("errors.setup_2fa"));
    } finally {
      setSecurityBusy(false);
    }
  }

  async function handleVerify2Fa(e: React.FormEvent) {
    e.preventDefault();
    setSecurityError(null);
    setSecurityBusy(true);
    try {
      const response = await api.enable2fa(verificationCode);
      if (response.success) {
        setTwoFactorEnabled(true);
        setSetupData(null);
        setVerificationCode("");
      } else {
        throw new Error(t("errors.verify_2fa_failed"));
      }
    } catch (err) {
      setSecurityError(err instanceof ApiError && err.message ? err.message : t("errors.invalid_totp_code"));
    } finally {
      setSecurityBusy(false);
    }
  }

  async function handleDisable2Fa() {
    if (!window.confirm(t("settings.disable_2fa_confirm"))) return;
    setSecurityError(null);
    setSecurityBusy(true);
    try {
      const response = await api.disable2fa();
      if (response.success) {
        setTwoFactorEnabled(false);
        setSetupData(null);
      } else {
        throw new Error();
      }
    } catch (err) {
      setSecurityError(err instanceof ApiError && err.message ? err.message : t("errors.disable_2fa_failed"));
    } finally {
      setSecurityBusy(false);
    }
  }

  async function handleCreateUser(e: React.FormEvent) {
    e.preventDefault();
    setCreateError(null);
    setCreateBusy(true);
    try {
      await api.createUser(newUsername, newPassword);
      setIsCreateModalOpen(false);
      setNewUsername("");
      setNewPassword("");
      // Refresh list
      const list = await api.listUsers();
      setUsers(list);
    } catch (err) {
      setCreateError(err instanceof ApiError && err.message ? err.message : t("errors.create_user_failed"));
    } finally {
      setCreateBusy(false);
    }
  }

  async function handleDeleteUser(userId: string, username: string) {
    if (!window.confirm(t("settings.delete_user_confirm_msg", { name: username }))) return;
    setUsersError(null);
    try {
      await api.deleteUser(userId);
      setUsers(prev => prev.filter(u => u.id !== userId));
    } catch (err) {
      setUsersError(err instanceof ApiError && err.message ? err.message : t("errors.delete_user_failed"));
    }
  }

  return (
    <div className="space-y-6 max-w-none">
      <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between border-b border-red-950/20 pb-4">
        <h1 className="text-xl sm:text-2xl font-bold font-serif tracking-wide text-slate-100 glow-text">{t("settings.title")}</h1>
        <div className="flex gap-1.5 p-1 rounded-lg bg-slate-950/60 border border-red-950/20 backdrop-blur-sm shadow-inner select-none w-full sm:w-auto overflow-x-auto">
          <button
            onClick={() => setActiveTab("security")}
            className={`flex flex-1 sm:flex-none items-center justify-center gap-1.5 whitespace-nowrap px-3 py-1.5 text-xs font-semibold rounded-md transition-all cursor-pointer ${
              activeTab === "security"
                ? "bg-[#b8282e]/20 text-white border-b border-[#e04444] glow-text"
                : "text-slate-400 hover:text-slate-200 hover:bg-red-950/10"
            }`}
          >
            <Shield size={14} className="text-[#e04444] shrink-0" />
            {t("settings.security_tab")}
          </button>
          <button
            onClick={() => setActiveTab("users")}
            className={`flex flex-1 sm:flex-none items-center justify-center gap-1.5 whitespace-nowrap px-3 py-1.5 text-xs font-semibold rounded-md transition-all cursor-pointer ${
              activeTab === "users"
                ? "bg-[#b8282e]/20 text-white border-b border-[#e04444] glow-text"
                : "text-slate-400 hover:text-slate-200 hover:bg-red-950/10"
            }`}
          >
            <Users size={14} className="text-[#e04444] shrink-0" />
            {t("settings.users_tab")}
          </button>
        </div>
      </div>

      {activeTab === "security" ? (
        <div className="rounded-xl glass-panel p-6 shadow-xl space-y-6">
          <div className="flex items-center gap-3">
            <div className="p-2 rounded-lg bg-[#b8282e]/10 text-[#e04444] shadow-[0_0_6px_rgba(224,68,68,0.2)] shrink-0">
              <Key size={20} />
            </div>
            <div className="min-w-0">
              <h2 className="text-base font-bold font-serif text-slate-100 glow-text tracking-wide">{t("settings.two_factor_auth")}</h2>
              <p className="text-xs text-slate-400 mt-1">{t("settings.two_factor_auth_desc")}</p>
            </div>
          </div>

          <div className="h-px bg-red-950/20" />

          {checking2Fa ? (
            <p className="text-sm text-slate-400">{t("settings.loading_security")}</p>
          ) : (
            <div className="space-y-4">
              <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-3 py-2">
                <div className="flex items-center gap-2 min-w-0">
                  {twoFactorEnabled ? (
                    <>
                      <CheckCircle size={18} className="text-emerald-400 shrink-0" />
                      <span className="text-sm font-medium text-emerald-400">{t("settings.2fa_status_enabled")}</span>
                    </>
                  ) : (
                    <>
                      <AlertTriangle size={18} className="text-amber-400 shrink-0" />
                      <span className="text-sm font-medium text-amber-400">{t("settings.2fa_status_disabled")}</span>
                    </>
                  )}
                </div>
                {twoFactorEnabled ? (
                  <Button
                    variant="danger"
                    onClick={handleDisable2Fa}
                    disabled={securityBusy}
                    className="text-xs"
                  >
                    {t("settings.disable_2fa")}
                  </Button>
                ) : !setupData ? (
                  <Button
                    variant="primary"
                    onClick={handleSetup2Fa}
                    disabled={securityBusy}
                    className="text-xs"
                  >
                    {t("settings.enable_2fa")}
                  </Button>
                ) : null}
              </div>

              {securityError && (
                <div className="p-3 rounded-lg border border-rose-500/30 bg-rose-500/10 text-sm text-rose-200">
                  {securityError}
                </div>
              )}

              {/* 2FA Setup Flow Wizard */}
              {setupData && !twoFactorEnabled && (
                <div className="border-t border-slate-500/20 pt-6 space-y-6">
                  <h3 className="text-sm font-semibold text-slate-200">{t("settings.setup_2fa_title")}</h3>
                  <p className="text-xs text-slate-400 leading-relaxed whitespace-pre-line">
                    {t("settings.2fa_setup_instructions")}
                  </p>

                  <div className="flex flex-col sm:flex-row items-center gap-6 justify-center max-w-lg mx-auto py-2">
                    <div className="bg-white p-2.5 rounded-lg shrink-0 shadow-md">
                      <QRCodeSVG value={setupData.provisioningUri} size={150} />
                    </div>
                    <div className="space-y-3 min-w-0 w-full text-center sm:text-left">
                      <p className="text-xs text-slate-300 font-medium">{t("settings.manual_entry_key")}</p>
                      <code className="block bg-slate-950/50 px-3 py-2 rounded border border-slate-800 text-xs font-mono text-blue-400 select-all break-all tracking-wider">
                        {setupData.sharedSecret}
                      </code>
                    </div>
                  </div>

                  <form onSubmit={handleVerify2Fa} className="max-w-xs mx-auto space-y-4">
                    <label className="block text-sm text-slate-300 text-center">
                      {t("settings.enter_verification_code")}
                      <input
                        type="text"
                        pattern="[0-9]*"
                        inputMode="numeric"
                        maxLength={6}
                        placeholder="000000"
                        className="mt-2 h-10 w-full rounded-md border border-slate-800 bg-slate-950/30 px-3 text-center text-lg font-mono tracking-widest outline-none focus:border-blue-400"
                        value={verificationCode}
                        onChange={(e) => setVerificationCode(e.target.value.replace(/\D/g, ""))}
                      />
                    </label>
                    <div className="flex gap-2">
                      <Button
                        type="button"
                        variant="ghost"
                        onClick={() => setSetupData(null)}
                        className="w-1/2 min-w-0 whitespace-normal text-xs"
                      >
                        {t("common.cancel")}
                      </Button>
                      <Button
                        type="submit"
                        variant="primary"
                        disabled={securityBusy || verificationCode.length < 6}
                        className="w-1/2 min-w-0 whitespace-normal text-xs"
                      >
                        {securityBusy ? t("common.saving") : t("settings.verify_and_enable")}
                      </Button>
                    </div>
                  </form>
                </div>
              )}
            </div>
          )}
        </div>
      ) : (
        <div className="rounded-xl glass-panel p-6 shadow-xl space-y-4">
          <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-3">
            <h2 className="text-base font-bold font-serif text-slate-100 glow-text tracking-wide">{t("settings.admin_users")}</h2>
            <Button
              variant="primary"
              className="h-8 text-xs flex items-center justify-center gap-1 self-start sm:self-auto"
              onClick={() => {
                setCreateError(null);
                setIsCreateModalOpen(true);
              }}
            >
              <UserPlus size={14} className="shrink-0" />
              {t("settings.add_user")}
            </Button>
          </div>

          {usersError && (
            <div className="p-3 rounded-lg border border-rose-500/30 bg-rose-500/10 text-sm text-rose-200">
              {usersError}
            </div>
          )}

          {loadingUsers ? (
            <p className="text-sm text-slate-400 py-4">{t("settings.loading_users")}</p>
          ) : (
            <div className="overflow-x-auto rounded-lg border border-slate-800 bg-slate-900/30">
              <table className="w-full border-collapse text-left text-sm text-slate-300">
                <thead className="bg-slate-800/60 text-slate-200">
                  <tr>
                    <th className="px-4 py-3 font-semibold">{t("settings.th_username")}</th>
                    <th className="px-4 py-3 font-semibold">{t("settings.th_2fa_status")}</th>
                    <th className="px-4 py-3 text-right font-semibold">{t("settings.th_actions")}</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-800/50">
                  {users.map((u) => (
                    <tr key={u.id} className="hover:bg-slate-800/20">
                      <td className="px-4 py-3 font-medium text-slate-100 flex items-center gap-2">
                        {u.username}
                        {u.id === currentUserId && (
                          <span className="text-[10px] bg-blue-500/10 text-blue-400 px-1.5 py-0.5 rounded-full border border-blue-500/20">
                            {t("settings.badge_you")}
                          </span>
                        )}
                      </td>
                      <td className="px-4 py-3 text-xs">
                        {u.twoFactorEnabled ? (
                          <span className="text-emerald-400 bg-emerald-500/5 px-2 py-1 rounded border border-emerald-500/10">
                            {t("settings.2fa_badge_enabled")}
                          </span>
                        ) : (
                          <span className="text-slate-400 bg-slate-500/5 px-2 py-1 rounded border border-slate-500/10">
                            {t("settings.2fa_badge_disabled")}
                          </span>
                        )}
                      </td>
                      <td className="px-4 py-3 text-right">
                        <Button
                          variant="ghost"
                          disabled={u.id === currentUserId}
                          className="h-8 w-8 p-0 text-slate-400 hover:text-rose-400 disabled:opacity-30"
                          onClick={() => void handleDeleteUser(u.id, u.username)}
                          title={u.id === currentUserId ? t("settings.cannot_delete_self") : t("settings.delete_user")}
                        >
                          <Trash2 size={16} />
                        </Button>
                      </td>
                    </tr>
                  ))}
                  {users.length === 0 && (
                    <tr>
                      <td colSpan={3} className="px-4 py-8 text-center text-slate-500">
                        {t("settings.no_users")}
                      </td>
                    </tr>
                  )}
                </tbody>
              </table>
            </div>
          )}

          {/* Create User Modal Dialog */}
          {isCreateModalOpen && (
            <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/80 p-4 backdrop-blur-sm animate-fade-in">
              <div className="w-full max-w-md glass-panel rounded-xl p-6 shadow-2xl space-y-4 relative z-10 animate-in zoom-in-95 duration-150">
                <div className="flex items-center justify-between pb-3 border-b border-red-950/20">
                  <h3 className="text-base font-bold font-serif text-slate-100 glow-text tracking-wide">{t("settings.add_user_title")}</h3>
                  <button
                    onClick={() => setIsCreateModalOpen(false)}
                    className="text-slate-400 hover:text-slate-200 hover:bg-red-950/20 rounded p-1 transition-colors text-xs outline-none cursor-pointer"
                  >
                    ✕
                  </button>
                </div>

                {createError && (
                  <div className="p-3 rounded-lg border border-rose-500/30 bg-rose-500/10 text-xs text-rose-200">
                    {createError}
                  </div>
                )}

                <form onSubmit={handleCreateUser} className="space-y-4">
                  <label className="block text-xs font-semibold tracking-wider text-slate-300 uppercase">
                    {t("settings.username")}
                    <input
                      type="text"
                      className="mt-2 h-10 w-full rounded-md border border-red-950/45 bg-slate-950/40 px-3 text-slate-200 outline-none focus:border-[#e04444] focus:ring-2 focus:ring-[#b8282e]/25 transition-all duration-200 text-sm font-medium"
                      value={newUsername}
                      onChange={(e) => setNewUsername(e.target.value)}
                    />
                  </label>
                  <label className="mt-4 block text-xs font-semibold tracking-wider text-slate-300 uppercase">
                    {t("settings.password")}
                    <input
                      type="password"
                      className="mt-2 h-10 w-full rounded-md border border-red-950/45 bg-slate-950/40 px-3 text-slate-200 outline-none focus:border-[#e04444] focus:ring-2 focus:ring-[#b8282e]/25 transition-all duration-200 text-sm font-medium"
                      value={newPassword}
                      onChange={(e) => setNewPassword(e.target.value)}
                    />
                  </label>
                  <div className="flex justify-end gap-2 pt-2">
                    <Button
                      type="button"
                      variant="ghost"
                      onClick={() => setIsCreateModalOpen(false)}
                      disabled={createBusy}
                      className="text-xs"
                    >
                      {t("common.cancel")}
                    </Button>
                    <Button
                      type="submit"
                      variant="primary"
                      disabled={createBusy || !newUsername || !newPassword}
                      className="text-xs"
                    >
                      {createBusy ? t("common.saving") : t("settings.create")}
                    </Button>
                  </div>
                </form>
              </div>
            </div>
          )}
        </div>
      )}
    </div>
  );
}
