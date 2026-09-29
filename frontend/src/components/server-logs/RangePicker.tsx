import { useTranslation } from "react-i18next";
import { cn } from "@/lib/cn";
import { RANGE_PRESETS, type RangePreset, type TimeRange } from "@/components/server-logs/logStyles";

export default function RangePicker({
  value,
  onChange,
  presets = RANGE_PRESETS
}: {
  value: TimeRange;
  onChange: (range: TimeRange) => void;
  presets?: readonly RangePreset[];
}) {
  const { t, i18n } = useTranslation();
  const custom = !("preset" in value);
  const format = new Intl.DateTimeFormat(i18n.language, { dateStyle: "short", timeStyle: "short" });

  return (
    <div className="flex flex-wrap items-center gap-1">
      {presets.map((preset) => (
        <button
          key={preset}
          type="button"
          onClick={() => onChange({ preset })}
          className={cn(
            "rounded-md border px-2.5 py-1 text-xs font-semibold cursor-pointer transition-colors",
            "preset" in value && value.preset === preset
              ? "border-[#b8282e]/60 bg-[#b8282e]/15 text-slate-100"
              : "border-slate-600/70 text-slate-300 hover:text-white hover:border-slate-500/60"
          )}
        >
          {t(`serverLogs.range.${preset}`)}
        </button>
      ))}
      {custom && (
        <span className="rounded-md border border-[#b8282e]/60 bg-[#b8282e]/15 px-2.5 py-1 text-xs font-semibold text-slate-100">
          {format.format(new Date(value.from))} – {format.format(new Date(value.to))}
        </span>
      )}
    </div>
  );
}
