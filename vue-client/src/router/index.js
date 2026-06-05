import { createRouter, createWebHistory } from 'vue-router'

const routes = [
  { path: '/', name: 'overview', component: () => import('../views/Overview.vue'), meta: { title: 'Overview' } },
  { path: '/live', name: 'live', component: () => import('../views/LiveMonitoring.vue'), meta: { title: 'Live Monitoring' } },
  { path: '/patients/:id?', name: 'patient', component: () => import('../views/PatientDetails.vue'), meta: { title: 'Patient Details' } },
  { path: '/alerts', name: 'alerts', component: () => import('../views/Alerts.vue'), meta: { title: 'Alert Center' } },
  { path: '/analytics', name: 'analytics', component: () => import('../views/Analytics.vue'), meta: { title: 'Analytics' } },
  { path: '/ai', name: 'ai', component: () => import('../views/AiPredictions.vue'), meta: { title: 'AI Predictions' } },
  { path: '/system', name: 'system', component: () => import('../views/SystemHealth.vue'), meta: { title: 'System Health' } }
]

const router = createRouter({
  history: createWebHistory(),
  routes,
  linkActiveClass: 'active'
})

router.afterEach((to) => {
  document.title = `${to.meta.title ?? 'Dashboard'} · Smart Health`
})

export default router
