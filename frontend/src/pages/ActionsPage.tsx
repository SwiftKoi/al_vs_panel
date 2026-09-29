import { useEffect, useState, type ComponentType, type ReactNode } from "react";
import { AlertTriangle, Ban, ChevronRight, Gamepad2, Gavel, LandPlot, Loader2, MapPin, ShieldCheck, UserCog, UserX } from "lucide-react";
import { useTranslation } from "react-i18next";
import { serverApi, type LandClaimSetting, type TeleportCoordinates } from "@/api/servers";
import PageHeader from "@/components/layout/PageHeader";
import Modal from "@/components/ui/Modal";
import Panel from "@/components/ui/Panel";
import Select from "@/components/ui/Select";
import { useServer } from "@/context/ServerContext";
import { useToast } from "@/context/ToastContext";

type ActionId = "gameMode" | "teleport" | "warn" | "kick" | "ban" | "hardban" | "unban" | "landClaim" | "classReselect";

// Each action is a card; clicking it opens that action's dialog. Add new actions here and in DIALOGS.
const ACTIONS: { id: ActionId; icon: ComponentType<{ size?: number }> }[] = [
  { id: "gameMode", icon: Gamepad2 },
  { id: "teleport", icon: MapPin },
  { id: "warn", icon: AlertTriangle },
  { id: "kick", icon: UserX },
  { id: "ban", icon: Ban },
  { id: "hardban", icon: Gavel },
  { id: "unban", icon: ShieldCheck },
  { id: "landClaim", icon: LandPlot },
  { id: "classReselect", icon: UserCog }
];

const GAME_MODES = [0, 1, 2] as const;
const COORDINATE_TYPES: TeleportCoordinates[] = ["pretty", "absolute", "relative"];
const COORDINATE_PREFIX: Record<TeleportCoordinates, string> = { pretty: "", absolute: "=", relative: "~" };
const MAX_REASON = 200;
const LAND_CLAIM_SETTINGS: LandClaimSetting[] = ["allowance", "maxAreas"];
// Extra allowance has no game limit (int max); extra areas are capped at 9999.
const MAX_LAND_CLAIM: Record<LandClaimSetting, number> = { allowance: 2_147_483_647, maxAreas: 9999 };
const inputClass = "h-10 w-full rounded-md border border-red-950/45 bg-slate-950/40 px-3 text-sm font-medium text-slate-100 outline-none transition-all placeholder:text-slate-500 hover:border-red-900/60 focus:border-[#e04444] focus:ring-2 focus:ring-[#b8282e]/25";

interface DialogProps {
  serverId: string;
  onClose: () => void;
}

function useOnlinePlayers(serverId: string) {
  const [players, setPlayers] = useState<string[] | null>(null);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    let cancelled = false;
    serverApi.connections(serverId)
      .then((response) => {
        if (cancelled) return;
        setPlayers([...new Set(response.connections.map((c) => c.playerName).filter((n): n is string => !!n))]
          .sort((a, b) => a.localeCompare(b)));
      })
      .catch(() => {
        if (!cancelled) setFailed(true);
      });
    return () => { cancelled = true; };
  }, [serverId]);

  return { players, failed };
}

function Field({ label, hint, children }: { label: string; hint?: string; children: ReactNode }) {
  return (
    <div>
      <div className="mb-1.5 text-[11px] font-semibold uppercase tracking-wider text-slate-400">{label}</div>
      {children}
      {hint && <p className="mt-1 text-[11px] text-slate-500">{hint}</p>}
    </div>
  );
}

