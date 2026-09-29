import { useEffect, useMemo, useState, type ReactNode } from "react";
import { useSearchParams } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { Crosshair, ExternalLink, Loader2, MapPin, Search, User } from "lucide-react";
import Panel from "@/components/ui/Panel";
import Button from "@/components/ui/Button";
import { ApiError } from "@/api/client";
import {
  serverLogsApi,
  type ItemTotal,
  type LocationReport,
  type LogEntry,
  type PlayerActivity,
  type PlayerSummary
} from "@/api/serverLogs";
import { formatToken } from "@/lib/logQuery";
import { cn } from "@/lib/cn";
import RangePicker from "@/components/server-logs/RangePicker";
import { parseRange, resolveRange, writeRange, type RangePreset, type TimeRange } from "@/components/server-logs/logStyles";

const PRESETS: readonly RangePreset[] = ["24h", "7d", "30d"];

function errorMessage(error: unknown, fallback: string) {
  return error instanceof ApiError && error.message ? error.message : fallback;
}

function Section({ title, children, empty }: { title: string; children: ReactNode; empty?: boolean }) {
  const { t } = useTranslation();
  return (
    <div>
      <div role="heading" aria-level={3} className="mb-2 text-[11px] font-semibold uppercase tracking-wider text-slate-400">{title}</div>
      {empty ? <p className="text-xs italic text-slate-400">{t("serverLogs.players.none")}</p> : children}
    </div>
  );
}

