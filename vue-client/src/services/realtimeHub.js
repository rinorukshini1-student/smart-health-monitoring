import { getConnection } from './signalr'

const vitalsSubs = new Set()
const riskSubs = new Set()
const alertSubs = new Set()
const metricsSubs = new Set()
const mlSubs = new Set()
const statusSubs = new Set()

let wired = false
let startPromise = null

function wireConnection() {
  if (wired) return
  wired = true
  const conn = getConnection()

  conn.on('vitalsReceived', (v) => vitalsSubs.forEach((fn) => fn(v)))
  conn.on('riskReceived', (r) => riskSubs.forEach((fn) => fn(r)))
  conn.on('alertReceived', (a) => alertSubs.forEach((fn) => fn(a)))
  conn.on('metricsReceived', (m) => metricsSubs.forEach((fn) => fn(m)))
  conn.on('mlPredictionReceived', (p) => mlSubs.forEach((fn) => fn(p)))

  conn.onreconnecting(() => statusSubs.forEach((fn) => fn('down')))
  conn.onreconnected(() => statusSubs.forEach((fn) => fn('live')))
  conn.onclose(() => {
    startPromise = null
    statusSubs.forEach((fn) => fn('down'))
  })
}

/** Start the shared SignalR connection once (safe to call from any page). */
export function ensureHubStarted() {
  wireConnection()
  const conn = getConnection()
  if (conn.state === 'Connected') {
    statusSubs.forEach((fn) => fn('live'))
    return Promise.resolve()
  }
  if (!startPromise) {
    statusSubs.forEach((fn) => fn('connecting'))
    startPromise = conn.start()
      .then(() => statusSubs.forEach((fn) => fn('live')))
      .catch((err) => {
        startPromise = null
        statusSubs.forEach((fn) => fn('down'))
        throw err
      })
  }
  return startPromise
}

export function subscribeVitals(fn) {
  vitalsSubs.add(fn)
  return () => vitalsSubs.delete(fn)
}

export function subscribeRisk(fn) {
  riskSubs.add(fn)
  return () => riskSubs.delete(fn)
}

export function subscribeAlerts(fn) {
  alertSubs.add(fn)
  return () => alertSubs.delete(fn)
}

export function subscribeMetrics(fn) {
  metricsSubs.add(fn)
  return () => metricsSubs.delete(fn)
}

export function subscribeMlPrediction(fn) {
  mlSubs.add(fn)
  return () => mlSubs.delete(fn)
}

export function subscribeStatus(fn) {
  statusSubs.add(fn)
  return () => statusSubs.delete(fn)
}
