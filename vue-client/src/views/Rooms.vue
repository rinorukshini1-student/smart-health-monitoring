<script setup>

import { ref, computed, onMounted, onUnmounted } from 'vue'

import { useRouter } from 'vue-router'

import { api } from '../services/api'

import { useRealtime } from '../stores/realtime'

import { ensureHubStarted, subscribeVitals, subscribeAlerts } from '../services/realtimeHub'

import { clockTime, classifyVitals } from '../utils/format'



const router = useRouter()

const rt = useRealtime()



const ROOM_NUMBERS = Array.from({ length: 10 }, (_, i) => String(101 + i))



const roomVitals = ref({})

const serverAlerts = ref([])



let pollTimer = null

let unsubVitals = null

let unsubAlerts = null



function normRoom(n) {

  return n == null ? '' : String(n)

}



function onVitals(reading) {

  const room = normRoom(reading.roomNumber)

  if (!room) return

  const status = classifyVitals(reading)

  roomVitals.value = {

    ...roomVitals.value,

    [room]: {

      patientId: reading.patientId,

      patientName: reading.patientName,

      roomNumber: room,

      heartRate: reading.heartRate,

      spo2: reading.spo2,

      temperature: reading.temperature,

      systolicBp: reading.systolicBp,

      diastolicBp: reading.diastolicBp,

      respiratoryRate: reading.respiratoryRate,

      status,

      recordedAt: reading.recordedAt

    }

  }

}



function onAlert(alert) {

  if (alert?.alertType === 'AI_HEART_RISK') return

  const exists = serverAlerts.value.some(a => a.alertId === alert.alertId)

  if (!exists) {

    serverAlerts.value = [alert, ...serverAlerts.value]

  }

}



function seedFromLiveRows(rows) {

  const map = { ...roomVitals.value }

  for (const r of rows) {

    const room = normRoom(r.roomNumber)

    map[room] = {

      patientId: r.patientId,

      patientName: r.patientName,

      roomNumber: room,

      heartRate: r.heartRate,

      spo2: r.spo2,

      temperature: r.temperature,

      systolicBp: r.systolicBp,

      diastolicBp: r.diastolicBp,

      respiratoryRate: r.respiratoryRate,

      status: r.status || classifyVitals(r),

      recordedAt: r.lastUpdate

    }

  }

  roomVitals.value = map

}



/** Same merge logic as Alerts.vue: SignalR + REST, deduped by alertId. */

const mergedAlerts = computed(() => {

  const map = new Map()

  for (const a of [...rt.alerts, ...serverAlerts.value]) {

    if (a.alertType === 'AI_HEART_RISK') continue

    if (!map.has(a.alertId)) map.set(a.alertId, a)

  }

  return [...map.values()].sort((a, b) => new Date(b.recordedAt) - new Date(a.recordedAt))

})



function latestAlertForRoom(roomNum) {

  const room = normRoom(roomNum)

  return mergedAlerts.value.find(a => normRoom(a.roomNumber) === room)

}



/** Room state aligned with Alerts severity + Live/Overview vital classification. */

function resolveRoomState(roomNum, vitals) {

  const hasVitals = vitals?.heartRate != null

  if (!hasVitals) {

    return { stateLabel: 'Në pritje', stateClass: 'stable', alertActive: false, footer: 'Duke pritur' }

  }



  const latest = latestAlertForRoom(roomNum)

  const vitalStatus = vitals.status || classifyVitals(vitals)



  // Latest alert takes priority (same source as Alerts page).

  if (latest?.severity === 'CRITICAL') {

    return {

      stateLabel: 'Kritik',

      stateClass: 'critical',

      alertActive: true,

      footer: latest.message

    }

  }

  if (latest?.severity === 'WARNING') {

    return {

      stateLabel: 'Paralajmërim',

      stateClass: 'warning',

      alertActive: true,

      footer: latest.message

    }

  }

  if (latest?.severity === 'INFO') {

    return {

      stateLabel: 'Duke u rikuperuar',

      stateClass: 'recovering',

      alertActive: false,

      footer: latest.message || clockTime(vitals.recordedAt)

    }

  }



  // Fallback: backend/live vital status (same as Monitorimi live & Overview KPIs).

  if (vitalStatus === 'Critical') {

    return {

      stateLabel: 'Kritik',

      stateClass: 'critical',

      alertActive: true,

      footer: 'Vlera kritike e vitalëve'

    }

  }

  if (vitalStatus === 'Warning') {

    return {

      stateLabel: 'Paralajmërim',

      stateClass: 'warning',

      alertActive: true,

      footer: 'Vlera jashtë normës'

    }

  }



  return {

    stateLabel: 'Stabil',

    stateClass: 'stable',

    alertActive: false,

    footer: clockTime(vitals.recordedAt)

  }

}



async function refreshFromApi() {

  try {

    const [live, alerts] = await Promise.all([

      api.live(),

      api.alerts({ limit: 250 })

    ])

    seedFromLiveRows(live)

    serverAlerts.value = alerts

  } catch { /* keep last snapshot */ }

}



onMounted(async () => {

  await ensureHubStarted()

  unsubVitals = subscribeVitals(onVitals)

  unsubAlerts = subscribeAlerts(onAlert)

  await refreshFromApi()

  pollTimer = setInterval(refreshFromApi, 3000)

})



onUnmounted(() => {

  unsubVitals?.()

  unsubAlerts?.()

  if (pollTimer) clearInterval(pollTimer)

})



const rooms = computed(() => ROOM_NUMBERS.map(roomNum => {

  const v = roomVitals.value[roomNum]

  const state = resolveRoomState(roomNum, v)



  return {

    roomNumber: roomNum,

    patientName: v?.patientName ?? `Pacienti ${Number(roomNum) - 100}`,

    patientId: v?.patientId,

    heartRate: v?.heartRate ?? null,

    spo2: v?.spo2 ?? null,

    temperature: v?.temperature ?? null,

    ...state

  }

}))



function openPatient(room) {

  if (room.patientId) router.push(`/patients/${room.patientId}`)

}

</script>



<template>

  <div class="rooms-page">

    <section class="rooms-heading" />



    <section class="rooms-grid" aria-label="Dhomat e pacientëve">

      <article

        v-for="room in rooms"

        :key="room.roomNumber"

        class="room-card"

        :class="{ 'room-card--alert': room.stateClass === 'critical', 'room-card--warning': room.stateClass === 'warning' }"

        :data-room="room.roomNumber"

        @click="openPatient(room)"

      >

        <div class="room-card__top">

          <div>

            <span class="room-label">Dhoma {{ room.roomNumber }}</span>

            <h2>{{ room.patientName }}</h2>

          </div>

          <span class="room-state" :class="room.stateClass">{{ room.stateLabel }}</span>

        </div>



        <div class="vitals-row">

          <div>

            <span class="metric-label">BPM</span>

            <strong class="metric-value heart-rate">{{ room.heartRate ?? '—' }}</strong>

          </div>

          <div>

            <span class="metric-label">SpO₂</span>

            <strong class="metric-value spo2">{{ room.spo2 != null ? `${room.spo2}%` : '—' }}</strong>

          </div>

          <div>

            <span class="metric-label">Temp</span>

            <strong class="metric-value temperature">

              {{ room.temperature != null ? `${room.temperature.toFixed(1)}°C` : '—' }}

            </strong>

          </div>

        </div>



        <div class="emergency-banner">URGJENCË</div>

        <time class="last-updated">{{ room.footer }}</time>

      </article>

    </section>

  </div>

</template>


