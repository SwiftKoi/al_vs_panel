import { Loader2 } from "lucide-react";

export default function BackgroundTaskBar({
  description,
  status,
  onHide
}: {
  description: string | null;
  status: string | null;
  onHide: () => void;
}) {
  if (description === null) return null;

  return (
    <div className="flex items-center justify-between gap-3 rounded-lg border border-red-500/30 bg-red-950/20 px-4 py-3 text-xs text-red-200">
      <div className="flex items-center gap-2">
        <Loader2 size={14} className="animate-spin text-[#e04444] shrink-0" />
        <span className="font-medium">{description}</span>
        <span className="bg-[#e04444]/20 px-2 py-0.5 rounded text-[10px] font-semibold text-[#f87171] uppercase">
          {status}
        </span>
      </div>
      <button onClick={onHide} className="hover:text-white text-[#e04444] transition-colors cursor-pointer">
        Hide
      </button>
    </div>
  );
}
