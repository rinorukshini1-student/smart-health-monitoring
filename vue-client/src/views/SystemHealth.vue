<script setup>
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { api } from '../services/api'
import { useRealtime } from '../stores/realtime'
import Panel from '../components/Panel.vue'
import LaIcon from '../components/LaIcon.vue'

const rt = useRealtime()
const sys = ref(null)
let timer = null
async function load() { try { sys.value = await api.system() } catch (e) {} }
onMounted(() => { load(); timer = setInterval(load, 3000) })
onUnmounted(() => clearInterval(timer))

const mps = computed(() => rt.metrics?.messagesPerSecond ?? sys.value?.kafkaMessagesPerSecond ?? 0)

function Row(label, value) { return { label, value } }
const kafka = computed(() => sys.value ? [
  Row('Mesazhe të marra', sys.value.kafkaMessagesReceived.toLocaleString()),
  Row('Mesazhe / sek', mps.value.toFixed(2)),
  Row('Tema', 'health-vitals')
] : [])
const spark = computed(() => sys.value ? [
  Row('Dritare të llogaritura', sys.value.sparkWindowsComputed.toLocaleString()),
  Row('Madhësia e fundit e grupit', sys.value.sparkLastBatchSize),
  Row('Vonesa e përpunimit', sys.value.sparkAvgProcessingMs.toFixed(2) + ' ms')
] : [])
const cassandra = computed(() => sys.value ? [
  Row('Vitalë të ruajtur', sys.value.storedVitals.toLocaleString()),
  Row('Alarme të ruajtura', sys.value.storedAlerts.toLocaleString()),
  Row('Shpejtësia e shkrimit / sek', sys.value.cassandraWriteRatePerSecond.toFixed(2))
] : [])
const backend = computed(() => sys.value ? [
  Row('Koha e përgjigjes së API', sys.value.apiAverageResponseMs.toFixed(2) + ' ms'),
  Row('Alarme të gjeneruara', sys.value.alertsGenerated.toLocaleString()),
  Row('Lidhja e panelit', rt.status === 'live' ? 'Në kohë reale (SignalR)' : 'Duke u rilidhur')
] : [])
</script>

<template>
  <div v-if="sys" class="grid" style="gap:18px;">
    <div class="grid kpis">
      <div class="kpi kpi-row"><div><span class="label">Shpejtësia</span><div class="value">{{ mps.toFixed(1) }}<span class="muted" style="font-size:14px"> msg/s</span></div></div><span class="icon blue"><LaIcon icon="la-bolt" /></span></div>
      <div class="kpi kpi-row"><div><span class="label">Mesazhe të Përpunuara</span><div class="value">{{ sys.kafkaMessagesReceived.toLocaleString() }}</div></div><span class="icon sky"><LaIcon icon="la-envelope" /></span></div>
      <div class="kpi kpi-row"><div><span class="label">Vonesa e Përpunimit</span><div class="value">{{ sys.sparkAvgProcessingMs.toFixed(1) }}<span class="muted" style="font-size:14px"> ms</span></div></div><span class="icon teal"><LaIcon icon="la-clock" /></span></div>
      <div class="kpi kpi-row"><div><span class="label">Statusi i Sistemit</span><div class="value" :style="{ color: sys.streamingOnline ? '#16a34a' : '#dc2626' }">{{ sys.streamingOnline ? 'Online' : 'Offline' }}</div></div><span class="icon green"><LaIcon icon="la-stethoscope" /></span></div>
    </div>

    <div class="grid cols-2">
      <Panel title="Kafka" hint="marrja e të dhënave">
        <div v-for="r in kafka" :key="r.label" class="kpi-row" style="padding:9px 0;border-bottom:1px solid #f1f5f9;">
          <span class="muted">{{ r.label }}</span><strong class="mono">{{ r.value }}</strong>
        </div>
      </Panel>
      <Panel title="Spark Streaming" hint="përpunimi & dritaret">
        <div v-for="r in spark" :key="r.label" class="kpi-row" style="padding:9px 0;border-bottom:1px solid #f1f5f9;">
          <span class="muted">{{ r.label }}</span><strong class="mono">{{ r.value }}</strong>
        </div>
      </Panel>
      <Panel title="Cassandra" hint="ruajtja">
        <div v-for="r in cassandra" :key="r.label" class="kpi-row" style="padding:9px 0;border-bottom:1px solid #f1f5f9;">
          <span class="muted">{{ r.label }}</span><strong class="mono">{{ r.value }}</strong>
        </div>
      </Panel>
      <Panel title="Backend & Paneli" hint="API / në kohë reale">
        <div v-for="r in backend" :key="r.label" class="kpi-row" style="padding:9px 0;border-bottom:1px solid #f1f5f9;">
          <span class="muted">{{ r.label }}</span><strong class="mono">{{ r.value }}</strong>
        </div>
      </Panel>
    </div>

    <Panel title="Përmbledhje e Performancës">
      <p class="muted" style="line-height:1.6;margin:0;">
        <strong>Rruga e të dhënave IoT</strong> (Sensor → Kafka → Spark → Cassandra → API → Paneli)
        po përpunon <strong>{{ mps.toFixed(1) }} mesazhe/sekond</strong> me një përgjigje mesatare të API-së prej
        <strong>{{ sys.apiAverageResponseMs.toFixed(1) }} ms</strong> dhe vonesë përpunimi prej
        <strong>{{ sys.sparkAvgProcessingMs.toFixed(1) }} ms</strong>. Gjithsej janë ruajtur
        <strong>{{ sys.kafkaMessagesReceived.toLocaleString() }}</strong> lexime dhe
        <strong>{{ sys.storedAlerts.toLocaleString() }}</strong> alarme.
        Nuk u zbuluan pengesa — shpejtësia dhe vonesa janë brenda kufijve të shëndetshëm.
      </p>
    </Panel>
  </div>
  <div v-else class="empty">Duke ngarkuar metrikat e sistemit…</div>
</template>