/** Online-player picker with typing. With allowOffline, any typed name is accepted (e.g. to ban someone who left). */
function PlayerField({ serverId, value, onChange, allowOffline = false }: {
  serverId: string;
  value: string;
  onChange: (name: string) => void;
  allowOffline?: boolean;
}) {
  const { t } = useTranslation();
  const { players, failed } = useOnlinePlayers(serverId);

  // Pre-select the only online player; offline-capable pickers start empty on purpose.
  useEffect(() => {
    if (!allowOffline && players?.length === 1 && !value) onChange(players[0]);
  }, [players, allowOffline, value, onChange]);

  let body: ReactNode;
  if (!allowOffline && failed) body = <p className="text-red-300">{t("actions.loadFailed")}</p>;
  else if (!allowOffline && players === null) body = <Loader2 size={16} className="animate-spin text-[#e04444]" />;
  else if (!allowOffline && players?.length === 0) body = <p className="text-slate-400">{t("actions.noPlayers")}</p>;
  else body = (
    <Select
      searchable
      allowCustom={allowOffline}
      value={value || null}
      onChange={onChange}
      placeholder={t(allowOffline ? "actions.typeOrChoosePlayer" : "actions.choosePlayer")}
      options={(players ?? []).map((name) => ({ value: name, label: name }))}
    />
  );

  return <Field label={t("actions.player")} hint={allowOffline ? t("actions.offlineHint") : undefined}>{body}</Field>;
}

function ReasonField({ value, onChange, required = false }: { value: string; onChange: (reason: string) => void; required?: boolean }) {
  const { t } = useTranslation();
  return (
    <Field label={t(required ? "actions.reasonRequired" : "actions.reason")}>
      <input className={inputClass} value={value} maxLength={MAX_REASON} placeholder={t("actions.reasonPlaceholder")} onChange={(e) => onChange(e.target.value)} />
    </Field>
  );
}

/** Shared dialog frame: shows the command that will be sent and runs it on Execute. */
function ActionDialog({ id, command, run, success, danger = false, onClose, children }: {
  id: ActionId;
  command: string | null;
  run: () => Promise<unknown>;
  success: string;
  danger?: boolean;
  onClose: () => void;
  children: ReactNode;
}) {
  const { t } = useTranslation();
  const toast = useToast();
  const [sending, setSending] = useState(false);
  const Icon = ACTIONS.find((a) => a.id === id)!.icon;

  const execute = async () => {
    if (!command) return;
    setSending(true);
    try {
      await run();
      toast.success(success);
      onClose();
    } catch {
      toast.error(t("actions.failed"));
    } finally {
      setSending(false);
    }
  };

  return (
    <Modal
      isOpen
      onClose={onClose}
      title={t(`actions.${id}.title`)}
      subtitle={t(`actions.${id}.description`)}
      icon={<Icon size={20} />}
      confirmLabel={t("actions.execute")}
      confirmVariant={danger ? "danger" : "primary"}
      onConfirm={execute}
      confirmDisabled={!command}
      isSubmitting={sending}
    >
      <div className="space-y-4">
        {children}
        {command && (
          <div className="text-[11px] text-slate-400">
            {t("actions.preview")}: <code className="break-all text-slate-200">{command}</code>
          </div>
        )}
      </div>
    </Modal>
  );
}

function GameModeDialog({ serverId, onClose }: DialogProps) {
  const { t } = useTranslation();
  const [player, setPlayer] = useState("");
  const [mode, setMode] = useState<number | null>(null);
  const modeName = mode === null ? "" : t(`actions.gameMode.modes.${mode}`);

  return (
    <ActionDialog
      id="gameMode"
      onClose={onClose}
      command={player && mode !== null ? `/gamemode ${player} ${mode}` : null}
      run={() => serverApi.setGameMode(serverId, player, mode!)}
      success={t("actions.gameMode.success", { player, mode: modeName })}
    >
      <PlayerField serverId={serverId} value={player} onChange={setPlayer} />
      <Field label={t("actions.gameMode.mode")}>
        <Select value={mode} onChange={setMode} placeholder={t("actions.gameMode.chooseMode")}
          options={GAME_MODES.map((value) => ({ value, label: `${value} — ${t(`actions.gameMode.modes.${value}`)}` }))} />
      </Field>
    </ActionDialog>
  );
}

