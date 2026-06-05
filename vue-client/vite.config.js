import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

// Build output goes straight into the backend's wwwroot so `dotnet run` can serve the SPA.
export default defineConfig({
  plugins: [vue()],
  server: {
    port: 5173,
    proxy: {
      '/api': { target: 'http://localhost:5099', changeOrigin: true },
      '/healthHub': { target: 'http://localhost:5099', changeOrigin: true, ws: true }
    }
  },
  build: {
    outDir: '../Vue.Api/wwwroot',
    emptyOutDir: true
  }
})
