// Base URL for REST + SignalR. Empty = same origin (browser dev / SPA served by Vue.Api).
// On tablet / remote client set VITE_API_BASE_URL, e.g. http://178.105.181.143:5099
export const API_BASE = (import.meta.env.VITE_API_BASE_URL || '').replace(/\/$/, '')

export function apiUrl(path) {
  return `${API_BASE}${path}`
}
