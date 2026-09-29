import { createContext, useContext, type PropsWithChildren } from "react";
import type { AuthenticatedUser } from "@/api/client";

type Session = { user: AuthenticatedUser; isAdmin: boolean };

const SessionContext = createContext<Session | null>(null);

/**
 * The signed-in user. Role checks here only shape the UI; the backend enforces access.
 */
export function SessionProvider({ user, children }: PropsWithChildren<{ user: AuthenticatedUser }>) {
  return <SessionContext.Provider value={{ user, isAdmin: user.role === "Admin" }}>{children}</SessionContext.Provider>;
}

export function useSession() {
  const session = useContext(SessionContext);
  if (!session) throw new Error("useSession must be used inside SessionProvider");
  return session;
}
