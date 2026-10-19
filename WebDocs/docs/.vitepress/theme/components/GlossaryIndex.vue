<script setup lang="ts">
defineProps<{ letters: string[] }>()
</script>

<template>
  <nav class="gi" aria-label="Jump to letter">
    <a v-for="l in letters" :key="l" :href="`#letter-${l.toLowerCase()}`">{{ l }}</a>
  </nav>
</template>

<style scoped>
.gi {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  margin: 8px 0 24px;
}
.gi a {
  display: inline-block;
  min-width: 32px;
  text-align: center;
  padding: 4px 8px;
  border: 1px solid var(--vp-c-divider);
  border-radius: 8px;
  font-weight: 600;
  font-size: 13px;
  text-decoration: none !important;
}
.gi a:hover {
  border-color: var(--vp-c-brand-1);
}
</style>
