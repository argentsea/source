<script setup lang="ts">
import { computed } from 'vue'
import { useData, useRoute } from 'vitepress'

// Draws the page title and frontmatter description as a band above the article.
// The Markdown h1 stays in the document for structure and is hidden visually (see custom.css).
const { frontmatter, page } = useData()
const route = useRoute()

const sections: Record<string, string> = {
  introduction: 'Introduction',
  'getting-started': 'Getting started',
  guides: 'Guide',
  reference: 'Reference'
}

const eyebrow = computed(() => {
  const first = route.path.split('/').filter(Boolean)[0] ?? ''
  return sections[first] ?? 'ArgentSea'
})
const title = computed(() => frontmatter.value.title ?? page.value.title)
const description = computed(() => frontmatter.value.description as string | undefined)
const show = computed(() => frontmatter.value.layout !== 'home' && frontmatter.value.band !== false)
</script>

<template>
  <header v-if="show" class="as-band">
    <span class="as-band-eyebrow">{{ eyebrow }}</span>
    <h1 class="as-band-title">{{ title }}</h1>
    <p v-if="description" class="as-band-desc">{{ description }}</p>
  </header>
</template>
