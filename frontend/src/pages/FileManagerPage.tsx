import { useState, useEffect, useCallback, useRef } from "react";
import { useSearchParams } from "react-router-dom";
import {
  Folder,
  FileText,
  FileCode,
  ArrowUp,
  ArrowDown,
  RotateCw,
  Upload,
  Download,
  Edit,
  Edit3,
  Move,
  Archive,
  Package,
  XCircle,
  Trash2,
  ChevronRight,
  Check,
  FolderPlus,
  Loader2,
  AlertCircle,
  Keyboard,
  Search,
  Scissors,
  ClipboardPaste,
  FolderOpen,
  AlertTriangle,
  Image as ImageIcon
} from "lucide-react";
import Panel from "@/components/ui/Panel";
import PageHeader from "@/components/layout/PageHeader";
import Modal from "@/components/ui/Modal";
import CodeEditorModal from "@/components/editor/CodeEditorModal";
import BackgroundTaskBar from "@/components/file-manager/BackgroundTaskBar";
import FileManagerEmptyState from "@/components/file-manager/FileManagerEmptyState";
import FileDetailsPanel from "@/components/file-manager/FileDetailsPanel";
import FileManagerActions from "@/components/file-manager/FileManagerActions";
import FolderPicker from "@/components/file-manager/FolderPicker";
import FileIcon from "@/components/file-manager/FileIcon";
import ImagePreview from "@/components/file-manager/ImagePreview";
import ContextMenu, { type ContextMenuEntry } from "@/components/file-manager/ContextMenu";
import LiveServerWarning, { type LiveWarningRequest } from "@/components/file-manager/LiveServerWarning";
import TrashDialog from "@/components/file-manager/TrashDialog";
import { serverApi } from "@/api/servers";
import { useTranslation } from "react-i18next";
import { useServer } from "@/context/ServerContext";
import { filesApi, type DirectoryListingDto, type FileRootDto, type SearchResponseDto, type SearchResultDto, type TrackedOperationDto, type TrashEntryDto } from "@/api/files";
import type { ActiveModal, FileItem, FileSortField, SortDirection } from "@/components/file-manager/types";
import { FILE_MANAGER_UP_DROP_TARGET } from "@/lib/constants";
import { collectDroppedFiles, defaultArchiveName, filesFromFolderInput, formatFileSize, formatRelativeTime, isPreviewableImage, mapFileEntry, normalizeFolderPath, sortFileItems, type DroppedFile } from "@/lib/fileManager";
import { useToast } from "@/context/ToastContext";
import { useUploads } from "@/context/UploadContext";

