<script setup lang="ts">
const tenants = ['Tenant A', 'Tenant B', 'Tenant C', 'Tenant D']
</script>

<template>
  <section class="topo-wrap">
    <div class="as-section">
      <div class="cols">
        <div class="copy">
          <span class="as-eyebrow">Designed for scale-out</span>
          <h2 class="as-section-title">One application. Many databases. Zero special cases.</h2>
          <p>
            Whether you partition a huge data set into shards or isolate every customer in a tenant database, the
            topology is the same: a <strong>shard set</strong> of databases, each with a <strong>read</strong> and a
            <strong>write</strong> endpoint. Your code addresses the set, or a single shard by key, and ArgentSea opens
            the right connections, copies the parameters, runs the shards concurrently and merges the results.
          </p>
          <ul class="bullets">
            <li><strong>Route by key.</strong> A <code>ShardKey</code> carries the shard id, so the right database is always known.</li>
            <li><strong>Fan out.</strong> <code>ReadAll</code>, <code>ReadFirst</code> and <code>Write</code> run across every shard, or a targeted subset, in parallel.</li>
            <li><strong>Scale reads.</strong> Point read connections at replicas, writes at the primary. Nothing in your code changes.</li>
          </ul>
          <a class="link" href="/introduction/core-concepts">Walk through the core concepts →</a>
        </div>
        <div class="diagram as-anim" aria-label="Diagram: an application fanning out across tenant databases, each with a write primary and read replica">
          <svg viewBox="0 0 520 360">
            <defs>
              <linearGradient id="appg" x1="0" y1="0" x2="1" y2="1">
                <stop offset="0" stop-color="#c9a85a" />
                <stop offset="1" stop-color="#ebdc9e" />
              </linearGradient>
            </defs>
            <!-- app node -->
            <g>
              <rect x="20" y="140" width="120" height="80" rx="12" fill="url(#appg)" />
              <text x="80" y="174" text-anchor="middle" class="t-dark b">Your app</text>
              <text x="80" y="196" text-anchor="middle" class="t-dark s">SqlShardSets["Tenants"]</text>
            </g>
            <!-- links -->
            <g fill="none" stroke-width="2">
              <path v-for="(t, i) in tenants" :key="t" class="link-path" :style="{ animationDelay: `${i * 0.5}s` }"
                :d="`M140 180 C 230 180, 230 ${50 + i * 86}, 300 ${50 + i * 86}`" />
            </g>
            <!-- tenants -->
            <g v-for="(t, i) in tenants" :key="t" :transform="`translate(300, ${22 + i * 86})`">
              <rect width="200" height="58" rx="10" class="tenant" />
              <text x="12" y="22" class="t b">{{ t }}</text>
              <text x="12" y="42" class="t s">shard {{ i }}</text>
              <g transform="translate(110, 10)">
                <rect width="38" height="38" rx="8" class="db write" />
                <text x="19" y="24" text-anchor="middle" class="t-dark xs">W</text>
                <rect x="46" width="38" height="38" rx="8" class="db read" />
                <text x="65" y="24" text-anchor="middle" class="t xs">R</text>
                <path d="M38 19 L46 19" class="repl" />
              </g>
            </g>
          </svg>
          <div class="legend">
            <span><i class="sw w"></i>write primary</span>
            <span><i class="sw r"></i>read replica</span>
          </div>
        </div>
      </div>
    </div>
  </section>
</template>

<style scoped>
.topo-wrap {
  background: var(--vp-c-bg-alt);
  border-top: 1px solid var(--vp-c-divider);
  border-bottom: 1px solid var(--vp-c-divider);
}
.cols {
  display: grid;
  grid-template-columns: 1.05fr 1fr;
  gap: 48px;
  align-items: center;
}
@media (max-width: 860px) {
  .cols {
    grid-template-columns: 1fr;
  }
}
.copy p {
  color: var(--vp-c-text-2);
  line-height: 1.65;
}
.bullets {
  margin: 18px 0 22px;
  padding-left: 18px;
  color: var(--vp-c-text-2);
  line-height: 1.6;
}
.bullets li {
  margin-bottom: 8px;
}
.link {
  font-weight: 600;
  color: var(--vp-c-brand-1);
}
.diagram svg {
  width: 100%;
  height: auto;
}
.t {
  fill: var(--vp-c-text-1);
  font-size: 13px;
}
.t-dark {
  fill: #141210;
  font-size: 13px;
}
.b {
  font-weight: 700;
}
.s {
  font-size: 10.5px;
  font-family: var(--vp-font-family-mono);
  opacity: 0.85;
}
.xs {
  font-size: 11px;
  font-weight: 700;
}
.tenant {
  fill: var(--vp-c-bg-soft);
  stroke: var(--vp-c-divider);
}
.db.write {
  fill: #d4b96a;
}
.db.read {
  fill: var(--vp-c-bg);
  stroke: var(--vp-c-brand-2);
  stroke-width: 1.5;
}
.repl {
  stroke: var(--vp-c-brand-2);
  stroke-width: 1.5;
  stroke-dasharray: 2 2;
}
.link-path {
  stroke: var(--vp-c-brand-2);
  stroke-dasharray: 6 10;
  animation: flow 1.6s linear infinite;
  opacity: 0.9;
}
@keyframes flow {
  to {
    stroke-dashoffset: -32;
  }
}
.legend {
  display: flex;
  gap: 18px;
  justify-content: flex-end;
  font-size: 12px;
  color: var(--vp-c-text-2);
  margin-top: 6px;
}
.sw {
  display: inline-block;
  width: 12px;
  height: 12px;
  border-radius: 3px;
  margin-right: 6px;
  vertical-align: -1px;
}
.sw.w {
  background: #d4b96a;
}
.sw.r {
  border: 1.5px solid var(--vp-c-brand-2);
}
</style>
