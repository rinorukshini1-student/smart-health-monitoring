<script setup>
defineProps({
  icon: { type: String, required: true }
})
</script>

<template>
  <i :class="['las', icon]" aria-hidden="true"></i>
</template>
