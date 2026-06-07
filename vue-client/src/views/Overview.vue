<script setup>
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { api } from '../services/api'
import KpiCard from '../components/KpiCard.vue'
import Panel from '../components/Panel.vue'
import { timeAgo, severityClass } from '../utils/format'
import { translateSeverity } from '../utils/i18n'

const data = ref(null)
let timer = null

async function load() {
  try { data.value = await api.overview() } catch (e) { /* keep last */ }
}
onMounted(() => { load(); timer = setInterval(load, 5000) })
onUnmounted(() => clearInterval(timer))

const baseBar = (categories, color) => ({
  chart: { type: 'bar', height: 240, toolbar: { show: false }, fontFamily: 'inherit' },
  plotOptions: { bar: { borderRadius: 4, columnWidth: '55%' } },
  dataLabels: { enabled: false },
  colors: [color],
  grid: { borderColor: '#eef2f7' },
  xaxis: { categories, labels: { rotate: -45, style: { fontSize: '10px' } } }
})
const baseLine = (categories, color) => ({
  chart: { type: 'area', height: 240, toolbar: { show: false }, fontFamily: 'inherit' },
  stroke: { curve: 'smooth', width: 2 },
  fill: { type: 'gradient', gradient: { shadeIntensity: 1, opacityFrom: 0.4, opacityTo: 0.05 } },
  dataLabels: { enabled: false },
  colors: [color],
  grid: { borderColor: '#eef2f7' },
  xaxis: { categories, labels: { rotate: -45, style: { fontSize: '10px' } } }
})

const msgChart = computed(() => ({
  options: baseBar((data.value?.messagesPerMinute ?? []).map(p => p.label), '#2563eb'),
  series: [{ name: 'Mesazhe', data: (data.value?.messagesPerMinute ?? []).map(p => p.value) }]
}))
const alertChart = computed(() => ({
  options: baseBar((data.value?.alertsPerHour ?? []).map(p => p.label), '#dc2626'),
  series: [{ name: 'Alarme', data: (data.value?.alertsPerHour ?? []).map(p => p.value) }]
}))
const hrChart = computed(() => ({
  options: baseLine((data.value?.heartRateTrend ?? []).map(p => p.label), '#0ea5e9'),
  series: [{ name: 'Pulsi mesatar', data: (data.value?.heartRateTrend ?? []).map(p => p.value) }]
}))
const tempChart = computed(() => ({
  options: baseLine((data.value?.temperatureTrend ?? []).map(p => p.label), '#0d9488'),
  series: [{ name: 'Temp mesatare', data: (data.value?.temperatureTrend ?? []).map(p => p.value) }]
}))
</script>

<template>
  <div v-if="data" class="grid" style="gap:18px;">
    <div class="grid kpis">
      <KpiCard label="Pacientë Totalë" :value="data.totalPatients" icon="la-users" tone="blue" meta="Të regjistruar në listë" />
      <KpiCard label="Pacientë Aktivë" :value="data.activePatients" icon="la-heartbeat" tone="green" meta="Raportuan në 2 min të fundit" />
      <KpiCard label="Alarme Kritike" :value="data.criticalAlerts" icon="la-exclamation-triangle" tone="red" meta="" />
      <KpiCard label="Pulsi Mesatar" :value="data.averageHeartRate + ' bpm'" icon="la-heart" tone="sky" meta="Për pacientët aktivë" />
      <KpiCard label="Temperatura Mesatare" :value="data.averageTemperature + '°C'" icon="la-thermometer-half" tone="teal" meta="Për pacientët aktivë" />
      <KpiCard label="SpO₂ Mesatar" :value="data.averageSpo2 + '%'" icon="la-wind" tone="blue" meta="Për pacientët aktivë" />
    </div>

    <div class="grid cols-2">
      <Panel title="Mesazhe të Përpunuara / Minutë" hint="">
        <apexchart type="bar" height="240" :options="msgChart.options" :series="msgChart.series" />
      </Panel>
      <Panel title="Alarme / Orë" hint="">
        <apexchart type="bar" height="240" :options="alertChart.options" :series="alertChart.series" />
      </Panel>
      <Panel title="Trendi i Pulsit Mesatar">
        <apexchart type="area" height="240" :options="hrChart.options" :series="hrChart.series" />
      </Panel>
      <Panel title="Trendi i Temperaturës Mesatare">
        <apexchart type="area" height="240" :options="tempChart.options" :series="tempChart.series" />
      </Panel>
    </div>

    <div class="grid cols-2">
      <Panel title="Leximet e Fundit të Sensorëve">
        <div v-if="!data.recentReadings.length" class="empty">Ende pa lexime…</div>
        <table v-else>
          <thead><tr><th>Pacienti</th><th>Dhoma</th><th>Leximi</th><th>Koha</th></tr></thead>
          <tbody>
            <tr v-for="(e, i) in data.recentReadings" :key="i">
              <td>{{ e.patientId }}</td>
              <td class="mono">{{ e.roomNumber }}</td>
              <td class="muted">{{ e.text }}</td>
              <td class="muted">{{ timeAgo(e.timestamp) }}</td>
            </tr>
          </tbody>
        </table>
      </Panel>
      <Panel title="Alarmet e Fundit">
        <div v-if="!data.recentAlerts.length" class="empty">Ende pa alarme…</div>
        <table v-else>
          <thead><tr><th>Pacienti</th><th>Severiteti</th><th>Mesazhi</th><th>Koha</th></tr></thead>
          <tbody>
            <tr v-for="(e, i) in data.recentAlerts" :key="i">
              <td>{{ e.patientId }}</td>
              <td><span class="badge" :class="severityClass(e.severity)">{{ translateSeverity(e.severity) }}</span></td>
              <td class="muted">{{ e.text }}</td>
              <td class="muted">{{ timeAgo(e.timestamp) }}</td>
            </tr>
          </tbody>
        </table>
      </Panel>
    </div>
  </div>
  <div v-else class="empty">Duke ngarkuar përmbledhjen…</div>
</template>
