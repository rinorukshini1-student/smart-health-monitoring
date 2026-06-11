<script setup>
import { ref, computed, onMounted, onUnmounted, watch } from 'vue'
import { api } from '../services/api'
import { ensureHubStarted, subscribeMlPrediction } from '../services/realtimeHub'
import { useAiModel } from '../composables/useAi'
import Panel from '../components/Panel.vue'
import KpiCard from '../components/KpiCard.vue'
import { riskClass, pct, timeAgo, clockTime } from '../utils/format'
import { translateRiskCategory, translateModelFactor } from '../utils/i18n'

const factors = ref([])
const metrics = ref(null)
const selected = ref(null)
const history = ref([])
// ML predictions live only on this page (not in the global realtime store).
const mlPredictions = ref({})

const { info: modelInfo, loading: modelLoading, error: modelError, load: loadModelInfo } = useAiModel()

function onMlPrediction(p) {
  mlPredictions.value = { ...mlPredictions.value, [p.patientId]: p }
}

let unsubMl = null

onMounted(async () => {
  loadModelInfo()
  await ensureHubStarted()
  unsubMl = subscribeMlPrediction(onMlPrediction)
  try {
    const [preds, f, m] = await Promise.all([api.aiPredictions(), api.aiFactors().catch(() => []), api.aiMetrics().catch(() => null)])
    const map = {}
    for (const p of preds) map[p.patientId] = p
    mlPredictions.value = map
    factors.value = f
    metrics.value = m
  } catch (e) {}
})

onUnmounted(() => {
  unsubMl?.()
})

// Metrics row for the model chosen during training (Accuracy/Precision/Recall/F1/AUC).
const chosenMetric = computed(() => {
  const mi = modelInfo.value
  if (!mi?.metrics?.length) return null
  return mi.metrics.find(m => m.model === mi.chosenModel) ?? mi.metrics[0]
})
const trainedAtLabel = computed(() => {
  const ts = modelInfo.value?.trainedAt
  return ts ? new Date(ts).toLocaleString('sq-AL') : '—'
})

const predictions = computed(() => Object.values(mlPredictions.value).sort((a, b) => b.riskProbability - a.riskProbability))
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
    labels: ['Rrezik i Lartë', 'Rrezik Mesatar', 'Rrezik i Ulët'],
    colors: ['#dc2626', '#d97706', '#16a34a'],
    legend: { position: 'bottom' },
    plotOptions: { pie: { donut: { labels: { show: true, total: { show: true, label: 'Pacientë' } } } } }
  },
  series: [dist.value.HIGH, dist.value.MEDIUM, dist.value.LOW]
}))

const factorChart = computed(() => ({
  options: {
    chart: { type: 'bar', height: 300, toolbar: { show: false }, fontFamily: 'inherit' },
    plotOptions: { bar: { horizontal: true, borderRadius: 4, barHeight: '62%' } },
    colors: ['#7c3aed'], dataLabels: { enabled: false },
    xaxis: { categories: factors.value.map(f => translateModelFactor(f.factor)) }
  },
  series: [{ name: 'Rëndësia', data: factors.value.map(f => +(f.importance * 100).toFixed(2)) }]
}))

const histChart = computed(() => ({
  options: {
    chart: { type: 'area', height: 280, toolbar: { show: false }, fontFamily: 'inherit' },
    stroke: { curve: 'smooth', width: 2 }, colors: ['#7c3aed'], dataLabels: { enabled: false },
    fill: { type: 'gradient', gradient: { opacityFrom: 0.4, opacityTo: 0.05 } },
    yaxis: { min: 0, max: 1, labels: { formatter: v => Math.round(v * 100) + '%' } },
    xaxis: { categories: history.value.map(p => clockTime(p.timestamp)), labels: { show: false } }
  },
  series: [{ name: 'Rreziku', data: history.value.map(p => p.riskProbability) }]
}))

const bestMetric = computed(() => {
  if (!metrics.value?.metrics) return null
  return metrics.value.metrics.find(m => m.model === metrics.value.chosenModel) ?? metrics.value.metrics[0]
})
</script>

