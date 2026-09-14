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
  AlertCircle
} from "lucide-react";
import Panel from "@/components/ui/Panel";
import PageHeader from "@/components/layout/PageHeader";
import Modal from "@/components/ui/Modal";
import CodeEditorModal from "@/components/editor/CodeEditorModal";
import BackgroundTaskBar from "@/components/file-manager/BackgroundTaskBar";
import FileManagerEmptyState from "@/components/file-manager/FileManagerEmptyState";
import FileDetailsPanel from "@/components/file-manager/FileDetailsPanel";
import FileManagerActions from "@/components/file-manager/FileManagerActions";
import { useTranslation } from "react-i18next";
import { useServer } from "@/context/ServerContext";
import { filesApi, type FileRootDto } from "@/api/files";
import type { ActiveModal, FileItem, FileSortField, SortDirection } from "@/components/file-manager/types";
import { FILE_MANAGER_UP_DROP_TARGET } from "@/lib/constants";
import { mapFileEntry, sortFileItems } from "@/lib/fileManager";

export default function FileManagerPage() {
  const { t } = useTranslation();
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

  // Background Task tracking
  const [activeTaskId, setActiveTaskId] = useState<string | null>(null);
  const [activeTaskStatus, setActiveTaskStatus] = useState<string | null>(null);
  const [activeTaskDesc, setActiveTaskDesc] = useState<string | null>(null);

  // Modal State
  const [activeModal, setActiveModal] = useState<ActiveModal>(null);
  const [modalInput, setModalInput] = useState("");
  const [selectedFileToUpload, setSelectedFileToUpload] = useState<File | null>(null);

  // Code Editor State
  const [isEditorOpen, setIsEditorOpen] = useState(false);
  const [editingFile, setEditingFile] = useState<FileItem | null>(null);
  const [editorContent, setEditorContent] = useState("");

  // Sorting State
  const [sortField, setSortField] = useState<FileSortField>("name");
  const [sortDirection, setSortDirection] = useState<SortDirection>("asc");

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
    const rootToLoad = selectedRootId || "data";
    setLoading(true);
    setError(null);
    filesApi.list(selectedServer.id, rootToLoad, currentPath)
      .then((res) => {
        setRoots(res.roots);
        if (!selectedRootId) {
          setSelectedRootId(rootToLoad);
        }

        setItems(res.entries.map(mapFileEntry));
      })
      .catch((err) => {
        setError(err instanceof Error ? err.message : String(err));
      })
      .finally(() => {
        setLoading(false);
      });
  }, [selectedServer?.id, selectedRootId, currentPath]);

  // Load directories on init/change
  useEffect(() => {
    fetchDirectoryListing();
  }, [fetchDirectoryListing]);

  // Reset states when server changes
  useEffect(() => {
    const params = new URLSearchParams(window.location.search);
    const hasDeepLink = params.has("root") || params.has("path");
    if (hasDeepLink) return;

    setSelectedRootId("");
    setCurrentPath("");
    setSelectedItems([]);
    setItems([]);
    setRoots([]);
  }, [selectedServer?.id]);

  // Deep-link navigation via ?root= and ?path= (e.g. quick actions). Runs after
  // the reset effect so it can override the freshly reset state, then clears params.
  useEffect(() => {
    if (!selectedServer) return;
    const targetRoot = searchParams.get("root");
    const targetPath = searchParams.get("path");
    if (targetRoot === null && targetPath === null) return;
    if (targetRoot) setSelectedRootId(targetRoot);
    if (targetPath !== null) {
      setCurrentPath(targetPath);
      setTempPath(targetPath);
      setSelectedItems([]);
    }
    setSearchParams({}, { replace: true });
  }, [selectedServer?.id, searchParams, setSearchParams]);

  // Task Status Poller
  useEffect(() => {
    if (!activeTaskId) return;
    const timer = setInterval(() => {
      filesApi.getOperationStatus(activeTaskId)
        .then((res) => {
          setActiveTaskStatus(res.status);
          setActiveTaskDesc(res.description);
          if (res.status === "Completed") {
            clearInterval(timer);
            setActiveTaskId(null);
            fetchDirectoryListing();
          } else if (res.status === "Failed") {
            clearInterval(timer);
            alert(`Background task failed: ${res.errorMessage || "Unknown error"}`);
            setActiveTaskId(null);
            fetchDirectoryListing();
          }
        })
        .catch(() => {
          clearInterval(timer);
          setActiveTaskId(null);
        });
    }, 1500);
    return () => clearInterval(timer);
  }, [activeTaskId, fetchDirectoryListing]);

  const handleSort = (field: FileSortField) => {
    if (sortField === field) {
      setSortDirection((prev) => (prev === "asc" ? "desc" : "asc"));
    } else {
      setSortField(field);
      setSortDirection("asc");
    }
  };

  const sortedItems = sortFileItems(items, sortField, sortDirection);

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

    if (!item.isFolder) return;

    clearLongPressTimer();
    longPressTriggeredRef.current = false;
    longPressTimerRef.current = window.setTimeout(() => {
      longPressTriggeredRef.current = true;
      setSelectedItems((prev) => {
        const exists = prev.some((i) => i.name === item.name);
        return exists ? prev.filter((i) => i.name !== item.name) : [...prev, item];
      });
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
    if (lastPointerTypeRef.current === "touch" && item.isFolder) {
      if (longPressTriggeredRef.current) {
        longPressTriggeredRef.current = false;
        return;
      }
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
      const lastIndex = sortedItems.findIndex((i) => i.name === lastSelected.name);
      const currentIndex = sortedItems.findIndex((i) => i.name === item.name);
      if (lastIndex !== -1 && currentIndex !== -1) {
        const start = Math.min(lastIndex, currentIndex);
        const end = Math.max(lastIndex, currentIndex);
        const rangeItems = sortedItems.slice(start, end + 1);
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

  const handleItemDoubleClick = (item: FileItem) => {
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
        setIsEditorOpen(true);
      })
      .catch((err) => {
        alert("Error loading content: " + (err instanceof Error ? err.message : String(err)));
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
    setSelectedFileToUpload(null);
    setActiveModal("upload");
  };

  const openMkdirModal = () => {
    setModalInput("");
    setActiveModal("mkdir");
  };

  const openRenameModal = () => {
    if (!selectedItem) return;
    setModalInput(selectedItem.name);
    setActiveModal("rename");
  };

  const openMoveModal = () => {
    if (selectedItems.length === 0) return;
    setModalInput(currentPath);
    setActiveModal("move");
  };

  const openPackModal = () => {
    if (selectedItems.length === 0) return;
    const defaultName = selectedItems.length === 1 
      ? (selectedItems[0].name.endsWith(".zip") ? selectedItems[0].name : `${selectedItems[0].name.split(".")[0]}.zip`)
      : "archive.zip";
    setModalInput(defaultName);
    setActiveModal("pack");
  };

  const openUnpackModal = () => {
    if (selectedItems.length === 0) return;
    setModalInput(currentPath);
    setActiveModal("unpack");
  };

  const openDeleteModal = () => {
    if (selectedItems.length === 0) return;
    setActiveModal("delete");
  };

  const closeModal = () => {
    setActiveModal(null);
    setModalInput("");
    setSelectedFileToUpload(null);
  };

  // Download Trigger
  const handleDownload = () => {
    if (!selectedServer || !selectedRootId || !selectedItem) return;
    const relativeFilePath = currentPath ? `${currentPath}/${selectedItem.name}` : selectedItem.name;
    const url = filesApi.getDownloadUrl(selectedServer.id, selectedRootId, relativeFilePath);

    // Trigger download in browser
    const link = document.createElement("a");
    link.href = url;
    link.download = selectedItem.name;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
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

  const handleFilesDrop = (files: File[]) => {
    if (!selectedServer || files.length === 0) return;
    setDragActive(false);
    setLoading(true);
    setError(null);
    Promise.allSettled(
      files.map((file) => filesApi.upload(selectedServer.id, selectedRootId || "data", currentPath, file))
    )
      .then((results) => {
        const failed = results.filter((r) => r.status === "rejected");
        if (failed.length > 0) {
          const reason = (failed[0] as PromiseRejectedResult).reason;
          setError(
            `Failed to upload ${failed.length} file(s): ${reason instanceof Error ? reason.message : String(reason)}`
          );
        }
        fetchDirectoryListing();
      })
      .catch((err) => {
        setError(err instanceof Error ? err.message : String(err));
        setLoading(false);
        fetchDirectoryListing();
      });
  };

  const handleDrop = (e: React.DragEvent) => {
    e.preventDefault();
    dragDepthRef.current = 0;
    if (dragSource) {
      setDragActive(false);
      return;
    }
    handleFilesDrop(Array.from(e.dataTransfer.files));
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
    if (!selectedServer || !dragSource) return;

    const sourcePath = currentPath ? `${currentPath}/${dragSource.name}` : dragSource.name;
    const targetFolderPath = currentPath ? `${currentPath}/${targetFolder.name}` : targetFolder.name;

    // Prevent moving an item into itself or into one of its own subdirectories.
    if (targetFolderPath === sourcePath || targetFolderPath.startsWith(sourcePath + "/")) {
      setDragSource(null);
      setDropTargetName(null);
      setError(t("fileManager.cannotMoveIntoItself"));
      return;
    }

    const destPath = `${targetFolderPath}/${dragSource.name}`;
    setDragSource(null);
    setDropTargetName(null);
    setLoading(true);
    setError(null);
    filesApi.move(selectedServer.id, selectedRootId || "data", sourcePath, destPath)
      .then(() => {
        fetchDirectoryListing();
        setSelectedItems([]);
      })
      .catch((err) => {
        setError("Error moving: " + (err instanceof Error ? err.message : String(err)));
        setLoading(false);
      });
  };

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
    if (!selectedServer || !dragSource || !currentPath) return;

    const parts = currentPath.split("/").filter(Boolean);
    parts.pop();
    const parentPath = parts.join("/");
    const sourcePath = currentPath ? `${currentPath}/${dragSource.name}` : dragSource.name;
    const destPath = parentPath ? `${parentPath}/${dragSource.name}` : dragSource.name;

    setDragSource(null);
    setDropTargetName(null);
    setLoading(true);
    setError(null);
    filesApi.move(selectedServer.id, selectedRootId || "data", sourcePath, destPath)
      .then(() => {
        fetchDirectoryListing();
        setSelectedItems([]);
      })
      .catch((err) => {
        setError("Error moving: " + (err instanceof Error ? err.message : String(err)));
        setLoading(false);
      });
  };

  // Modal Submit Handlers
  const handleModalConfirm = () => {
    if (!selectedServer || !selectedRootId) return;

    if (activeModal === "mkdir" && modalInput.trim()) {
      const relativeDirPath = currentPath ? `${currentPath}/${modalInput.trim()}` : modalInput.trim();
      setLoading(true);
      filesApi.createDirectory(selectedServer.id, selectedRootId, relativeDirPath)
        .then(() => {
          closeModal();
          fetchDirectoryListing();
        })
        .catch((err) => {
          alert("Error creating folder: " + (err instanceof Error ? err.message : String(err)));
          setLoading(false);
        });
    } else if (activeModal === "rename" && selectedItem && modalInput.trim()) {
      const relativeFilePath = currentPath ? `${currentPath}/${selectedItem.name}` : selectedItem.name;
      setLoading(true);
      filesApi.rename(selectedServer.id, selectedRootId, relativeFilePath, modalInput.trim())
        .then(() => {
          closeModal();
          fetchDirectoryListing();
          setSelectedItems([]);
        })
        .catch((err) => {
          alert("Error renaming: " + (err instanceof Error ? err.message : String(err)));
          setLoading(false);
        });
    } else if (activeModal === "move" && selectedItems.length > 0 && modalInput.trim()) {
      const destFolder = modalInput.trim();
      setLoading(true);
      setError(null);

      let failedCount = 0;
      let firstError: string | null = null;

      (async () => {
        for (const item of selectedItems) {
          const source = currentPath ? `${currentPath}/${item.name}` : item.name;
          const dest = destFolder ? `${destFolder}/${item.name}` : item.name;
          try {
            await filesApi.move(selectedServer.id, selectedRootId, source, dest);
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
          setError(`Failed to move ${failedCount} item(s): ${firstError}`);
        }
        closeModal();
        fetchDirectoryListing();
        setSelectedItems([]);
      });
    } else if (activeModal === "pack" && selectedItems.length > 0 && modalInput.trim()) {
      const source = selectedItems.map(item => currentPath ? `${currentPath}/${item.name}` : item.name).join(";");
      const dest = currentPath ? `${currentPath}/${modalInput.trim()}` : modalInput.trim();
      setLoading(true);
      filesApi.compress(selectedServer.id, selectedRootId, source, dest)
        .then((res) => {
          closeModal();
          setLoading(false);
          setActiveTaskId(res.taskId);
          setActiveTaskStatus("Running");
          setActiveTaskDesc(`Compressing ${selectedItems.length} items to '${dest}'`);
        })
        .catch((err) => {
          alert("Error starting archiving: " + (err instanceof Error ? err.message : String(err)));
          setLoading(false);
        });
    } else if (activeModal === "unpack" && selectedItems.length > 0 && modalInput.trim()) {
      const dest = modalInput.trim();
      setLoading(true);
      setError(null);

      let lastTaskId: string | null = null;
      let failedCount = 0;
      let firstError: string | null = null;

      (async () => {
        for (const item of selectedItems) {
          const source = currentPath ? `${currentPath}/${item.name}` : item.name;
          try {
            const res = await filesApi.extract(selectedServer.id, selectedRootId, source, dest);
            lastTaskId = res.taskId;
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
          setError(`Failed to extract ${failedCount} item(s): ${firstError}`);
        }
        closeModal();
        setLoading(false);
        if (lastTaskId) {
          setActiveTaskId(lastTaskId);
          setActiveTaskStatus("Running");
          setActiveTaskDesc(`Extracting archives to '${dest}'`);
        } else {
          fetchDirectoryListing();
        }
      });
    } else if (activeModal === "delete" && selectedItems.length > 0) {
      setLoading(true);
      setError(null);

      let failedCount = 0;
      let firstError: string | null = null;

      (async () => {
        for (const item of selectedItems) {
          const relativeFilePath = currentPath ? `${currentPath}/${item.name}` : item.name;
          try {
            await filesApi.delete(selectedServer.id, selectedRootId, relativeFilePath);
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
          setError(`Failed to delete ${failedCount} item(s): ${firstError}`);
        }
        closeModal();
        fetchDirectoryListing();
        setSelectedItems([]);
      });
    } else if (activeModal === "upload" && selectedFileToUpload) {
      setLoading(true);
      filesApi.upload(selectedServer.id, selectedRootId, currentPath, selectedFileToUpload)
        .then(() => {
          closeModal();
          fetchDirectoryListing();
        })
        .catch((err) => {
          alert("Error uploading: " + (err instanceof Error ? err.message : String(err)));
          setLoading(false);
        });
    }
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
      <BackgroundTaskBar
        description={activeTaskId ? activeTaskDesc : null}
        status={activeTaskStatus}
        onHide={() => setActiveTaskId(null)}
      />

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
                <span className="text-slate-500">/</span>
                {pathParts.map((part, idx) => (
                  <div key={idx} className="flex items-center gap-1.5 shrink-0">
                    <span
                      onClick={(e) => {
                        e.stopPropagation();
                        const subPath = pathParts.slice(0, idx + 1).join("/");
                        setCurrentPath(subPath);
                        setTempPath(subPath);
                        setSelectedItems([]);
                      }}
                      className="hover:text-[#e04444] hover:underline cursor-pointer"
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
        </div>

        {/* Divider */}
        <div className="h-px w-full bg-red-950/20" />

        <FileManagerActions
          selectedItems={selectedItems}
          onUpload={openUploadModal}
          onMkdir={openMkdirModal}
          onDownload={handleDownload}
          onEdit={() => selectedItem && openEditorForItem(selectedItem)}
          onRename={openRenameModal}
          onMove={openMoveModal}
          onUnpack={openUnpackModal}
          onPack={openPackModal}
          onDelete={openDeleteModal}
        />
      </div>

      {/* Main Grid: File Table + Details Sidebar */}
      <div className="grid grid-cols-1 lg:grid-cols-4 gap-4 items-start">
        {/* Files Table */}
        <div
          className="lg:col-span-3"
          onDragEnter={handleDragEnter}
          onDragOver={handleDragOver}
          onDragLeave={handleDragLeave}
          onDrop={handleDrop}
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
              <div>{error}</div>
            </div>
          )}

          <div className="overflow-x-auto">
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
                  sortedItems.map((item) => {
                    const isSelected = selectedItems.some((i) => i.name === item.name);
                    return (
                      <tr
                        key={item.name}
                        draggable={!isTouchDevice}
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
                        onDoubleClick={() => handleItemDoubleClick(item)}
                        className={`cursor-pointer transition-colors select-none touch-no-callout ${
                          isSelected
                            ? "bg-[#e04444]/15 text-white font-semibold border-l-2 border-l-[#e04444]"
                            : dropTargetName === item.name
                              ? "bg-red-950/40 text-white ring-2 ring-inset ring-[#e04444]"
                              : "hover:bg-slate-800/40 text-slate-300"
                        }`}
                      >
                        <td className="py-2 px-4">
                          <div className="flex items-center gap-2.5">
                            {item.isFolder ? (
                              <Folder size={16} className="text-[#ffd8a0] shrink-0" fill="currentColor" fillOpacity={0.2} />
                            ) : item.name.endsWith(".json") || item.name.endsWith(".yaml") || item.name.endsWith(".yml") ? (
                              <FileCode size={16} className="text-amber-400 shrink-0" />
                            ) : (
                              <FileText size={16} className="text-slate-400 shrink-0" />
                            )}
                            <span className="truncate">{item.name}</span>
                          </div>
                        </td>
                        <td className="py-2 px-4 text-slate-400">{item.size}</td>
                        <td className="py-2 px-4 text-slate-400 text-[11px]">{item.modified}</td>
                      </tr>
                    );
                  })
                )}
              </tbody>
            </table>
          </div>
        </Panel>
        </div>

        <FileDetailsPanel selectedItems={selectedItems} currentPath={currentPath} />
      </div>

      {/* Modal Dialogs */}
      {/* 1. Upload Modal */}
      <Modal
        isOpen={activeModal === "upload"}
        onClose={closeModal}
        title={t("fileManager.modals.uploadTitle")}
        icon={<Upload size={20} />}
        onConfirm={handleModalConfirm}
        confirmDisabled={!selectedFileToUpload || loading}
        confirmLabel={loading ? "Uploading..." : t("fileManager.upload")}
      >
        <div className="space-y-3">
          <label className="block text-xs font-medium text-slate-300">
            {t("fileManager.modals.uploadSelect")}
          </label>
          <input
            type="file"
            onChange={(e) => setSelectedFileToUpload(e.target.files?.[0] || null)}
            className="w-full text-xs text-slate-300 file:mr-3 file:py-1.5 file:px-3 file:rounded-md file:border-0 file:text-[11px] file:font-bold file:uppercase file:bg-gradient-to-r file:from-[#981d22] file:to-[#b8282e] file:hover:from-[#b8282e] file:hover:to-[#e04444] file:text-white cursor-pointer bg-slate-950/60 p-2 rounded border border-red-950/45"
          />
          {selectedFileToUpload && (
            <div className="text-[11px] text-slate-400 font-mono">
              Selected: {selectedFileToUpload.name} ({(selectedFileToUpload.size / 1024).toFixed(1)} KB)
            </div>
          )}
        </div>
      </Modal>

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
          <input
            type="text"
            value={modalInput}
            onChange={(e) => setModalInput(e.target.value)}
            placeholder="Root relative path (e.g. Backups)"
            className="w-full bg-slate-950/60 border border-red-950/45 rounded px-3 py-2 text-xs font-mono text-slate-200 focus:outline-none focus:border-[#e04444]"
          />
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
        confirmDisabled={!modalInput.trim() || loading}
        confirmLabel={loading ? "Extracting..." : t("fileManager.unpack")}
      >
        <div className="space-y-3">
          <label className="block text-xs font-semibold tracking-wider text-slate-300 uppercase">
            {t("fileManager.modals.extractTo")}
          </label>
          <input
            type="text"
            value={modalInput}
            onChange={(e) => setModalInput(e.target.value)}
            placeholder="Destination directory relative path"
            className="w-full bg-slate-950/60 border border-red-950/45 rounded px-3 py-2 text-xs font-mono text-slate-200 focus:outline-none focus:border-[#e04444]"
          />
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
        confirmLabel={loading ? "Deleting..." : t("fileManager.delete")}
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
      </Modal>

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
          serverId={selectedServer.id}
          rootId={selectedRootId}
          onSave={(updatedContent) =>
            filesApi.saveContent(selectedServer.id, selectedRootId, currentPath ? `${currentPath}/${editingFile.name}` : editingFile.name, updatedContent)
              .then(() => {
                setEditorContent(updatedContent);
                fetchDirectoryListing();
              })
              .catch((err) => {
                alert("Error saving file: " + (err instanceof Error ? err.message : String(err)));
              })
          }
        />
      )}
    </div>
  );
}
