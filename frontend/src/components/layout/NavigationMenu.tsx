import type { ComponentType } from "react";
import { useLocation, useNavigate } from "react-router-dom";
import { BarChart3, Wand2, Code2, FileSearch, Folder, LayoutDashboard, Package, ScrollText, Server, Settings } from "lucide-react";
import { useTranslation } from "react-i18next";
import { cn } from "@/lib/cn";
import { useSession } from "@/context/SessionContext";

const menuItemBaseClass = "flex items-center gap-2 rounded-md px-3 py-2 text-[14px] leading-5 font-medium whitespace-nowrap transition-all duration-200 cursor-pointer";

const navItemClass = (isActive: boolean) =>
  cn(menuItemBaseClass, "text-left", isActive ? "bg-[#b8282e]/10 text-[#e04444] md:border-l-2 md:border-[#b8282e] md:rounded-l-none md:pl-2.5 md:shadow-[inset_4px_0_12px_rgba(184,40,46,0.08)] glow-text" : "text-slate-300 hover:bg-red-950/10 hover:text-white hover:pl-[14px]");

const items: { path: string; label: string; icon: ComponentType<{ size?: number }>; adminOnly?: boolean }[] = [
  { path: "/", label: "navigation.overview", icon: LayoutDashboard },
  { path: "/server", label: "navigation.server", icon: Server, adminOnly: true },
  { path: "/files", label: "navigation.files", icon: Folder, adminOnly: true },
  { path: "/editor", label: "navigation.editor", icon: Code2, adminOnly: true },
  { path: "/mods", label: "navigation.mods", icon: Package, adminOnly: true },
  { path: "/analytics", label: "navigation.analytics", icon: BarChart3, adminOnly: true },
  { path: "/server-logs", label: "navigation.serverLogs", icon: FileSearch },
  { path: "/actions", label: "navigation.actions", icon: Wand2 },
  { path: "/logs", label: "navigation.logs", icon: ScrollText, adminOnly: true },
  { path: "/settings", label: "navigation.settings", icon: Settings }
];

export default function NavigationMenu() {
  const { t } = useTranslation();
  const location = useLocation();
  const navigate = useNavigate();
  const { isAdmin } = useSession();

  // Match whole path segments, so "/server" is not active on "/server-logs".
  const isActive = (path: string) =>
    path === "/" ? location.pathname === "/" : location.pathname === path || location.pathname.startsWith(`${path}/`);

  return (
    <nav className="scroll-fade-x flex md:flex-col gap-1 overflow-x-auto md:overflow-x-visible pb-1 md:pb-0">
      {items.filter((item) => isAdmin || !item.adminOnly).map(({ path, label, icon: Icon }) => (
        <button key={path} type="button" onClick={() => navigate(path)} className={navItemClass(isActive(path))}>
          <Icon size={16} /> {t(label)}
        </button>
      ))}
    </nav>
  );
}
