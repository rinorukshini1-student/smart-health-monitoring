import { Capacitor } from '@capacitor/core'
import { PushNotifications } from '@capacitor/push-notifications'
import { LocalNotifications } from '@capacitor/local-notifications'
import { App } from '@capacitor/app'
import { apiUrl } from '../config'

let initialized = false
let routerRef = null

export function setNotificationRouter(router) {
  routerRef = router
}

export async function initNotifications() {
  if (initialized) return
  initialized = true

  if (Capacitor.isNativePlatform()) {
    await initNativePush()
    await initLocalNotifications()
    App.addListener('appUrlOpen', ({ url }) => handleDeepLink(url))
  } else if ('Notification' in window && Notification.permission === 'default') {
    // Optional browser permission for desktop demo.
    try { await Notification.requestPermission() } catch { /* ignore */ }
  }
}

async function initNativePush() {
  const perm = await PushNotifications.requestPermissions()
  if (perm.receive !== 'granted') return

  PushNotifications.addListener('registration', async ({ value: token }) => {
    try {
      await fetch(apiUrl('/api/push/register'), {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ token, platform: Capacitor.getPlatform() })
      })
    } catch { /* server may be offline during setup */ }
  })

  PushNotifications.addListener('registrationError', (err) => {
    console.warn('Push registration failed', err)
  })

  PushNotifications.addListener('pushNotificationReceived', (notification) => {
    const data = notification.data || {}
    showLocalAlertNotification({
      alertId: data.alertId,
      patientId: data.patientId,
      roomNumber: data.roomNumber,
      severity: data.severity || 'INFO',
      message: notification.body || data.message || 'Alarm i ri',
      alertType: data.alertType || 'Alert'
    })
  })

  PushNotifications.addListener('pushNotificationActionPerformed', ({ notification }) => {
    const data = notification.data || {}
    navigateToAlerts(data.patientId)
  })

  await PushNotifications.register()
}

async function initLocalNotifications() {
  const perm = await LocalNotifications.requestPermissions()
  if (perm.display !== 'granted') return

  LocalNotifications.addListener('localNotificationActionPerformed', ({ notification }) => {
    navigateToAlerts(notification.extra?.patientId)
  })

  await LocalNotifications.createChannel({
    id: 'health-alerts',
    name: 'Alarme shëndetësore',
    description: 'Alarme klinike në kohë reale',
    importance: 5,
    vibration: true,
    sound: 'default'
  })
}

export async function showLocalAlertNotification(alert) {
  const title = `${severityLabel(alert.severity)} · ${alert.alertType || 'Alarm'}`
  const body = `${alert.patientId} · Dhoma ${alert.roomNumber}\n${alert.message}`

  if (Capacitor.isNativePlatform()) {
    await LocalNotifications.schedule({
      notifications: [{
        id: notificationId(alert),
        title,
        body,
        channelId: 'health-alerts',
        extra: { patientId: alert.patientId, route: '/alerts' }
      }]
    })
    return
  }

  if ('Notification' in window && Notification.permission === 'granted') {
    const n = new Notification(title, { body, tag: alert.alertId || alert.patientId })
    n.onclick = () => { window.focus(); navigateToAlerts(alert.patientId) }
  }
}

function navigateToAlerts(patientId) {
  if (!routerRef) return
  routerRef.push(patientId ? { path: '/alerts', query: { q: patientId } } : '/alerts')
}

function handleDeepLink(url) {
  try {
    const path = new URL(url).pathname
    if (path.startsWith('/alerts') && routerRef) routerRef.push(path)
  } catch { /* ignore */ }
}

function severityLabel(severity) {
  if (severity === 'CRITICAL') return 'KRITIK'
  if (severity === 'WARNING') return 'PARALAJMËRIM'
  return 'INFO'
}

function notificationId(alert) {
  const raw = alert.alertId || `${alert.patientId}-${alert.message}`
  let hash = 0
  for (let i = 0; i < raw.length; i++) hash = ((hash << 5) - hash + raw.charCodeAt(i)) | 0
  return Math.abs(hash) % 2000000000
}
