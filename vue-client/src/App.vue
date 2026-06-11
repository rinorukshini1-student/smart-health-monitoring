<script setup>
import { ref, computed, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import { useRealtime } from './stores/realtime'
import LaIcon from './components/LaIcon.vue'
import logoUrl from './assets/logo.png'

const rt = useRealtime()
const route = useRoute()
const menuOpen = ref(false)

const nav = [
  { to: '/', icon: 'la-chart-pie', label: 'Dashboard' },
  { to: '/live', icon: 'la-heartbeat', label: 'Monitorimi në kohë reale' },
  { to: '/rooms', icon: 'la-door-open', label: 'Dhomat' },
  { to: '/patients', icon: 'la-user-md', label: 'Detajet e pacientit' },
    { to: '/alerts', icon: 'la-bell', label: 'Alarmet' },
  { to: '/analytics', icon: 'la-chart-line', label: 'Analitika' },
  { to: '/ai', icon: 'la-robot', label: 'Parashikimet AI' },
  { to: '/system', icon: 'la-stethoscope', label: 'Shëndeti i sistemit' }
]

const title = computed(() => route.meta.title ?? 'Paneli')
const subtitle = computed(() => ({
  'Përmbledhje': '',
  'Monitorimi në kohë Reale': '',
  'Dhomat': '',
  'Detajet e pacientit': '',
  'Alarmet': '',
  'Analitika': '',
  'Parashikimet AI': 'Parashikimi i rrezikut të infarktit',
  'Shëndeti i sistemit': ''
}[title.value] ?? ''))

const connLabel = computed(() => ({
  live: 'Në kohë reale',
  connecting: 'Duke u lidhur…',
  down: 'Duke u rilidhur…'
}[rt.status]))

onMounted(() => rt.init())
</script>

<template>
  <div class="shell">
    <div v-if="menuOpen" class="scrim" @click="menuOpen = false"></div>
    <aside class="sidebar" :class="{ open: menuOpen }">
      <div class="brand">
        <div class="logo"><img :src="logoUrl" alt="Smart Health" /></div>
        <div>
          <h1>Smart Health</h1>
          <!--<small>Monitorimi IoT · Vue</small>-->
        </div>
      </div>
      <RouterLink v-for="n in nav" :key="n.to" :to="n.to" class="nav-link" @click="menuOpen = false">
        <span class="ic"><LaIcon :icon="n.icon" /></span>{{ n.label }}
      </RouterLink>
      <!--<div class="sidebar-foot">
        Rruga IoT: Sensor → Kafka → Spark → Cassandra → API → Paneli<br />
        Demonstrim i pavarur · gjeneron të dhëna automatikisht
      </div>-->
    </aside>

    <div class="main">
      <header class="topbar">
        <div style="display:flex;align-items:center;gap:14px;">
          <button class="menu-btn" @click="menuOpen = !menuOpen" aria-label="Hap menunë">
            <LaIcon icon="la-bars" />
          </button>
          <div>
            <h2>{{ title }}</h2>
            <div class="sub">{{ subtitle }}</div>
          </div>
        </div>
        <div class="conn">
          <span class="dot" :class="{ live: rt.status === 'live', down: rt.status === 'down' }"></span>
          {{ connLabel }}
        </div>
      </header>
      <main class="content">
        <RouterView v-slot="{ Component }">
          <component :is="Component" />
        </RouterView>
      </main>
    </div>
  </div>
</template>
