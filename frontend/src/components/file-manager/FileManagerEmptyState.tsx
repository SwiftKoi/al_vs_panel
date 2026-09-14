import { Folder } from "lucide-react";
import Panel from "@/components/ui/Panel";

export default function FileManagerEmptyState() {
  return (
    <div className="mx-auto max-w-7xl">
      <Panel className="flex flex-col items-center justify-center p-12 text-center">
        <Folder size={48} className="text-[#e04444] opacity-40 filter drop-shadow-[0_0_8px_rgba(184,40,46,0.3)] mb-3" />
        <p className="text-sm text-slate-400 font-semibold tracking-wide">
          Please select a server instance to view files.
        </p>
      </Panel>
    </div>
  );
}