<template>
  <div class="grid" style="gap:18px;">
    <div class="grid kpis">
      <KpiCard label="Rrezik i Lartë" :value="dist.HIGH" icon="la-exclamation-circle" tone="red" meta="probabilitet ≥ 70%" />
      <KpiCard label="Rrezik Mesatar" :value="dist.MEDIUM" icon="la-exclamation-triangle" tone="amber" meta="probabilitet 30–70%" />
      <KpiCard label="Rrezik i Ulët" :value="dist.LOW" icon="la-check-circle" tone="green" meta="probabilitet < 30%" />
      <KpiCard label="Rreziku Mesatar" :value="pct(avgRisk)" icon="la-robot" tone="blue" meta="Për të gjithë pacientët" />
      <KpiCard v-if="bestMetric" label="Modeli" :value="metrics.chosenModel" icon="la-brain" tone="teal"
               :meta="`F1 ${bestMetric.f1?.toFixed(3)} · AUC ${bestMetric.auc?.toFixed(3)}`" />
    </div>

    <div class="grid cols-2">
      <Panel title="Shpërndarja e Rrezikut">
        <apexchart type="donut" height="280" :options="donut.options" :series="donut.series" />
      </Panel>
      <Panel title="Faktorët Kryesorë të Rrezikut" hint="rëndësia e permutimit (%)">
        <apexchart v-if="factors.length" type="bar" height="300" :options="factorChart.options" :series="factorChart.series" />
        <div v-else class="empty">Pa të dhëna për faktorët…</div>
      </Panel>
    </div>

    <Panel title="Pacientët me Rrezik të Lartë" hint="kliko një rresht për historinë e parashikimit">
      <div class="table-wrap">
        <table>
          <thead><tr><th>Pacienti</th><th>Rezultati i Rrezikut</th><th>Kategoria</th><th>Pulsi</th><th>Tensioni i Gjakut</th><th>Faktori Kryesor</th><th>Përditësimi i Fundit</th></tr></thead>
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
              <td><span class="badge" :class="riskClass(p.riskCategory)">{{ translateRiskCategory(p.riskCategory) }}</span></td>
              <td class="mono">{{ p.heartRate }} bpm</td>
              <td class="mono">{{ p.systolicBp }}/{{ p.diastolicBp }}</td>
              <td class="muted">{{ p.topFactors?.[0] ?? '—' }}</td>
              <td class="muted">{{ timeAgo(p.recordedAt) }}</td>
            </tr>
            <tr v-if="!predictions.length"><td colspan="7" class="empty">Duke pritur parashikimet…</td></tr>
          </tbody>
        </table>
      </div>
    </Panel>

    <Panel :title="`Historia e Parashikimit · ${selected ?? ''}`" hint="rreziku i infarktit në kohë">
      <apexchart v-if="history.length" type="area" height="280" :options="histChart.options" :series="histChart.series" />
      <div v-else class="empty">Zgjidh një pacient për të parë historinë…</div>
    </Panel>

    <Panel title="Informacioni i Modelit AI" hint="ML.NET · model-info">
      <div v-if="modelLoading" class="empty">Duke ngarkuar informacionin e modelit…</div>
      <div v-else-if="modelError" class="ai-error">{{ modelError }}</div>
      <div v-else-if="modelInfo">
        <div class="model-info-grid">
          <div class="mi-cell"><span>Modeli i zgjedhur</span><strong>{{ modelInfo.chosenModel }}</strong></div>
          <div class="mi-cell"><span>Data e trajnimit</span><strong>{{ trainedAtLabel }}</strong></div>
          <div class="mi-cell"><span>Rreshta të dhënash</span><strong>{{ modelInfo.rows ?? '—' }}</strong></div>
          <div class="mi-cell"><span>Raste pozitive</span><strong>{{ modelInfo.positives ?? '—' }}</strong></div>
          <div class="mi-cell"><span>Saktësia (Accuracy)</span><strong>{{ chosenMetric ? chosenMetric.accuracy?.toFixed(3) : '—' }}</strong></div>
          <div class="mi-cell"><span>Preciziteti (Precision)</span><strong>{{ chosenMetric ? chosenMetric.precision?.toFixed(3) : '—' }}</strong></div>
          <div class="mi-cell"><span>Recall</span><strong>{{ chosenMetric ? chosenMetric.recall?.toFixed(3) : '—' }}</strong></div>
          <div class="mi-cell"><span>F1 Score</span><strong>{{ chosenMetric ? chosenMetric.f1?.toFixed(3) : '—' }}</strong></div>
          <div class="mi-cell"><span>AUC</span><strong>{{ chosenMetric ? chosenMetric.auc?.toFixed(3) : '—' }}</strong></div>
          <div class="mi-cell" v-if="modelInfo.classificationThreshold != null"><span>Pragu i klasifikimit</span><strong>{{ modelInfo.classificationThreshold }}</strong></div>
        </div>

        <div v-if="modelInfo.topFactors?.length" style="margin-top:16px;">
          <div class="muted" style="font-size:12px;text-transform:uppercase;letter-spacing:.4px;margin-bottom:8px;">Faktorët kryesorë</div>
          <div style="display:flex;flex-wrap:wrap;gap:8px;">
            <span v-for="(f,i) in modelInfo.topFactors.slice(0,5)" :key="i" class="badge plain">{{ translateModelFactor(f.factor) }}</span>
          </div>
        </div>
      </div>
      <div v-else class="empty">Informacioni i modelit nuk është i disponueshëm.</div>
    </Panel>

    <p class="ai-disclaimer">Ky vlerësim është gjeneruar nga modeli AI dhe shërben vetëm si ndihmë analitike, jo si diagnozë mjekësore.</p>
  </div>
</template>
