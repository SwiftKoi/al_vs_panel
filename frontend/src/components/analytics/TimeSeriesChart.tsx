import { useRef } from "react";

export const SERIES_COLOR = "#3987e5";
export const DROP_COLOR = "#e66767";

// Single-series time chart with a shared hover index, so several charts stacked
// on one time axis highlight the same moment. Null values leave a gap.
export default function TimeSeriesChart({
  title,
  times,
  values,
  format,
  start,
  end,
  hovered,
  onHover,
  markers = [],
  dashedMarkers = [],
  minimumMax = 0
}: {
  title: string;
  times: number[];
  values: (number | null)[];
  format: (value: number) => string;
  start: number;
  end: number;
  hovered?: number;
  onHover: (index?: number) => void;
  markers?: number[];
  dashedMarkers?: number[];
  minimumMax?: number;
}) {
  const ref = useRef<HTMLDivElement>(null);
  const width = 1000;
  const height = 100;
  const present = values.filter((value): value is number => value !== null);
  const peak = Math.max(minimumMax, ...present, 0);
  const max = peak * 1.1 || 1;
  const span = Math.max(1, end - start);
  const x = (at: number) => ((at - start) / span) * width;
  const y = (value: number) => height - (value / max) * height;

  let path = "";
  let drawing = false;
  values.forEach((value, i) => {
    if (value === null) {
      drawing = false;
      return;
    }
    path += `${drawing ? "L" : "M"}${x(times[i]).toFixed(1)},${y(value).toFixed(1)} `;
    drawing = true;
  });

  const handleMove = (clientX: number) => {
    const rect = ref.current?.getBoundingClientRect();
    if (!rect || times.length === 0) return;
    const at = start + ((clientX - rect.left) / rect.width) * span;
    let nearest = 0;
    for (let i = 1; i < times.length; i++) if (Math.abs(times[i] - at) < Math.abs(times[nearest] - at)) nearest = i;
    onHover(nearest);
  };

  const hoveredValue = hovered === undefined ? null : values[hovered];

  return (
    <div>
      <div className="flex justify-between text-[10px] text-slate-500 mb-1">
        <span className="font-semibold uppercase tracking-wider">{title}</span>
        <span className="font-mono">{present.length ? `max ${format(peak)}` : "—"}</span>
      </div>
      <div
        ref={ref}
        className="relative h-24 border-b border-slate-700/40 cursor-crosshair"
        onMouseMove={(event) => handleMove(event.clientX)}
        onTouchMove={(event) => handleMove(event.touches[0].clientX)}
        onMouseLeave={() => onHover(undefined)}
      >
        <svg viewBox={`0 0 ${width} ${height}`} preserveAspectRatio="none" className="absolute inset-0 h-full w-full overflow-visible">
          {dashedMarkers.map((at) => (
            <line key={`j${at}`} x1={x(at)} x2={x(at)} y1={0} y2={height} stroke="#94a3b8" strokeOpacity={0.5} strokeDasharray="3 3" vectorEffect="non-scaling-stroke" />
          ))}
          {markers.map((at) => (
            <line key={`d${at}`} x1={x(at)} x2={x(at)} y1={0} y2={height} stroke={DROP_COLOR} strokeWidth={2} vectorEffect="non-scaling-stroke" />
          ))}
          <path d={path} fill="none" stroke={SERIES_COLOR} strokeWidth={2} strokeLinejoin="round" vectorEffect="non-scaling-stroke" />
          {hovered !== undefined && (
            <line x1={x(times[hovered])} x2={x(times[hovered])} y1={0} y2={height} stroke="#e2e8f0" strokeOpacity={0.6} vectorEffect="non-scaling-stroke" />
          )}
        </svg>
        {hovered !== undefined && hoveredValue !== null && (
          <span
            className="pointer-events-none absolute -top-1 rounded bg-slate-950/90 px-1.5 py-0.5 text-[10px] font-mono text-slate-100"
            style={{ left: `${(x(times[hovered]) / width) * 100}%`, transform: "translateX(-50%)" }}
          >
            {format(hoveredValue)}
          </span>
        )}
      </div>
    </div>
  );
}

export function TimeAxis({ start, end }: { start: number; end: number }) {
  return (
    <div className="flex justify-between text-[10px] font-mono text-slate-500">
      <span>{new Date(start).toLocaleString()}</span>
      <span>{new Date(end).toLocaleString()}</span>
    </div>
  );
}
