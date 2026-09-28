import { ChevronDown, ChevronLeft, ChevronRight, ChevronsUpDown, ChevronUp, Search } from "lucide-react";
import { useEffect, useMemo, useState, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { cn } from "@/lib/cn";

export type SortValue = number | string | null | undefined;
export type SortDirection = "asc" | "desc";

export interface DataTableColumn<T> {
  id: string;
  header: ReactNode;
  cell: (row: T) => ReactNode;
  /** Value used for sorting; omit to make the column unsortable. Nulls always sort last. */
  sortValue?: (row: T) => SortValue;
  /** Direction used the first time the column is clicked. Numbers usually start descending. */
  firstDirection?: SortDirection;
  align?: "left" | "right";
  title?: string;
  className?: string;
}

interface DataTableProps<T> {
  rows: T[];
  columns: DataTableColumn<T>[];
  rowKey: (row: T) => string;
  /** Text searched by the filter box; omit to hide the box. */
  searchText?: (row: T) => string;
  searchPlaceholder?: string;
  defaultSort?: { id: string; direction: SortDirection };
  pageSize?: number;
  emptyText?: ReactNode;
  onRowClick?: (row: T) => void;
  rowClassName?: (row: T) => string | false | undefined;
  /** Mobile card layout. When given, cards replace the table below the md breakpoint. */
  renderCard?: (row: T) => ReactNode;
  /** Extra content shown on the left of the toolbar, e.g. a title. */
  toolbar?: ReactNode;
  className?: string;
}

const PAGE_SIZES = [10, 25, 50, 100];

function compare(a: SortValue, b: SortValue) {
  const aMissing = a === null || a === undefined;
  const bMissing = b === null || b === undefined;
  if (aMissing || bMissing) return aMissing === bMissing ? 0 : aMissing ? 1 : -1;
  if (typeof a === "number" && typeof b === "number") return a - b;
  return String(a).localeCompare(String(b), undefined, { numeric: true, sensitivity: "base" });
}

export default function DataTable<T>({
  rows,
  columns,
  rowKey,
  searchText,
  searchPlaceholder,
  defaultSort,
  pageSize: initialPageSize = 25,
  emptyText,
  onRowClick,
  rowClassName,
  renderCard,
  toolbar,
  className
}: DataTableProps<T>) {
  const { t } = useTranslation();
  const [query, setQuery] = useState("");
  const [sort, setSort] = useState(defaultSort);
  const [page, setPage] = useState(0);
  const [pageSize, setPageSize] = useState(initialPageSize);

  const filtered = useMemo(() => {
    const needle = query.trim().toLocaleLowerCase();
    if (!needle || !searchText) return rows;
    return rows.filter((row) => searchText(row).toLocaleLowerCase().includes(needle));
  }, [rows, query, searchText]);

  const sorted = useMemo(() => {
    const column = sort && columns.find((c) => c.id === sort.id);
    if (!column?.sortValue || !sort) return filtered;
    const value = column.sortValue;
    return [...filtered].sort((a, b) => {
      const va = value(a);
      const vb = value(b);
      // Missing values stay last in both directions.
      if (va === null || va === undefined || vb === null || vb === undefined) return compare(va, vb);
      return sort.direction === "asc" ? compare(va, vb) : compare(vb, va);
    });
  }, [filtered, sort, columns]);

  const pageCount = Math.max(1, Math.ceil(sorted.length / pageSize));
  useEffect(() => { if (page > pageCount - 1) setPage(pageCount - 1); }, [page, pageCount]);
  useEffect(() => setPage(0), [query, sort, pageSize]);
  const visible = sorted.slice(page * pageSize, page * pageSize + pageSize);

  const toggleSort = (column: DataTableColumn<T>) => {
    if (!column.sortValue) return;
    setSort((current) =>
      current?.id === column.id
        ? { id: column.id, direction: current.direction === "asc" ? "desc" : "asc" }
        : { id: column.id, direction: column.firstDirection ?? "desc" }
    );
  };

  const showPager = sorted.length > PAGE_SIZES[0];

  return (
    <div className={cn("space-y-2", className)}>
      {(toolbar || searchText) && (
        <div className="flex flex-wrap items-center justify-between gap-2 px-4 pt-3">
          <div className="min-w-0">{toolbar}</div>
          {searchText && (
            <label className="relative flex items-center">
              <Search size={13} className="absolute left-2.5 text-slate-500 pointer-events-none" />
              <input
                type="search"
                value={query}
                onChange={(event) => setQuery(event.target.value)}
                placeholder={searchPlaceholder ?? t("table.search")}
                className="h-8 w-52 max-w-full rounded-md border border-red-950/45 bg-slate-950/40 pl-8 pr-3 text-xs text-slate-200 outline-none focus:border-[#e04444] focus:ring-2 focus:ring-[#b8282e]/25"
              />
            </label>
          )}
        </div>
      )}

      {sorted.length === 0 ? (
        <p className="px-4 py-4 text-xs text-slate-500">{query ? t("table.noMatches") : emptyText ?? t("table.empty")}</p>
      ) : (
        <>
          <div className={cn("overflow-x-auto", renderCard && "hidden md:block")}>
            <table className="w-full text-xs">
              <thead className="text-[10px] uppercase tracking-wider text-slate-500">
                <tr className="border-b border-red-950/20">
                  {columns.map((column) => {
                    const active = sort?.id === column.id;
                    const Icon = !active ? ChevronsUpDown : sort?.direction === "asc" ? ChevronUp : ChevronDown;
                    return (
                      <th
                        key={column.id}
                        scope="col"
                        title={column.title}
                        aria-sort={active ? (sort?.direction === "asc" ? "ascending" : "descending") : undefined}
                        className={cn("px-4 py-2 font-semibold whitespace-nowrap", column.align === "right" ? "text-right" : "text-left")}
                      >
                        {column.sortValue ? (
                          <button
                            type="button"
                            onClick={() => toggleSort(column)}
                            className={cn(
                              "inline-flex items-center gap-1 uppercase tracking-wider hover:text-slate-200 transition-colors",
                              column.align === "right" && "flex-row-reverse",
                              active && "text-slate-200"
                            )}
                          >
                            {column.header}
                            <Icon size={11} className={active ? "text-[#e04444]" : "opacity-40"} />
                          </button>
                        ) : (
                          column.header
                        )}
                      </th>
                    );
                  })}
                </tr>
              </thead>
              <tbody>
                {visible.map((row) => (
                  <tr
                    key={rowKey(row)}
                    onClick={onRowClick ? () => onRowClick(row) : undefined}
                    className={cn(
                      "border-b border-red-950/10 last:border-0",
                      onRowClick && "cursor-pointer hover:bg-slate-800/30",
                      rowClassName?.(row)
                    )}
                  >
                    {columns.map((column) => (
                      <td
                        key={column.id}
                        className={cn(
                          "px-4 py-2 whitespace-nowrap",
                          column.align === "right" ? "text-right font-mono text-slate-300" : "text-left",
                          column.className
                        )}
                      >
                        {column.cell(row)}
                      </td>
                    ))}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {renderCard && (
            <ul className="md:hidden divide-y divide-red-950/15">
              {visible.map((row) => (
                <li
                  key={rowKey(row)}
                  onClick={onRowClick ? () => onRowClick(row) : undefined}
                  className={cn(onRowClick && "cursor-pointer", rowClassName?.(row))}
                >
                  {renderCard(row)}
                </li>
              ))}
            </ul>
          )}
        </>
      )}

      {showPager && (
        <div className="flex flex-wrap items-center justify-between gap-2 px-4 pb-3 text-[11px] text-slate-400">
          <span>
            {t("table.range", {
              from: sorted.length === 0 ? 0 : page * pageSize + 1,
              to: Math.min(sorted.length, (page + 1) * pageSize),
              total: sorted.length
            })}
            {query && rows.length !== sorted.length && ` · ${t("table.filteredFrom", { total: rows.length })}`}
          </span>
          <div className="flex items-center gap-2">
            <select
              value={pageSize}
              onChange={(event) => setPageSize(Number(event.target.value))}
              aria-label={t("table.rowsPerPage")}
              className="h-7 rounded-md border border-red-950/45 bg-slate-950/40 px-1.5 text-[11px] text-slate-200 outline-none"
            >
              {PAGE_SIZES.map((size) => <option key={size} value={size}>{t("table.perPage", { count: size })}</option>)}
            </select>
            <button
              type="button"
              onClick={() => setPage((p) => Math.max(0, p - 1))}
              disabled={page === 0}
              aria-label={t("table.previous")}
              className="h-7 w-7 inline-flex items-center justify-center rounded-md border border-red-950/45 disabled:opacity-30 hover:text-slate-100"
            >
              <ChevronLeft size={14} />
            </button>
            <span className="font-mono">{page + 1} / {pageCount}</span>
            <button
              type="button"
              onClick={() => setPage((p) => Math.min(pageCount - 1, p + 1))}
              disabled={page >= pageCount - 1}
              aria-label={t("table.next")}
              className="h-7 w-7 inline-flex items-center justify-center rounded-md border border-red-950/45 disabled:opacity-30 hover:text-slate-100"
            >
              <ChevronRight size={14} />
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
