import { useState } from "react";
import { useTranslation } from "react-i18next";
import type { LogHistogram as Histogram } from "@/api/serverLogs";
import { cn } from "@/lib/cn";

/** Entries per time bucket. Hover for counts; click a bar or drag across bars to zoom into that time. */
export default function LogHistogram({ histogram, onZoom }: { histogram: Histogram; onZoom: (from: string, to: string) => void }) {
  const { t, i18n } = useTranslation();
  const [hover, setHover] = useState<number | null>(null);
  const [drag, setDrag] = useState<{ start: number; end: number } | null>(null);
  const counts = histogram.counts;
  const max = Math.max(1, ...counts);
  const start = Date.parse(histogram.from);
  const bucket = histogram.bucketMilliseconds;
  const end = Date.parse(histogram.to);
  // Show the date whenever the chart spans more than one calendar day (a 24 h range crosses midnight).
  const multiDay = new Date(start).toDateString() !== new Date(end).toDateString();
  const format = new Intl.DateTimeFormat(i18n.language, multiDay ? { day: "numeric", month: "short", hour: "2-digit", minute: "2-digit" } : { hour: "2-digit", minute: "2-digit" });
  const ticks = [0, 0.25, 0.5, 0.75, 1];
  const number = new Intl.NumberFormat(i18n.language);

  const selection = drag ? [Math.min(drag.start, drag.end), Math.max(drag.start, drag.end)] : null;
  const finish = () => {
    if (!selection) return;
    const from = new Date(start + selection[0] * bucket).toISOString();
    const to = new Date(Math.min(Date.parse(histogram.to), start + (selection[1] + 1) * bucket)).toISOString();
    setDrag(null);
    onZoom(from, to);
  };

  return (
    <div className="select-none">
      <div
        className="relative flex h-16 items-end gap-px"
        onMouseLeave={() => { setHover(null); setDrag(null); }}
        onMouseUp={finish}
      >
        {counts.map((count, index) => {
          const selected = selection && index >= selection[0] && index <= selection[1];
          return (
            <div
              key={index}
              className="relative flex h-full flex-1 cursor-zoom-in items-end"
              onMouseEnter={() => { setHover(index); if (drag) setDrag({ ...drag, end: index }); }}
              onMouseDown={() => setDrag({ start: index, end: index })}
            >
              <div
                className={selected ? "w-full rounded-t-sm bg-[#e04444]" : hover === index ? "w-full rounded-t-sm bg-[#e04444]/80" : "w-full rounded-t-sm bg-[#b8282e]/55"}
                style={{ height: count ? `${Math.max(4, (count / max) * 100)}%` : "0" }}
              />
            </div>
          );
        })}
        {hover !== null && (
          <div
            className="pointer-events-none absolute -top-8 z-10 whitespace-nowrap rounded border border-slate-600/70 bg-slate-950/95 px-2 py-1 text-[11px] text-slate-200"
            style={{ left: `${(hover / counts.length) * 100}%`, transform: hover > counts.length / 2 ? "translateX(-100%)" : undefined }}
          >
            {format.format(new Date(start + hover * bucket))} · {t("serverLogs.search.entriesCount", { count: counts[hover], formatted: number.format(counts[hover]) })}
          </div>
        )}
      </div>
      <div className="relative mt-1 h-4 text-[11px] text-slate-400">
        {ticks.map((tick, index) => (
          <span
            key={tick}
            className={cn("absolute whitespace-nowrap", index % 2 === 1 && "hidden sm:inline")}
            style={{ left: `${tick * 100}%`, transform: tick === 0 ? undefined : tick === 1 ? "translateX(-100%)" : "translateX(-50%)" }}
          >
            {format.format(new Date(start + tick * (end - start)))}
          </span>
        ))}
      </div>
    </div>
  );
}
