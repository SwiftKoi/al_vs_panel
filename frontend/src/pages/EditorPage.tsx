import { useState, useEffect, useMemo, useCallback } from "react";
import CodeMirror from "@uiw/react-codemirror";
import { vscodeDark } from "@uiw/codemirror-theme-vscode";
import { Save, Check, X, FileCode, AlertTriangle, Loader2, AlertCircle } from "lucide-react";
import { useTranslation } from "react-i18next";
import Panel from "@/components/ui/Panel";
import PageHeader from "@/components/layout/PageHeader";
import Button from "@/components/ui/Button";
import Modal from "@/components/ui/Modal";
import EditorSettingsBar from "@/components/editor/EditorSettingsBar";
import {
  EditorTab,
  getStoredTabs,
  saveStoredTabs,
  getActiveTabId,
  setActiveTabId,
  removeTabFromStorage
} from "@/lib/editorTabs";
import { detectLanguage, loadLanguageExtension, buildEditorExtensions } from "@/lib/editorLanguage";
import { filesApi } from "@/api/files";
import { useServer } from "@/context/ServerContext";

export default function EditorPage() {
  const { t } = useTranslation();
  const { selectedServer } = useServer();
  const serverId = selectedServer?.id;

  const [tabs, setTabs] = useState<EditorTab[]>([]);
  const [activeTabId, setActiveId] = useState<string | null>(null);
  const [fontSize, setFontSize] = useState("13px");
  const [isSaving, setIsSaving] = useState(false);
  const [loadingTabId, setLoadingTabId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [closingTabId, setClosingTabId] = useState<string | null>(null);

  // Load tabs for the current server; reset when the server selection changes.
  useEffect(() => {
    if (!serverId) {
      setTabs([]);
      setActiveId(null);
      return;
    }
    const stored = getStoredTabs();
    const visible = stored.filter((t) => t.serverId === serverId || !t.serverId);
    setTabs(visible);
    const storedActive = getActiveTabId();
    if (storedActive && visible.some((t) => t.id === storedActive)) {
      setActiveId(storedActive);
    } else if (visible.length > 0) {
      setActiveId(visible[0].id);
    } else {
      setActiveId(null);
    }
  }, [serverId]);

  const activeTab = useMemo(
    () => tabs.find((t) => t.id === activeTabId) ?? null,
    [tabs, activeTabId]
  );

  const langName = useMemo(
    () => (activeTab ? detectLanguage(activeTab.name) : "json"),
    [activeTab]
  );

  const langExtension = useMemo(() => loadLanguageExtension(langName), [langName]);

  const editorExtensions = useMemo(() => buildEditorExtensions(langExtension), [langExtension]);

  const loadTabContent = useCallback(
    (tab: EditorTab) => {
      if (!tab.serverId || !tab.rootId || tab.content || loadingTabId === tab.id) return;
      setLoadingTabId(tab.id);
      setError(null);
      filesApi.getContent(tab.serverId, tab.rootId, tab.path)
        .then((res) => {
          setTabs((prev) => {
            const updated = prev.map((t) =>
              t.id === tab.id ? { ...t, content: res.content, isSaved: true } : t
            );
            saveStoredTabs(updated);
            return updated;
          });
        })
        .catch((err) => {
          setError(`${t("editor.loadError")} ${err instanceof Error ? err.message : String(err)}`);
        })
        .finally(() => {
          setLoadingTabId((cur) => (cur === tab.id ? null : cur));
        });
    },
    [loadingTabId, t]
  );

  // Lazily fetch content when the active tab has none loaded yet.
  useEffect(() => {
    if (!activeTab || !activeTab.serverId || !activeTab.rootId) return;
    if (!activeTab.content) {
      loadTabContent(activeTab);
    }
  }, [activeTab?.id, activeTab?.content, loadTabContent]);

  const handleTabChange = (val: string) => {
    if (!activeTabId) return;
    setError(null);
    setTabs((prev) => {
      const updated = prev.map((tab) =>
        tab.id === activeTabId ? { ...tab, content: val, isSaved: false } : tab
      );
      saveStoredTabs(updated);
      return updated;
    });
  };

  const handleSelectTab = (id: string) => {
    setActiveId(id);
    setActiveTabId(id);
    const tab = tabs.find((t) => t.id === id);
    if (tab && !tab.content) {
      loadTabContent(tab);
    }
  };

  const closeTab = (id: string) => {
    setClosingTabId(null);
    const updated = removeTabFromStorage(id);
    setTabs(updated);
    if (activeTabId === id) {
      const index = tabs.findIndex((t) => t.id === id);
      const next = updated[Math.max(0, index - 1)] ?? null;
      setActiveId(next ? next.id : null);
    }
  };

  const handleRequestCloseTab = (e: React.MouseEvent, id: string) => {
    e.stopPropagation();
    const tab = tabs.find((t) => t.id === id);
    if (tab && !tab.isSaved) {
      setClosingTabId(id);
      return;
    }
    closeTab(id);
  };

  const handleSaveActive = useCallback(() => {
    if (!activeTab) return;
    if (!activeTab.serverId || !activeTab.rootId) {
      setError(t("editor.saveError"));
      return;
    }
    setIsSaving(true);
    setError(null);
    filesApi.saveContent(activeTab.serverId, activeTab.rootId, activeTab.path, activeTab.content)
      .then(() => {
        setTabs((prev) => {
          const updated = prev.map((tab) =>
            tab.id === activeTab.id ? { ...tab, isSaved: true } : tab
          );
          saveStoredTabs(updated);
          return updated;
        });
      })
      .catch((err) => {
        setError(`${t("editor.saveError")} ${err instanceof Error ? err.message : String(err)}`);
      })
      .finally(() => {
        setIsSaving(false);
      });
  }, [activeTab, t]);

  // Ctrl/Cmd+S saves the active tab.
  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "s") {
        e.preventDefault();
        handleSaveActive();
      }
    };
    window.addEventListener("keydown", handler);
    return () => window.removeEventListener("keydown", handler);
  }, [handleSaveActive]);

  if (!selectedServer) {
    return (
      <div className="max-w-none">
        <Panel className="flex flex-col items-center justify-center p-12 text-center">
          <FileCode size={48} className="text-[#e04444] opacity-40 filter drop-shadow-[0_0_8px_rgba(184,40,46,0.3)] mb-3" />
          <p className="text-sm text-slate-400 font-semibold tracking-wide">{t("editor.noServer")}</p>
        </Panel>
      </div>
    );
  }

  const closingTab = closingTabId ? tabs.find((t) => t.id === closingTabId) : null;

  return (
    <div className="space-y-4 max-w-none">
      {/* Header */}
      <PageHeader
        title={t("editor.title")}
        description={t("editor.description")}
        actions={
          activeTab ? (
            <>
              <EditorSettingsBar
                language={langName}
                fontSize={fontSize}
                onFontSizeChange={setFontSize}
              />

              {/* Save Active Tab Button */}
              <Button
                variant={activeTab.isSaved ? "secondary" : "primary"}
                onClick={handleSaveActive}
                disabled={isSaving || activeTab.isSaved}
                className="h-[32px] text-xs font-bold uppercase tracking-wider shrink-0"
              >
                {isSaving ? (
                  <Check size={14} className="animate-spin" />
                ) : activeTab.isSaved ? (
                  <Check size={14} className="text-[#ffd8a0]" />
                ) : (
                  <Save size={14} />
                )}
                <span>{isSaving ? t("editor.saving") : activeTab.isSaved ? t("editor.saved") : t("editor.save")}</span>
              </Button>
            </>
          ) : undefined
        }
      />

      {/* Code Editor Container */}
      <Panel className="relative flex flex-col h-[650px] overflow-hidden">
        {/* Tab Bar Header */}
        <div className="flex items-center bg-slate-950/60 border-b border-red-950/20 overflow-x-auto select-none shrink-0">
          <div className="flex items-center flex-1 overflow-x-auto min-w-0">
            {tabs.map((tab) => {
              const isActive = tab.id === activeTabId;
              return (
                <div
                  key={tab.id}
                  onClick={() => handleSelectTab(tab.id)}
                  className={`flex items-center gap-2 px-3.5 py-2.5 text-xs font-semibold border-r border-red-950/20 cursor-pointer transition-colors shrink-0 ${
                    isActive
                      ? "bg-slate-950/20 text-[#e04444] border-t-2 border-t-[#b8282e] glow-text"
                      : "text-slate-400 hover:bg-red-950/10 hover:text-slate-200"
                  }`}
                >
                  <FileCode size={14} className={isActive ? "text-[#e04444]" : "text-slate-500"} />
                  <span className="truncate max-w-[140px]">{tab.name}</span>
                  {!tab.isSaved && (
                    <span className="h-1.5 w-1.5 rounded-full bg-amber-400 shrink-0" title={t("editor.unsavedChanges")} />
                  )}
                  <button
                    onClick={(e) => handleRequestCloseTab(e, tab.id)}
                    className="p-0.5 rounded hover:bg-red-950/20 text-slate-500 hover:text-slate-200 transition-colors ml-1 cursor-pointer"
                  >
                    <X size={12} />
                  </button>
                </div>
              );
            })}
          </div>
        </div>

        {/* Error Banner */}
        {error && (
          <div className="flex items-start gap-2.5 bg-[#5e1215]/20 border border-red-900/40 p-3.5 text-xs text-red-200 font-semibold shrink-0">
            <AlertCircle size={16} className="text-[#e04444] shrink-0 mt-0.5" />
            <div>{error}</div>
          </div>
        )}

        {/* Editor Main Content Area */}
        {activeTab ? (
          <div className="flex-1 overflow-hidden bg-slate-950/30 text-left relative">
            {loadingTabId === activeTab.id && (
              <div className="absolute inset-0 flex items-center justify-center bg-slate-950/50 backdrop-blur-sm z-10">
                <Loader2 size={32} className="animate-spin text-[#e04444]" />
              </div>
            )}
            <CodeMirror
              key={activeTab.id}
              value={activeTab.content}
              height="100%"
              theme={vscodeDark}
              extensions={editorExtensions}
              onChange={handleTabChange}
              style={{ fontSize }}
              className="h-full text-left font-mono"
            />
          </div>
        ) : (
          <div className="flex-1 flex flex-col items-center justify-center p-8 text-center bg-slate-950/60 text-slate-500">
            <FileCode size={48} className="text-[#e04444] opacity-25 filter drop-shadow-[0_0_8px_rgba(184,40,46,0.3)] mb-3" />
            <p className="text-sm font-semibold tracking-wide">{t("editor.noTabsOpen")}</p>
          </div>
        )}

        {/* Footer Status Bar */}
        {activeTab && (
          <div className="flex items-center justify-between px-4 py-1.5 bg-slate-950/60 border-t border-red-950/20 text-[11px] font-mono text-slate-400 shrink-0">
            <div className="flex items-center gap-4 truncate">
              <span className="truncate max-w-sm" title={activeTab.path}>
                Path: {activeTab.path}
              </span>
              <span>Lines: {activeTab.content.split("\n").length}</span>
              <span>Chars: {activeTab.content.length}</span>
            </div>
            <div>
              {activeTab.isSaved ? (
                <span className="text-emerald-400 flex items-center gap-1">
                  <Check size={12} /> {t("editor.saved")}
                </span>
              ) : (
                <span className="text-amber-400">{t("editor.unsavedChanges")}</span>
              )}
            </div>
          </div>
        )}
      </Panel>

      {/* Close Unsaved Tab Confirmation */}
      <Modal
        isOpen={closingTabId !== null}
        onClose={() => setClosingTabId(null)}
        title={t("editor.confirmCloseTitle")}
        icon={<AlertTriangle size={20} className="text-amber-400" />}
        confirmLabel={t("editor.close")}
        confirmVariant="danger"
        onConfirm={() => closingTabId && closeTab(closingTabId)}
      >
        <p className="leading-relaxed text-slate-300">
          {t("editor.confirmCloseMessage")}{" "}
          <strong className="text-slate-100 font-mono">{closingTab?.name}</strong>?
        </p>
      </Modal>
    </div>
  );
}
