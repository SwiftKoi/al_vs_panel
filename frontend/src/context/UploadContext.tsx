import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { filesApi } from "@/api/files";

/** Uploads running at the same time; the rest wait in the queue. */
const MAX_PARALLEL_UPLOADS = 2;

export type UploadStatus = "queued" | "uploading" | "saving" | "done" | "failed" | "cancelled";

export interface UploadItem {
  id: number;
  file: File;
  serverId: string;
  rootId: string;
  /** Destination folder, relative to the root. */
  folder: string;
  /** Create missing folders on the server (folder uploads). */
  createFolders: boolean;
  status: UploadStatus;
  loaded: number;
  total: number;
  /** Bytes per second, smoothed. */
  speed: number;
  startedAt?: number;
  finishedAt?: number;
  error?: string;
}

interface UploadContextValue {
  uploads: UploadItem[];
  /** Each file goes to its own folder (folder uploads keep their structure). */
  enqueue: (files: { file: File; folder: string }[], target: { serverId: string; rootId: string; createFolders?: boolean }) => void;
  cancel: (id: number) => void;
  cancelAll: () => void;
  retry: (id: number) => void;
  clearFinished: () => void;
  /** Called with the upload after each one completes successfully. */
  onCompleted: (listener: (item: UploadItem) => void) => () => void;
}

const UploadContext = createContext<UploadContextValue | null>(null);

export const isActiveUpload = (item: UploadItem) =>
  item.status === "queued" || item.status === "uploading" || item.status === "saving";

export function UploadProvider({ children }: { children: ReactNode }) {
  const [uploads, setUploads] = useState<UploadItem[]>([]);
  const nextId = useRef(1);
  const controllers = useRef(new Map<number, AbortController>());
  const listeners = useRef(new Set<(item: UploadItem) => void>());

  const update = useCallback((id: number, patch: Partial<UploadItem> | ((item: UploadItem) => Partial<UploadItem>)) => {
    setUploads((prev) => prev.map((item) =>
      item.id === id ? { ...item, ...(typeof patch === "function" ? patch(item) : patch) } : item
    ));
  }, []);

  const start = useCallback((item: UploadItem) => {
    // The scheduler effect can run twice for the same state (StrictMode); start once.
    if (controllers.current.has(item.id)) return;
    const controller = new AbortController();
    controllers.current.set(item.id, controller);
    const startedAt = performance.now();
    let lastSample = { time: startedAt, loaded: 0 };
    update(item.id, { status: "uploading", startedAt: Date.now(), loaded: 0, speed: 0, error: undefined });

    filesApi.uploadWithProgress(item.serverId, item.rootId, item.folder, item.file, {
      signal: controller.signal,
      onProgress: ({ loaded, total }) => {
        const now = performance.now();
        const elapsed = (now - lastSample.time) / 1000;
        if (elapsed < 0.25 && loaded < total) return; // throttle re-renders
        const instant = elapsed > 0 ? (loaded - lastSample.loaded) / elapsed : 0;
        lastSample = { time: now, loaded };
        update(item.id, (current) => ({
          loaded,
          total,
          // Exponential smoothing keeps the speed and ETA from jumping around.
          speed: current.speed === 0 ? instant : current.speed * 0.7 + instant * 0.3
        }));
      },
      onUploaded: () => update(item.id, (current) => ({ status: "saving", loaded: current.total }))
    }, item.createFolders)
      .then(() => {
        const finished = { ...item, status: "done" as const, loaded: item.file.size, finishedAt: Date.now() };
        update(item.id, { status: "done", loaded: item.file.size, finishedAt: finished.finishedAt });
        listeners.current.forEach((listener) => listener(finished));
      })
      .catch((err: unknown) => {
        const cancelled = err instanceof DOMException && err.name === "AbortError";
        update(item.id, {
          status: cancelled ? "cancelled" : "failed",
          error: cancelled ? undefined : err instanceof Error ? err.message : String(err),
          finishedAt: Date.now()
        });
      })
      .finally(() => controllers.current.delete(item.id));
  }, [update]);

  // Scheduler: keep up to MAX_PARALLEL_UPLOADS running, oldest queued first.
  useEffect(() => {
    const running = uploads.filter((item) => item.status === "uploading" || item.status === "saving").length;
    const free = MAX_PARALLEL_UPLOADS - running;
    if (free <= 0) return;
    uploads.filter((item) => item.status === "queued").slice(0, free).forEach(start);
  }, [uploads, start]);

  // Leaving the page would abort the uploads, so ask first.
  const hasActive = uploads.some(isActiveUpload);
  useEffect(() => {
    if (!hasActive) return;
    const handler = (event: BeforeUnloadEvent) => {
      event.preventDefault();
      event.returnValue = "";
    };
    window.addEventListener("beforeunload", handler);
    return () => window.removeEventListener("beforeunload", handler);
  }, [hasActive]);

  const value = useMemo<UploadContextValue>(() => ({
    uploads,
    enqueue: (files, target) => {
      const added = files.map<UploadItem>(({ file, folder }) => ({
        id: nextId.current++,
        file,
        folder,
        serverId: target.serverId,
        rootId: target.rootId,
        createFolders: target.createFolders ?? false,
        status: "queued",
        loaded: 0,
        total: file.size,
        speed: 0
      }));
      setUploads((prev) => [...prev, ...added]);
    },
    cancel: (id) => {
      const controller = controllers.current.get(id);
      if (controller) controller.abort();
      else update(id, (item) => (item.status === "queued" ? { status: "cancelled", finishedAt: Date.now() } : {}));
    },
    cancelAll: () => {
      controllers.current.forEach((controller) => controller.abort());
      setUploads((prev) => prev.map((item) =>
        item.status === "queued" ? { ...item, status: "cancelled", finishedAt: Date.now() } : item
      ));
    },
    retry: (id) => update(id, { status: "queued", loaded: 0, speed: 0, error: undefined, finishedAt: undefined }),
    clearFinished: () => setUploads((prev) => prev.filter(isActiveUpload)),
    onCompleted: (listener) => {
      listeners.current.add(listener);
      return () => { listeners.current.delete(listener); };
    }
  }), [uploads, update]);

  return <UploadContext.Provider value={value}>{children}</UploadContext.Provider>;
}

export function useUploads(): UploadContextValue {
  const context = useContext(UploadContext);
  if (!context) throw new Error("useUploads must be used inside UploadProvider");
  return context;
}
