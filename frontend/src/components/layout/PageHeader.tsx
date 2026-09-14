import type { PropsWithChildren, ReactNode } from "react";

export interface PageHeaderProps extends PropsWithChildren {
  title: string;
  description?: string;
  actions?: ReactNode;
}

export default function PageHeader({ title, description, actions, children }: PageHeaderProps) {
  return (
    <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
      <div>
        <h1 className="text-2xl font-bold text-slate-100 font-serif tracking-wide glow-text">{title}</h1>
        {description && <p className="mt-1.5 text-xs text-slate-400 tracking-wider uppercase font-medium">{description}</p>}
      </div>

      {(actions || children) && (
        <div className="flex items-center gap-3 shrink-0">
          {actions}
          {children}
        </div>
      )}
    </div>
  );
}
