import { createRouter, createWebHistory } from 'vue-router'

const routes = [
  { path: '/', name: 'overview', component: () => import('../views/Overview.vue'), meta: { title: 'Dashboard' } },
  { path: '/live', name: 'live', component: () => import('../views/LiveMonitoring.vue'), meta: { title: 'Monitorimi në kohë reale' } },
  { path: '/rooms', name: 'rooms', component: () => import('../views/Rooms.vue'), meta: { title: 'Dhomat' } },
  { path: '/patients/:id?', name: 'patient', component: () => import('../views/PatientDetails.vue'), meta: { title: 'Detajet e pacientit' } },
    { path: '/alerts', name: 'alerts', component: () => import('../views/Alerts.vue'), meta: { title: 'Alarmet' } },
  { path: '/analytics', name: 'analytics', component: () => import('../views/Analytics.vue'), meta: { title: 'Analitika' } },
  { path: '/ai', name: 'ai', component: () => import('../views/AiPredictions.vue'), meta: { title: 'Parashikimet AI' } },
  { path: '/system', name: 'system', component: () => import('../views/SystemHealth.vue'), meta: { title: 'Shëndeti i sistemit' } }
]

const router = createRouter({
  history: createWebHistory(),
  routes,
  linkActiveClass: 'active'
})

router.afterEach((to) => {
  document.title = `${to.meta.title ?? 'Paneli'} · Smart Health`
})

export default router
