import { defineConfig, loadEnv } from "vite";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, ".", "");
  const backendTarget = env.VITE_API_TARGET || "http://localhost:5053";

  return {
    plugins: [react(), tailwindcss()],
    resolve: { alias: { "@": "/src" } },
    server: {
      port: 5173,
      proxy: {
        "/auth": backendTarget,
        "/api": backendTarget,
        "/health": backendTarget
      }
    }
  };
});
