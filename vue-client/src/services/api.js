import { apiUrl } from '../config'

async function get(path) {
  const res = await fetch(apiUrl(path), { headers: { Accept: 'application/json' } })
  if (!res.ok) throw new Error(`${res.status} ${res.statusText} for ${path}`)
  return res.json()
}

export const api = {
  overview: () => get('/api/overview'),
  live: () => get('/api/live'),
  patients: () => get('/api/patients'),
  patient: (id, range = 'day') => get(`/api/patients/${id}?range=${range}`),
  patientProfile: (id) => get(`/api/patients/${id}/profile`),
  alerts: (params = {}) => {
    const q = new URLSearchParams()
    if (params.severity) q.set('severity', params.severity)
    if (params.type) q.set('type', params.type)
    if (params.q) q.set('q', params.q)
    if (params.limit) q.set('limit', params.limit)
    const qs = q.toString()
    return get(`/api/alerts${qs ? '?' + qs : ''}`)
  },
  analytics: () => get('/api/analytics'),
  highRisk: (limit = 8) => get(`/api/high-risk?limit=${limit}`),
  system: () => get('/api/system'),
  aiPredictions: () => get('/api/ai/predictions'),
  aiPatient: (id) => get(`/api/ai/patient/${id}`),
  aiHistory: (id) => get(`/api/ai/history/${id}`),
  aiFactors: () => get('/api/ai/factors'),
  aiMetrics: () => get('/api/ai/metrics'),
  registerPush: (token, platform) =>
    fetch(apiUrl('/api/push/register'), {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ token, platform })
    })
}