function ItemTable({ items, onItem }: { items: ItemTotal[]; onItem: (item: string) => void }) {
  const { t, i18n } = useTranslation();
  const number = new Intl.NumberFormat(i18n.language);
  return (
    <table className="w-full table-fixed text-xs">
      <tbody>
        {items.map((item) => (
          <tr key={item.item} className="border-b border-slate-800/60 last:border-0">
            <td className="py-1 pr-2">
              <button type="button" onClick={() => onItem(item.item)} className="break-all text-left font-mono text-slate-200 hover:text-white hover:underline cursor-pointer">
                {item.item}
              </button>
            </td>
            <td className="w-12 py-1 text-right align-top tabular-nums text-slate-100">{number.format(item.quantity)}</td>
            <td className="w-12 py-1 pl-2 text-right align-top tabular-nums text-slate-400" title={t("serverLogs.players.timesHint")}>{number.format(item.events)}×</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

const ENTRY_PREVIEW = 8;

/** Newest lines first; long lists show a preview and expand in place instead of scrolling inside the page. */
function EntryList({ entries, format }: { entries: LogEntry[]; format: Intl.DateTimeFormat }) {
  const { t } = useTranslation();
  const [all, setAll] = useState(false);
  const shown = all ? entries : entries.slice(0, ENTRY_PREVIEW);
  return (
    <div>
      <ul className="space-y-1 font-mono text-[11px]">
        {shown.map((entry) => (
          <li key={entry.id} className="flex gap-2">
            <span className="shrink-0 tabular-nums text-slate-400">{format.format(new Date(entry.timestamp))}</span>
            <span className="min-w-0 break-all text-slate-200">{entry.message}</span>
          </li>
        ))}
      </ul>
      {entries.length > ENTRY_PREVIEW && (
        <button type="button" onClick={() => setAll((value) => !value)} className="mt-1.5 text-[11px] text-sky-300 hover:text-sky-200 cursor-pointer">
          {all ? t("serverLogs.players.showLess") : t("serverLogs.players.showAll", { count: entries.length })}
        </button>
      )}
    </div>
  );
}

/** Who did what (per player), and who did what at a spot (location lookup). Built from the audit log. */
export default function PlayersTab({ serverId }: { serverId: string }) {
  const { t, i18n } = useTranslation();
  const [params, setParams] = useSearchParams();
  const rangeKey = `${params.get("range") ?? ""}|${params.get("from") ?? ""}|${params.get("to") ?? ""}`;
  const range = useMemo(() => parseRange(params, "7d"), [rangeKey]);
  const selected = params.get("player");
  const placeKey = ["x", "y", "z", "r"].map((key) => params.get(key) ?? "").join("|");
  const place = useMemo(() => {
    const x = Number(params.get("x"));
    const z = Number(params.get("z"));
    if (params.get("x") === null || params.get("z") === null || !Number.isFinite(x) || !Number.isFinite(z)) return null;
    const y = params.get("y");
    return { x, y: y !== null && y !== "" && Number.isFinite(Number(y)) ? Number(y) : null, z, radius: Math.min(1000, Math.max(1, Number(params.get("r")) || 32)) };
  }, [placeKey]);

  const [players, setPlayers] = useState<PlayerSummary[]>();
  const [filter, setFilter] = useState("");
  const [activity, setActivity] = useState<PlayerActivity>();
  const [location, setLocation] = useState<LocationReport>();
  const [error, setError] = useState<string>();
  const [form, setForm] = useState({ x: place?.x?.toString() ?? "", y: place?.y?.toString() ?? "", z: place?.z?.toString() ?? "", r: place?.radius?.toString() ?? "32" });

  const number = new Intl.NumberFormat(i18n.language);
  const dateTime = useMemo(() => new Intl.DateTimeFormat(i18n.language, { dateStyle: "short", timeStyle: "short" }), [i18n.language]);
  const dayLabel = useMemo(() => new Intl.DateTimeFormat(i18n.language, { day: "numeric", month: "short" }), [i18n.language]);
  const rangeParams = useMemo(() => resolveRange(range), [range]);

  const update = (change: (next: URLSearchParams) => void, push = false) => setParams((current) => {
    const next = new URLSearchParams(current);
    change(next);
    return next;
  }, { replace: !push });

  useEffect(() => {
    let active = true;
    serverLogsApi.players(serverId, rangeParams.from, rangeParams.to)
      .then((result) => { if (active) setPlayers(result.players); })
      .catch((err) => { if (active) setError(errorMessage(err, t("serverLogs.errors.load"))); });
    return () => { active = false; };
  }, [serverId, rangeParams, t]);

  useEffect(() => {
    if (!selected) { setActivity(undefined); return; }
    let active = true;
    setActivity(undefined);
    serverLogsApi.playerActivity(serverId, selected, rangeParams.from, rangeParams.to)
      .then((result) => { if (active) setActivity(result); })
      .catch((err) => { if (active) setError(errorMessage(err, t("serverLogs.errors.load"))); });
    return () => { active = false; };
  }, [serverId, selected, rangeParams, t]);

  useEffect(() => {
    if (!place) { setLocation(undefined); return; }
    let active = true;
    setLocation(undefined);
    serverLogsApi.location(serverId, place, rangeParams.from, rangeParams.to)
      .then((result) => { if (active) setLocation(result); })
      .catch((err) => { if (active) setError(errorMessage(err, t("serverLogs.errors.load"))); });
    return () => { active = false; };
  }, [serverId, place, rangeParams, t]);

  const openSearch = (query: string) => update((next) => {
    next.set("tab", "search");
    next.set("q", query);
    ["player", "x", "y", "z", "r"].forEach((key) => next.delete(key));
  }, true);

  const selectPlayer = (name: string) => update((next) => {
    next.set("player", name);
    ["x", "y", "z", "r"].forEach((key) => next.delete(key));
  }, true);

  const showPlace = (x: number, y: number | null, z: number, radius = 32) => {
    setForm({ x: String(x), y: y === null ? "" : String(y), z: String(z), r: String(radius) });
    update((next) => {
      next.delete("player");
      next.set("x", String(x));
      if (y === null) next.delete("y"); else next.set("y", String(y));
      next.set("z", String(z));
      next.set("r", String(radius));
    }, true);
  };

  const nearValue = (x: number, y: number | null, z: number, radius: number) => `${x},${y === null ? "" : `${y},`}${z}~${radius}`;

  const visiblePlayers = (players ?? []).filter((player) => player.name.toLowerCase().includes(filter.trim().toLowerCase()));
  const maxDay = Math.max(1, ...(activity?.days ?? []).map((day) => day.actions + day.rejectedPositions));
  const input = "h-8 w-full rounded-md border border-slate-600/70 bg-slate-950/60 px-2 font-mono text-xs text-slate-100 focus:border-[#b8282e]/70 focus:outline-none";

  return (
    <div className="space-y-4">
      <Panel className="space-y-4 p-4">
        <RangePicker value={range} onChange={(value: TimeRange) => update((next) => writeRange(next, value))} presets={PRESETS} />
        <form
          className="flex flex-wrap items-end gap-2 border-t border-slate-800/70 pt-4"
          onSubmit={(event) => {
            event.preventDefault();
            const x = Number.parseInt(form.x, 10);
            const z = Number.parseInt(form.z, 10);
            if (Number.isNaN(x) || Number.isNaN(z)) return;
            const y = form.y.trim() === "" ? null : Number.parseInt(form.y, 10);
            showPlace(x, Number.isNaN(y as number) ? null : y, z, Number.parseInt(form.r, 10) || 32);
          }}
        >
          <div className="w-full">
            <div className="flex items-center gap-1.5 text-xs font-semibold text-slate-200">
              <MapPin size={14} className="text-sky-300" /> {t("serverLogs.players.location.heading")}
            </div>
            <p className="mt-0.5 text-[11px] text-slate-400">{t("serverLogs.players.location.help")}</p>
          </div>
          {(["x", "y", "z", "r"] as const).map((key) => (
            <label key={key} className="w-[calc(50%-0.25rem)] text-[11px] uppercase tracking-wide text-slate-400 sm:w-24">
              {t(`serverLogs.players.location.${key}`)}
              <input
                value={form[key]}
                onChange={(event) => setForm((current) => ({ ...current, [key]: event.target.value }))}
                inputMode="numeric"
                placeholder={key === "y" ? t("serverLogs.players.location.optional") : undefined}
                className={input}
              />
            </label>
          ))}
          <Button type="submit" variant="primary" className="h-8 w-full sm:w-auto">
            <Search size={13} /> {t("serverLogs.players.location.submit")}
          </Button>
        </form>
      </Panel>

      {error && <Panel className="p-4 text-sm text-rose-300">{error}</Panel>}

      <div className="grid gap-4 lg:grid-cols-[16rem_minmax(0,1fr)]">
        <Panel className="h-fit p-3 lg:sticky lg:top-4">
          <input value={filter} onChange={(event) => setFilter(event.target.value)} placeholder={t("serverLogs.players.filter")} className={cn(input, "mb-2 font-sans")} />
          {!players ? (
            <div className="flex justify-center py-6"><Loader2 size={18} className="animate-spin text-[#e04444]" /></div>
          ) : visiblePlayers.length === 0 ? (
            <p className="py-4 text-center text-xs text-slate-400">{t("serverLogs.players.empty")}</p>
          ) : (
            <ul className="max-h-80 space-y-0.5 overflow-y-auto pb-6 [mask-image:linear-gradient(to_bottom,black_calc(100%-2rem),transparent)] lg:max-h-[70vh]">
              {visiblePlayers.map((player) => (
                <li key={player.name}>
                  <button
                    type="button"
                    onClick={() => selectPlayer(player.name)}
                    className={cn(
                      "w-full rounded px-2 py-1.5 text-left cursor-pointer",
                      selected?.toLowerCase() === player.name.toLowerCase() ? "bg-[#b8282e]/20" : "hover:bg-slate-800/60"
                    )}
                  >
                    <div className="flex items-center justify-between gap-2 text-xs">
                      <span className="truncate font-medium text-slate-100">{player.name}</span>
                      <span className="shrink-0 tabular-nums text-slate-400">{number.format(player.actions)}</span>
                    </div>
                    <div className="mt-0.5 flex flex-wrap gap-x-2 text-[11px] text-slate-400">
                      <span>{dateTime.format(new Date(player.lastSeen))}</span>
                      {player.commands > 0 && <span>{t("serverLogs.players.commandsShort", { count: player.commands })}</span>}
                      {player.deaths > 0 && <span>{t("serverLogs.players.deathsShort", { count: player.deaths })}</span>}
                      {player.rejectedPositions > 0 && (
                        <span className={player.rejectedPositions >= 100 ? "text-amber-300" : undefined}>{t("serverLogs.players.rejectedShort", { count: player.rejectedPositions })}</span>
                      )}
                    </div>
                  </button>
                </li>
              ))}
            </ul>
          )}
        </Panel>

        <div className="min-w-0 space-y-4">
          {place ? (
            <Panel className="space-y-4 p-4">
              <div className="flex flex-wrap items-center gap-2">
                <Crosshair size={16} className="text-sky-300" />
                <h2 className="font-sans text-sm font-bold text-slate-100">
                  {t("serverLogs.players.location.title", { x: place.x, y: place.y ?? "—", z: place.z, radius: place.radius })}
                </h2>
                <Button className="ml-auto h-8" onClick={() => openSearch(formatToken("near", nearValue(place.x, place.y, place.z, place.radius)))}>
                  <Search size={13} /> {t("serverLogs.players.openSearch")}
                </Button>
              </div>
              {!location ? (
                <div className="flex justify-center py-10"><Loader2 size={20} className="animate-spin text-[#e04444]" /></div>
              ) : location.players.length === 0 ? (
                <p className="text-sm text-slate-300">{t("serverLogs.players.location.empty")}</p>
              ) : (
                <>
                  <Section title={t("serverLogs.players.location.who")}>
                    <table className="w-full text-xs">
                      <tbody>
                        {location.players.map((player) => (
                          <tr key={player.name} className="border-b border-slate-800/60 align-top last:border-0">
                            <td className="py-1.5 pr-3">
                              <button type="button" onClick={() => selectPlayer(player.name)} className="flex items-center gap-1 font-medium text-emerald-300 hover:text-emerald-200 cursor-pointer">
                                <User size={11} /> {player.name}
                              </button>
                            </td>
                            <td className="py-1.5 pr-3">
                              <div className="flex flex-wrap gap-1">
                                {player.actions.map((action) => (
                                  <button
                                    key={action.value}
                                    type="button"
                                    onClick={() => openSearch(`${formatToken("near", nearValue(place.x, place.y, place.z, place.radius))} ${formatToken("player", player.name)} ${formatToken("action", action.value)}`)}
                                    className="rounded border border-slate-600/70 px-1.5 text-[11px] text-slate-300 hover:border-[#b8282e]/60 hover:text-white cursor-pointer"
                                  >
                                    {t(`serverLogs.actions.${action.value}`, { defaultValue: action.value })} {number.format(action.count)}
                                  </button>
                                ))}
                              </div>
                            </td>
                            <td className="hidden whitespace-nowrap py-1.5 text-right text-[11px] text-slate-400 sm:table-cell">
                              {dateTime.format(new Date(player.firstSeen))} – {dateTime.format(new Date(player.lastSeen))}
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </Section>
                  <div className="grid gap-4 md:grid-cols-2">
                    <Section title={t("serverLogs.players.taken")} empty={location.taken.length === 0}>
                      <ItemTable items={location.taken} onItem={(item) => openSearch(`${formatToken("near", nearValue(place.x, place.y, place.z, place.radius))} ${formatToken("item", item)} action:take`)} />
                    </Section>
                    <Section title={t("serverLogs.players.put")} empty={location.put.length === 0}>
                      <ItemTable items={location.put} onItem={(item) => openSearch(`${formatToken("near", nearValue(place.x, place.y, place.z, place.radius))} ${formatToken("item", item)} action:put`)} />
                    </Section>
                  </div>
                </>
              )}
            </Panel>
          ) : !selected ? (
            <Panel className="p-8 text-center text-sm text-slate-300">{t("serverLogs.players.pick")}</Panel>
          ) : !activity ? (
            <Panel className="flex justify-center py-16"><Loader2 size={22} className="animate-spin text-[#e04444]" /></Panel>
          ) : (
            <Panel className="space-y-5 p-4">
              <div className="flex flex-wrap items-center gap-2">
                <User size={16} className="text-emerald-300" />
                <h2 className="font-sans text-base font-bold text-slate-100">{activity.name}</h2>
                {activity.firstSeen && activity.lastSeen && (
                  <span className="text-xs text-slate-400">{dateTime.format(new Date(activity.firstSeen))} – {dateTime.format(new Date(activity.lastSeen))}</span>
                )}
                <Button className="ml-auto h-8" onClick={() => openSearch(formatToken("player", activity.name))}>
                  <Search size={13} /> {t("serverLogs.players.openSearch")}
                </Button>
              </div>

              {activity.days.length > 0 && (
                <Section title={t("serverLogs.players.perDay")}>
                  <div className="flex h-16 items-end gap-1 border-b border-slate-700/70">
                    {activity.days.map((day) => (
                      <div
                        key={day.day}
                        className="flex h-full flex-1 flex-col justify-end"
                        title={t("serverLogs.players.dayHint", { day: new Date(day.day).toLocaleDateString(i18n.language), actions: day.actions, rejected: day.rejectedPositions })}
                      >
                        {day.rejectedPositions > 0 && <div className="w-full bg-amber-400/60" style={{ height: `${(day.rejectedPositions / maxDay) * 100}%` }} />}
                        <div className="w-full rounded-t-sm bg-[#b8282e]/70" style={{ height: `${(day.actions / maxDay) * 100}%` }} />
                      </div>
                    ))}
                  </div>
                  <div className="mt-1 flex gap-1 text-[10px] tabular-nums text-slate-400">
                    {activity.days.map((day, index) => (
                      <span key={day.day} className="flex-1 truncate text-center">
                        {activity.days.length <= 14 || index % Math.ceil(activity.days.length / 7) === 0 ? dayLabel.format(new Date(day.day)) : ""}
                      </span>
                    ))}
                  </div>
                  <p className="mt-1.5 flex flex-wrap gap-x-3 text-[11px] text-slate-400">
                    <span className="flex items-center gap-1"><span className="h-2 w-2 rounded-sm bg-[#b8282e]/70" /> {t("serverLogs.players.perDayActions")}</span>
                    {activity.days.some((day) => day.rejectedPositions > 0) && (
                      <span className="flex items-center gap-1"><span className="h-2 w-2 rounded-sm bg-amber-400/60" /> {t("serverLogs.players.perDayRejected")}</span>
                    )}
                  </p>
                </Section>
              )}

              <Section title={t("serverLogs.players.actions")} empty={activity.actions.length === 0}>
                <div className="flex flex-wrap gap-1.5">
                  {activity.actions.map((action) => (
                    <button
                      key={action.value}
                      type="button"
                      onClick={() => openSearch(`${formatToken("player", activity.name)} ${formatToken("action", action.value)}`)}
                      className="rounded-full border border-slate-600/70 px-2.5 py-0.5 text-[11px] text-slate-300 hover:border-[#b8282e]/60 hover:text-white cursor-pointer"
                    >
                      {t(`serverLogs.actions.${action.value}`, { defaultValue: action.value })} <span className="tabular-nums text-slate-400">{number.format(action.count)}</span>
                    </button>
                  ))}
                </div>
              </Section>

              <div className="grid gap-5 md:grid-cols-2">
                <Section title={t("serverLogs.players.taken")} empty={activity.taken.length === 0}>
                  <ItemTable items={activity.taken} onItem={(item) => openSearch(`${formatToken("player", activity.name)} ${formatToken("item", item)} action:take`)} />
                </Section>
                <Section title={t("serverLogs.players.put")} empty={activity.put.length === 0}>
                  <ItemTable items={activity.put} onItem={(item) => openSearch(`${formatToken("player", activity.name)} ${formatToken("item", item)} action:put`)} />
                </Section>
                <Section title={t("serverLogs.players.places")} empty={activity.places.length === 0}>
                  <ul className="space-y-0.5 text-xs">
                    {activity.places.map((spot) => (
                      <li key={`${spot.x},${spot.z}`} className="flex flex-wrap items-center justify-between gap-x-2">
                        <button type="button" onClick={() => showPlace(spot.x, spot.y, spot.z)} className="flex items-center gap-1 font-mono text-sky-300 hover:text-sky-200 cursor-pointer">
                          <Crosshair size={11} /> {spot.x}, {spot.y ?? "—"}, {spot.z}
                        </button>
                        <span className="tabular-nums text-slate-400">{number.format(spot.count)}× · {dateTime.format(new Date(spot.lastSeen))}</span>
                      </li>
                    ))}
                  </ul>
                </Section>
                <Section title={t("serverLogs.players.kills")} empty={activity.kills.length === 0}>
                  <ul className="space-y-0.5 text-xs">
                    {activity.kills.map((kill) => (
                      <li key={kill.value} className="flex justify-between gap-2">
                        <span className="min-w-0 break-all font-mono text-slate-200">{kill.value}</span>
                        <span className="shrink-0 tabular-nums text-slate-300">{number.format(kill.count)}</span>
                      </li>
                    ))}
                  </ul>
                </Section>
              </div>

              <Section title={t("serverLogs.players.commands")} empty={activity.commands.length === 0}>
                <EntryList entries={activity.commands} format={dateTime} />
              </Section>
              <div className="grid gap-5 md:grid-cols-2">
                <Section title={t("serverLogs.players.deaths")} empty={activity.deaths.length === 0}>
                  <EntryList entries={activity.deaths} format={dateTime} />
                </Section>
                <Section title={t("serverLogs.players.sessions")} empty={activity.sessions.length === 0}>
                  <EntryList entries={activity.sessions} format={dateTime} />
                </Section>
              </div>
              <p className="flex items-center gap-1 text-[11px] text-slate-400">
                <ExternalLink size={11} /> {t("serverLogs.players.analyticsHint")}
              </p>
            </Panel>
          )}
        </div>
      </div>
    </div>
  );
}