export default function FileManagerPage() {
  const { t, i18n } = useTranslation();
  const { selectedServer } = useServer();
  const [searchParams, setSearchParams] = useSearchParams();

  // File Manager State
  const [roots, setRoots] = useState<FileRootDto[]>([]);
  const [selectedRootId, setSelectedRootId] = useState<string>(() => searchParams.get("root") || "");
  const [currentPath, setCurrentPath] = useState<string>(() => searchParams.get("path") || "");
  const [selectedItems, setSelectedItems] = useState<FileItem[]>([]);
  const selectedItem = selectedItems.length === 1 ? selectedItems[0] : null;
  const [items, setItems] = useState<FileItem[]>([]);
  const [isEditingPath, setIsEditingPath] = useState(false);
  const [tempPath, setTempPath] = useState<string>(() => searchParams.get("path") || "");
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [listingNotice, setListingNotice] = useState<Pick<DirectoryListingDto, "truncated" | "skipped"> | null>(null);
  // Only the newest listing request may update the table; slower, older responses are dropped.
  const listingRequestRef = useRef(0);

  // Background Task tracking (every started zip/unzip task is polled until it finishes)
  const [operations, setOperations] = useState<TrackedOperationDto[]>([]);
  // Jobs seen running in this tab; when one stops running we announce its result.
  const watchedTasksRef = useRef(new Map<string, string>());
  // Zip-for-download jobs: when one completes, the browser download starts automatically.
  const pendingDownloadsRef = useRef(new Map<string, { url: string; name: string }>());
  const [pollTick, setPollTick] = useState(0);
  // Upload name clashes waiting for a decision.
  const [uploadConflict, setUploadConflict] = useState<{ files: DroppedFile[]; conflicts: string[] } | null>(null);

  // Modal State
  const [activeModal, setActiveModal] = useState<ActiveModal>(null);
  const [modalInput, setModalInput] = useState("");
  // Search (server-side name search, optionally through subfolders).
  const [searchOpen, setSearchOpen] = useState(false);
  const [searchQuery, setSearchQuery] = useState("");
  const [searchRecursive, setSearchRecursive] = useState(true);
  const [searching, setSearching] = useState(false);
  const [searchResult, setSearchResult] = useState<{ query: string; folder: string; recursive: boolean; response: SearchResponseDto } | null>(null);
  // After jumping to a search hit, select it once its folder has loaded.
  const pendingSelectRef = useRef<string | null>(null);
  const searchRequestRef = useRef(0);
  const [maxUploadBytes, setMaxUploadBytes] = useState(0);
  // Names of items that just appeared (uploads, new folders, renames) — highlighted briefly.
  const [highlighted, setHighlighted] = useState<Set<string>>(new Set());
  const [showShortcuts, setShowShortcuts] = useState(false);
  const [renaming, setRenaming] = useState<{ from: string; value: string } | null>(null);
  // Name of the image open in the viewer.
  const [previewName, setPreviewName] = useState<string | null>(null);
  const renameActiveRef = useRef<string | null>(null);
  // Rows with a request in flight show a spinner and ignore clicks.
  const [busyNames, setBusyNames] = useState<Set<string>>(new Set());
  const [liveWarning, setLiveWarning] = useState<LiveWarningRequest | null>(null);
  const [trashOpen, setTrashOpen] = useState(false);
  const [trashRetentionDays, setTrashRetentionDays] = useState(7);
  const [deletePermanently, setDeletePermanently] = useState(false);
  // Cut items waiting to be pasted (Ctrl+X / Ctrl+V); a paste moves them within the same root.
  const [clipboard, setClipboard] = useState<{ serverId: string; rootId: string; folder: string; names: string[] } | null>(null);
  const [contextMenu, setContextMenu] = useState<{ x: number; y: number; item: FileItem | null } | null>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const folderInputRef = useRef<HTMLInputElement>(null);
  const searchInputRef = useRef<HTMLInputElement>(null);
  const tableRef = useRef<HTMLDivElement>(null);
  const typeAheadRef = useRef({ text: "", at: 0 });
  const toast = useToast();
  const uploads = useUploads();

  // Code Editor State
  const [isEditorOpen, setIsEditorOpen] = useState(false);
  const [editingFile, setEditingFile] = useState<FileItem | null>(null);
  const [editorContent, setEditorContent] = useState("");
  const [editorModified, setEditorModified] = useState<string | undefined>(undefined);

  // Sorting State
  // Sort order is remembered per browser (localStorage; falls back to name ascending).
  const [sortField, setSortField] = useState<FileSortField>(() => readStoredSort().field);
  const [sortDirection, setSortDirection] = useState<SortDirection>(() => readStoredSort().direction);
  useEffect(() => {
    try {
      localStorage.setItem(SORT_STORAGE_KEY, JSON.stringify({ field: sortField, direction: sortDirection }));
    } catch {
      // Storage unavailable (private mode); sorting still works for this visit.
    }
  }, [sortField, sortDirection]);

  // Drag & Drop State
  const [dragActive, setDragActive] = useState(false);
  const [dragSource, setDragSource] = useState<FileItem | null>(null);
  const [dropTargetName, setDropTargetName] = useState<string | null>(null);
  const dragDepthRef = useRef(0);

  // Touch interactions (mobile): single tap opens folders, long-press selects them,
  // tapping a file selects it so the Edit button can be used.
  const [isTouchDevice, setIsTouchDevice] = useState<boolean>(
    () => typeof window !== "undefined" && window.matchMedia("(pointer: coarse)").matches
  );
  const lastPointerTypeRef = useRef<"mouse" | "touch">("mouse");
  const longPressTimerRef = useRef<number | null>(null);
  const longPressTriggeredRef = useRef(false);
  const pointerStartPosRef = useRef<{ x: number; y: number } | null>(null);

  // Fetch Listing callback
  const fetchDirectoryListing = useCallback(() => {
    if (!selectedServer) return;
    const requestId = ++listingRequestRef.current;
    const isCurrent = () => requestId === listingRequestRef.current;
    setLoading(true);
    setError(null);

    if (!selectedRootId) {
      // No root chosen yet: ask the server which roots exist instead of assuming one.
      filesApi.roots(selectedServer.id)
        .then((res) => {
          if (!isCurrent()) return;
          setRoots(res);
          const preferred = res.find((r) => r.id === "data") ?? res.find((r) => r.isWritable) ?? res[0];
          if (preferred) {
            setSelectedRootId(preferred.id);
          } else {
            setItems([]);
            setLoading(false);
          }
        })
        .catch((err) => {
          if (!isCurrent()) return;
          setError(errorText(err));
          setLoading(false);
        });
      return;
    }

    filesApi.list(selectedServer.id, selectedRootId, currentPath)
      .then((res) => {
        if (!isCurrent()) return;
        setRoots(res.roots);
        setItems(res.entries.map(mapFileEntry));
        setMaxUploadBytes(res.maximumUploadBytes);
        if (res.trashRetentionDays > 0) setTrashRetentionDays(res.trashRetentionDays);
        setListingNotice(res.truncated || res.skipped > 0 ? { truncated: res.truncated, skipped: res.skipped } : null);
      })
      .catch((err) => {
        if (!isCurrent()) return;
        setError(errorText(err));
      })
      .finally(() => {
        if (isCurrent()) setLoading(false);
      });
  }, [selectedServer?.id, selectedRootId, currentPath]);

  const startTask = (taskId: string, description: string) => {
    watchedTasksRef.current.set(taskId, description);
    setPollTick((tick) => tick + 1);
  };

  const cutSelection = (itemsToCut: FileItem[] = selectedItems) => {
    if (!selectedServer || !selectedRootId || itemsToCut.length === 0 || isReadOnly) return;
    setClipboard({ serverId: selectedServer.id, rootId: selectedRootId, folder: currentPath, names: itemsToCut.map((i) => i.name) });
    toast.info(t("fileManager.clipboard.cut", { count: itemsToCut.length }));
  };

  const pasteClipboard = () => {
    if (!clipboard || !selectedServer || !selectedRootId) return;
    if (clipboard.serverId !== selectedServer.id || clipboard.rootId !== selectedRootId) {
      toast.error(t("fileManager.clipboard.otherRoot"));
      return;
    }
    if (clipboard.folder === currentPath) {
      toast.info(t("fileManager.clipboard.sameFolder"));
      return;
    }
    const pairs = clipboard.names.map<[string, string]>((name) => [
      clipboard.folder ? `${clipboard.folder}/${name}` : name,
      currentPath ? `${currentPath}/${name}` : name
    ]);
    if (pairs.some(([source]) => currentPath === source || currentPath.startsWith(`${source}/`))) {
      toast.error(t("fileManager.cannotMoveIntoItself"));
      return;
    }
    const serverId = selectedServer.id;
    const rootId = selectedRootId;
    void confirmLive(pairs.flat()).then((ok) => { if (ok) pasteMove(serverId, rootId, pairs); });
  };

  const pasteMove = (serverId: string, rootId: string, pairs: [string, string][]) => {
    setLoading(true);
    Promise.allSettled(pairs.map(([source, dest]) => filesApi.move(serverId, rootId, source, dest)))
      .then((results) => {
        const moved = pairs.filter((_, index) => results[index].status === "fulfilled");
        const failed = results.filter((r) => r.status === "rejected") as PromiseRejectedResult[];
        if (failed.length > 0) toast.error(`Failed to move ${failed.length} item(s): ${errorText(failed[0].reason)}`);
        if (moved.length > 0) {
          showMovedToast(moved);
          highlight(moved.map(([, dest]) => dest.split("/").pop()!));
        }
        setClipboard(null);
        fetchDirectoryListing();
      });
  };

  /** "Moved N item(s)" with an Undo that moves everything back. */
  const showMovedToast = (pairs: [string, string][]) => {
    if (!selectedServer || !selectedRootId) return;
    const serverForUndo = selectedServer.id;
    const rootForUndo = selectedRootId;
    toast.success(t("fileManager.toasts.moved", { count: pairs.length }), {
      duration: 8000,
      action: {
        label: t("fileManager.toasts.undo"),
        onClick: () => {
          Promise.allSettled(pairs.map(([source, dest]) => filesApi.move(serverForUndo, rootForUndo, dest, source)))
            .then((results) => {
              const failed = results.filter((r) => r.status === "rejected") as PromiseRejectedResult[];
              if (failed.length > 0) toast.error(errorText(failed[0].reason));
              else toast.info(t("fileManager.toasts.undone"));
              fetchDirectoryListing();
            });
        }
      }
    });
  };

  const openOperationFolder = (op: TrackedOperationDto) => {
    if (!op.target) return;
    setSelectedRootId(op.target.rootId);
    setCurrentPath(op.target.folder);
    setTempPath(op.target.folder);
    setSelectedItems([]);
  };

  // Load directories on init/change
  useEffect(() => {
    fetchDirectoryListing();
  }, [fetchDirectoryListing]);

  // Reset when the user switches server (not on first mount, which may carry a deep link).
  const previousServerRef = useRef(selectedServer?.id);
  useEffect(() => {
    if (previousServerRef.current === selectedServer?.id) return;
    const hadServer = previousServerRef.current !== undefined;
    previousServerRef.current = selectedServer?.id;
    if (!hadServer) return;
    setSelectedRootId("");
    setCurrentPath("");
    setSelectedItems([]);
    setItems([]);
    setRoots([]);
    setClipboard(null);
  }, [selectedServer?.id]);

  // The folder lives in the URL (?root=&path=): each navigation is a history entry, so the
  // browser's back/forward buttons work and folders can be bookmarked or linked.
  const urlRoot = searchParams.get("root") ?? "";
  const urlPath = searchParams.get("path") ?? "";
  // URL -> state (back/forward, deep links).
  useEffect(() => {
    if (urlRoot && urlRoot !== selectedRootId) setSelectedRootId(urlRoot);
    if (urlPath !== currentPath && (urlRoot || urlPath)) {
      setCurrentPath(urlPath);
      setTempPath(urlPath);
      setSelectedItems([]);
    }
    // Only react to URL changes; state changes are pushed by the effect below.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [urlRoot, urlPath]);
  // State -> URL.
  useEffect(() => {
    if (!selectedRootId) return;
    if (urlRoot === selectedRootId && urlPath === currentPath) return;
    const next: Record<string, string> = { root: selectedRootId };
    if (currentPath) next.path = currentPath;
    // Filling in the default root is not a navigation, so it must not add a history entry.
    setSearchParams(next, { replace: !urlRoot });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedRootId, currentPath]);

  // Job poller: one request for all recent jobs. Runs every 1.5 s while anything is running
  // (including jobs started in other tabs or before this page was opened), otherwise once.
  const serverId = selectedServer?.id;
  const anyRunning = operations.some((op) => op.status === "Running") || watchedTasksRef.current.size > 0;
  useEffect(() => {
    if (!serverId) return;
    let cancelled = false;
    const poll = () => filesApi.recentOperations()
      .then((all) => {
        if (cancelled) return;
        const mine = all.filter((op) => !op.target || op.target.serverId === serverId);
        setOperations(mine);

        let anyFinished = false;
        for (const [taskId, description] of watchedTasksRef.current) {
          const op = mine.find((o) => o.taskId === taskId);
          if (op?.status === "Running") continue;
          watchedTasksRef.current.delete(taskId);
          anyFinished = true;
          if (!op) {
            // Usually the panel restarted and forgot the job; its outcome is unknown.
            toast.error(`${description}: ${t("fileManager.taskLost")}`);
          } else if (op.status === "Completed" && pendingDownloadsRef.current.has(taskId)) {
            const download = pendingDownloadsRef.current.get(taskId)!;
            pendingDownloadsRef.current.delete(taskId);
            triggerDownloadRef.current(download.url, download.name);
            toast.success(t("fileManager.downloadZip.started", { name: download.name }));
          } else if (op.status === "Completed") {
            toast.success(t("fileManager.toasts.taskDone", { description: op.description }), op.target ? {
              action: { label: t("fileManager.jobs.openFolder"), onClick: () => openOperationFolderRef.current(op) }
            } : undefined);
          } else if (op.status === "Cancelled") {
            toast.info(t("fileManager.toasts.taskCancelled", { description: op.description }));
          } else {
            toast.error(`${op.description}: ${op.errorMessage || "Unknown error"}`);
          }
        }
        // Keep watching jobs that other tabs started, so their results refresh this listing too.
        mine.filter((op) => op.status === "Running").forEach((op) => {
          if (!watchedTasksRef.current.has(op.taskId)) watchedTasksRef.current.set(op.taskId, op.description);
        });
        if (anyFinished) fetchDirectoryListing();
      })
      .catch(() => undefined);

    void poll();
    if (!anyRunning) return () => { cancelled = true; };
    const timer = window.setInterval(() => void poll(), 1500);
    return () => { cancelled = true; window.clearInterval(timer); };
  }, [serverId, anyRunning, pollTick, fetchDirectoryListing, t, toast]);

  const triggerDownloadRef = useRef<(url: string, name: string) => void>(() => undefined);
  const openOperationFolderRef = useRef(openOperationFolder);
  openOperationFolderRef.current = openOperationFolder;

  const cancelOperation = (taskId: string) => {
    filesApi.cancelOperation(taskId)
      .then(() => setPollTick((tick) => tick + 1))
      .catch((err) => toast.error(errorText(err)));
  };

  const handleSort = (field: FileSortField) => {
    if (sortField === field) {
      setSortDirection((prev) => (prev === "asc" ? "desc" : "asc"));
    } else {
      setSortField(field);
      setSortDirection("asc");
    }
  };

  const sortedItems = sortFileItems(items, sortField, sortDirection);
  const isReadOnly = roots.find((r) => r.id === selectedRootId)?.isWritable === false;
  const protectedPaths = roots.find((r) => r.id === selectedRootId)?.protectedPaths ?? [];
  const serverOnline = selectedServer?.status === "online";
  /** True when the path is, is inside, or contains a path the live server uses. */
  const isProtectedPath = (path: string) => {
    const p = path.replace(/^\/+|\/+$/g, "");
    return protectedPaths.some((pp) => p === pp || p.startsWith(`${pp}/`) || (p !== "" && pp.startsWith(`${p}/`)));
  };
  const inProtectedFolder = currentPath !== "" && isProtectedPath(currentPath);

  /**
   * Resolves true right away unless the server is online and a path is protected; then it
   * shows the live-server warning (with a fresh player count) and resolves with the choice.
   */
  const confirmLive = async (paths: string[]): Promise<boolean> => {
    const risky = [...new Set(paths.map((p) => p.replace(/^\/+|\/+$/g, "")))].filter(isProtectedPath);
    if (!serverOnline || risky.length === 0 || !selectedServer) return true;
    let players: number | null = null;
    try {
      const res = await serverApi.connections(selectedServer.id);
      players = new Set(res.connections.map((c) => c.playerName ?? `${c.remoteAddress}:${c.remotePort}`)).size;
    } catch {
      players = null;
    }
    return new Promise<boolean>((resolve) => setLiveWarning({ paths: risky, players, resolve }));
  };
  const joinPath = (folder: string, name: string) => (folder ? `${folder}/${name}` : name);
  const cutNames = clipboard && clipboard.rootId === selectedRootId && clipboard.folder === currentPath
    ? new Set(clipboard.names) : null;
  // Everything that acts on "the rows on screen" (range select, keyboard) uses visibleItems.
  const visibleItems = sortedItems;

  const openSearch = () => {
    setSearchOpen(true);
    window.setTimeout(() => { searchInputRef.current?.focus(); searchInputRef.current?.select(); }, 0);
  };

  const closeSearch = () => {
    searchRequestRef.current++;
    setSearchOpen(false);
    setSearchResult(null);
    setSearching(false);
  };

  const runSearch = () => {
    const query = searchQuery.trim();
    if (!selectedServer || !selectedRootId || !query) return;
    const requestId = ++searchRequestRef.current;
    const folder = currentPath;
    setSearching(true);
    filesApi.search(selectedServer.id, selectedRootId, folder, query, searchRecursive)
      .then((response) => {
        if (requestId === searchRequestRef.current) setSearchResult({ query, folder, recursive: searchRecursive, response });
      })
      .catch((err) => { if (requestId === searchRequestRef.current) toast.error(errorText(err)); })
      .finally(() => { if (requestId === searchRequestRef.current) setSearching(false); });
  };

  /** Opens the folder containing a hit and selects it (or enters the folder when it is one and `enter` is set). */
  const openSearchHit = (hit: SearchResultDto, enter = false) => {
    const slash = hit.path.lastIndexOf("/");
    const parent = slash === -1 ? "" : hit.path.slice(0, slash);
    const folder = enter && hit.isFolder ? hit.path : parent;
    pendingSelectRef.current = enter && hit.isFolder ? null : hit.name;
    if (folder === currentPath) {
      const item = items.find((i) => i.name === hit.name);
      if (item && !enter) setSelectedItems([item]);
      tableRef.current?.querySelector(`[data-name="${CSS.escape(hit.name)}"]`)?.scrollIntoView({ block: "nearest" });
      return;
    }
    setCurrentPath(folder);
    setTempPath(folder);
    setSelectedItems([]);
  };


  const highlight = useCallback((names: string[]) => {
    setHighlighted((prev) => new Set([...prev, ...names]));
    window.setTimeout(() => {
      setHighlighted((prev) => {
        const next = new Set(prev);
        names.forEach((name) => next.delete(name));
        return next;
      });
    }, 4000);
  }, []);

  // Select a pending search hit once its folder's listing has arrived.
  useEffect(() => {
    const name = pendingSelectRef.current;
    if (!name) return;
    const item = items.find((i) => i.name === name);
    if (!item) return;
    pendingSelectRef.current = null;
    setSelectedItems([item]);
    highlight([name]);
    window.setTimeout(() => {
      tableRef.current?.querySelector(`[data-name="${CSS.escape(name)}"]`)?.scrollIntoView({ block: "nearest" });
    }, 0);
  }, [items, highlight]);

  // Refresh (at most every 800 ms) and highlight when an upload into this folder finishes.
  const locationRef = useRef({ serverId: "", rootId: "", folder: "" });
  locationRef.current = { serverId: selectedServer?.id ?? "", rootId: selectedRootId, folder: currentPath };
  const refreshTimerRef = useRef<number | null>(null);
  useEffect(() => uploads.onCompleted((item) => {
    const here = locationRef.current;
    if (item.serverId !== here.serverId || item.rootId !== here.rootId) return;
    const inside = here.folder === "" ? item.folder : item.folder.startsWith(`${here.folder}/`) ? item.folder.slice(here.folder.length + 1) : null;
    if (item.folder !== here.folder && !inside) return;
    // A file inside an uploaded folder highlights that folder's row.
    highlight([item.folder === here.folder ? item.file.name : inside!.split("/")[0]]);
    if (refreshTimerRef.current !== null) return;
    refreshTimerRef.current = window.setTimeout(() => {
      refreshTimerRef.current = null;
      fetchDirectoryListing();
    }, 800);
  }), [uploads, fetchDirectoryListing, highlight]);

  /** Top-level name a dropped entry creates in the current folder: its folder, or the file itself. */
  const topName = (entry: DroppedFile) => (entry.relDir ? entry.relDir.split("/")[0] : entry.file.name);

  const queueUploads = (entries: DroppedFile[]) => {
    if (!selectedServer || !selectedRootId || entries.length === 0) return;
    const root = roots.find((r) => r.id === selectedRootId);
    if (root && !root.isWritable) {
      toast.error(t("fileManager.uploads.readOnly"));
      return;
    }
    // Reject oversized files up front instead of sending megabytes the server will refuse.
    const accepted = entries.filter(({ file }) => {
      if (maxUploadBytes > 0 && file.size > maxUploadBytes) {
        toast.error(t("fileManager.uploads.tooLarge", {
          name: file.name, size: formatFileSize(file.size), limit: formatFileSize(maxUploadBytes)
        }));
        return false;
      }
      return true;
    });

    // A file can't replace a folder and vice versa; same kind needs a decision
    // (files: replace; folders: merge into the existing one).
    const existing = new Map(items.map((i) => [i.name, i]));
    const kindClash = (entry: DroppedFile) => {
      const found = existing.get(topName(entry));
      return !!found && found.isFolder !== (entry.relDir !== "");
    };
    [...new Set(accepted.filter(kindClash).map(topName))].forEach((name) =>
      toast.error(t("fileManager.uploads.kindExists", { name })));
    const uploadable = accepted.filter((entry) => !kindClash(entry));
    const conflicts = [...new Set(uploadable.filter((entry) => existing.has(topName(entry))).map(topName))];
    if (conflicts.length > 0) {
      setUploadConflict({ files: uploadable, conflicts });
      return;
    }
    void startUploads(uploadable);
  };

  const startUploads = async (entries: DroppedFile[]) => {
    if (!selectedServer || !selectedRootId || entries.length === 0) return;
    const base = currentPath;
    const planned = entries.map(({ file, relDir }) => ({ file, folder: joinPath(base, relDir).replace(/\/$/, "") }));
    if (!(await confirmLive(planned.map(({ file, folder }) => joinPath(folder, file.name))))) return;
    uploads.enqueue(planned, {
      serverId: selectedServer.id,
      rootId: selectedRootId,
      createFolders: entries.some((entry) => entry.relDir !== "")
    });
  };

  const resolveUploadConflict = (choice: "replace" | "keepBoth" | "skip") => {
    if (!uploadConflict) return;
    const clashing = new Set(uploadConflict.conflicts);
    const taken = new Set(items.map((i) => i.name));
    // One new name per clashing top-level entry, shared by every file inside it.
    const renamed = new Map<string, string>();
    const freeName = (name: string, isFolder: boolean) => {
      const dot = isFolder ? -1 : name.lastIndexOf(".");
      const base = dot > 0 ? name.slice(0, dot) : name;
      const ext = dot > 0 ? name.slice(dot) : "";
      let n = 1;
      while (taken.has(`${base} (${n})${ext}`)) n++;
      const free = `${base} (${n})${ext}`;
      taken.add(free);
      return free;
    };
    const entries = uploadConflict.files.flatMap((entry) => {
      const top = topName(entry);
      if (!clashing.has(top) || choice === "replace") return [entry];
      if (choice === "skip") return [];
      const isFolder = entry.relDir !== "";
      if (!renamed.has(top)) renamed.set(top, freeName(top, isFolder));
      const newTop = renamed.get(top)!;
      if (isFolder) {
        return [{ file: entry.file, relDir: [newTop, ...entry.relDir.split("/").slice(1)].join("/") }];
      }
      return [{ file: new File([entry.file], newTop, { type: entry.file.type, lastModified: entry.file.lastModified }), relDir: "" }];
    });
    setUploadConflict(null);
    void startUploads(entries);
  };

  const handleUp = () => {
    const parts = currentPath.split("/").filter(Boolean);
    if (parts.length > 0) {
      parts.pop();
      const newP = parts.join("/");
      setCurrentPath(newP);
      setTempPath(newP);
      setSelectedItems([]);
    }
  };

  const clearLongPressTimer = () => {
    if (longPressTimerRef.current !== null) {
      window.clearTimeout(longPressTimerRef.current);
      longPressTimerRef.current = null;
    }
  };

  const handleRowPointerDown = (e: React.PointerEvent, item: FileItem) => {
    if (e.pointerType !== "touch") {
      lastPointerTypeRef.current = "mouse";
      pointerStartPosRef.current = null;
      return;
    }
    lastPointerTypeRef.current = "touch";
    setIsTouchDevice(true);
    pointerStartPosRef.current = { x: e.clientX, y: e.clientY };

    // Long-press opens the same menu as a right-click (Android also sends a native
    // contextmenu event, which lands in the same state; iOS only gets this timer).
    clearLongPressTimer();
    longPressTriggeredRef.current = false;
    const x = e.clientX;
    const y = e.clientY;
    longPressTimerRef.current = window.setTimeout(() => {
      longPressTriggeredRef.current = true;
      setSelectedItems((prev) => (prev.some((i) => i.name === item.name) ? prev : [item]));
      setContextMenu({ x, y, item });
    }, 500);
  };

  const handleRowPointerMove = (e: React.PointerEvent) => {
    if (e.pointerType !== "touch" || longPressTimerRef.current === null) return;
    const start = pointerStartPosRef.current;
    if (!start) return;
    const distance = Math.abs(e.clientX - start.x) + Math.abs(e.clientY - start.y);
    if (distance > 10) {
      clearLongPressTimer();
      longPressTriggeredRef.current = false;
      pointerStartPosRef.current = null;
    }
  };

  const handleRowPointerEnd = () => {
    clearLongPressTimer();
    pointerStartPosRef.current = null;
  };

  const handleItemClick = (item: FileItem, e?: React.MouseEvent) => {
    if (busyNames.has(item.name)) return;
    if (lastPointerTypeRef.current === "touch" && longPressTriggeredRef.current) {
      longPressTriggeredRef.current = false;
      return;
    }
    if (lastPointerTypeRef.current === "touch" && item.isFolder) {
      handleItemDoubleClick(item);
      return;
    }
    if (e && (e.ctrlKey || e.metaKey)) {
      setSelectedItems((prev) => {
        const exists = prev.some((i) => i.name === item.name);
        return exists ? prev.filter((i) => i.name !== item.name) : [...prev, item];
      });
    } else if (e && e.shiftKey && selectedItems.length > 0) {
      const lastSelected = selectedItems[selectedItems.length - 1];
      const lastIndex = visibleItems.findIndex((i) => i.name === lastSelected.name);
      const currentIndex = visibleItems.findIndex((i) => i.name === item.name);
      if (lastIndex !== -1 && currentIndex !== -1) {
        const start = Math.min(lastIndex, currentIndex);
        const end = Math.max(lastIndex, currentIndex);
        const rangeItems = visibleItems.slice(start, end + 1);
        setSelectedItems((prev) => {
          const next = [...prev];
          rangeItems.forEach((ri) => {
            if (!next.some((i) => i.name === ri.name)) {
              next.push(ri);
            }
          });
          return next;
        });
      }
    } else {
      setSelectedItems([item]);
    }
  };

  const previewUrl = (item: FileItem) =>
    selectedServer && selectedRootId
      ? filesApi.getPreviewUrl(selectedServer.id, selectedRootId, joinPath(currentPath, item.name), item.modifiedMs)
      : "";

  const handleItemDoubleClick = (item: FileItem) => {
    if (!item.isFolder && isPreviewableImage(item.name)) {
      setPreviewName(item.name);
      return;
    }
    if (item.isFolder) {
      const newP = currentPath ? `${currentPath}/${item.name}` : item.name;
      setCurrentPath(newP);
      setTempPath(newP);
      setSelectedItems([]);
    } else {
      openEditorForItem(item);
    }
  };

  const openEditorForItem = (item: FileItem) => {
    if (!selectedServer || !selectedRootId) return;
    const relativeFilePath = currentPath ? `${currentPath}/${item.name}` : item.name;
    setLoading(true);
    filesApi.getContent(selectedServer.id, selectedRootId, relativeFilePath)
      .then((res) => {
        setEditingFile(item);
        setEditorContent(res.content);
        setEditorModified(res.modified);
        setIsEditorOpen(true);
      })
      .catch((err) => {
        toast.error("Error loading content: " + errorText(err));
      })
      .finally(() => {
        setLoading(false);
      });
  };

  const handlePathSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    setCurrentPath(tempPath);
    setIsEditingPath(false);
    setSelectedItems([]);
  };

  // Modal Triggers
  const openUploadModal = () => {
    fileInputRef.current?.click();
  };

  const openMkdirModal = () => {
    setModalInput("");
    setActiveModal("mkdir");
  };

  // Rename happens in place: the row's name turns into a text field (Enter saves, Esc cancels).
  const openRenameModal = () => {
    if (!selectedItem || isReadOnly) return;
    renameActiveRef.current = selectedItem.name;
    setRenaming({ from: selectedItem.name, value: selectedItem.name });
  };

  const commitRename = async () => {
    // Enter and the blur that follows both land here; only the first one counts.
    if (!renaming || renameActiveRef.current !== renaming.from || !selectedServer || !selectedRootId) return;
    renameActiveRef.current = null;
    const oldName = renaming.from;
    const newName = renaming.value.trim();
    if (!newName || newName === oldName) {
      setRenaming(null);
      return;
    }
    if (newName.includes("/") || newName.includes("\\") || newName === "." || newName === "..") {
      toast.error(t("fileManager.renameInvalid"));
      setRenaming(null);
      return;
    }
    const oldPath = joinPath(currentPath, oldName);
    const renamedPath = joinPath(currentPath, newName);
    if (!(await confirmLive([oldPath, renamedPath]))) {
      setRenaming(null);
      return;
    }
    const serverId = selectedServer.id;
    const rootId = selectedRootId;
    setRenaming(null);
    // Show the new name straight away; a failure puts the old one back.
    setItems((prev) => prev.map((i) => (i.name === oldName ? { ...i, name: newName } : i)));
    setBusyNames((prev) => new Set(prev).add(newName));
    filesApi.rename(serverId, rootId, oldPath, newName)
      .then(() => {
        toast.success(t("fileManager.toasts.renamed", { name: newName }), {
          duration: 8000,
          action: {
            label: t("fileManager.toasts.undo"),
            onClick: () => filesApi.rename(serverId, rootId, renamedPath, oldName)
              .then(() => { toast.info(t("fileManager.toasts.undone")); fetchDirectoryListing(); })
              .catch((err) => toast.error(errorText(err)))
          }
        });
        highlight([newName]);
        setSelectedItems([]);
      })
      .catch((err) => {
        setItems((prev) => prev.map((i) => (i.name === newName ? { ...i, name: oldName } : i)));
        toast.error("Error renaming: " + errorText(err));
      })
      .finally(() => {
        setBusyNames((prev) => { const next = new Set(prev); next.delete(newName); return next; });
        fetchDirectoryListing();
      });
  };

  const openMoveModal = () => {
    if (selectedItems.length === 0) return;
    setModalInput(currentPath);
    setActiveModal("move");
  };

  const openPackModal = () => {
    if (selectedItems.length === 0) return;
    setModalInput(defaultArchiveName(selectedItems));
    setActiveModal("pack");
  };

  const openUnpackModal = () => {
    if (selectedItems.length === 0) return;
    setModalInput(currentPath);
    setActiveModal("unpack");
  };

  const openDeleteModal = () => {
    if (selectedItems.length === 0) return;
    setDeletePermanently(false);
    setActiveModal("delete");
  };

  const closeModal = () => {
    setActiveModal(null);
    setModalInput("");
  };

  // Download Trigger
  const triggerBrowserDownload = (url: string, name: string) => {
    const link = document.createElement("a");
    link.href = url;
    link.download = name;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
  };

  triggerDownloadRef.current = triggerBrowserDownload;

  /** One file downloads directly; several items or a folder are zipped on the server first. */
  const handleDownload = (targets: FileItem[] = selectedItems) => {
    if (!selectedServer || !selectedRootId || targets.length === 0) return;
    if (targets.length === 1 && !targets[0].isFolder) {
      const item = targets[0];
      triggerBrowserDownload(filesApi.getDownloadUrl(selectedServer.id, selectedRootId, joinPath(currentPath, item.name)), item.name);
      return;
    }
    if (isReadOnly) {
      toast.error(t("fileManager.downloadZip.readOnly"));
      return;
    }
    const serverId = selectedServer.id;
    const rootId = selectedRootId;
    const name = targets.length === 1
      ? `${targets[0].name}.zip`
      : `${currentPath ? currentPath.split("/").pop() : "files"}-${targets.length}-items.zip`;
    filesApi.prepareDownloadArchive(serverId, rootId, targets.map((item) => joinPath(currentPath, item.name)))
      .then(({ taskId, archiveId }) => {
        pendingDownloadsRef.current.set(taskId, { url: filesApi.downloadArchiveUrl(serverId, rootId, archiveId, name), name });
        startTask(taskId, t("fileManager.downloadZip.preparing", { name }));
      })
      .catch((err) => toast.error(errorText(err)));
  };

  // Drag & Drop Handlers

  // Upload: dragging files from the OS over the file table area.
  const handleDragEnter = (e: React.DragEvent) => {
    e.preventDefault();
    dragDepthRef.current += 1;
    if (!dragSource) setDragActive(true);
  };

  const handleDragOver = (e: React.DragEvent) => {
    e.preventDefault();
    // During an internal move the folder rows handle drop effect themselves.
    if (dragSource) return;
    e.dataTransfer.dropEffect = "copy";
    setDragActive(true);
  };

  const handleDragLeave = (e: React.DragEvent) => {
    e.preventDefault();
    dragDepthRef.current = Math.max(0, dragDepthRef.current - 1);
    if (dragDepthRef.current === 0) setDragActive(false);
  };

  const handleFilesDrop = (dataTransfer: DataTransfer) => {
    setDragActive(false);
    // Folders are walked recursively; the upload queue recreates their structure.
    void collectDroppedFiles(dataTransfer)
      .then(queueUploads)
      .catch((err) => toast.error(errorText(err)));
  };

  const handleDrop = (e: React.DragEvent) => {
    e.preventDefault();
    dragDepthRef.current = 0;
    if (dragSource) {
      setDragActive(false);
      return;
    }
    handleFilesDrop(e.dataTransfer);
  };

  // Move: dragging a row onto a folder row.
  const handleRowDragStart = (e: React.DragEvent, item: FileItem) => {
    e.dataTransfer.effectAllowed = "move";
    e.dataTransfer.setData("text/plain", item.name);
    setDragSource(item);
  };

  const handleRowDragEnd = () => {
    setDragSource(null);
    setDropTargetName(null);
  };

  const handleFolderDragOver = (e: React.DragEvent, item: FileItem) => {
    if (!dragSource || !item.isFolder || item.name === dragSource.name) return;
    e.preventDefault();
    e.dataTransfer.dropEffect = "move";
    setDropTargetName(item.name);
  };

  const handleFolderDragLeave = (item: FileItem) => {
    if (dropTargetName === item.name) setDropTargetName(null);
  };

  const handleFolderDrop = (e: React.DragEvent, targetFolder: FileItem) => {
    e.preventDefault();
    e.stopPropagation();
    if (!selectedServer || !selectedRootId || !dragSource) return;

    const sourcePath = currentPath ? `${currentPath}/${dragSource.name}` : dragSource.name;
    const targetFolderPath = currentPath ? `${currentPath}/${targetFolder.name}` : targetFolder.name;

    // Prevent moving an item into itself or into one of its own subdirectories.
    if (targetFolderPath === sourcePath || targetFolderPath.startsWith(sourcePath + "/")) {
      setDragSource(null);
      setDropTargetName(null);
      toast.error(t("fileManager.cannotMoveIntoItself"));
      return;
    }

    const destPath = `${targetFolderPath}/${dragSource.name}`;
    setDragSource(null);
    setDropTargetName(null);
    const serverId = selectedServer.id;
    const rootId = selectedRootId;
    void confirmLive([sourcePath, destPath]).then((ok) => {
      if (!ok) return;
      setLoading(true);
      setError(null);
      filesApi.move(serverId, rootId, sourcePath, destPath)
        .then(() => {
          showMovedToast([[sourcePath, destPath]]);
          fetchDirectoryListing();
          setSelectedItems([]);
        })
        .catch((err) => {
          toast.error("Error moving: " + errorText(err));
          setLoading(false);
        });
    });
  };

  // Move: dropping a dragged row onto a path segment moves it into that folder.
  const crumbDropProps = (folder: string) => ({
    onDragOver: (e: React.DragEvent) => {
      if (!dragSource || folder === currentPath) return;
      e.preventDefault();
      e.stopPropagation();
      e.dataTransfer.dropEffect = "move";
      setDropTargetName(`crumb:${folder}`);
    },
    onDragLeave: () => {
      if (dropTargetName === `crumb:${folder}`) setDropTargetName(null);
    },
    onDrop: (e: React.DragEvent) => {
      e.preventDefault();
      e.stopPropagation();
      if (!selectedServer || !selectedRootId || !dragSource || folder === currentPath) return;
      const sourcePath = joinPath(currentPath, dragSource.name);
      const destPath = joinPath(folder, dragSource.name);
      setDragSource(null);
      setDropTargetName(null);
      const serverId = selectedServer.id;
      const rootId = selectedRootId;
      void confirmLive([sourcePath, destPath]).then((ok) => {
        if (!ok) return;
        filesApi.move(serverId, rootId, sourcePath, destPath)
          .then(() => {
            showMovedToast([[sourcePath, destPath]]);
            fetchDirectoryListing();
            setSelectedItems([]);
          })
          .catch((err) => toast.error("Error moving: " + errorText(err)));
      });
    }
  });

  // Move: dropping a dragged item onto the "Up" row moves it into the parent directory.
  const handleUpDragOver = (e: React.DragEvent) => {
    if (!dragSource) return;
    e.preventDefault();
    e.dataTransfer.dropEffect = "move";
    setDropTargetName(FILE_MANAGER_UP_DROP_TARGET);
  };

  const handleUpDragLeave = () => {
    if (dropTargetName === FILE_MANAGER_UP_DROP_TARGET) setDropTargetName(null);
  };

  const handleUpDrop = (e: React.DragEvent) => {
    e.preventDefault();
    e.stopPropagation();
    if (!selectedServer || !selectedRootId || !dragSource || !currentPath) return;

    const parts = currentPath.split("/").filter(Boolean);
    parts.pop();
    const parentPath = parts.join("/");
    const sourcePath = currentPath ? `${currentPath}/${dragSource.name}` : dragSource.name;
    const destPath = parentPath ? `${parentPath}/${dragSource.name}` : dragSource.name;

    setDragSource(null);
    setDropTargetName(null);
    const serverId = selectedServer.id;
    const rootId = selectedRootId;
    void confirmLive([sourcePath, destPath]).then((ok) => {
      if (!ok) return;
      setLoading(true);
      setError(null);
      filesApi.move(serverId, rootId, sourcePath, destPath)
        .then(() => {
          showMovedToast([[sourcePath, destPath]]);
          fetchDirectoryListing();
          setSelectedItems([]);
        })
        .catch((err) => {
          toast.error("Error moving: " + errorText(err));
          setLoading(false);
        });
    });
  };

  // Modal Submit Handlers
  const handleModalConfirm = async () => {
    if (!selectedServer || !selectedRootId) return;

    // Paths each action would change; the live-server check runs before anything happens.
    const selectedPaths = selectedItems.map((item) => joinPath(currentPath, item.name));
    const touched =
      activeModal === "delete" ? selectedPaths
      : activeModal === "rename" && selectedItem ? [joinPath(currentPath, selectedItem.name), joinPath(currentPath, modalInput.trim())]
      : activeModal === "move" ? [...selectedPaths, ...selectedItems.map((item) => joinPath(normalizeFolderPath(modalInput), item.name))]
      : activeModal === "unpack" ? [normalizeFolderPath(modalInput)]
      : activeModal === "mkdir" ? [joinPath(currentPath, modalInput.trim())]
      : [];
    if (touched.length > 0 && !(await confirmLive(touched))) return;

    if (activeModal === "mkdir" && modalInput.trim()) {
      const relativeDirPath = currentPath ? `${currentPath}/${modalInput.trim()}` : modalInput.trim();
      setLoading(true);
      filesApi.createDirectory(selectedServer.id, selectedRootId, relativeDirPath)
        .then(() => {
          closeModal();
          toast.success(t("fileManager.toasts.created", { name: modalInput.trim() }));
          highlight([modalInput.trim()]);
          fetchDirectoryListing();
        })
        .catch((err) => {
          toast.error("Error creating folder: " + errorText(err));
          closeModal();
          setLoading(false);
        });
    } else if (activeModal === "rename" && selectedItem && modalInput.trim()) {
      const relativeFilePath = currentPath ? `${currentPath}/${selectedItem.name}` : selectedItem.name;
      setLoading(true);
      filesApi.rename(selectedServer.id, selectedRootId, relativeFilePath, modalInput.trim())
        .then(() => {
          const newName = modalInput.trim();
          closeModal();
          const oldName = selectedItem.name;
          const renamedPath = currentPath ? `${currentPath}/${newName}` : newName;
          const serverForUndo = selectedServer.id;
          const rootForUndo = selectedRootId;
          toast.success(t("fileManager.toasts.renamed", { name: newName }), {
            duration: 8000,
            action: {
              label: t("fileManager.toasts.undo"),
              onClick: () => filesApi.rename(serverForUndo, rootForUndo, renamedPath, oldName)
                .then(() => { toast.info(t("fileManager.toasts.undone")); fetchDirectoryListing(); })
                .catch((err) => toast.error(errorText(err)))
            }
          });
          highlight([newName]);
          fetchDirectoryListing();
          setSelectedItems([]);
        })
        .catch((err) => {
          toast.error("Error renaming: " + errorText(err));
          closeModal();
          setLoading(false);
        });
    } else if (activeModal === "move" && selectedItems.length > 0) {
      // An empty destination means the root folder.
      const destFolder = normalizeFolderPath(modalInput);
      if (destFolder === currentPath) {
        closeModal();
        return;
      }
      setLoading(true);
      setError(null);

      let failedCount = 0;
      let firstError: string | null = null;
      const movedPairs: [string, string][] = [];

      (async () => {
        for (const item of selectedItems) {
          const source = currentPath ? `${currentPath}/${item.name}` : item.name;
          const dest = destFolder ? `${destFolder}/${item.name}` : item.name;
          try {
            await filesApi.move(selectedServer.id, selectedRootId, source, dest);
            movedPairs.push([source, dest]);
          } catch (err) {
            failedCount++;
            if (!firstError) {
              firstError = err instanceof Error ? err.message : String(err);
            }
          }
        }
      })()
      .then(() => {
        if (failedCount > 0) {
          toast.error(`Failed to move ${failedCount} item(s): ${firstError}`);
        }
        if (movedPairs.length > 0) {
          showMovedToast(movedPairs);
        }
        closeModal();
        fetchDirectoryListing();
        setSelectedItems([]);
      });
    } else if (activeModal === "pack" && selectedItems.length > 0 && modalInput.trim()) {
      const sources = selectedItems.map(item => currentPath ? `${currentPath}/${item.name}` : item.name);
      const dest = currentPath ? `${currentPath}/${modalInput.trim()}` : modalInput.trim();
      setLoading(true);
      filesApi.compress(selectedServer.id, selectedRootId, sources, dest)
        .then((res) => {
          closeModal();
          setLoading(false);
          startTask(res.taskId, `Compressing ${sources.length} item(s) to '${dest}'`);
        })
        .catch((err) => {
          toast.error("Error starting archiving: " + errorText(err));
          closeModal();
          setLoading(false);
        });
    } else if (activeModal === "unpack" && selectedItems.length > 0) {
      // An empty destination means the root folder.
      const dest = normalizeFolderPath(modalInput);
      setLoading(true);
      setError(null);

      let failedCount = 0;
      let firstError: string | null = null;

      (async () => {
        for (const item of selectedItems) {
          const source = currentPath ? `${currentPath}/${item.name}` : item.name;
          try {
            const res = await filesApi.extract(selectedServer.id, selectedRootId, source, dest);
            startTask(res.taskId, `Extracting '${item.name}' to '/${dest}'`);
          } catch (err) {
            failedCount++;
            if (!firstError) {
              firstError = err instanceof Error ? err.message : String(err);
            }
          }
        }
      })()
      .then(() => {
        if (failedCount > 0) {
          toast.error(`Failed to extract ${failedCount} item(s): ${firstError}`);
        }
        closeModal();
        setLoading(false);
      });
    } else if (activeModal === "delete" && selectedItems.length > 0) {
      setLoading(true);
      setError(null);
      const permanent = deletePermanently;
      const serverId = selectedServer.id;
      const rootId = selectedRootId;
      const trashed: TrashEntryDto[] = [];
      setBusyNames(new Set(selectedItems.map((item) => item.name)));
      closeModal();
      let failedCount = 0;
      let firstError: string | null = null;

      for (const item of selectedItems) {
        try {
          const entry = await filesApi.delete(serverId, rootId, joinPath(currentPath, item.name), permanent);
          if (entry) trashed.push(entry);
        } catch (err) {
          failedCount++;
          firstError ??= errorText(err);
        }
      }

      if (failedCount > 0) {
        toast.error(`Failed to delete ${failedCount} item(s): ${firstError}`);
      }
      const done = selectedItems.length - failedCount;
      if (done > 0 && permanent) {
        toast.success(t("fileManager.toasts.deleted", { count: done }));
      } else if (trashed.length > 0) {
        toast.success(t("fileManager.toasts.trashed", { count: trashed.length }), {
          duration: 10000,
          action: {
            label: t("fileManager.toasts.undo"),
            onClick: () => {
              Promise.allSettled(trashed.map((entry) => filesApi.restoreTrash(serverId, rootId, entry.id)))
                .then((results) => {
                  const failed = results.filter((r) => r.status === "rejected") as PromiseRejectedResult[];
                  if (failed.length > 0) toast.error(errorText(failed[0].reason));
                  else toast.info(t("fileManager.toasts.undone"));
                  fetchDirectoryListing();
                });
            }
          }
        });
      }
      setBusyNames(new Set());
      fetchDirectoryListing();
      setSelectedItems([]);
    }
  };

  const restoreFromTrash = async (entry: TrashEntryDto): Promise<boolean> => {
    if (!selectedServer || !selectedRootId) return false;
    if (!(await confirmLive([entry.originalPath]))) return false;
    try {
      await filesApi.restoreTrash(selectedServer.id, selectedRootId, entry.id);
      toast.success(t("fileManager.trash.restored", { path: `/${entry.originalPath}` }));
      return true;
    } catch (err) {
      toast.error(errorText(err));
      return false;
    }
  };

  // Keyboard shortcuts. Re-registered each render so handlers see current state.
  useEffect(() => {
    const focusRow = (item: FileItem | undefined) => {
      if (!item) return;
      tableRef.current
        ?.querySelector(`[data-name="${CSS.escape(item.name)}"]`)
        ?.scrollIntoView({ block: "nearest" });
    };

    const handler = (e: KeyboardEvent) => {
      if (activeModal || isEditorOpen || uploadConflict || contextMenu || liveWarning || trashOpen || previewName) return;
      if (showShortcuts) {
        if (e.key === "Escape" || e.key === "?") { e.preventDefault(); setShowShortcuts(false); }
        return;
      }

      const target = e.target as HTMLElement | null;
      const typing = !!target && (target.isContentEditable || ["INPUT", "TEXTAREA", "SELECT"].includes(target.tagName));
      if (typing) {
        if (target === searchInputRef.current && e.key === "Escape") {
          e.preventDefault();
          closeSearch();
        }
        return;
      }

      const ctrl = e.ctrlKey || e.metaKey;
      const lastSelected = selectedItems[selectedItems.length - 1];
      const index = lastSelected ? visibleItems.findIndex((i) => i.name === lastSelected.name) : -1;

      if (e.key === "ArrowDown" || e.key === "ArrowUp") {
        if (e.altKey && e.key === "ArrowUp") { e.preventDefault(); handleUp(); return; }
        if (visibleItems.length === 0) return;
        e.preventDefault();
        const next = e.key === "ArrowDown"
          ? Math.min(visibleItems.length - 1, index + 1)
          : Math.max(0, index === -1 ? visibleItems.length - 1 : index - 1);
        const item = visibleItems[next];
        if (e.shiftKey) {
          setSelectedItems((prev) => [...prev.filter((i) => i.name !== item.name), item]);
        } else {
          setSelectedItems([item]);
        }
        focusRow(item);
      } else if (e.key === "Home" || e.key === "End") {
        if (visibleItems.length === 0) return;
        e.preventDefault();
        const item = e.key === "Home" ? visibleItems[0] : visibleItems[visibleItems.length - 1];
        setSelectedItems([item]);
        focusRow(item);
      } else if (e.key === "Enter") {
        if (selectedItem) { e.preventDefault(); handleItemDoubleClick(selectedItem); }
      } else if (e.key === "Backspace") {
        e.preventDefault();
        handleUp();
      } else if (e.key === "F2") {
        e.preventDefault();
        openRenameModal();
      } else if (e.key === "Delete") {
        e.preventDefault();
        openDeleteModal();
      } else if (ctrl && e.key.toLowerCase() === "x") {
        e.preventDefault();
        cutSelection();
      } else if (ctrl && e.key.toLowerCase() === "v") {
        e.preventDefault();
        pasteClipboard();
      } else if (ctrl && e.key.toLowerCase() === "a") {
        e.preventDefault();
        setSelectedItems(visibleItems);
      } else if (ctrl && e.key.toLowerCase() === "u") {
        e.preventDefault();
        openUploadModal();
      } else if (ctrl && e.shiftKey && e.key.toLowerCase() === "n") {
        e.preventDefault();
        openMkdirModal();
      } else if ((ctrl && e.key.toLowerCase() === "f") || e.key === "/") {
        e.preventDefault();
        openSearch();
      } else if (e.key === "F5" || (ctrl && e.key.toLowerCase() === "r")) {
        e.preventDefault();
        fetchDirectoryListing();
      } else if (e.key === "Escape") {
        if (selectedItems.length > 0) setSelectedItems([]);
        else if (searchOpen) closeSearch();
        else setClipboard(null);
      } else if (e.key === "?") {
        e.preventDefault();
        setShowShortcuts(true);
      } else if (!ctrl && !e.altKey && e.key.length === 1 && e.key !== " ") {
        // Type-ahead: letters typed within a second jump to the first name starting with them.
        const now = Date.now();
        const state = typeAheadRef.current;
        state.text = now - state.at < 1000 ? state.text + e.key.toLowerCase() : e.key.toLowerCase();
        state.at = now;
        const match = visibleItems.find((i) => i.name.toLowerCase().startsWith(state.text));
        if (match) {
          setSelectedItems([match]);
          focusRow(match);
        }
      }
    };

    window.addEventListener("keydown", handler);
    return () => window.removeEventListener("keydown", handler);
  });

  const buildContextMenu = (item: FileItem | null): ContextMenuEntry[] => {
    const canWrite = !isReadOnly;
    const paste: ContextMenuEntry = clipboard && canWrite
      ? { label: t("fileManager.clipboard.paste"), icon: <ClipboardPaste size={13} />, shortcut: "Ctrl+V", onSelect: pasteClipboard,
          disabled: clipboard.rootId !== selectedRootId || clipboard.folder === currentPath }
      : null;

    if (!item) {
      return [
        canWrite ? { label: t("fileManager.upload"), icon: <Upload size={13} />, shortcut: "Ctrl+U", onSelect: openUploadModal } : null,
        canWrite ? { label: t("fileManager.newFolder"), icon: <FolderPlus size={13} />, shortcut: "Ctrl+Shift+N", onSelect: openMkdirModal } : null,
        paste,
        null,
        { label: t("fileManager.refresh"), icon: <RotateCw size={13} />, shortcut: "F5", onSelect: fetchDirectoryListing }
      ].filter((entry, index, all) => entry !== null || (index > 0 && all[index - 1] !== null));
    }

    // Acts on the whole selection when the clicked row is part of it.
    const targets = selectedItems.some((i) => i.name === item.name) ? selectedItems : [item];
    const single = targets.length === 1 ? targets[0] : null;
    const entries: ContextMenuEntry[] = [
      single?.isFolder
        ? { label: t("fileManager.open"), icon: <FolderOpen size={13} />, shortcut: "Enter", onSelect: () => handleItemDoubleClick(single) }
        : single && isPreviewableImage(single.name)
          ? { label: t("fileManager.preview.open"), icon: <ImageIcon size={13} />, shortcut: "Enter", onSelect: () => setPreviewName(single.name) }
          : single
            ? { label: t("fileManager.edit"), icon: <Edit size={13} />, shortcut: "Enter", onSelect: () => openEditorForItem(single) }
            : null,
      {
        label: single && !single.isFolder ? t("fileManager.download") : t("fileManager.downloadZip.action"),
        icon: <Download size={13} />,
        onSelect: () => handleDownload(targets)
      },
      null
    ];
    if (canWrite) {
      entries.push(
        single ? { label: t("fileManager.rename"), icon: <Edit3 size={13} />, shortcut: "F2", onSelect: openRenameModal } : null,
        { label: t("fileManager.clipboard.cutAction"), icon: <Scissors size={13} />, shortcut: "Ctrl+X", onSelect: () => cutSelection(targets) },
        paste,
        { label: t("fileManager.move"), icon: <Move size={13} />, onSelect: openMoveModal },
        { label: t("fileManager.pack"), icon: <Package size={13} />, onSelect: openPackModal },
        targets.every((i) => i.name.toLowerCase().endsWith(".zip"))
          ? { label: t("fileManager.unpack"), icon: <Archive size={13} />, onSelect: openUnpackModal }
          : null,
        null,
        { label: t("fileManager.delete"), icon: <Trash2 size={13} />, shortcut: "Del", danger: true, onSelect: openDeleteModal }
      );
    }
    // Drop empty slots and duplicate/edge separators.
    return entries
      .filter((entry, index, all) => entry !== null || all.slice(0, index).some((e) => e !== null))
      .filter((entry, index, all) => entry !== null ? true : all[index - 1] !== null && all.slice(index + 1).some((e) => e !== null));
  };

  const pathParts = currentPath.split("/").filter(Boolean);

  if (!selectedServer) {
    return <FileManagerEmptyState />;
  }

  return (
    <div className="space-y-4 max-w-none">
      <PageHeader
        title={t("fileManager.title")}
        description={t("fileManager.description")}
      />

      {/* Background Task Bar */}
      <BackgroundTaskBar operations={operations} onCancel={cancelOperation} onOpenFolder={openOperationFolder} />

      {/* Unified Top Control Header */}
      <div className="flex flex-col gap-3 glass-panel rounded-lg p-3 shadow-md">
        {/* Address Bar */}
        <div className="flex flex-wrap items-center gap-2">
          {/* Root directory selector */}
          <select
            value={selectedRootId}
            onChange={(e) => {
              setSelectedRootId(e.target.value);
              setCurrentPath("");
              setSelectedItems([]);
            }}
            className="rounded-md bg-slate-950/40 border border-red-950/45 px-3 py-1.5 text-xs font-semibold text-slate-200 focus:outline-none focus:border-[#e04444] cursor-pointer h-[32px] shrink-0"
          >
            {roots.map((r) => (
              <option key={r.id} value={r.id} className="bg-slate-950 text-slate-200">
                {r.displayName}
              </option>
            ))}
          </select>

          {/* Back/Up Button */}
          <button
            onClick={handleUp}
            disabled={!currentPath}
            className="flex items-center gap-1.5 rounded-md bg-slate-950/30 hover:bg-red-950/15 border border-red-950/45 px-3 py-1.5 text-xs font-semibold text-slate-200 transition-colors disabled:opacity-40 disabled:cursor-not-allowed shrink-0 h-[32px]"
            title={t("fileManager.up")}
          >
            <ArrowUp size={14} className="text-[#e04444]" />
            <span>{t("fileManager.up")}</span>
          </button>

          {/* Location Path Input Bar */}
          <div className="flex-1 min-w-0 flex items-center bg-slate-950/60 border border-red-950/45 rounded-md px-3 text-xs font-mono text-slate-200 focus-within:border-[#e04444] transition-colors h-[32px]">
            {isEditingPath ? (
              <form onSubmit={handlePathSubmit} className="flex-1 flex items-center gap-1.5">
                <input
                  type="text"
                  value={tempPath}
                  onChange={(e) => setTempPath(e.target.value)}
                  onBlur={() => setIsEditingPath(false)}
                  autoFocus
                  className="w-full bg-transparent text-xs font-mono text-slate-100 focus:outline-none"
                />
                <button type="submit" className="text-[#e04444] hover:text-[#f87171]">
                  <Check size={14} />
                </button>
              </form>
            ) : (
              <div
                onClick={() => {
                  setTempPath(currentPath);
                  setIsEditingPath(true);
                }}
                className="flex-1 flex items-center gap-1.5 cursor-text overflow-x-auto whitespace-nowrap select-none"
                title="Click to edit path"
              >
                <span
                  className={`rounded px-0.5 ${dropTargetName === "crumb:" ? "bg-red-950/60 text-white ring-1 ring-[#e04444]" : "text-slate-500"}`}
                  {...crumbDropProps("")}
                >
                  /
                </span>
                {pathParts.map((part, idx) => (
                  <div key={idx} className="flex items-center gap-1.5 shrink-0">
                    <span
                      {...crumbDropProps(pathParts.slice(0, idx + 1).join("/"))}
                      onClick={(e) => {
                        e.stopPropagation();
                        const subPath = pathParts.slice(0, idx + 1).join("/");
                        setCurrentPath(subPath);
                        setTempPath(subPath);
                        setSelectedItems([]);
                      }}
                      className={`rounded px-0.5 hover:text-[#e04444] hover:underline cursor-pointer ${
                        dropTargetName === `crumb:${pathParts.slice(0, idx + 1).join("/")}` ? "bg-red-950/60 text-white ring-1 ring-[#e04444]" : ""
                      }`}
                    >
                      {part}
                    </span>
                    {idx < pathParts.length - 1 && (
                      <span className="text-slate-600 select-none">/</span>
                    )}
                  </div>
                ))}
              </div>
            )}
          </div>

          {/* Refresh Button */}
          <button
            onClick={() => {
              setSelectedItems([]);
              fetchDirectoryListing();
            }}
            disabled={loading}
            className="flex items-center gap-1.5 rounded-md bg-slate-950/30 hover:bg-red-950/15 border border-red-950/45 px-3 py-1.5 text-xs font-semibold text-slate-200 transition-colors disabled:opacity-40 disabled:cursor-not-allowed shrink-0 h-[32px] cursor-pointer"
            title={t("fileManager.refresh")}
          >
            <RotateCw size={14} className={`text-[#e04444] ${loading ? "animate-spin" : ""}`} />
            <span>{t("fileManager.refresh")}</span>
          </button>

          <button
            onClick={() => (searchOpen ? closeSearch() : openSearch())}
            className={`flex items-center gap-1.5 rounded-md border px-3 py-1.5 text-xs font-semibold transition-colors shrink-0 h-[32px] cursor-pointer ${
              searchOpen ? "bg-red-950/30 border-[#e04444]/60 text-slate-100" : "bg-slate-950/30 hover:bg-red-950/15 border-red-950/45 text-slate-200"
            }`}
            title={`${t("fileManager.search.button")} (/)`}
          >
            <Search size={14} className="text-[#e04444]" />
            <span>{t("fileManager.search.button")}</span>
          </button>

          {!isReadOnly && (
            <button
              onClick={() => setTrashOpen(true)}
              className="flex items-center gap-1.5 rounded-md bg-slate-950/30 hover:bg-red-950/15 border border-red-950/45 px-3 py-1.5 text-xs font-semibold text-slate-200 transition-colors shrink-0 h-[32px] cursor-pointer"
              title={t("fileManager.trash.title")}
            >
              <Trash2 size={14} className="text-[#e04444]" />
              <span className="hidden md:inline">{t("fileManager.trash.button")}</span>
            </button>
          )}

          <button
            onClick={() => setShowShortcuts(true)}
            className="hidden sm:flex items-center gap-1.5 rounded-md bg-slate-950/30 hover:bg-red-950/15 border border-red-950/45 px-3 py-1.5 text-xs font-semibold text-slate-200 transition-colors shrink-0 h-[32px] cursor-pointer"
            title={`${t("fileManager.shortcuts.title")} (?)`}
          >
            <Keyboard size={14} className="text-[#e04444]" />
          </button>
        </div>

        {/* Divider */}
        <div className="h-px w-full bg-red-950/20" />

        <FileManagerActions
          selectedItems={selectedItems}
          onUpload={openUploadModal}
          onUploadFolder={() => folderInputRef.current?.click()}
          onMkdir={openMkdirModal}
          onDownload={() => handleDownload()}
          onEdit={() => selectedItem && openEditorForItem(selectedItem)}
          onRename={openRenameModal}
          onMove={openMoveModal}
          onUnpack={openUnpackModal}
          onPack={openPackModal}
          onDelete={openDeleteModal}
          readOnly={isReadOnly}
        />
      </div>

      {serverOnline && inProtectedFolder && (
        <div className="flex items-start gap-2.5 rounded-lg border border-amber-500/30 bg-amber-950/25 px-4 py-2.5 text-xs text-amber-100">
          <AlertTriangle size={15} className="mt-0.5 shrink-0 text-amber-400" />
          <span>{t("fileManager.live.banner")}</span>
        </div>
      )}

      {searchOpen && (
        <Panel className="border border-slate-700/40 p-3 space-y-3">
          <form
            onSubmit={(e) => { e.preventDefault(); runSearch(); }}
            className="flex flex-wrap items-center gap-2"
          >
            <div className="flex min-w-[14rem] flex-1 items-center gap-2 bg-slate-950/60 border border-red-950/45 rounded-md px-3 h-[32px] focus-within:border-[#e04444]">
              <Search size={14} className="text-slate-500 shrink-0" />
              <input
                ref={searchInputRef}
                type="search"
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                placeholder={t("fileManager.search.placeholder", { folder: `/${currentPath}` })}
                className="w-full bg-transparent text-xs text-slate-100 placeholder:text-slate-500 focus:outline-none"
              />
            </div>
            <label className="flex items-center gap-1.5 text-xs text-slate-300 cursor-pointer select-none">
              <input
                type="checkbox"
                checked={searchRecursive}
                onChange={(e) => setSearchRecursive(e.target.checked)}
                className="accent-[#e04444]"
              />
              {t("fileManager.search.includeSubfolders")}
            </label>
            <button
              type="submit"
              disabled={!searchQuery.trim() || searching}
              className="flex items-center gap-1.5 rounded-md bg-[#e04444] hover:bg-[#c93a3a] px-3 h-[32px] text-xs font-bold text-white disabled:opacity-40 cursor-pointer disabled:cursor-not-allowed"
            >
              {searching ? <Loader2 size={14} className="animate-spin" /> : <Search size={14} />}
              {t("fileManager.search.run")}
            </button>
            <button type="button" onClick={closeSearch} className="text-xs text-slate-400 hover:text-slate-100 cursor-pointer px-1">
              {t("fileManager.search.close")}
            </button>
          </form>

          {searchResult && (
            <div className="space-y-2">
              <div className="text-[11px] text-slate-400">
                {t("fileManager.search.summary", {
                  count: searchResult.response.results.length,
                  query: searchResult.query,
                  folder: `/${searchResult.folder}`
                })}
                {searchResult.recursive ? ` ${t("fileManager.search.withSubfolders")}` : ""}
                {searchResult.response.truncated && (
                  <span className="text-amber-300"> · {t("fileManager.search.truncated")}</span>
                )}
              </div>
              {searchResult.response.results.length > 0 && (
                <ul className="max-h-72 overflow-y-auto rounded border border-slate-700/40 divide-y divide-slate-800/60 text-xs font-mono">
                  {searchResult.response.results.map((hit) => {
                    const slash = hit.path.lastIndexOf("/");
                    const location = slash === -1 ? "/" : `/${hit.path.slice(0, slash)}`;
                    return (
                      <li key={hit.path}>
                        <button
                          type="button"
                          onClick={() => openSearchHit(hit)}
                          onDoubleClick={() => (hit.isFolder ? openSearchHit(hit, true) : undefined)}
                          className="flex w-full items-center gap-2.5 px-3 py-1.5 text-left hover:bg-slate-800/40 cursor-pointer"
                          title={hit.isFolder ? t("fileManager.search.folderHint") : t("fileManager.search.fileHint")}
                        >
                          {hit.isFolder ? (
                            <Folder size={14} className="text-[#ffd8a0] shrink-0" fill="currentColor" fillOpacity={0.2} />
                          ) : (
                            <FileText size={14} className="text-slate-400 shrink-0" />
                          )}
                          <span className="truncate text-slate-200">{hit.name}</span>
                          <span className="ml-auto truncate text-[11px] text-slate-500 max-w-[45%]">{location}</span>
                          <span className="w-20 shrink-0 text-right text-[11px] text-slate-500">
                            {hit.isFolder ? "—" : formatFileSize(hit.size)}
                          </span>
                        </button>
                      </li>
                    );
                  })}
                </ul>
              )}
            </div>
          )}
        </Panel>
      )}

      {clipboard && (
        <div className="flex flex-wrap items-center gap-2 rounded-lg border border-slate-600/40 bg-slate-900/60 px-4 py-2 text-xs text-slate-300">
          <Scissors size={14} className="text-[#e04444]" />
          <span>
            {t("fileManager.clipboard.banner", { count: clipboard.names.length, folder: `/${clipboard.folder}` })}
          </span>
          <button
            type="button"
            onClick={pasteClipboard}
            disabled={clipboard.rootId !== selectedRootId || clipboard.folder === currentPath}
            className="ml-auto flex items-center gap-1 rounded-md border border-red-950/45 bg-slate-950/30 px-2.5 py-1 font-semibold text-slate-200 hover:bg-red-950/15 disabled:opacity-40 cursor-pointer disabled:cursor-not-allowed"
          >
            <ClipboardPaste size={13} />
            {t("fileManager.clipboard.pasteHere")}
          </button>
          <button type="button" onClick={() => setClipboard(null)} className="text-slate-400 hover:text-slate-100 cursor-pointer">
            {t("fileManager.modals.cancel")}
          </button>
        </div>
      )}

      {/* Main Grid: File Table + Details Sidebar */}
      <div className="grid grid-cols-1 lg:grid-cols-4 gap-4 items-start">
        {/* Files Table */}
        <div
          className="lg:col-span-3"
          onDragEnter={handleDragEnter}
          onDragOver={handleDragOver}
          onDragLeave={handleDragLeave}
          onDrop={handleDrop}
          onContextMenu={(e) => {
            // Rows stop propagation, so this is a right-click on empty space.
            e.preventDefault();
            setSelectedItems([]);
            setContextMenu({ x: e.clientX, y: e.clientY, item: null });
          }}
        >
        <Panel className="overflow-hidden border border-slate-700/40 relative min-h-[300px]">
          {loading && items.length === 0 && (
            <div className="absolute inset-0 flex items-center justify-center bg-slate-950/40 backdrop-blur-xs z-10">
              <Loader2 size={32} className="animate-spin text-[#e04444]" />
            </div>
          )}

          {dragActive && !dragSource && (
            <div className="pointer-events-none absolute inset-0 z-20 flex items-center justify-center border-2 border-dashed border-[#e04444]/60 bg-red-950/20 backdrop-blur-xs">
              <div className="flex flex-col items-center gap-2 text-red-200">
                <Upload size={36} className="text-[#e04444]" />
                <span className="text-sm font-semibold">{t("fileManager.dropToUpload")}</span>
              </div>
            </div>
          )}

          {error && (
            <div className="m-4 flex items-start gap-2.5 rounded-md bg-rose-950/30 border border-rose-500/30 p-3.5 text-xs text-rose-200 font-medium">
              <AlertCircle size={16} className="text-rose-400 shrink-0 mt-0.5" />
              <div className="whitespace-pre-line">{error}</div>
            </div>
          )}

          {listingNotice && (
            <div className="mx-4 mt-4 flex items-start gap-2.5 rounded-md bg-amber-950/30 border border-amber-500/30 p-3 text-xs text-amber-200">
              <AlertCircle size={16} className="text-amber-400 shrink-0 mt-0.5" />
              <div>
                {listingNotice.truncated && <div>{t("fileManager.listingTruncated", { count: items.length })}</div>}
                {listingNotice.skipped > 0 && <div>{t("fileManager.listingSkipped", { count: listingNotice.skipped })}</div>}
              </div>
            </div>
          )}

          <div className="overflow-x-auto" ref={tableRef}>
            <table className="w-full text-left text-xs border-collapse">
              <thead>
                <tr className="border-b border-slate-700/60 text-slate-400 bg-slate-800/30 select-none">
                  <th
                    onClick={() => handleSort("name")}
                    className="py-2.5 px-4 font-medium cursor-pointer hover:text-slate-200 transition-colors"
                  >
                    <div className="flex items-center gap-1.5">
                      <span>{t("fileManager.name")}</span>
                      {sortField === "name" && (
                        sortDirection === "asc" ? <ArrowUp size={13} className="text-[#e04444]" /> : <ArrowDown size={13} className="text-[#e04444]" />
                      )}
                    </div>
                  </th>
                  <th
                    onClick={() => handleSort("size")}
                    className="py-2.5 px-4 font-medium w-32 cursor-pointer hover:text-slate-200 transition-colors"
                  >
                    <div className="flex items-center gap-1.5">
                      <span>{t("fileManager.size")}</span>
                      {sortField === "size" && (
                        sortDirection === "asc" ? <ArrowUp size={13} className="text-[#e04444]" /> : <ArrowDown size={13} className="text-[#e04444]" />
                      )}
                    </div>
                  </th>
                  <th
                    onClick={() => handleSort("modified")}
                    className="py-2.5 px-4 font-medium w-48 cursor-pointer hover:text-slate-200 transition-colors"
                  >
                    <div className="flex items-center gap-1.5">
                      <span>{t("fileManager.modified")}</span>
                      {sortField === "modified" && (
                        sortDirection === "asc" ? <ArrowUp size={13} className="text-[#e04444]" /> : <ArrowDown size={13} className="text-[#e04444]" />
                      )}
                    </div>
                  </th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-800/60 font-mono">
                {currentPath && (
                  <tr
                    onClick={handleUp}
                    onDragOver={dragSource ? handleUpDragOver : undefined}
                    onDragLeave={dragSource ? handleUpDragLeave : undefined}
                    onDrop={dragSource ? handleUpDrop : undefined}
                    className={`cursor-pointer transition-colors select-none touch-no-callout ${
                      dropTargetName === FILE_MANAGER_UP_DROP_TARGET
                        ? "bg-red-950/40 text-white ring-2 ring-inset ring-[#e04444]"
                        : "hover:bg-slate-800/40 text-slate-300"
                    }`}
                    title={t("fileManager.up")}
                  >
                    <td className="py-2 px-4">
                      <div className="flex items-center gap-2.5">
                        <ArrowUp size={16} className="text-[#e04444] shrink-0" />
                        <span>..</span>
                      </div>
                    </td>
                    <td className="py-2 px-4 text-slate-500">—</td>
                    <td className="py-2 px-4 text-slate-500 text-[11px]">—</td>
                  </tr>
                )}
                {items.length === 0 && !loading ? (
                  <tr>
                    <td colSpan={3} className="text-center py-12 text-slate-500 font-sans italic">
                      This folder is empty.
                    </td>
                  </tr>
                ) : (
                  visibleItems.map((item) => {
                    const isSelected = selectedItems.some((i) => i.name === item.name);
                    const isNew = highlighted.has(item.name);
                    return (
                      <tr
                        key={item.name}
                        data-name={item.name}
                        draggable={!isTouchDevice && renaming?.from !== item.name}
                        onPointerDown={(e) => handleRowPointerDown(e, item)}
                        onPointerMove={handleRowPointerMove}
                        onPointerUp={handleRowPointerEnd}
                        onPointerCancel={handleRowPointerEnd}
                        onPointerLeave={handleRowPointerEnd}
                        onDragStart={(e) => handleRowDragStart(e, item)}
                        onDragEnd={handleRowDragEnd}
                        onDragOver={item.isFolder ? (e) => handleFolderDragOver(e, item) : undefined}
                        onDragLeave={item.isFolder ? () => handleFolderDragLeave(item) : undefined}
                        onDrop={item.isFolder ? (e) => handleFolderDrop(e, item) : undefined}
                        onClick={(e) => handleItemClick(item, e)}
                        onContextMenu={(e) => {
                          e.preventDefault();
                          e.stopPropagation();
                          if (!selectedItems.some((i) => i.name === item.name)) setSelectedItems([item]);
                          setContextMenu({ x: e.clientX, y: e.clientY, item });
                        }}
                        onDoubleClick={() => handleItemDoubleClick(item)}
                        className={`cursor-pointer transition-colors select-none touch-no-callout ${
                          isSelected
                            ? "bg-[#e04444]/15 text-white font-semibold border-l-2 border-l-[#e04444]"
                            : dropTargetName === item.name
                              ? "bg-red-950/40 text-white ring-2 ring-inset ring-[#e04444]"
                              : cutNames?.has(item.name)
                                ? "opacity-45 hover:bg-slate-800/40 text-slate-300"
                              : isNew
                                ? "bg-emerald-900/25 text-emerald-100 hover:bg-emerald-900/35"
                                : "hover:bg-slate-800/40 text-slate-300"
                        }`}
                      >
                        <td className="py-2 px-4">
                          <div className="flex items-center gap-2.5">
                            {isTouchDevice && (
                              // Touch has no Ctrl/Shift: a checkbox per row builds a multi-selection.
                              <input
                                type="checkbox"
                                checked={isSelected}
                                aria-label={item.name}
                                onClick={(e) => e.stopPropagation()}
                                onPointerDown={(e) => e.stopPropagation()}
                                onChange={() => setSelectedItems((prev) =>
                                  prev.some((i) => i.name === item.name) ? prev.filter((i) => i.name !== item.name) : [...prev, item])}
                                className="h-4 w-4 shrink-0 accent-[#e04444]"
                              />
                            )}
                            <FileIcon name={item.name} isFolder={item.isFolder} />
                            {renaming?.from === item.name ? (
                              <input
                                autoFocus
                                value={renaming.value}
                                onChange={(e) => setRenaming({ from: item.name, value: e.target.value })}
                                onFocus={(e) => {
                                  // Select the name without its extension, like desktop file managers.
                                  const dot = item.isFolder ? -1 : e.target.value.lastIndexOf(".");
                                  e.target.setSelectionRange(0, dot > 0 ? dot : e.target.value.length);
                                }}
                                onKeyDown={(e) => {
                                  e.stopPropagation();
                                  if (e.key === "Enter") { e.preventDefault(); void commitRename(); }
                                  if (e.key === "Escape") { e.preventDefault(); renameActiveRef.current = null; setRenaming(null); }
                                }}
                                onBlur={() => void commitRename()}
                                onClick={(e) => e.stopPropagation()}
                                onDoubleClick={(e) => e.stopPropagation()}
                                className="w-full min-w-0 rounded border border-[#e04444] bg-slate-950 px-1.5 py-0.5 font-mono text-xs text-slate-100 focus:outline-none"
                              />
                            ) : (
                              <span className="truncate">{item.name}</span>
                            )}
                            {busyNames.has(item.name) && <Loader2 size={12} className="animate-spin text-slate-400 shrink-0" />}
                          </div>
                        </td>
                        <td className="py-2 px-4 text-slate-400">{item.size}</td>
                        <td className="py-2 px-4 text-slate-400 text-[11px]" title={item.modified}>
                          {formatRelativeTime(item.modifiedMs, i18n.resolvedLanguage ?? "en")}
                        </td>
                      </tr>
                    );
                  })
                )}
              </tbody>
            </table>
          </div>
        </Panel>
        </div>

        <FileDetailsPanel
          selectedItems={selectedItems}
          currentPath={currentPath}
          serverId={selectedServer.id}
          rootId={selectedRootId}
          previewUrl={previewUrl}
          onPreview={(item) => setPreviewName(item.name)}
        />
      </div>

      {/* Modal Dialogs */}
      <input
        ref={folderInputRef}
        type="file"
        multiple
        className="hidden"
        // Non-standard but supported by every current browser: pick a whole folder.
        {...{ webkitdirectory: "", directory: "" }}
        onChange={(e) => {
          if (e.target.files) queueUploads(filesFromFolderInput(e.target.files));
          e.target.value = "";
        }}
      />
      <input
        ref={fileInputRef}
        type="file"
        multiple
        className="hidden"
        onChange={(e) => {
          queueUploads(Array.from(e.target.files ?? []).map((file) => ({ file, relDir: "" })));
          e.target.value = "";
        }}
      />

      {/* 2. mkdir Modal */}
      <Modal
        isOpen={activeModal === "mkdir"}
        onClose={closeModal}
        title={t("fileManager.newFolder")}
        icon={<FolderPlus size={20} className="text-[#ffd8a0] filter drop-shadow-[0_0_4px_rgba(255,216,160,0.4)]" />}
        onConfirm={handleModalConfirm}
        confirmDisabled={!modalInput.trim() || loading}
        confirmLabel={loading ? "Creating..." : t("fileManager.newFolder")}
      >
        <div className="space-y-3">
          <label className="block text-xs font-semibold tracking-wider text-slate-300 uppercase">
            Folder name
          </label>
          <input
            type="text"
            value={modalInput}
            onChange={(e) => setModalInput(e.target.value)}
            placeholder="New Directory"
            className="w-full bg-slate-950/60 border border-red-950/45 rounded px-3 py-2 text-xs font-mono text-slate-200 focus:outline-none focus:border-[#e04444]"
          />
        </div>
      </Modal>

      {/* 3. Rename Modal */}
      <Modal
        isOpen={activeModal === "rename"}
        onClose={closeModal}
        title={t("fileManager.modals.renameTitle")}
        icon={<Edit3 size={20} className="text-[#e04444]" />}
        onConfirm={handleModalConfirm}
        confirmDisabled={!modalInput.trim() || modalInput === selectedItem?.name || loading}
        confirmLabel={loading ? "Renaming..." : t("fileManager.rename")}
      >
        <div className="space-y-3">
          <label className="block text-xs font-semibold tracking-wider text-slate-300 uppercase">
            {t("fileManager.modals.newName")}
          </label>
          <input
            type="text"
            value={modalInput}
            onChange={(e) => setModalInput(e.target.value)}
            className="w-full bg-slate-950/60 border border-red-950/45 rounded px-3 py-2 text-xs font-mono text-slate-200 focus:outline-none focus:border-[#e04444]"
          />
        </div>
      </Modal>

      {/* 4. Move Modal */}
      <Modal
        isOpen={activeModal === "move"}
        onClose={closeModal}
        title={selectedItems.length > 1 ? t("fileManager.modals.moveMultipleTitle", { count: selectedItems.length }) : t("fileManager.modals.moveTitle")}
        icon={<Move size={20} className="text-[#e04444]" />}
        onConfirm={handleModalConfirm}
        confirmDisabled={loading}
        confirmLabel={loading ? "Moving..." : t("fileManager.move")}
      >
        <div className="space-y-3">
          <label className="block text-xs font-semibold tracking-wider text-slate-300 uppercase">
            {t("fileManager.modals.destinationPath")}
          </label>
          {activeModal === "move" && selectedRootId && (
            <FolderPicker
              serverId={selectedServer.id}
              rootId={selectedRootId}
              value={modalInput}
              onChange={setModalInput}
              excluded={selectedItems.filter((i) => i.isFolder).map((i) => (currentPath ? `${currentPath}/${i.name}` : i.name))}
            />
          )}
        </div>
      </Modal>

      {/* 5. Pack Modal */}
      <Modal
        isOpen={activeModal === "pack"}
        onClose={closeModal}
        title={t("fileManager.modals.packTitle")}
        icon={<Package size={20} className="text-[#e04444]" />}
        onConfirm={handleModalConfirm}
        confirmDisabled={!modalInput.trim() || loading}
        confirmLabel={loading ? "Compressing..." : t("fileManager.pack")}
      >
        <div className="space-y-3">
          <label className="block text-xs font-semibold tracking-wider text-slate-300 uppercase">
            {t("fileManager.modals.archiveName")}
          </label>
          <input
            type="text"
            value={modalInput}
            onChange={(e) => setModalInput(e.target.value)}
            className="w-full bg-slate-950/60 border border-red-950/45 rounded px-3 py-2 text-xs font-mono text-slate-200 focus:outline-none focus:border-[#e04444]"
          />
        </div>
      </Modal>

      {/* 6. Unpack Modal */}
      <Modal
        isOpen={activeModal === "unpack"}
        onClose={closeModal}
        title={t("fileManager.modals.unpackTitle")}
        icon={<Archive size={20} className="text-[#e04444]" />}
        onConfirm={handleModalConfirm}
        confirmDisabled={loading}
        confirmLabel={loading ? "Extracting..." : t("fileManager.unpack")}
      >
        <div className="space-y-3">
          <label className="block text-xs font-semibold tracking-wider text-slate-300 uppercase">
            {t("fileManager.modals.extractTo")}
          </label>
          {activeModal === "unpack" && selectedRootId && (
            <FolderPicker serverId={selectedServer.id} rootId={selectedRootId} value={modalInput} onChange={setModalInput} />
          )}
        </div>
      </Modal>

      {/* 7. Delete Modal */}
      <Modal
        isOpen={activeModal === "delete"}
        onClose={closeModal}
        title={t("fileManager.modals.deleteTitle")}
        icon={<Trash2 size={20} className="text-rose-400" />}
        onConfirm={handleModalConfirm}
        confirmDisabled={loading}
        confirmVariant="danger"
        confirmLabel={loading ? "Deleting..." : deletePermanently ? t("fileManager.trash.deleteForever") : t("fileManager.trash.moveToTrash")}
      >
        <p className="leading-relaxed text-slate-300">
          {selectedItems.length > 1 ? (
            <span>
              {t("fileManager.modals.deleteConfirmMultiple", { count: selectedItems.length })}
            </span>
          ) : (
            <span>
              {t("fileManager.modals.deleteConfirm")}{" "}
              <strong className="text-slate-100 font-mono">{selectedItem?.name}</strong>?
            </span>
          )}
        </p>
        <p className="mt-2 text-xs text-slate-400">
          {deletePermanently
            ? t("fileManager.trash.permanentNote")
            : t("fileManager.trash.trashNote", { days: trashRetentionDays })}
        </p>
        <label className="mt-3 flex items-center gap-2 text-xs text-slate-300 cursor-pointer select-none">
          <input
            type="checkbox"
            checked={deletePermanently}
            onChange={(e) => setDeletePermanently(e.target.checked)}
            className="accent-[#e04444]"
          />
          {t("fileManager.trash.permanentOption")}
        </label>
      </Modal>

      <Modal
        isOpen={uploadConflict !== null}
        onClose={() => setUploadConflict(null)}
        title={t("fileManager.uploads.conflictTitle")}
        icon={<Upload size={20} className="text-[#e04444]" />}
        onConfirm={() => resolveUploadConflict("replace")}
        confirmLabel={t("fileManager.uploads.replace")}
        confirmVariant="danger"
      >
        <div className="space-y-3 text-xs text-slate-300">
          <p>{t("fileManager.uploads.conflictText", { count: uploadConflict?.conflicts.length ?? 0 })}</p>
          <p className="text-slate-400">{t("fileManager.uploads.conflictHint")}</p>
          <ul className="max-h-40 overflow-y-auto rounded border border-slate-700/50 bg-slate-950/40 px-3 py-2 font-mono text-slate-200">
            {uploadConflict?.conflicts.map((name) => <li key={name} className="truncate">{name}</li>)}
          </ul>
          <div className="flex flex-wrap gap-2">
            <button
              type="button"
              onClick={() => resolveUploadConflict("keepBoth")}
              className="rounded-md border border-red-950/45 bg-slate-950/30 px-3 py-1.5 font-semibold text-slate-200 hover:bg-red-950/15 cursor-pointer"
            >
              {t("fileManager.uploads.keepBoth")}
            </button>
            <button
              type="button"
              onClick={() => resolveUploadConflict("skip")}
              className="rounded-md border border-red-950/45 bg-slate-950/30 px-3 py-1.5 font-semibold text-slate-200 hover:bg-red-950/15 cursor-pointer"
            >
              {t("fileManager.uploads.skip")}
            </button>
          </div>
        </div>
      </Modal>

      {previewName && (() => {
        const images = visibleItems.filter((i) => !i.isFolder && isPreviewableImage(i.name));
        const index = images.findIndex((i) => i.name === previewName);
        return index === -1 ? null : (
          <ImagePreview
            images={images}
            index={index}
            urlFor={previewUrl}
            onIndexChange={(next) => {
              setPreviewName(images[next].name);
              setSelectedItems([images[next]]);
            }}
            onDownload={(item) => handleDownload([item])}
            onClose={() => setPreviewName(null)}
          />
        );
      })()}

      <LiveServerWarning request={liveWarning} onDone={() => setLiveWarning(null)} />

      {selectedRootId && !isReadOnly && (
        <TrashDialog
          isOpen={trashOpen}
          onClose={() => setTrashOpen(false)}
          serverId={selectedServer.id}
          rootId={selectedRootId}
          retentionDays={trashRetentionDays}
          onRestore={restoreFromTrash}
          onChanged={fetchDirectoryListing}
        />
      )}

      {contextMenu && (
        <ContextMenu
          x={contextMenu.x}
          y={contextMenu.y}
          onClose={() => setContextMenu(null)}
          entries={buildContextMenu(contextMenu.item)}
        />
      )}

      {showShortcuts && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4" onClick={() => setShowShortcuts(false)}>
          <div className="fixed inset-0 bg-black/70 backdrop-blur-sm" />
          <div
            role="dialog"
            aria-label={t("fileManager.shortcuts.title")}
            className="relative z-10 w-full max-w-md rounded-xl glass-panel p-5 shadow-2xl"
            onClick={(e) => e.stopPropagation()}
          >
            <div className="flex items-center gap-2 mb-4">
              <Keyboard size={18} className="text-[#e04444]" />
              <h2 className="text-sm font-bold text-slate-100">{t("fileManager.shortcuts.title")}</h2>
            </div>
            <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-2 text-xs">
              {SHORTCUTS.map(([keys, label]) => (
                <div key={label} className="contents">
                  <dt className="font-mono text-slate-200 whitespace-nowrap">
                    {keys.map((key) => (
                      <kbd key={key} className="mr-1 rounded border border-slate-600/70 bg-slate-800/70 px-1.5 py-0.5 text-[11px]">{key}</kbd>
                    ))}
                  </dt>
                  <dd className="text-slate-400">{t(`fileManager.shortcuts.${label}`)}</dd>
                </div>
              ))}
            </dl>
          </div>
        </div>
      )}

      {/* Code Editor Modal */}
      {editingFile && (
        <CodeEditorModal
          isOpen={isEditorOpen}
          onClose={() => {
            setIsEditorOpen(false);
            setEditingFile(null);
          }}
          filename={editingFile.name}
          filePath={currentPath ? `${currentPath}/${editingFile.name}` : editingFile.name}
          initialContent={editorContent}
          modified={editorModified}
          serverId={selectedServer.id}
          rootId={selectedRootId}
          onSave={async (updatedContent) => {
            if (!(await confirmLive([joinPath(currentPath, editingFile.name)]))) {
              throw new Error(t("fileManager.live.cancelled"));
            }
            await filesApi.saveContent(selectedServer.id, selectedRootId, currentPath ? `${currentPath}/${editingFile.name}` : editingFile.name, updatedContent, editorModified)
              .then((res) => {
                setEditorContent(updatedContent);
                setEditorModified(res.modified);
                toast.success(t("fileManager.toasts.saved", { name: editingFile.name }));
                fetchDirectoryListing();
              });
          }}
        />
      )}
    </div>
  );
}

