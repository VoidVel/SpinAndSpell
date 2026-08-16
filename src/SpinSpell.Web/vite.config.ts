import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

const API_TARGET = process.env.VITE_API_TARGET ?? 'http://localhost:8080'

export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    // Bound to 0.0.0.0 so phones on the same wifi can reach the dev server —
    // this app cannot be tested meaningfully on one machine.
    host: true,
    port: 5173,

    // The API is proxied rather than called directly, so a phone only ever
    // talks to one port. Otherwise every playtest needs a Windows firewall
    // exception for 8080 and the client needs to guess the host's LAN IP.
    proxy: {
      '/api': { target: API_TARGET, changeOrigin: true },
      // ws:true matters — SignalR upgrades to a WebSocket and the proxy has to
      // carry the upgrade through, not just the negotiate POST.
      '/hub': { target: API_TARGET, changeOrigin: true, ws: true },
    },
  },
})
