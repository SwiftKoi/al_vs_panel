import { FileArchive, FileCode, FileImage, FileText, Folder, Globe, ScrollText, type LucideIcon } from "lucide-react";

const BY_EXTENSION: Record<string, [LucideIcon, string]> = {
  zip: [FileArchive, "text-violet-300"],
  "7z": [FileArchive, "text-violet-300"],
  gz: [FileArchive, "text-violet-300"],
  tar: [FileArchive, "text-violet-300"],
  json: [FileCode, "text-amber-400"],
  yaml: [FileCode, "text-amber-400"],
  yml: [FileCode, "text-amber-400"],
  xml: [FileCode, "text-amber-400"],
  cs: [FileCode, "text-sky-300"],
  js: [FileCode, "text-sky-300"],
  sh: [FileCode, "text-sky-300"],
  log: [ScrollText, "text-slate-300"],
  txt: [FileText, "text-slate-300"],
  md: [FileText, "text-slate-300"],
  png: [FileImage, "text-emerald-300"],
  jpg: [FileImage, "text-emerald-300"],
  jpeg: [FileImage, "text-emerald-300"],
  gif: [FileImage, "text-emerald-300"],
  webp: [FileImage, "text-emerald-300"],
  // Vintage Story world save.
  vcdbs: [Globe, "text-[#e04444]"]
};

/** Icon by entry type and file extension. */
export default function FileIcon({ name, isFolder, size = 16 }: { name: string; isFolder: boolean; size?: number }) {
  if (isFolder) return <Folder size={size} className="text-[#ffd8a0] shrink-0" fill="currentColor" fillOpacity={0.2} />;
  const dot = name.lastIndexOf(".");
  const [Icon, color] = BY_EXTENSION[dot > 0 ? name.slice(dot + 1).toLowerCase() : ""] ?? [FileText, "text-slate-400"];
  return <Icon size={size} className={`${color} shrink-0`} />;
}