function errorText(err: unknown): string {
  return err instanceof Error ? err.message : String(err);
}

const SHORTCUTS: [string[], string][] = [
  [["↑", "↓"], "navigate"],
  [["Shift", "↑/↓"], "extend"],
  [["Enter"], "openItem"],
  [["Backspace"], "up"],
  [["F2"], "rename"],
  [["Del"], "delete"],
  [["Ctrl", "A"], "selectAll"],
  [["Ctrl", "U"], "upload"],
  [["Ctrl", "Shift", "N"], "newFolder"],
  [["/"], "search"],
  [["F5"], "refresh"],
  [["Esc"], "clear"],
  [["a–z"], "typeAhead"],
  [["?"], "help"]
];

const SORT_STORAGE_KEY = "fileManager.sort";

function readStoredSort(): { field: FileSortField; direction: SortDirection } {
  try {
    const stored = JSON.parse(localStorage.getItem(SORT_STORAGE_KEY) ?? "null") as { field?: string; direction?: string } | null;
    if (stored && ["name", "size", "modified"].includes(stored.field ?? "") && ["asc", "desc"].includes(stored.direction ?? "")) {
      return { field: stored.field as FileSortField, direction: stored.direction as SortDirection };
    }
  } catch {
    // Unavailable or corrupt storage: use the default.
  }
  return { field: "name", direction: "asc" };
}
