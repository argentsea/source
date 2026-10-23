<script setup lang="ts">
const features = [
  {
    title: 'Sharding and tenanting',
    icon: '/images/sharding.svg',
    link: '/guides/sharding',
    text:
      'Spread one data set across many databases, or give each cloud tenant a database of their own. Querying a shard set reads like querying one database: route by key, or fan out to every shard concurrently.'
  },
  {
    title: 'Compiled mapping',
    icon: '/images/mapping.svg',
    link: '/guides/mapping',
    text:
      'Decorate model properties with attributes and let the Mapper move values to parameters and from result sets. Reflection runs once per model; after that it is compiled code at hand-written ADO.NET speed.'
  },
  {
    title: 'Configuration, not connection strings',
    icon: '/images/properties.svg',
    link: '/guides/configuration',
    text:
      'Every connection property is a setting in an inheritable hierarchy. Set a login once for a whole shard set, keep passwords in a secret store, and promote the same layout across environments.'
  },
  {
    title: 'Resilient by default',
    icon: '/images/resilience.svg',
    link: '/guides/resilience',
    text:
      'Transient failures are retried with a configurable backoff; persistent failures trip a per-endpoint circuit breaker so a dead database cannot stall your whole service. Every retry is logged.'
  },
  {
    title: 'Performance first',
    icon: '/images/performance.svg',
    link: '/guides/queries',
    text:
      'Static SQL in stored procedures or deployed statement files, parameters declared once and copied per connection, and shard queries that run in parallel. The framework is never the bottleneck.'
  },
  {
    title: 'A real Orleans provider',
    icon: '/images/tooling.svg',
    link: '/guides/orleans',
    text:
      'The only Microsoft Orleans storage provider that keeps grain state in third-normal-form tables instead of blobs, so SQL can query across grains. It clusters silos through SQL and runs many tenant databases behind one cluster.'
  }
]
</script>

<template>
  <section class="as-section">
    <span class="as-eyebrow">Why developers pick it</span>
    <h2 class="as-section-title">Everything a data layer needs. One thing nobody else offers.</h2>
    <p class="as-section-lede">
      Most data frameworks assume one database. ArgentSea assumes many, and makes the ordinary case better along the way.
    </p>
    <div class="grid">
      <a v-for="f in features" :key="f.title" class="as-card feature" :href="f.link">
        <img :src="f.icon" :alt="''" class="icon" />
        <h3>{{ f.title }}</h3>
        <p>{{ f.text }}</p>
        <span class="more">Learn more →</span>
      </a>
    </div>
  </section>
</template>

<style scoped>
.grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(300px, 1fr));
  gap: 18px;
}
.feature {
  display: block;
  color: inherit;
  text-decoration: none !important;
}
.icon {
  width: 44px;
  height: 44px;
  border-radius: 10px;
  margin-bottom: 14px;
}
.feature h3 {
  margin: 0 0 8px;
  font-size: 18px;
  font-weight: 650;
  letter-spacing: -0.01em;
}
.feature p {
  margin: 0 0 14px;
  color: var(--vp-c-text-2);
  font-size: 14.5px;
  line-height: 1.6;
}
.more {
  color: var(--vp-c-brand-1);
  font-weight: 600;
  font-size: 14px;
}
</style>
