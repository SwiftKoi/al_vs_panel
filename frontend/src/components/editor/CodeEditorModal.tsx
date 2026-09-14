import { useState, useMemo } from "react";
import CodeMirror from "@uiw/react-codemirror";
import { vscodeDark } from "@uiw/codemirror-theme-vscode";
import { Save, Check, X, FileCode, ExternalLink } from "lucide-react";
import { useTranslation } from "react-i18next";
import { useNavigate } from "react-router-dom";
import { addTabToEditor } from "@/lib/editorTabs";
import { detectLanguage, loadLanguageExtension, buildEditorExtensions } from "@/lib/editorLanguage";
import EditorSettingsBar from "@/components/editor/EditorSettingsBar";
import Button from "@/components/ui/Button";

interface CodeEditorModalProps {
  isOpen: boolean;
  onClose: () => void;
  filename: string;
  filePath: string;
  initialContent?: string;
  onSave?: (content: string) => void;
  serverId?: string;
  rootId?: string;
}

export default function CodeEditorModal({
  isOpen,
  onClose,
  filename,
  filePath,
  initialContent = "",
  onSave,
  serverId,
  rootId
}: CodeEditorModalProps) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [code, setCode] = useState(initialContent);
  const [isSaved, setIsSaved] = useState(true);
  const [isSaving, setIsSaving] = useState(false);
  const [fontSize, setFontSize] = useState("13px");

  const langName = useMemo(() => detectLanguage(filename), [filename]);

  const langExtension = useMemo(() => loadLanguageExtension(langName), [langName]);

  const editorExtensions = useMemo(() => buildEditorExtensions(langExtension), [langExtension]);

  if (!isOpen) return null;

  const handleChange = (val: string) => {
    setCode(val);
    setIsSaved(false);
  };

  const handleSave = () => {
    setIsSaving(true);
    setTimeout(() => {
      onSave?.(code);
      setIsSaving(false);
      setIsSaved(true);
    }, 400);
  };

  const handleMoveToEditor = () => {
    addTabToEditor({
      name: filename,
      path: filePath,
      content: code,
      serverId,
      rootId
    });
    onClose();
    navigate("/editor");
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-2 sm:p-4">
      {/* Backdrop overlay */}
      <div
        className="fixed inset-0 bg-black/80 backdrop-blur-sm transition-opacity"
        onClick={onClose}
      />

      {/* Editor Container Dialog */}
      <div className="relative z-10 w-full max-w-6xl h-[88vh] flex flex-col rounded-xl glass-panel shadow-2xl overflow-hidden animate-in zoom-in-95 duration-150">
        {/* Header / Top Control Bar */}
        <div className="flex flex-wrap items-center justify-between gap-3 px-4 py-3 bg-slate-950/60 border-b border-red-950/20 shrink-0">
          <div className="flex items-center gap-3 min-w-0">
            <FileCode size={20} className="text-[#e04444] shrink-0" />
            <div className="min-w-0">
              <div className="flex items-center gap-2">
                <span className="font-bold text-sm text-slate-100 truncate font-serif tracking-wide glow-text">
                  {filename}
                </span>
                {!isSaved && (
                  <span className="inline-block h-2 w-2 rounded-full bg-amber-400 animate-pulse" title={t("editor.unsavedChanges")} />
                )}
              </div>
              <div className="text-[11px] font-mono text-slate-400 truncate max-w-md sm:max-w-xl">
                {filePath}
              </div>
            </div>
          </div>

          <div className="flex items-center gap-2 sm:gap-3 shrink-0">
            {/* Language & Font Size settings */}
            <EditorSettingsBar
              language={langName}
              fontSize={fontSize}
              onFontSizeChange={setFontSize}
            />

            {/* Move to Editor Button */}
            <button
              onClick={handleMoveToEditor}
              className="flex items-center gap-1.5 px-3 h-[32px] rounded-md text-xs font-semibold bg-slate-950/30 hover:bg-red-950/15 border border-red-950/45 text-slate-200 transition-all shadow-sm cursor-pointer"
              title={t("editor.openInEditor")}
            >
              <ExternalLink size={14} className="text-[#e04444]" />
              <span className="hidden md:inline">{t("editor.openInEditor")}</span>
            </button>

            {/* Save Button */}
            <Button
              variant={isSaved ? "secondary" : "primary"}
              onClick={handleSave}
              disabled={isSaving || isSaved}
              className="h-[32px] text-xs font-bold uppercase tracking-wider shrink-0"
            >
              {isSaving ? (
                <Check size={14} className="animate-spin" />
              ) : isSaved ? (
                <Check size={14} className="text-[#ffd8a0]" />
              ) : (
                <Save size={14} />
              )}
              <span>{isSaving ? t("editor.saving") : isSaved ? t("editor.saved") : t("editor.save")}</span>
            </Button>

            {/* Close Button */}
            <button
              onClick={onClose}
              className="rounded-md p-1.5 text-slate-400 hover:bg-red-950/20 hover:text-white transition-colors cursor-pointer"
              title={t("editor.close")}
            >
              <X size={18} />
            </button>
          </div>
        </div>

        {/* CodeMirror Editor Work Area */}
        <div className="flex-1 overflow-hidden bg-slate-950/30 text-left">
          <CodeMirror
            value={code}
            height="100%"
            theme={vscodeDark}
            extensions={editorExtensions}
            onChange={handleChange}
            style={{ fontSize }}
            className="h-full text-left font-mono"
          />
        </div>

        {/* Bottom Status Bar */}
        <div className="flex items-center justify-between px-4 py-1.5 bg-slate-950/60 border-t border-red-950/20 text-[11px] font-mono text-slate-400 shrink-0">
          <div className="flex items-center gap-4">
            <span>Encoding: UTF-8</span>
            <span>Lines: {code.split("\n").length}</span>
            <span>Chars: {code.length}</span>
          </div>
          <div>
            {isSaved ? (
              <span className="text-[#ffd8a0] flex items-center gap-1">
                <Check size={12} className="text-[#e04444]" /> {t("editor.saved")}
              </span>
            ) : (
              <span className="text-amber-400">{t("editor.unsavedChanges")}</span>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}
