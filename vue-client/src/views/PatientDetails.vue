<script setup>
import { ref, computed, onMounted, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { api } from '../services/api'
import Panel from '../components/Panel.vue'
import { clockTime, riskClass, riskColor, pct, timeAgo } from '../utils/format'

const route = useRoute()
const router = useRouter()
const patients = ref([])
const detail = ref(null)
const profile = ref(null)
const ml = ref(null)
const mlHistory = ref([])
const range = ref('day')

const currentId = computed(() => route.params.id || patients.value[0]?.patientId)

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
  } catch (e) { /* keep last */ }
}

onMounted(loadRoster)
watch([currentId, range], loadPatient, { immediate: true })

const labels = computed(() => (detail.value?.timestamps ?? []).map(clockTime))
const lineOpts = (color, categories) => ({
  chart: { type: 'line', height: 220, toolbar: { show: false }, fontFamily: 'inherit', animations: { enabled: false } },
  stroke: { curve: 'smooth', width: 2 }, colors: [color], dataLabels: { enabled: false },
  grid: { borderColor: '#eef2f7' }, xaxis: { categories, labels: { show: false } }
})
const hr = computed(() => ({ options: lineOpts('#0ea5e9', labels.value), series: [{ name: 'HR', data: detail.value?.heartRates ?? [] }] }))
const temp = computed(() => ({ options: lineOpts('#0d9488', labels.value), series: [{ name: 'Temp', data: detail.value?.temperatures ?? [] }] }))
const spo2 = computed(() => ({ options: lineOpts('#2563eb', labels.value), series: [{ name: 'SpO2', data: detail.value?.spo2s ?? [] }] }))
const bp = computed(() => ({
  options: { ...lineOpts('#dc2626', labels.value), colors: ['#dc2626', '#f59e0b'] },
  series: [{ name: 'Systolic', data: detail.value?.systolics ?? [] }, { name: 'Diastolic', data: detail.value?.diastolics ?? [] }]
}))

const mlHistChart = computed(() => ({
  options: {
    chart: { type: 'area', height: 220, toolbar: { show: false }, fontFamily: 'inherit' },
    stroke: { curve: 'smooth', width: 2 }, colors: ['#7c3aed'], dataLabels: { enabled: false },
    fill: { type: 'gradient', gradient: { opacityFrom: 0.4, opacityTo: 0.05 } },
    yaxis: { min: 0, max: 1, labels: { formatter: v => Math.round(v * 100) + '%' } },
    xaxis: { categories: mlHistory.value.map(p => clockTime(p.timestamp)), labels: { show: false } }
  },
  series: [{ name: 'Heart-attack risk', data: mlHistory.value.map(p => p.riskProbability) }]
}))

const ruleFactors = computed(() => (detail.value?.riskFactors ?? '').split(';').map(s => s.trim()).filter(Boolean))
</script>

