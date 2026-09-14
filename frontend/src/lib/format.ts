export function clampPercent(value: number) {
  return Math.max(0, Math.min(100, value));
}

export function formatBytes(value: number) {
  if (!Number.isFinite(value) || value < 0) return "—";

  const units = ["B", "KiB", "MiB", "GiB", "TiB"];
  let amount = value;
  let unit = 0;
  while (amount >= 1024 && unit < units.length - 1) {
    amount /= 1024;
    unit += 1;
  }

  return `${amount.toFixed(unit === 0 ? 0 : 1)} ${units[unit]}`;
}
