<script setup>
import { ref, computed, onMounted } from 'vue'
import { api } from '../services/api'
import { useRealtime } from '../stores/realtime'
import Panel from '../components/Panel.vue'
import LaIcon from '../components/LaIcon.vue'
import { severityClass, clockTime } from '../utils/format'
import { translateSeverity, translateAlertType, filterLabels } from '../utils/i18n'

const rt = useRealtime()
const serverAlerts = ref([])
const search = ref('')
const severity = ref('all')
const typeFilter = ref('all')

const severityFilters = [
  { key: 'all', label: filterLabels.all },
  { key: 'CRITICAL', label: filterLabels.CRITICAL },
  { key: 'WARNING', label: filterLabels.WARNING },
  { key: 'INFO', label: filterLabels.INFO }
]

onMounted(async () => { try { serverAlerts.value = await api.alerts({ limit: 250 }) } catch (e) {} })

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
      <div class="kpi kpi-row"><div><span class="label">Alarme Totalë</span><div class="value">{{ counts.total }}</div></div><span class="icon blue"><LaIcon icon="la-bell" /></span></div>
      <div class="kpi kpi-row"><div><span class="label">Kritik</span><div class="value" style="color:#dc2626">{{ counts.critical }}</div></div><span class="icon red"><LaIcon icon="la-ban" /></span></div>
      <div class="kpi kpi-row"><div><span class="label">Paralajmërim</span><div class="value" style="color:#d97706">{{ counts.warning }}</div></div><span class="icon amber"><LaIcon icon="la-exclamation-triangle" /></span></div>
      <div class="kpi kpi-row"><div><span class="label">Informacion</span><div class="value" style="color:#2563eb">{{ counts.info }}</div></div><span class="icon sky"><LaIcon icon="la-info-circle" /></span></div>
    </div>

    <Panel title="Regjistri i Alarmeve" hint="në kohë reale · më të rejat së pari">
      <div class="controls" style="margin-bottom:14px;">
        <input class="search" type="search" v-model="search" placeholder="Kërko pacient, dhomë ose mesazh…" />
        <button v-for="s in severityFilters" :key="s.key" class="chip"
                :class="{ active: severity === s.key }" @click="severity = s.key">{{ s.label }}</button>
        <select v-model="typeFilter">
          <option v-for="t in types" :key="t" :value="t">{{ t === 'all' ? 'Të gjitha llojet' : translateAlertType(t) }}</option>
        </select>
      </div>
      <div class="table-wrap">
        <table>
          <thead><tr><th>Pacienti</th><th>Lloji</th><th>Niveli i rrezikut</th><th>Vlera</th><th>Mesazhi</th><th>Koha</th></tr></thead>
          <tbody>
            <tr v-for="a in filtered.slice(0, 200)" :key="a.alertId">
              <td><strong>{{ a.patientId }}</strong> <span class="muted">Dh {{ a.roomNumber }}</span></td>
              <td>{{ translateAlertType(a.alertType) }}</td>
              <td><span class="badge" :class="severityClass(a.severity)">{{ translateSeverity(a.severity) }}</span></td>
              <td class="mono">{{ a.value }}</td>
              <td class="muted">{{ a.message }}</td>
              <td class="muted">{{ clockTime(a.recordedAt) }}</td>
            </tr>
            <tr v-if="!filtered.length"><td colspan="7" class="empty">Asnjë alarm nuk përputhet me filtrat…</td></tr>
          </tbody>
        </table>
      </div>
    </Panel>
  </div>
</template>
