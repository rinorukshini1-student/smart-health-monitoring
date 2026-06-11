import { defineStore } from 'pinia'
import {
  ensureHubStarted,
  subscribeVitals,
  subscribeRisk,
  subscribeAlerts,
  subscribeMetrics,
  subscribeStatus
} from '../services/realtimeHub'
import { showLocalAlertNotification } from '../services/notifications'
import { isMlAlert } from '../utils/alerts'

export const useRealtime = defineStore('realtime', {
  state: () => ({
    status: 'connecting',
    vitals: {},
    risk: {},
    alerts: [],
    metrics: null,
    lastEventAt: null,
    started: false
  }),
  getters: {
    vitalsList: (s) => Object.values(s.vitals),
    criticalCount: (s) => s.alerts.filter(a => a.severity === 'CRITICAL').length
  },
  actions: {
    async init() {
      if (this.started) return
      this.started = true

      subscribeStatus((s) => { this.status = s })
      subscribeVitals((v) => {
        this.vitals = { ...this.vitals, [v.patientId]: v }
        this.lastEventAt = Date.now()
      })
      subscribeRisk((r) => {
        this.risk = { ...this.risk, [r.patientId]: r }
      })
      subscribeMetrics((m) => { this.metrics = m })
      subscribeAlerts((a) => {
        // ML predictions surface only on Patient Details — not in the global alert feed.
        if (isMlAlert(a)) return
        this.alerts.unshift(a)
        if (this.alerts.length > 300) this.alerts.length = 300
        showLocalAlertNotification(a)
      })

      try {
        await ensureHubStarted()
      } catch {
        setTimeout(() => { this.started = false; this.init() }, 4000)
      }
    }
  }
})

