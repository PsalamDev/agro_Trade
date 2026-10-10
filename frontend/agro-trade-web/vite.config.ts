import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    port: 5173,
    // Never silently drift to 5174/5175 when the port is taken — fail loudly instead,
    // so the URL the browser opens is always the URL that's really running.
    strictPort: true,
    proxy: {
      // All API traffic flows to the ASP.NET Core host (CORS is configured too,
      // but a proxy keeps dev simple and avoids absolute URLs).
      '/api': {
        target: 'http://localhost:5000',
        changeOrigin: true,
      },
      '/health': {
        target: 'http://localhost:5000',
        changeOrigin: true,
      },
      // Uploaded product images / business documents are stored by the API
      // under /uploads; proxy them so relative URLs work in dev.
      '/uploads': {
        target: 'http://localhost:5000',
        changeOrigin: true,
      },
    },
  },
})