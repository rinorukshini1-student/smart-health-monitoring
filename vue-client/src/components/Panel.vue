<script setup>
defineProps({ title: String, hint: String })
</script>

<template>
  <section class="panel">
    <div class="panel-head" v-if="title || $slots.actions">
      <div>
        <h3>{{ title }}</h3>
      </div>
      <div class="hint" v-if="hint">{{ hint }}</div>
      <slot name="actions" />
    </div>
    <div class="panel-body">
      <slot />
    </div>
  </section>
</template>
