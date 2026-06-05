<script setup>
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { api } from '../services/api'
import Panel from '../components/Panel.vue'
import { riskClass } from '../utils/format'

const data = ref(null)
let timer = null
async function load() { try { data.value = await api.analytics() } catch (e) {} }
onMounted(() => { load(); timer = setInterval(load, 7000) })
onUnmounted(() => clearInterval(timer))

const lineOpts = (color, categories) => ({
  chart: { type: 'line', height: 260, toolbar: { show: false }, fontFamily: 'inherit', animations: { enabled: false } },
  stroke: { curve: 'smooth', width: 2 }, colors: [color], dataLabels: { enabled: false },
  grid: { borderColor: '#eef2f7' }, xaxis: { categories, labels: { rotate: -45, style: { fontSize: '10px' } } }
})
const cats = computed(() => (data.value?.avgHeartRatePerWindow ?? []).map(p => p.label))
const hr = computed(() => ({ options: lineOpts('#0ea5e9', cats.value), series: [{ name: 'Avg HR', data: (data.value?.avgHeartRatePerWindow ?? []).map(p => p.value) }] }))
const temp = computed(() => ({ options: lineOpts('#0d9488', (data.value?.avgTemperaturePerWindow ?? []).map(p => p.label)), series: [{ name: 'Avg Temp', data: (data.value?.avgTemperaturePerWindow ?? []).map(p => p.value) }] }))
const spo2 = computed(() => ({ options: lineOpts('#2563eb', (data.value?.avgSpo2PerWindow ?? []).map(p => p.label)), series: [{ name: 'Avg SpO2', data: (data.value?.avgSpo2PerWindow ?? []).map(p => p.value) }] }))

const freq = computed(() => ({
  options: {
    chart: { type: 'bar', height: 300, toolbar: { show: false }, fontFamily: 'inherit' },
    plotOptions: { bar: { horizontal: true, borderRadius: 4, barHeight: '60%' } },
    colors: ['#dc2626'], dataLabels: { enabled: true },
    xaxis: { categories: (data.value?.alertFrequencyByType ?? []).map(p => p.label) }
  },
  series: [{ name: 'Alerts', data: (data.value?.alertFrequencyByType ?? []).map(p => p.value) }]
}))
</script>

<template>
  <div v-if="data" class="grid" style="gap:18px;">
    <Panel title="Average Heart Rate per 5-min Window" :hint="`${data.totalWindows} windows computed`">
      <apexchart type="line" height="260" :options="hr.options" :series="hr.series" />
    </Panel>

    <div class="grid cols-2">
      <Panel title="Average Temperature per Window">
        <apexchart type="line" height="260" :options="temp.options" :series="temp.series" />
      </Panel>
      <Panel title="Average SpO₂ per Window">
        <apexchart type="line" height="260" :options="spo2.options" :series="spo2.series" />
      </Panel>
    </div>

    <div class="grid cols-2">
      <Panel title="Alert Frequency by Type">
        <apexchart v-if="data.alertFrequencyByType.length" type="bar" height="300" :options="freq.options" :series="freq.series" />
        <div v-else class="empty">No alerts yet…</div>
      </Panel>
      <Panel title="Highest-Risk Patients" hint="rule-based score">
        <table>
          <thead><tr><th>Patient</th><th>Room</th><th>Score</th><th>Category</th></tr></thead>
          <tbody>
            <tr v-for="r in data.highestRiskPatients" :key="r.patientId">
              <td><strong>{{ r.patientId }}</strong></td>
              <td class="mono">{{ r.roomNumber }}</td>
              <td class="mono spark">{{ r.score }}</td>
              <td><span class="badge" :class="riskClass(r.category)">{{ r.category }}</span></td>
            </tr>
            <tr v-if="!data.highestRiskPatients.length"><td colspan="4" class="empty">No data yet…</td></tr>
          </tbody>
        </table>
      </Panel>
    </div>
  </div>
  <div v-else class="empty">Loading analytics…</div>
</template>
