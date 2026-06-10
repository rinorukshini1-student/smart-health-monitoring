import { defineConfig, loadEnv } from 'vite'
import vue from '@vitejs/plugin-vue'

const LIVE_API = 'http://178.105.181.143:5099'

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '')
  const apiTarget = env.VITE_API_BASE_URL || LIVE_API
  const isMobileBuild = mode === 'mobile'

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
      outDir: isMobileBuild ? 'dist' : '../Vue.Api/wwwroot',
      emptyOutDir: true
    }
  }
})