function TeleportDialog({ serverId, onClose }: DialogProps) {
  const { t } = useTranslation();
  const [player, setPlayer] = useState("");
  const [type, setType] = useState<TeleportCoordinates>("pretty");
  const [coords, setCoords] = useState({ x: "", y: "", z: "" });
  const numbers = [coords.x, coords.y, coords.z].map((v) => (v.trim() === "" ? NaN : Number(v)));
  const valid = player && numbers.every(Number.isFinite);
  const prefix = COORDINATE_PREFIX[type];
  const position = numbers.map((n) => `${prefix}${n}`).join(" ");

  return (
    <ActionDialog
      id="teleport"
      onClose={onClose}
      command={valid ? `/tp ${player} ${position}` : null}
      run={() => serverApi.teleport(serverId, player, type, numbers[0], numbers[1], numbers[2])}
      success={t("actions.teleport.success", { player, position })}
    >
      <PlayerField serverId={serverId} value={player} onChange={setPlayer} />
      <Field label={t("actions.teleport.type")} hint={t(`actions.teleport.types.${type}.hint`)}>
        <Select value={type} onChange={setType} placeholder=""
          options={COORDINATE_TYPES.map((value) => ({ value, label: t(`actions.teleport.types.${value}.label`) }))} />
      </Field>
      <Field label={t("actions.teleport.coordinates")}>
        <div className="grid grid-cols-3 gap-2">
          {(["x", "y", "z"] as const).map((axis) => (
            <label key={axis} className="relative">
              <span className="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-xs font-semibold uppercase text-slate-500">{prefix}{axis}</span>
              <input
                className={`${inputClass} pl-9 tabular-nums`}
                inputMode="decimal"
                value={coords[axis]}
                onChange={(e) => setCoords((c) => ({ ...c, [axis]: e.target.value.replace(",", ".") }))}
              />
            </label>
          ))}
        </div>
      </Field>
    </ActionDialog>
  );
}

/** Warn (reason required), kick and ban (reason optional). */
function KickOrBanDialog({ serverId, onClose, id }: DialogProps & { id: "warn" | "kick" | "ban" }) {
  const { t } = useTranslation();
  const [player, setPlayer] = useState("");
  const [reason, setReason] = useState("");
  const text = reason.trim();

  return (
    <ActionDialog
      id={id}
      danger={id !== "warn"}
      onClose={onClose}
      command={player && (text || id !== "warn") ? `/${id} ${player}${text ? ` ${text}` : ""}` : null}
      run={() => serverApi[id](serverId, player, text)}
      success={t(`actions.${id}.success`, { player })}
    >
      {/* Bans also work for players who already left, so any name can be typed there. */}
      <PlayerField serverId={serverId} value={player} onChange={setPlayer} allowOffline={id === "ban"} />
      <ReasonField value={reason} onChange={setReason} required={id === "warn"} />
    </ActionDialog>
  );
}

/** Actions that only need a player: hard ban (any name) and class re-select (online players). */
function PlayerOnlyDialog({ serverId, onClose, id }: DialogProps & { id: "hardban" | "classReselect" }) {
  const { t } = useTranslation();
  const [player, setPlayer] = useState("");
  const command = id === "hardban" ? `/hardban ${player}` : `/player ${player} allowcharselonce`;

  return (
    <ActionDialog
      id={id}
      danger={id === "hardban"}
      onClose={onClose}
      command={player ? command : null}
      run={() => (id === "hardban" ? serverApi.hardban(serverId, player) : serverApi.allowClassReselect(serverId, player))}
      success={t(`actions.${id}.success`, { player })}
    >
      <PlayerField serverId={serverId} value={player} onChange={setPlayer} allowOffline={id === "hardban"} />
    </ActionDialog>
  );
}

