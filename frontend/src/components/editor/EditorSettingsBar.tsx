import { Sliders } from "lucide-react";

export interface EditorSettingsBarProps {
  language: string;
  fontSize: string;
  onFontSizeChange: (size: string) => void;
  className?: string;
}

export default function EditorSettingsBar({
  language,
  fontSize,
  onFontSizeChange,
  className = ""
}: EditorSettingsBarProps) {
  return (
    <div className={`hidden sm:flex items-center gap-2.5 bg-slate-950/30 border border-red-950/45 rounded-md px-3 h-[32px] text-xs font-semibold select-none ${className}`}>
      <Sliders size={14} className="text-[#e04444]" />
      <span className="font-serif text-[#e04444] text-[11px] font-bold tracking-widest uppercase leading-none translate-y-[0.5px]">
        {language}
      </span>
      <span className="text-red-950/40">|</span>
      <select
        value={fontSize}
        onChange={(e) => onFontSizeChange(e.target.value)}
        className="bg-transparent text-slate-200 text-xs font-semibold focus:outline-none cursor-pointer pr-1 h-full"
      >
        <option value="12px" className="bg-slate-950 text-slate-200">12px</option>
        <option value="13px" className="bg-slate-950 text-slate-200">13px</option>
        <option value="14px" className="bg-slate-950 text-slate-200">14px</option>
        <option value="16px" className="bg-slate-950 text-slate-200">16px</option>
      </select>
    </div>
  );
}
