import { Minus } from "lucide-react";
import { useTranslation } from "react-i18next";
import type { FacetValue, LogFacets as Facets } from "@/api/serverLogs";
import { filterState } from "@/lib/logQuery";
import { cn } from "@/lib/cn";

type Group = { key: "log" | "level" | "source" | "player" | "action"; values: FacetValue[] };

/** Counts for the current result; click a value to filter by it, "−" to exclude it. */
export default function LogFacets({
  facets,
  query,
  onToggle
}: {
  facets: Facets;
  query: string;
  onToggle: (key: string, value: string, exclude: boolean) => void;
}) {
  const { t, i18n } = useTranslation();
  const number = new Intl.NumberFormat(i18n.language, { notation: "compact" });
  const groups: Group[] = [
    { key: "log", values: facets.logs },
    { key: "level", values: facets.levels },
    { key: "action", values: facets.actions },
    { key: "player", values: facets.players },
    { key: "source", values: facets.sources }
  ];

  const label = (key: Group["key"], value: string) => {
    if (key === "log") return t(`serverLogs.logs.${value}`, { defaultValue: value });
    if (key === "action") return t(`serverLogs.actions.${value}`, { defaultValue: value });
    if (key === "level") return t(`serverLogs.levels.${value.toLowerCase()}`, { defaultValue: value });
    return value;
  };

  return (
    <div className="space-y-4 text-xs">
      {groups.filter((group) => group.values.length > 0).map((group) => (
        <div key={group.key}>
          <div className="mb-1.5 text-[11px] font-semibold uppercase tracking-wider text-slate-400">{t(`serverLogs.facets.${group.key}`)}</div>
          <ul className="space-y-0.5">
            {group.values.map((facet) => {
              const state = filterState(query, group.key, facet.value);
              return (
                <li key={facet.value} className="group flex items-center gap-1">
                  <button
                    type="button"
                    onClick={() => onToggle(group.key, facet.value, false)}
                    title={facet.value}
                    className={cn(
                      "flex min-w-0 flex-1 items-center justify-between gap-2 rounded px-1.5 py-0.5 text-left cursor-pointer",
                      state === "include" ? "bg-[#b8282e]/20 text-slate-100" : "text-slate-300 hover:bg-slate-800/60",
                      state === "exclude" && "text-slate-400 line-through"
                    )}
                  >
                    <span className="truncate">{label(group.key, facet.value)}</span>
                    <span className="shrink-0 tabular-nums text-slate-400">{number.format(facet.count)}</span>
                  </button>
                  <button
                    type="button"
                    onClick={() => onToggle(group.key, facet.value, true)}
                    title={t("serverLogs.facets.exclude")}
                    aria-label={t("serverLogs.facets.exclude")}
                    className={cn(
                      "rounded p-0.5 text-slate-400 hover:bg-slate-800 hover:text-rose-300 cursor-pointer",
                      state === "exclude" ? "opacity-100 text-rose-300" : "opacity-0 group-hover:opacity-100 focus:opacity-100"
                    )}
                  >
                    <Minus size={11} />
                  </button>
                </li>
              );
            })}
          </ul>
        </div>
      ))}
    </div>
  );
}
