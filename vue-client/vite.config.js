import { defineConfig, loadEnv } from 'vite'
import vue from '@vitejs/plugin-vue'

const LOCAL_API = 'http://localhost:5099'

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '')
  // Proxy target when VITE_API_BASE_URL is empty (local dev).
  const apiTarget = env.VITE_API_BASE_URL || LOCAL_API
  const isMobileBuild = mode === 'mobile'
  const isDockerBuild = mode === 'docker'

  return {
    plugins: [vue()],
    base: isMobileBuild ? './' : '/',
    server: {
      port: 5173,
      proxy: {
        '/api': { target: apiTarget, changeOrigin: true },
        '/healthHub': { target: apiTarget, changeOrigin: true, ws: true }
      }
    },
    build: {
      outDir: isMobileBuild || isDockerBuild ? 'dist' : '../Vue.Api/wwwroot',
      emptyOutDir: true
    }
  }
})
