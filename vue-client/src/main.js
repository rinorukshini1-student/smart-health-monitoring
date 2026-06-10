import { createApp } from 'vue'
import { createPinia } from 'pinia'
import VueApexCharts from 'vue3-apexcharts'
import { Capacitor } from '@capacitor/core'
import { StatusBar, Style } from '@capacitor/status-bar'
import { SplashScreen } from '@capacitor/splash-screen'
import App from './App.vue'
import router from './router'
import { initNotifications, setNotificationRouter } from './services/notifications'
import 'line-awesome/dist/line-awesome/css/line-awesome.min.css'
import './assets/theme.css'

async function bootstrapNativeShell() {
  if (!Capacitor.isNativePlatform()) return
  try {
    await StatusBar.setStyle({ style: Style.Light })
    await SplashScreen.hide()
  } catch { /* plugins optional during first setup */ }
}

const app = createApp(App)
app.use(createPinia())
app.use(router)
app.use(VueApexCharts)

setNotificationRouter(router)
bootstrapNativeShell().then(() => initNotifications())
app.mount('#app')
