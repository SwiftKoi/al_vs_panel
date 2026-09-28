import { Loader2 } from "lucide-react";

export interface BackgroundTask {
  id: string;
  description: string;
  status: string;
}

export default function BackgroundTaskBar({ tasks }: { tasks: BackgroundTask[] }) {
  if (tasks.length === 0) return null;

  return (
    <div className="space-y-1.5 rounded-lg border border-red-500/30 bg-red-950/20 px-4 py-3 text-xs text-red-200">
      {tasks.map((task) => (
        <div key={task.id} className="flex items-center gap-2">
          <Loader2 size={14} className="animate-spin text-[#e04444] shrink-0" />
          <span className="font-medium truncate">{task.description}</span>
          <span className="bg-[#e04444]/20 px-2 py-0.5 rounded text-[10px] font-semibold text-[#f87171] uppercase">
            {task.status}
          </span>
        </div>
      ))}
    </div>
  );
}
