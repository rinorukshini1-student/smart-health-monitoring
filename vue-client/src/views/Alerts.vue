<script setup>
import { ref, computed, onMounted } from 'vue'
import { api } from '../services/api'
import { useRealtime } from '../stores/realtime'
import Panel from '../components/Panel.vue'
import { severityClass, clockTime } from '../utils/format'

const rt = useRealtime()
const serverAlerts = ref([])
const search = ref('')
const severity = ref('all')
const typeFilter = ref('all')

onMounted(async () => { try { serverAlerts.value = await api.alerts({ limit: 250 }) } catch (e) {} })

// Merge live (newest) with the initial server snapshot, de-duplicating by alertId.
const merged = computed(() => {
  const map = new Map()
  for (const a of [...rt.alerts, ...serverAlerts.value]) if (!map.has(a.alertId)) map.set(a.alertId, a)
  return [...map.values()].sort((a, b) => new Date(b.recordedAt) - new Date(a.recordedAt))
})

const types = computed(() => ['all', ...new Set(merged.value.map(a => a.alertType).filter(Boolean))])

const filtered = computed(() => merged.value.filter(a => {
  if (severity.value !== 'all' && a.severity !== severity.value) return false
  if (typeFilter.value !== 'all' && a.alertType !== typeFilter.value) return false
  if (search.value.trim()) {
    const q = search.value.toLowerCase()
    return a.patientId.toLowerCase().includes(q) || (a.roomNumber || '').includes(q) || (a.message || '').toLowerCase().includes(q)
  }
  return true
}))

const counts = computed(() => ({
  critical: merged.value.filter(a => a.severity === 'CRITICAL').length,
  warning: merged.value.filter(a => a.severity === 'WARNING').length,
  info: merged.value.filter(a => a.severity === 'INFO').length,
  total: merged.value.length
}))
</script>

<template>
  <div class="grid" style="gap:18px;">
    <div class="grid kpis">
      <div class="kpi kpi-row"><div><span class="label">Total Alerts</span><div class="value">{{ counts.total }}</div></div><span class="icon blue">🚨</span></div>
      <div class="kpi kpi-row"><div><span class="label">Critical</span><div class="value" style="color:#dc2626">{{ counts.critical }}</div></div><span class="icon red">⛔</span></div>
      <div class="kpi kpi-row"><div><span class="label">Warning</span><div class="value" style="color:#d97706">{{ counts.warning }}</div></div><span class="icon amber">⚠</span></div>
      <div class="kpi kpi-row"><div><span class="label">Info</span><div class="value" style="color:#2563eb">{{ counts.info }}</div></div><span class="icon sky">ℹ</span></div>
    </div>

    <Panel title="Alert Log" hint="live · newest first">
      <div class="controls" style="margin-bottom:14px;">
        <input class="search" type="search" v-model="search" placeholder="Search patient, room or message…" />
        <button v-for="s in ['all','CRITICAL','WARNING','INFO']" :key="s" class="chip"
                :class="{ active: severity === s }" @click="severity = s">{{ s === 'all' ? 'All' : s }}</button>
        <select v-model="typeFilter">
          <option v-for="t in types" :key="t" :value="t">{{ t === 'all' ? 'All types' : t }}</option>
        </select>
      </div>
      <div class="table-wrap">
        <table>
          <thead><tr><th>Alert ID</th><th>Patient</th><th>Type</th><th>Severity</th><th>Value</th><th>Message</th><th>Timestamp</th></tr></thead>
          <tbody>
            <tr v-for="a in filtered.slice(0, 200)" :key="a.alertId">
              <td class="mono muted">{{ a.alertId.slice(0, 8) }}</td>
              <td><strong>{{ a.patientId }}</strong> <span class="muted">Rm {{ a.roomNumber }}</span></td>
              <td>{{ a.alertType }}</td>
              <td><span class="badge" :class="severityClass(a.severity)">{{ a.severity }}</span></td>
              <td class="mono">{{ a.value }}</td>
              <td class="muted">{{ a.message }}</td>
              <td class="muted">{{ clockTime(a.recordedAt) }}</td>
            </tr>
            <tr v-if="!filtered.length"><td colspan="7" class="empty">No alerts match the filters…</td></tr>
          </tbody>
        </table>
      </div>
    </Panel>
  </div>
</template>
