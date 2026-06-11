<script setup>
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { useRouter } from 'vue-router'
import { api } from '../services/api'
import { ensureHubStarted, subscribeVitals } from '../services/realtimeHub'
import Panel from '../components/Panel.vue'
import { classifyVitals, statusClass, timeAgo } from '../utils/format'
import { translateStatus, filterLabels } from '../utils/i18n'

const router = useRouter()
const search = ref('')
const statusFilter = ref('all')
const sortKey = ref('roomNumber')
const sortDir = ref('asc')

const patientVitals = ref({})
let pollTimer = null
let unsubVitals = null

const statusFilters = [
  { key: 'all', label: filterLabels.all },
  { key: 'normal', label: filterLabels.normal },
  { key: 'warning', label: filterLabels.warning },
  { key: 'critical', label: filterLabels.critical }
]

function onVitals(v) {
  patientVitals.value = { ...patientVitals.value, [v.patientId]: v }
}

function seedFromLiveRows(rows) {
  const vitals = { ...patientVitals.value }
  for (const r of rows) {
    vitals[r.patientId] = {
      patientId: r.patientId, patientName: r.patientName, roomNumber: r.roomNumber, age: r.age,
      heartRate: r.heartRate, spo2: r.spo2, temperature: r.temperature, systolicBp: r.systolicBp,
      diastolicBp: r.diastolicBp, respiratoryRate: r.respiratoryRate, recordedAt: r.lastUpdate
    }
  }
  patientVitals.value = vitals
}

async function refreshFromApi() {
  try {
    seedFromLiveRows(await api.live())
  } catch { /* keep last snapshot */ }
}

onMounted(async () => {
  await ensureHubStarted()
  unsubVitals = subscribeVitals(onVitals)
  await refreshFromApi()
  pollTimer = setInterval(refreshFromApi, 3000)
})

onUnmounted(() => {
  unsubVitals?.()
  if (pollTimer) clearInterval(pollTimer)
})

const rows = computed(() => Object.values(patientVitals.value).map(v => ({
  ...v,
  status: classifyVitals(v)
})))

const filtered = computed(() => {
  let list = rows.value
  if (statusFilter.value !== 'all') list = list.filter(r => r.status.toLowerCase() === statusFilter.value)
  if (search.value.trim()) {
    const q = search.value.toLowerCase()
    list = list.filter(r => r.patientId.toLowerCase().includes(q) || (r.patientName || '').toLowerCase().includes(q) || String(r.roomNumber || '').includes(q))
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
      <div class="kpi"><span class="label">Të monitoruar</span><span class="value">{{ counts.all }}</span></div>
      <div class="kpi"><span class="label">Normal</span><span class="value" style="color:#16a34a">{{ counts.normal }}</span></div>
      <div class="kpi"><span class="label">Paralajmërim</span><span class="value" style="color:#d97706">{{ counts.warning }}</span></div>
      <div class="kpi"><span class="label">Kritik</span><span class="value" style="color:#dc2626">{{ counts.critical }}</span></div>
    </div>

    <Panel>
      <div class="controls" style="margin-bottom:14px;">
        <input class="search" type="search" v-model="search" placeholder="Kërko pacient ose dhomë…" />
        <button v-for="s in statusFilters" :key="s.key" class="chip"
                :class="{ active: statusFilter === s.key }" @click="statusFilter = s.key">
          {{ s.label }}
        </button>
      </div>
      <div class="table-wrap">
        <table>
          <thead>
            <tr>
              <th class="sortable" @click="sortBy('patientId')">ID Pacienti</th>
              <th class="sortable" @click="sortBy('heartRate')">Pulsi</th>
              <th class="sortable" @click="sortBy('temperature')">Temp</th>
              <th class="sortable" @click="sortBy('spo2')">SpO₂</th>
              <th>Tensioni i Gjakut</th>
              <th class="sortable" @click="sortBy('respiratoryRate')">Frymëmarrja</th>
              <th class="sortable" @click="sortBy('recordedAt')">Përditësimi i Fundit</th>
              <th class="sortable" @click="sortBy('status')">Statusi</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="r in filtered" :key="r.patientId" class="clickable" @click="router.push(`/patients/${r.patientId}`)">
              <td><strong>{{ r.patientId }}</strong><div class="muted" style="font-size:12px">{{ r.patientName }} · Dh {{ r.roomNumber }}</div></td>
              <td class="mono">{{ r.heartRate }} <span class="muted">bpm</span></td>
              <td class="mono">{{ r.temperature?.toFixed(1) }}°C</td>
              <td class="mono">{{ r.spo2 }}%</td>
              <td class="mono">{{ r.systolicBp }}/{{ r.diastolicBp }}</td>
              <td class="mono">{{ r.respiratoryRate }}</td>
              <td class="muted">{{ timeAgo(r.recordedAt) }}</td>
              <td><span class="badge" :class="statusClass(r.status)">{{ translateStatus(r.status) }}</span></td>
            </tr>
            <tr v-if="!filtered.length"><td colspan="8" class="empty">Duke pritur të dhëna në kohë reale…</td></tr>
          </tbody>
        </table>
      </div>
    </Panel>
  </div>
</template>
