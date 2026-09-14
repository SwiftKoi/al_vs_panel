import { useState, type PropsWithChildren } from "react";
import Button from "@/components/ui/Button";
import LanguageSwitcher from "@/components/layout/LanguageSwitcher";
import ServerSelector from "@/components/layout/ServerSelector";
import NavigationMenu from "@/components/layout/NavigationMenu";
import QuickActions from "@/components/layout/QuickActions";
import { useTranslation } from "react-i18next";

function FloatingEmbers() {
  const [particles] = useState(() =>
    Array.from({ length: 15 }, (_, i) => ({
      id: i,
      left: `${Math.random() * 100}%`,
      size: `${Math.random() * 3 + 2}px`,
      delay: `${Math.random() * 10}s`,
      duration: `${Math.random() * 15 + 10}s`,
    }))
  );

  return (
    <div className="fixed inset-0 overflow-hidden pointer-events-none z-0">
      {particles.map((p) => (
        <span
          key={p.id}
          className="absolute bottom-[-10px] rounded-full bg-gradient-to-t from-[#ffd8a0] to-[#b8282e] opacity-35 animate-ember-float"
          style={{
            left: p.left,
            width: p.size,
            height: p.size,
            animationDelay: p.delay,
            animationDuration: p.duration,
            boxShadow: "0 0 6px #e07a3a, 0 0 10px #b8282e",
          }}
        />
      ))}
    </div>
  );
}

export default function AppShell({ children, onLogout }: PropsWithChildren<{ onLogout: () => void }>) {
  const { t } = useTranslation();

  return (
    <div className="relative min-h-screen max-w-full overflow-x-hidden bg-[#0a0a0c] text-slate-200">
      <FloatingEmbers />

      <header className="relative z-20 flex h-14 items-center justify-between border-b border-red-950/30 bg-slate-950/40 backdrop-blur-md px-4 gap-4">
        <div className="flex items-center gap-4 min-w-0">
          <ServerSelector />
        </div>
        <div className="flex items-center gap-2 shrink-0">
          <LanguageSwitcher />
          <Button variant="ghost" className="h-8 text-xs font-semibold" onClick={onLogout}>
            {t("common.logout")}
          </Button>
        </div>
      </header>

      <div className="relative z-10 grid min-h-[calc(100vh-3.5rem)] grid-cols-1 md:grid-cols-[220px_1fr]">
        <aside className="border-b border-red-950/30 bg-slate-950/20 backdrop-blur-sm p-4 md:border-b-0 md:border-r md:border-red-950/30">

          <NavigationMenu />

          <div className="h-px bg-red-950/20 my-3" />

          <QuickActions />
        </aside>
        <main className="p-4 sm:p-6 min-w-0 overflow-x-hidden">{children}</main>
      </div>
    </div>
  );
}
