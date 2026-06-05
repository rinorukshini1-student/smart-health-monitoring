import { defineStore } from 'pinia'
import { getConnection } from '../services/signalr'

// Central live-data store. One SignalR connection feeds every page in real time.
export const useRealtime = defineStore('realtime', {
  state: () => ({
    status: 'connecting',          // connecting | live | down
    vitals: {},                    // patientId -> latest VitalReadingDto
    risk: {},                      // patientId -> latest RiskScoreDto
    ml: {},                        // patientId -> latest MlPredictionDto
    alerts: [],                    // newest-first, capped
    metrics: null,                 // latest StreamMetricsDto
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
      const conn = getConnection()

      conn.on('vitalsReceived', (v) => { this.vitals[v.patientId] = v; this.lastEventAt = Date.now() })
      conn.on('riskReceived', (r) => { this.risk[r.patientId] = r })
      conn.on('mlPredictionReceived', (p) => { this.ml[p.patientId] = p })
      conn.on('metricsReceived', (m) => { this.metrics = m })
      conn.on('alertReceived', (a) => {
        this.alerts.unshift(a)
        if (this.alerts.length > 300) this.alerts.length = 300
      })

      conn.onreconnecting(() => { this.status = 'down' })
      conn.onreconnected(() => { this.status = 'live' })
      conn.onclose(() => { this.status = 'down' })

      try {
        await conn.start()
        this.status = 'live'
      } catch (e) {
        this.status = 'down'
        setTimeout(() => { this.started = false; this.init() }, 4000)
      }
    }
  }
})
