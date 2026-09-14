export interface EditorTab {
  id: string;
  name: string;
  path: string;
  content: string;
  isSaved: boolean;
  serverId?: string;
  rootId?: string;
}

export interface OpenEditorFile {
  name: string;
  path: string;
  content?: string;
  serverId?: string;
  rootId?: string;
}

export function getStoredTabs(): EditorTab[] {
  try {
    const raw = localStorage.getItem(EDITOR_TABS_STORAGE_KEY);
    if (!raw) return [];
    const parsed = JSON.parse(raw) as EditorTab[];
    return Array.isArray(parsed) ? parsed : [];
  } catch {
    return [];
  }
}

export function saveStoredTabs(tabs: EditorTab[]) {
  try {
    localStorage.setItem(EDITOR_TABS_STORAGE_KEY, JSON.stringify(tabs));
  } catch {
    // Ignore storage errors
  }
}

export function getActiveTabId(): string | null {
  return localStorage.getItem(ACTIVE_EDITOR_TAB_STORAGE_KEY);
}

export function setActiveTabId(id: string) {
  try {
    localStorage.setItem(ACTIVE_EDITOR_TAB_STORAGE_KEY, id);
  } catch {
    // Ignore storage errors
  }
}

export function addTabToEditor(file: OpenEditorFile) {
  const tabs = getStoredTabs();
  const existing = tabs.find((t) => t.path === file.path && t.serverId === file.serverId && t.rootId === file.rootId);

  if (existing) {
    if (file.content !== undefined && existing.content !== file.content) {
      const content = file.content;
      const updated = tabs.map((t) => (t.id === existing.id ? { ...t, content, isSaved: false } : t));
      saveStoredTabs(updated);
    }
    setActiveTabId(existing.id);
    return existing.id;
  }

  const newTab: EditorTab = {
    id: `${file.name}-${Date.now()}`,
    name: file.name,
    path: file.path,
    content: file.content ?? "",
    isSaved: file.content !== undefined,
    serverId: file.serverId,
    rootId: file.rootId
  };

  const updated = [...tabs, newTab];
  saveStoredTabs(updated);
  setActiveTabId(newTab.id);
  return newTab.id;
}

export function removeTabFromStorage(id: string): EditorTab[] {
  const tabs = getStoredTabs().filter((t) => t.id !== id);
  saveStoredTabs(tabs);
  if (getActiveTabId() === id) {
    const next = tabs.length > 0 ? tabs[tabs.length - 1].id : null;
    if (next) setActiveTabId(next);
    else {
      try {
        localStorage.removeItem(ACTIVE_EDITOR_TAB_STORAGE_KEY);
      } catch {
        // Ignore storage errors
      }
    }
  }
  return tabs;
}
import { ACTIVE_EDITOR_TAB_STORAGE_KEY, EDITOR_TABS_STORAGE_KEY } from "@/lib/constants";
