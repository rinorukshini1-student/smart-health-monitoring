<script setup>
import { ref, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { api } from '../services/api'
import { useRealtime } from '../stores/realtime'
import Panel from '../components/Panel.vue'
import { classifyVitals, statusClass, timeAgo } from '../utils/format'

const rt = useRealtime()
const router = useRouter()
const search = ref('')
const statusFilter = ref('all')
const sortKey = ref('roomNumber')
const sortDir = ref('asc')

onMounted(async () => {
  try {
    const rows = await api.live()
    for (const r of rows) {
      rt.vitals[r.patientId] = {
        patientId: r.patientId, patientName: r.patientName, roomNumber: r.roomNumber, age: r.age,
        heartRate: r.heartRate, spo2: r.spo2, temperature: r.temperature, systolicBp: r.systolicBp,
        diastolicBp: r.diastolicBp, respiratoryRate: r.respiratoryRate, recordedAt: r.lastUpdate
      }
      rt.risk[r.patientId] = { score: r.riskScore, category: r.riskCategory }
    }
  } catch (e) { /* SignalR will populate */ }
})

const rows = computed(() => rt.vitalsList.map(v => {
  const status = classifyVitals(v)
  return { ...v, status, risk: rt.risk[v.patientId]?.score ?? 0 }
}))

const filtered = computed(() => {
  let list = rows.value
  if (statusFilter.value !== 'all') list = list.filter(r => r.status.toLowerCase() === statusFilter.value)
  if (search.value.trim()) {
    const q = search.value.toLowerCase()
    list = list.filter(r => r.patientId.toLowerCase().includes(q) || (r.patientName || '').toLowerCase().includes(q) || (r.roomNumber || '').includes(q))
  }
  const dir = sortDir.value === 'asc' ? 1 : -1
  return [...list].sort((a, b) => {
    const av = a[sortKey.value], bv = b[sortKey.value]
    if (av < bv) return -1 * dir
    if (av > bv) return 1 * dir
    return 0
  })
})

const counts = computed(() => ({
  all: rows.value.length,
  normal: rows.value.filter(r => r.status === 'Normal').length,
  warning: rows.value.filter(r => r.status === 'Warning').length,
  critical: rows.value.filter(r => r.status === 'Critical').length
}))

function sortBy(key) {
  if (sortKey.value === key) sortDir.value = sortDir.value === 'asc' ? 'desc' : 'asc'
  else { sortKey.value = key; sortDir.value = 'asc' }
}
</script>

<template>
  <div class="grid" style="gap:18px;">
    <div class="grid kpis">
      <div class="kpi"><span class="label">Monitored</span><span class="value">{{ counts.all }}</span></div>
      <div class="kpi"><span class="label">Normal</span><span class="value" style="color:#16a34a">{{ counts.normal }}</span></div>
      <div class="kpi"><span class="label">Warning</span><span class="value" style="color:#d97706">{{ counts.warning }}</span></div>
      <div class="kpi"><span class="label">Critical</span><span class="value" style="color:#dc2626">{{ counts.critical }}</span></div>
    </div>

    <Panel title="Live Patient Vitals" hint="updates automatically via SignalR">
      <div class="controls" style="margin-bottom:14px;">
        <input class="search" type="search" v-model="search" placeholder="Search patient or room…" />
        <button v-for="s in ['all','normal','warning','critical']" :key="s" class="chip"
                :class="{ active: statusFilter === s }" @click="statusFilter = s">
          {{ s[0].toUpperCase() + s.slice(1) }}
        </button>
      </div>
      <div class="table-wrap">
        <table>
          <thead>
            <tr>
              <th class="sortable" @click="sortBy('patientId')">Patient ID</th>
              <th class="sortable" @click="sortBy('heartRate')">Heart Rate</th>
              <th class="sortable" @click="sortBy('temperature')">Temp</th>
              <th class="sortable" @click="sortBy('spo2')">SpO₂</th>
              <th>Blood Pressure</th>
              <th class="sortable" @click="sortBy('respiratoryRate')">Resp</th>
              <th class="sortable" @click="sortBy('risk')">Risk</th>
              <th class="sortable" @click="sortBy('recordedAt')">Last Update</th>
              <th class="sortable" @click="sortBy('status')">Status</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="r in filtered" :key="r.patientId" class="clickable" @click="router.push(`/patients/${r.patientId}`)">
              <td><strong>{{ r.patientId }}</strong><div class="muted" style="font-size:12px">{{ r.patientName }} · Rm {{ r.roomNumber }}</div></td>
              <td class="mono">{{ r.heartRate }} <span class="muted">bpm</span></td>
              <td class="mono">{{ r.temperature?.toFixed(1) }}°C</td>
              <td class="mono">{{ r.spo2 }}%</td>
              <td class="mono">{{ r.systolicBp }}/{{ r.diastolicBp }}</td>
              <td class="mono">{{ r.respiratoryRate }}</td>
              <td class="mono">{{ r.risk }}</td>
              <td class="muted">{{ timeAgo(r.recordedAt) }}</td>
              <td><span class="badge" :class="statusClass(r.status)">{{ r.status }}</span></td>
            </tr>
            <tr v-if="!filtered.length"><td colspan="9" class="empty">Waiting for live data…</td></tr>
          </tbody>
        </table>
      </div>
    </Panel>
  </div>
</template>
