export function timeAgo(ts) {
  if (!ts) return '—'
  const diff = (Date.now() - new Date(ts).getTime()) / 1000
  if (diff < 5) return 'tani'
  if (diff < 60) return `${Math.floor(diff)} sek më parë`
  if (diff < 3600) return `${Math.floor(diff / 60)} min më parë`
  if (diff < 86400) return `${Math.floor(diff / 3600)} orë më parë`
  return new Date(ts).toLocaleDateString('sq-AL')
}

export function clockTime(ts) {
  if (!ts) return '—'
  return new Date(ts).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' })
}

export function statusClass(status) {
  const s = (status || '').toLowerCase()
  if (s.includes('crit')) return 'critical'
  if (s.includes('warn')) return 'warning'
  return 'normal'
}

export function severityClass(sev) {
  const s = (sev || '').toUpperCase()
  if (s === 'CRITICAL') return 'critical'
  if (s === 'WARNING') return 'warning'
  return 'info'
}

export function riskClass(category) {
  const c = (category || '').toUpperCase()
  if (c.includes('HIGH')) return 'high'
  if (c.includes('MEDIUM') || c.includes('MED')) return 'medium'
  return 'low'
}

export function riskColor(category) {
  return { high: '#dc2626', medium: '#d97706', low: '#16a34a' }[riskClass(category)]
}

export function pct(v) { return `${Math.round((v ?? 0) * 100)}%` }

// Mirror of the backend AlertRules.Classify so live rows show status without a round-trip.
export function classifyVitals(v) {
  if (!v) return 'Normal'
  if (v.heartRate > 120 || v.heartRate < 50 || v.spo2 < 90 || v.temperature > 38.5 ||
      v.systolicBp > 140 || v.diastolicBp > 90 || v.respiratoryRate > 24 || v.respiratoryRate < 10) return 'Critical'
  if (v.heartRate > 110 || v.heartRate < 55 || v.spo2 < 94 || v.temperature > 37.8 ||
      v.systolicBp > 130 || v.diastolicBp > 85 || v.respiratoryRate > 22 || v.respiratoryRate < 11) return 'Warning'
  return 'Normal'
}
