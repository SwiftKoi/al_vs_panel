import { loadLanguage } from "@uiw/codemirror-extensions-langs";
import { EditorView } from "@codemirror/view";
import type { Extension } from "@codemirror/state";

export function shouldWrapEditorLines(): boolean {
  if (typeof window === "undefined") return false;
  return (
    window.matchMedia("(pointer: coarse)").matches ||
    window.matchMedia("(max-width: 767px)").matches
  );
}

export function buildEditorExtensions(langExtension: Extension | null | undefined): Extension[] {
  const base = langExtension ? [langExtension] : [];
  return shouldWrapEditorLines() ? [...base, EditorView.lineWrapping] : base;
}

export function detectLanguage(filename: string): string {
  const ext = filename.split(".").pop()?.toLowerCase();
  switch (ext) {
    case "json":
      return "json";
    case "yaml":
    case "yml":
      return "yaml";
    case "js":
    case "jsx":
      return "javascript";
    case "ts":
    case "tsx":
      return "typescript";
    case "html":
      return "html";
    case "css":
      return "css";
    case "md":
      return "markdown";
    case "py":
      return "python";
    case "cs":
      return "csharp";
    case "sh":
    case "bash":
      return "shell";
    case "xml":
      return "xml";
    default:
      return "json";
  }
}

export function loadLanguageExtension(langName: string) {
  try {
    return loadLanguage(langName as any) || [];
  } catch {
    return [];
  }
}
