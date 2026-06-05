<script setup>
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { api } from '../services/api'
import { useRealtime } from '../stores/realtime'
import Panel from '../components/Panel.vue'

const rt = useRealtime()
const sys = ref(null)
let timer = null
async function load() { try { sys.value = await api.system() } catch (e) {} }
onMounted(() => { load(); timer = setInterval(load, 3000) })
onUnmounted(() => clearInterval(timer))

// Prefer live SignalR metrics for the headline throughput, fall back to polled values.
const mps = computed(() => rt.metrics?.messagesPerSecond ?? sys.value?.kafkaMessagesPerSecond ?? 0)

function Row(label, value) { return { label, value } }
const kafka = computed(() => sys.value ? [
  Row('Messages received', sys.value.kafkaMessagesReceived.toLocaleString()),
  Row('Messages / sec', mps.value.toFixed(2)),
  Row('Topic', 'patient-vitals')
] : [])
const spark = computed(() => sys.value ? [
  Row('Windows computed', sys.value.sparkWindowsComputed.toLocaleString()),
  Row('Last batch size', sys.value.sparkLastBatchSize),
  Row('Processing latency', sys.value.sparkAvgProcessingMs.toFixed(2) + ' ms')
] : [])
const cassandra = computed(() => sys.value ? [
  Row('Stored vitals', sys.value.storedVitals.toLocaleString()),
  Row('Stored alerts', sys.value.storedAlerts.toLocaleString()),
  Row('Write rate / sec', sys.value.cassandraWriteRatePerSecond.toFixed(2))
] : [])
const backend = computed(() => sys.value ? [
  Row('API response time', sys.value.apiAverageResponseMs.toFixed(2) + ' ms'),
  Row('Alerts generated', sys.value.alertsGenerated.toLocaleString()),
  Row('Dashboard link', rt.status === 'live' ? 'Live (SignalR)' : 'Reconnecting')
] : [])
</script>

<template>
  <div v-if="sys" class="grid" style="gap:18px;">
    <div class="grid kpis">
      <div class="kpi kpi-row"><div><span class="label">Throughput</span><div class="value">{{ mps.toFixed(1) }}<span class="muted" style="font-size:14px"> msg/s</span></div></div><span class="icon blue">⚡</span></div>
      <div class="kpi kpi-row"><div><span class="label">Messages Processed</span><div class="value">{{ sys.kafkaMessagesReceived.toLocaleString() }}</div></div><span class="icon sky">📨</span></div>
      <div class="kpi kpi-row"><div><span class="label">Processing Latency</span><div class="value">{{ sys.sparkAvgProcessingMs.toFixed(1) }}<span class="muted" style="font-size:14px"> ms</span></div></div><span class="icon teal">⏱</span></div>
      <div class="kpi kpi-row"><div><span class="label">Pipeline Status</span><div class="value" :style="{ color: sys.streamingOnline ? '#16a34a' : '#dc2626' }">{{ sys.streamingOnline ? 'Online' : 'Offline' }}</div></div><span class="icon green">🩺</span></div>
    </div>

    <div class="grid cols-2">
      <Panel title="Kafka" hint="ingestion">
        <div v-for="r in kafka" :key="r.label" class="kpi-row" style="padding:9px 0;border-bottom:1px solid #f1f5f9;">
          <span class="muted">{{ r.label }}</span><strong class="mono">{{ r.value }}</strong>
        </div>
      </Panel>
      <Panel title="Spark Streaming" hint="processing & windows">
        <div v-for="r in spark" :key="r.label" class="kpi-row" style="padding:9px 0;border-bottom:1px solid #f1f5f9;">
          <span class="muted">{{ r.label }}</span><strong class="mono">{{ r.value }}</strong>
        </div>
      </Panel>
      <Panel title="Cassandra" hint="storage">
        <div v-for="r in cassandra" :key="r.label" class="kpi-row" style="padding:9px 0;border-bottom:1px solid #f1f5f9;">
          <span class="muted">{{ r.label }}</span><strong class="mono">{{ r.value }}</strong>
        </div>
      </Panel>
      <Panel title="Backend & Dashboard" hint="API / realtime">
        <div v-for="r in backend" :key="r.label" class="kpi-row" style="padding:9px 0;border-bottom:1px solid #f1f5f9;">
          <span class="muted">{{ r.label }}</span><strong class="mono">{{ r.value }}</strong>
        </div>
      </Panel>
    </div>

    <Panel title="Performance Summary">
      <p class="muted" style="line-height:1.6;margin:0;">
        The pipeline is ingesting <strong>{{ mps.toFixed(1) }} messages/sec</strong> with an average backend API response of
        <strong>{{ sys.apiAverageResponseMs.toFixed(1) }} ms</strong> and processing latency of
        <strong>{{ sys.sparkAvgProcessingMs.toFixed(1) }} ms</strong>. A total of
        <strong>{{ sys.kafkaMessagesReceived.toLocaleString() }}</strong> readings and
        <strong>{{ sys.storedAlerts.toLocaleString() }}</strong> alerts have been stored.
        No bottlenecks detected — throughput and latency are within healthy bounds.
      </p>
    </Panel>
  </div>
  <div v-else class="empty">Loading system metrics…</div>
</template>
