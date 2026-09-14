import { useLocation, useNavigate } from "react-router-dom";
import { Code2, Folder, LayoutDashboard, ScrollText, Server, Settings } from "lucide-react";
import { useTranslation } from "react-i18next";
import { cn } from "@/lib/cn";

const menuItemBaseClass = "flex items-center gap-2 rounded-md px-3 py-2 text-[14px] leading-5 font-medium whitespace-nowrap transition-all duration-200 cursor-pointer";

const navItemClass = (isActive: boolean) =>
  cn(menuItemBaseClass, "text-left", isActive ? "bg-[#b8282e]/10 text-[#e04444] border-l-2 border-[#b8282e] rounded-l-none pl-2.5 shadow-[inset_4px_0_12px_rgba(184,40,46,0.08)] glow-text" : "text-slate-300 hover:bg-red-950/10 hover:text-white hover:pl-[14px]");

export default function NavigationMenu() {
  const { t } = useTranslation();
  const location = useLocation();
  const navigate = useNavigate();

  const isActive = (path: string) =>
    path === "/" ? location.pathname === "/" : location.pathname.startsWith(path);

  return (
    <nav className="flex md:flex-col gap-1 overflow-x-auto md:overflow-x-visible pb-1 md:pb-0">
      <button type="button" onClick={() => navigate("/")} className={navItemClass(isActive("/"))}>
        <LayoutDashboard size={16} /> {t("navigation.overview")}
      </button>
      <button type="button" onClick={() => navigate("/server")} className={navItemClass(isActive("/server"))}>
        <Server size={16} /> {t("navigation.server")}
      </button>
      <button type="button" onClick={() => navigate("/files")} className={navItemClass(isActive("/files"))}>
        <Folder size={16} /> {t("navigation.files")}
      </button>
      <button type="button" onClick={() => navigate("/editor")} className={navItemClass(isActive("/editor"))}>
        <Code2 size={16} /> {t("navigation.editor")}
      </button>
      <button type="button" onClick={() => navigate("/logs")} className={navItemClass(isActive("/logs"))}>
        <ScrollText size={16} /> {t("navigation.logs")}
      </button>
      <button type="button" onClick={() => navigate("/settings")} className={navItemClass(isActive("/settings"))}>
        <Settings size={16} /> {t("navigation.settings")}
      </button>
    </nav>
  );
}
