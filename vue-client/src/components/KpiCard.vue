<script setup>
defineProps({
  label: String,
  value: [String, Number],
  meta: String,
  icon: { type: String, default: '•' },
  tone: { type: String, default: 'blue' }
})
</script>

<template>
  <div class="kpi">
    <div class="kpi-row">
      <span class="label">{{ label }}</span>
      <span class="icon" :class="tone">{{ icon }}</span>
    </div>
    <span class="value">{{ value }}</span>
    <span class="meta">{{ meta }}</span>
  </div>
</template>