<template>
  <div class="grid" style="gap:18px;">
    <div class="controls">
      <select :value="currentId" @change="router.push(`/patients/${$event.target.value}`)">
        <option v-for="p in patients" :key="p.patientId" :value="p.patientId">{{ p.patientId }} — {{ p.patientName }} (Rm {{ p.roomNumber }})</option>
      </select>
      <span style="flex:1"></span>
      <button v-for="r in [['hour','Last Hour'],['day','Last 24h'],['week','Last 7 Days']]" :key="r[0]"
              class="chip" :class="{ active: range === r[0] }" @click="range = r[0]">{{ r[1] }}</button>
    </div>

    <div v-if="detail" class="grid cols-3">
      <Panel title="Patient Information">
        <div class="grid" style="gap:8px;font-size:14px;">
          <div class="kpi-row"><span class="muted">Name</span><strong>{{ detail.patientName }}</strong></div>
          <div class="kpi-row"><span class="muted">Patient ID</span><strong>{{ detail.patientId }}</strong></div>
          <div class="kpi-row"><span class="muted">Room</span><strong>{{ detail.roomNumber }}</strong></div>
          <div class="kpi-row"><span class="muted">Age</span><strong>{{ detail.age }}</strong></div>
          <template v-if="profile">
            <div class="kpi-row"><span class="muted">Sex</span><strong>{{ profile.sex }}</strong></div>
            <div class="kpi-row"><span class="muted">Cholesterol</span><strong>{{ profile.cholesterol }} mg/dL</strong></div>
            <div class="kpi-row"><span class="muted">BMI</span><strong>{{ profile.bmi?.toFixed(1) }}</strong></div>
            <div class="kpi-row"><span class="muted">Smoker</span><strong>{{ profile.smoking ? 'Yes' : 'No' }}</strong></div>
            <div class="kpi-row"><span class="muted">Diabetes</span><strong>{{ profile.diabetes ? 'Yes' : 'No' }}</strong></div>
            <div class="kpi-row"><span class="muted">Family History</span><strong>{{ profile.familyHistory ? 'Yes' : 'No' }}</strong></div>
          </template>
        </div>
      </Panel>

      <Panel title="AI Heart-Attack Risk" hint="ML.NET" style="grid-column: span 2;">
        <div class="grid cols-2" style="gap:18px;">
          <div>
            <div style="display:flex;align-items:center;gap:18px;">
              <div style="text-align:center;">
                <div style="font-size:46px;font-weight:800;" :style="{ color: riskColor(ml?.riskCategory) }">
                  {{ ml ? pct(ml.riskProbability) : '—' }}
                </div>
                <span class="badge" :class="riskClass(ml?.riskCategory)">{{ ml?.riskCategory ?? 'N/A' }}</span>
              </div>
              <div style="flex:1">
                <div class="muted" style="font-size:13px;margin-bottom:8px;">Rule-based early-warning score</div>
                <div style="font-size:30px;font-weight:700;">{{ detail.currentRiskScore }}<span class="muted" style="font-size:15px;">/100</span></div>
                <div class="risk-meter" style="margin-top:8px;">
                  <span :style="{ width: detail.currentRiskScore + '%', background: riskColor(detail.currentRiskCategory) }"></span>
                </div>
                <span class="badge" :class="riskClass(detail.currentRiskCategory)" style="margin-top:8px;">{{ detail.currentRiskCategory }}</span>
              </div>
            </div>
            <div style="margin-top:16px;">
              <div class="muted" style="font-size:12px;text-transform:uppercase;letter-spacing:.4px;margin-bottom:8px;">Top Risk Factors</div>
              <div style="display:flex;flex-wrap:wrap;gap:8px;">
                <span v-for="(f,i) in (ml?.topFactors ?? ruleFactors)" :key="i" class="badge plain">{{ f }}</span>
              </div>
            </div>
          </div>
          <div>
            <div class="muted" style="font-size:12px;text-transform:uppercase;letter-spacing:.4px;margin-bottom:6px;">Prediction History</div>
            <apexchart v-if="mlHistory.length" type="area" height="220" :options="mlHistChart.options" :series="mlHistChart.series" />
            <div v-else class="empty">No prediction history yet…</div>
          </div>
        </div>
      </Panel>
    </div>

    <div v-if="detail" class="grid cols-2">
      <Panel :title="`Heart Rate · ${detail.heartRateStat.average} avg`" :hint="`min ${detail.heartRateStat.min} / max ${detail.heartRateStat.max}`">
        <apexchart type="line" height="220" :options="hr.options" :series="hr.series" />
      </Panel>
      <Panel :title="`Temperature · ${detail.temperatureStat.average}°C avg`" :hint="`min ${detail.temperatureStat.min} / max ${detail.temperatureStat.max}`">
        <apexchart type="line" height="220" :options="temp.options" :series="temp.series" />
      </Panel>
      <Panel :title="`SpO₂ · ${detail.spo2Stat.average}% avg`" :hint="`min ${detail.spo2Stat.min} / max ${detail.spo2Stat.max}`">
        <apexchart type="line" height="220" :options="spo2.options" :series="spo2.series" />
      </Panel>
      <Panel :title="`Blood Pressure · ${detail.bloodPressureStat.average} avg systolic`" hint="systolic / diastolic">
        <apexchart type="line" height="220" :options="bp.options" :series="bp.series" />
      </Panel>
    </div>

    <div v-else class="empty">Loading patient…</div>
  </div>
</template>
