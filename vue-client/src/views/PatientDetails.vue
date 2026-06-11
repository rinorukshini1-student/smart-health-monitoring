<script setup>
import { ref, computed, onMounted, onUnmounted, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { api } from '../services/api'
import { ensureHubStarted, subscribeMlPrediction } from '../services/realtimeHub'
import Panel from '../components/Panel.vue'
import { clockTime, riskClass, riskColor, pct } from '../utils/format'
import { translateRiskCategory, translateRiskCategoryLong, translateModelFactor } from '../utils/i18n'
import { usePatientRisk, buildHeartRecord } from '../composables/useAi'

const route = useRoute()
const router = useRouter()
const patients = ref([])
const detail = ref(null)
const profile = ref(null)
const ml = ref(null)
const mlHistory = ref([])
const range = ref('day')

const ranges = [
  ['hour', 'Ora e Fundit'],
  ['day', '24 Orët e Fundit'],
  ['week', '7 Ditët e Fundit']
]

const currentId = computed(() => route.params.id || patients.value[0]?.patientId)

const { result: aiResult, loading: aiLoading, error: aiError, predict } = usePatientRisk()

async function loadRoster() {
  patients.value = await api.patients()
  if (!route.params.id && patients.value.length) router.replace(`/patients/${patients.value[0].patientId}`)
}

async function loadPatient() {
  const id = currentId.value
  if (!id) return
  try {
    const [d, p, m, h] = await Promise.all([
      api.patient(id, range.value),
      api.patientProfile(id),
      api.aiPatient(id).catch(() => null),
      api.aiHistory(id).catch(() => [])
    ])
    detail.value = d; profile.value = p; ml.value = m; mlHistory.value = h

    // On-demand AI prediction (probability + risk level + recommendation + factors).
    if (p) {
      await predict(buildHeartRecord(p, null, d))
    }
  } catch (e) { /* keep last */ }
}

// Probability shown in the AI card: prefer the on-demand result, fall back to the worker prediction.
const aiProbability = computed(() => aiResult.value?.probability ?? ml.value?.riskProbability ?? null)
const aiRiskLevel = computed(() => aiResult.value?.riskLevel ?? ml.value?.riskCategory ?? null)
const aiFactors = computed(() => {
  const factors = aiResult.value?.mainFactors?.length ? aiResult.value.mainFactors : (ml.value?.topFactors ?? [])
  return factors.length ? factors : ruleFactors.value
})

function onMlPrediction(p) {
  if (p.patientId === currentId.value) ml.value = p
}

let unsubMl = null

onMounted(async () => {
  loadRoster()
  await ensureHubStarted()
  unsubMl = subscribeMlPrediction(onMlPrediction)
})
onUnmounted(() => unsubMl?.())
watch([currentId, range], loadPatient, { immediate: true })

const labels = computed(() => (detail.value?.timestamps ?? []).map(clockTime))
const lineOpts = (color, categories) => ({
  chart: { type: 'line', height: 220, toolbar: { show: false }, fontFamily: 'inherit', animations: { enabled: false } },
  stroke: { curve: 'smooth', width: 2 }, colors: [color], dataLabels: { enabled: false },
  grid: { borderColor: '#eef2f7' }, xaxis: { categories, labels: { show: false } }
})
const hr = computed(() => ({ options: lineOpts('#0ea5e9', labels.value), series: [{ name: 'Pulsi', data: detail.value?.heartRates ?? [] }] }))
const temp = computed(() => ({ options: lineOpts('#0d9488', labels.value), series: [{ name: 'Temperatura', data: detail.value?.temperatures ?? [] }] }))
const spo2 = computed(() => ({ options: lineOpts('#2563eb', labels.value), series: [{ name: 'SpO₂', data: detail.value?.spo2s ?? [] }] }))
const bp = computed(() => ({
  options: { ...lineOpts('#dc2626', labels.value), colors: ['#dc2626', '#f59e0b'] },
  series: [{ name: 'Sistolik', data: detail.value?.systolics ?? [] }, { name: 'Diastolik', data: detail.value?.diastolics ?? [] }]
}))

const mlHistChart = computed(() => ({
  options: {
    chart: { type: 'area', height: 220, toolbar: { show: false }, fontFamily: 'inherit' },
    stroke: { curve: 'smooth', width: 2 }, colors: ['#7c3aed'], dataLabels: { enabled: false },
    fill: { type: 'gradient', gradient: { opacityFrom: 0.4, opacityTo: 0.05 } },
    yaxis: { min: 0, max: 1, labels: { formatter: v => Math.round(v * 100) + '%' } },
    xaxis: { categories: mlHistory.value.map(p => clockTime(p.timestamp)), labels: { show: false } }
  },
  series: [{ name: 'Rreziku i infarktit', data: mlHistory.value.map(p => p.riskProbability) }]
}))

const ruleFactors = computed(() => (detail.value?.riskFactors ?? '').split(';').map(s => s.trim()).filter(Boolean))
</script>

<template>
  <div class="grid" style="gap:18px;">
    <div class="controls">
      <select :value="currentId" @change="router.push(`/patients/${$event.target.value}`)">
        <option v-for="p in patients" :key="p.patientId" :value="p.patientId">{{ p.patientId }} — {{ p.patientName }} (Dh {{ p.roomNumber }})</option>
      </select>
      <span style="flex:1"></span>
      <button v-for="r in ranges" :key="r[0]"
              class="chip" :class="{ active: range === r[0] }" @click="range = r[0]">{{ r[1] }}</button>
    </div>

    <div v-if="detail" class="grid cols-3">
      <Panel title="Informacioni i Pacientit">
        <div class="grid" style="gap:8px;font-size:14px;">
          <div class="kpi-row"><span class="muted">Emri</span><strong>{{ detail.patientName }}</strong></div>
          <div class="kpi-row"><span class="muted">ID Pacienti</span><strong>{{ detail.patientId }}</strong></div>
          <div class="kpi-row"><span class="muted">Dhoma</span><strong>{{ detail.roomNumber }}</strong></div>
          <div class="kpi-row"><span class="muted">Mosha</span><strong>{{ detail.age }}</strong></div>
          <template v-if="profile">
            <div class="kpi-row"><span class="muted">Gjinia</span><strong>{{ profile.sex }}</strong></div>
            <div class="kpi-row"><span class="muted">Kolesteroli</span><strong>{{ profile.cholesterol }} mg/dL</strong></div>
            <div class="kpi-row"><span class="muted">BMI</span><strong>{{ profile.bmi?.toFixed(1) }}</strong></div>
            <div class="kpi-row"><span class="muted">Duhanpirës</span><strong>{{ profile.smoking ? 'Po' : 'Jo' }}</strong></div>
            <div class="kpi-row"><span class="muted">Diabeti</span><strong>{{ profile.diabetes ? 'Po' : 'Jo' }}</strong></div>
            <div class="kpi-row"><span class="muted">Historia Familjare</span><strong>{{ profile.familyHistory ? 'Po' : 'Jo' }}</strong></div>
          </template>
        </div>
      </Panel>

      <Panel title="Rreziku AI i Infarktit" hint="ML.NET" style="grid-column: span 2;">
        <div class="grid cols-2" style="gap:18px;">
          <div>
            <div v-if="aiLoading" class="empty">Duke llogaritur vlerësimin AI…</div>
            <div v-else-if="aiError" class="ai-error">{{ aiError }}</div>

            <div style="display:flex;align-items:center;gap:18px;">
              <div style="text-align:center;">
                <div style="font-size:46px;font-weight:800;" :style="{ color: riskColor(aiRiskLevel) }">
                  {{ aiProbability != null ? pct(aiProbability) : '—' }}
                </div>
                <span class="badge" :class="riskClass(aiRiskLevel)">{{ aiRiskLevel ? translateRiskCategoryLong(aiRiskLevel) : 'N/A' }}</span>
              </div>
              <div style="flex:1">
                <div class="muted" style="font-size:13px;margin-bottom:8px;">Rezultati paralajmërues i bazuar në rregulla</div>
                <div style="font-size:30px;font-weight:700;">{{ detail.currentRiskScore }}<span class="muted" style="font-size:15px;">/100</span></div>
                <div class="risk-meter" style="margin-top:8px;">
                  <span :style="{ width: detail.currentRiskScore + '%', background: riskColor(detail.currentRiskCategory) }"></span>
                </div>
                <span class="badge" :class="riskClass(detail.currentRiskCategory)" style="margin-top:8px;">{{ translateRiskCategoryLong(detail.currentRiskCategory) }}</span>
              </div>
            </div>

            <div v-if="aiResult?.recommendation" class="ai-reco" :class="riskClass(aiRiskLevel)">
              {{ aiResult.recommendation }}
            </div>

            <div style="margin-top:16px;">
              <div class="muted" style="font-size:12px;text-transform:uppercase;letter-spacing:.4px;margin-bottom:8px;">Faktorët Kryesorë të Rrezikut</div>
              <div style="display:flex;flex-wrap:wrap;gap:8px;">
                <span v-for="(f,i) in aiFactors" :key="i" class="badge plain">{{ translateModelFactor(f) }}</span>
              </div>
            </div>

            <p class="ai-disclaimer">Ky vlerësim është gjeneruar nga modeli AI dhe shërben vetëm si ndihmë analitike, jo si diagnozë mjekësore.</p>
          </div>
          <div>
            <div class="muted" style="font-size:12px;text-transform:uppercase;letter-spacing:.4px;margin-bottom:6px;">Historia e Parashikimeve</div>
            <apexchart v-if="mlHistory.length" type="area" height="220" :options="mlHistChart.options" :series="mlHistChart.series" />
            <div v-else class="empty">Ende pa histori parashikimesh…</div>
          </div>
        </div>
      </Panel>
    </div>

    <div v-if="detail" class="grid cols-2">
      <Panel :title="`Pulsi · ${detail.heartRateStat.average} mesatar`" :hint="`min ${detail.heartRateStat.min} / max ${detail.heartRateStat.max}`">
        <apexchart type="line" height="220" :options="hr.options" :series="hr.series" />
      </Panel>
      <Panel :title="`Temperatura · ${detail.temperatureStat.average}°C mesatare`" :hint="`min ${detail.temperatureStat.min} / max ${detail.temperatureStat.max}`">
        <apexchart type="line" height="220" :options="temp.options" :series="temp.series" />
      </Panel>
      <Panel :title="`SpO₂ · ${detail.spo2Stat.average}% mesatar`" :hint="`min ${detail.spo2Stat.min} / max ${detail.spo2Stat.max}`">
        <apexchart type="line" height="220" :options="spo2.options" :series="spo2.series" />
      </Panel>
      <Panel :title="`Tensioni i Gjakut · ${detail.bloodPressureStat.average} sistolik mesatar`" hint="sistolik / diastolik">
        <apexchart type="line" height="220" :options="bp.options" :series="bp.series" />
      </Panel>
    </div>

    <div v-else class="empty">Duke ngarkuar pacientin…</div>
  </div>
</template>
