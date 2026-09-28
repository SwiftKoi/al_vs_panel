import { useEffect, useState } from "react";
import { ArrowUp, Folder, Home, Loader2 } from "lucide-react";
import { useTranslation } from "react-i18next";
import { filesApi } from "@/api/files";
import { normalizeFolderPath } from "@/lib/fileManager";

/**
 * Browsable folder chooser for Move/Extract. The text field stays editable; the list
 * below shows the subfolders of whatever path is typed, and clicking one enters it.
 * `excluded` paths (e.g. folders being moved) are hidden so they can't be picked.
 */
export default function FolderPicker({
  serverId,
  rootId,
  value,
  onChange,
  excluded = []
}: {
  serverId: string;
  rootId: string;
  value: string;
  onChange: (value: string) => void;
  excluded?: string[];
}) {
  const { t } = useTranslation();
  const folder = normalizeFolderPath(value);
  const [folders, setFolders] = useState<string[]>([]);
  const [loading, setLoading] = useState(false);
  const [missing, setMissing] = useState(false);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    // Debounced so typing a path doesn't fire a request per keystroke.
    const timer = window.setTimeout(() => {
      filesApi.list(serverId, rootId, folder)
        .then((res) => {
          if (cancelled) return;
          setFolders(res.entries.filter((e) => e.isFolder).map((e) => e.name));
          setMissing(false);
        })
        .catch(() => {
          if (cancelled) return;
          setFolders([]);
          setMissing(true);
        })
        .finally(() => { if (!cancelled) setLoading(false); });
    }, 250);
    return () => { cancelled = true; window.clearTimeout(timer); };
  }, [serverId, rootId, folder]);

  const parts = folder.split("/").filter(Boolean);
  const join = (name: string) => (folder ? `${folder}/${name}` : name);
  const visible = folders.filter((name) => !excluded.includes(join(name)));

  return (
    <div className="space-y-2">
      <input
        type="text"
        value={value}
        onChange={(e) => onChange(e.target.value)}
        placeholder={t("fileManager.picker.rootPlaceholder")}
        className="w-full bg-slate-950/60 border border-red-950/45 rounded px-3 py-2 text-xs font-mono text-slate-200 focus:outline-none focus:border-[#e04444]"
      />
      <div className="rounded border border-slate-700/50 bg-slate-950/40">
        <div className="flex items-center gap-1 border-b border-slate-700/40 px-2 py-1.5 text-[11px] font-mono text-slate-400 overflow-x-auto whitespace-nowrap">
          <button type="button" onClick={() => onChange("")} className="hover:text-slate-100 cursor-pointer" title={t("fileManager.picker.root")}>
            <Home size={12} />
          </button>
          {parts.map((part, index) => (
            <span key={index} className="flex items-center gap-1">
              <span className="text-slate-600">/</span>
              <button
                type="button"
                onClick={() => onChange(parts.slice(0, index + 1).join("/"))}
                className="hover:text-slate-100 cursor-pointer"
              >
                {part}
              </button>
            </span>
          ))}
          {loading && <Loader2 size={12} className="ml-auto animate-spin text-slate-500" />}
        </div>
        <ul className="max-h-48 overflow-y-auto py-1 text-xs">
          {parts.length > 0 && (
            <li>
              <button
                type="button"
                onClick={() => onChange(parts.slice(0, -1).join("/"))}
                className="flex w-full items-center gap-2 px-3 py-1.5 text-slate-300 hover:bg-slate-800/50 cursor-pointer"
              >
                <ArrowUp size={13} className="text-[#e04444]" />
                ..
              </button>
            </li>
          )}
          {missing ? (
            <li className="px-3 py-2 text-amber-300/90">{t("fileManager.picker.willNotExist")}</li>
          ) : visible.length === 0 && !loading ? (
            <li className="px-3 py-2 text-slate-500 italic">{t("fileManager.picker.noSubfolders")}</li>
          ) : visible.map((name) => (
            <li key={name}>
              <button
                type="button"
                onClick={() => onChange(join(name))}
                className="flex w-full items-center gap-2 px-3 py-1.5 font-mono text-slate-200 hover:bg-slate-800/50 cursor-pointer"
              >
                <Folder size={13} className="text-[#ffd8a0]" fill="currentColor" fillOpacity={0.2} />
                <span className="truncate">{name}</span>
              </button>
            </li>
          ))}
        </ul>
      </div>
      <div className="text-[11px] text-slate-500">
        {t("fileManager.picker.target")} <span className="font-mono text-slate-300">/{folder}</span>
      </div>
    </div>
  );
}