function LandClaimDialog({ serverId, onClose }: DialogProps) {
  const { t } = useTranslation();
  const [player, setPlayer] = useState("");
  const [setting, setSetting] = useState<LandClaimSetting>("allowance");
  const [value, setValue] = useState("");
  const max = MAX_LAND_CLAIM[setting];
  const number = /^\d{1,10}$/.test(value) ? Number(value) : NaN;
  const valid = player && number >= 0 && number <= max;
  const name = setting === "allowance" ? "landclaimallowance" : "landclaimmaxareas";

  return (
    <ActionDialog
      id="landClaim"
      onClose={onClose}
      command={valid ? `/player ${player} ${name} ${number}` : null}
      run={() => serverApi.setLandClaim(serverId, player, setting, number)}
      success={t("actions.landClaim.success", { player, setting: t(`actions.landClaim.settings.${setting}.label`), value: number })}
    >
      <PlayerField serverId={serverId} value={player} onChange={setPlayer} />
      <Field label={t("actions.landClaim.setting")} hint={t(`actions.landClaim.settings.${setting}.hint`)}>
        <Select value={setting} onChange={setSetting} placeholder=""
          options={LAND_CLAIM_SETTINGS.map((s) => ({ value: s, label: t(`actions.landClaim.settings.${s}.label`) }))} />
      </Field>
      <Field label={t("actions.landClaim.value")} hint={t("actions.landClaim.valueHint", { max: max.toLocaleString() })}>
        <input className={`${inputClass} tabular-nums`} inputMode="numeric" maxLength={String(max).length} value={value} placeholder="0"
          onChange={(e) => setValue(e.target.value.replace(/\D/g, ""))} />
      </Field>
    </ActionDialog>
  );
}

function UnbanDialog({ serverId, onClose }: DialogProps) {
  const { t } = useTranslation();
  const [player, setPlayer] = useState("");
  const name = player.trim();

  return (
    <ActionDialog
      id="unban"
      onClose={onClose}
      command={name ? `/unban ${name}` : null}
      run={() => serverApi.unban(serverId, name)}
      success={t("actions.unban.success", { player: name })}
    >
      <Field label={t("actions.player")} hint={t("actions.unban.hint")}>
        <input className={inputClass} value={player} maxLength={32} autoFocus placeholder={t("actions.typePlayer")} onChange={(e) => setPlayer(e.target.value)} />
      </Field>
    </ActionDialog>
  );
}

const DIALOGS: Record<ActionId, (props: DialogProps) => ReactNode> = {
  gameMode: GameModeDialog,
  teleport: TeleportDialog,
  warn: (props) => <KickOrBanDialog {...props} id="warn" />,
  kick: (props) => <KickOrBanDialog {...props} id="kick" />,
  ban: (props) => <KickOrBanDialog {...props} id="ban" />,
  hardban: (props) => <PlayerOnlyDialog {...props} id="hardban" />,
  unban: UnbanDialog,
  landClaim: LandClaimDialog,
  classReselect: (props) => <PlayerOnlyDialog {...props} id="classReselect" />
};

export default function ActionsPage() {
  const { t } = useTranslation();
  const { selectedServer } = useServer();
  const [open, setOpen] = useState<ActionId | null>(null);
  const online = selectedServer?.status === "online";
  const Dialog = open ? DIALOGS[open] : null;

  return (
    <div className="space-y-6">
      <PageHeader title={t("actions.title")} description={t("actions.description")} />

      {!online && <Panel className="p-4 text-sm text-slate-400">{t("actions.offline")}</Panel>}

      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
        {ACTIONS.map(({ id, icon: Icon }) => (
          <button
            key={id}
            type="button"
            disabled={!online}
            onClick={() => setOpen(id)}
            className="group glass-panel rounded-xl p-5 text-left transition-colors hover:border-red-900/60 disabled:opacity-50 disabled:cursor-not-allowed cursor-pointer"
          >
            <div className="flex items-start gap-3">
              <div className="rounded-lg bg-[#b8282e]/10 p-2.5 text-[#e04444]"><Icon size={22} /></div>
              <div className="min-w-0 flex-1">
                <div role="heading" aria-level={2} className="text-base font-semibold text-slate-100">{t(`actions.${id}.title`)}</div>
                <p className="mt-1 text-sm text-slate-400">{t(`actions.${id}.description`)}</p>
              </div>
              <ChevronRight size={18} className="mt-1 shrink-0 text-slate-500 transition-transform group-hover:translate-x-0.5 group-hover:text-slate-300" />
            </div>
          </button>
        ))}
      </div>

      {Dialog && selectedServer && <Dialog serverId={selectedServer.id} onClose={() => setOpen(null)} />}
    </div>
  );
}
