<script setup>
import { ref, computed, onMounted, watch } from 'vue'
import { api } from '../services/api'
import { useRealtime } from '../stores/realtime'
import Panel from '../components/Panel.vue'
import KpiCard from '../components/KpiCard.vue'
import { riskClass, pct, timeAgo, clockTime } from '../utils/format'

const rt = useRealtime()
const factors = ref([])
const metrics = ref(null)
const selected = ref(null)
const history = ref([])

onMounted(async () => {
  try {
    const [preds, f, m] = await Promise.all([api.aiPredictions(), api.aiFactors().catch(() => []), api.aiMetrics().catch(() => null)])
    for (const p of preds) rt.ml[p.patientId] = p
    factors.value = f
    metrics.value = m
  } catch (e) {}
})

const predictions = computed(() => Object.values(rt.ml).sort((a, b) => b.riskProbability - a.riskProbability))
const dist = computed(() => {
  const d = { HIGH: 0, MEDIUM: 0, LOW: 0 }
  for (const p of predictions.value) d[p.riskCategory] = (d[p.riskCategory] ?? 0) + 1
  return d
})
const avgRisk = computed(() => predictions.value.length ? predictions.value.reduce((s, p) => s + p.riskProbability, 0) / predictions.value.length : 0)

watch(predictions, (list) => { if (!selected.value && list.length) selectPatient(list[0].patientId) })

async function selectPatient(id) {
  selected.value = id
  try { history.value = await api.aiHistory(id) } catch (e) { history.value = [] }
}

const donut = computed(() => ({
  options: {
    chart: { type: 'donut', height: 280, fontFamily: 'inherit' },
    labels: ['High Risk', 'Medium Risk', 'Low Risk'],
    colors: ['#dc2626', '#d97706', '#16a34a'],
    legend: { position: 'bottom' },
    plotOptions: { pie: { donut: { labels: { show: true, total: { show: true, label: 'Patients' } } } } }
  },
  series: [dist.value.HIGH, dist.value.MEDIUM, dist.value.LOW]
}))

const factorChart = computed(() => ({
  options: {
    chart: { type: 'bar', height: 300, toolbar: { show: false }, fontFamily: 'inherit' },
    plotOptions: { bar: { horizontal: true, borderRadius: 4, barHeight: '62%' } },
    colors: ['#7c3aed'], dataLabels: { enabled: false },
    xaxis: { categories: factors.value.map(f => f.factor) }
  },
  series: [{ name: 'Importance', data: factors.value.map(f => +(f.importance * 100).toFixed(2)) }]
}))

const histChart = computed(() => ({
  options: {
    chart: { type: 'area', height: 280, toolbar: { show: false }, fontFamily: 'inherit' },
    stroke: { curve: 'smooth', width: 2 }, colors: ['#7c3aed'], dataLabels: { enabled: false },
    fill: { type: 'gradient', gradient: { opacityFrom: 0.4, opacityTo: 0.05 } },
    yaxis: { min: 0, max: 1, labels: { formatter: v => Math.round(v * 100) + '%' } },
    xaxis: { categories: history.value.map(p => clockTime(p.timestamp)), labels: { show: false } }
  },
  series: [{ name: 'Risk', data: history.value.map(p => p.riskProbability) }]
}))

const bestMetric = computed(() => {
  if (!metrics.value?.metrics) return null
  return metrics.value.metrics.find(m => m.model === metrics.value.chosenModel) ?? metrics.value.metrics[0]
})
</script>

<template>
  <div class="grid" style="gap:18px;">
    <div class="grid kpis">
      <KpiCard label="High Risk" :value="dist.HIGH" icon="🔴" tone="red" meta="≥ 70% probability" />
      <KpiCard label="Medium Risk" :value="dist.MEDIUM" icon="🟠" tone="amber" meta="30–70% probability" />
      <KpiCard label="Low Risk" :value="dist.LOW" icon="🟢" tone="green" meta="< 30% probability" />
      <KpiCard label="Avg Risk" :value="pct(avgRisk)" icon="🤖" tone="blue" meta="Across all patients" />
      <KpiCard v-if="bestMetric" label="Model" :value="metrics.chosenModel" icon="🧠" tone="teal"
               :meta="`F1 ${bestMetric.f1?.toFixed(3)} · AUC ${bestMetric.auc?.toFixed(3)}`" />
    </div>

    <div class="grid cols-2">
      <Panel title="Risk Distribution">
        <apexchart type="donut" height="280" :options="donut.options" :series="donut.series" />
      </Panel>
      <Panel title="Top Risk Factors" hint="permutation importance (%)">
        <apexchart v-if="factors.length" type="bar" height="300" :options="factorChart.options" :series="factorChart.series" />
        <div v-else class="empty">No factor data…</div>
      </Panel>
    </div>

    <Panel title="High-Risk Patients" hint="click a row for prediction history">
      <div class="table-wrap">
        <table>
          <thead><tr><th>Patient</th><th>Risk Score</th><th>Category</th><th>Heart Rate</th><th>Blood Pressure</th><th>Top Factor</th><th>Last Updated</th></tr></thead>
          <tbody>
            <tr v-for="p in predictions" :key="p.patientId" class="clickable"
                :class="{ active: selected === p.patientId }" @click="selectPatient(p.patientId)">
              <td><strong>{{ p.patientId }}</strong> <span class="muted">{{ p.patientName }}</span></td>
              <td>
                <div style="display:flex;align-items:center;gap:8px;">
                  <span class="mono spark">{{ pct(p.riskProbability) }}</span>
                  <div class="risk-meter" style="width:80px;"><span :style="{ width: pct(p.riskProbability), background: p.riskCategory === 'HIGH' ? '#dc2626' : p.riskCategory === 'MEDIUM' ? '#d97706' : '#16a34a' }"></span></div>
                </div>
              </td>
              <td><span class="badge" :class="riskClass(p.riskCategory)">{{ p.riskCategory }}</span></td>
              <td class="mono">{{ p.heartRate }} bpm</td>
              <td class="mono">{{ p.systolicBp }}/{{ p.diastolicBp }}</td>
              <td class="muted">{{ p.topFactors?.[0] ?? '—' }}</td>
              <td class="muted">{{ timeAgo(p.recordedAt) }}</td>
            </tr>
            <tr v-if="!predictions.length"><td colspan="7" class="empty">Waiting for predictions…</td></tr>
          </tbody>
        </table>
      </div>
    </Panel>

    <Panel :title="`Prediction History · ${selected ?? ''}`" hint="heart-attack risk over time">
      <apexchart v-if="history.length" type="area" height="280" :options="histChart.options" :series="histChart.series" />
      <div v-else class="empty">Select a patient to view history…</div>
    </Panel>
  </div>
</template>
