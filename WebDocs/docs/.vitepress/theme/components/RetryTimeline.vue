<script setup lang="ts">
import { computed, ref } from 'vue'

// Retry delays exactly as DataConnectionConfigurationBase computes them:
//   Lengthening  (2n - 1) * interval     (the default; Fibonacci is an obsolete alias)
//   Linear        n * interval
//   HalfSquare    floor(n^2 / 2) * interval
//   Squaring      2^(n - 1) * interval
type Mode = 'Lengthening' | 'Linear' | 'HalfSquare' | 'Squaring'
const modes: Mode[] = ['Lengthening', 'Linear', 'HalfSquare', 'Squaring']
const mode = ref<Mode>('Lengthening')
const interval = ref(250)
const retryCount = ref(6)

function multiplier(m: Mode, n: number): number {
  switch (m) {
    case 'Linear':
      return n
    case 'HalfSquare':
      return Math.floor((n * n) / 2)
    case 'Squaring':
      return 2 ** (n - 1)
    default:
      return 2 * n - 1
  }
}

const formula = computed(() => {
  switch (mode.value) {
    case 'Linear':
      return 'n × interval'
    case 'HalfSquare':
      return '⌊n² ÷ 2⌋ × interval'
    case 'Squaring':
      return '2ⁿ⁻¹ × interval'
    default:
      return '(2n − 1) × interval'
  }
})

const delays = computed(() => {
  const out: { n: number; ms: number; at: number }[] = []
  let at = 0
  for (let n = 1; n <= retryCount.value; n++) {
    const ms = multiplier(mode.value, n) * interval.value
    at += ms
    out.push({ n, ms, at })
  }
  return out
})
const total = computed(() => delays.value.reduce((s, d) => s + d.ms, 0))
const max = computed(() => Math.max(1, ...delays.value.map((d) => d.ms)))

function fmt(ms: number): string {
  return ms >= 1000 ? `${(ms / 1000).toFixed(ms % 1000 === 0 ? 0 : 2)} s` : `${ms} ms`
}
</script>

<template>
  <figure class="as-figure as-anim">
    <div class="as-figure-head">
      <h4 class="as-figure-title">Backoff before each retry</h4>
      <div class="chips" role="radiogroup" aria-label="RetryLengthening">
        <button v-for="m in modes" :key="m" type="button" class="as-chip" :class="{ on: m === mode }" role="radio" :aria-checked="m === mode" @click="mode = m">
          {{ m }}<span v-if="m === 'Lengthening'" class="def"> · default</span>
        </button>
      </div>
    </div>
    <div class="controls">
      <label>
        <span>RetryInterval</span>
        <input v-model.number="interval" type="range" min="50" max="1000" step="50" />
        <code>{{ interval }} ms</code>
      </label>
      <label>
        <span>RetryCount</span>
        <input v-model.number="retryCount" type="range" min="1" max="10" step="1" />
        <code>{{ retryCount }}</code>
      </label>
      <div class="formula">delay<sub>n</sub> = <code>{{ formula }}</code></div>
    </div>
    <ol class="bars">
      <li v-for="d in delays" :key="d.n">
        <span class="label">retry {{ d.n }}</span>
        <span class="track"><span class="fill" :style="{ width: `${(d.ms / max) * 100}%` }"></span></span>
        <span class="value">{{ fmt(d.ms) }}</span>
        <span class="at">after {{ fmt(d.at) }}</span>
      </li>
    </ol>
    <figcaption class="as-figure-caption">
      {{ retryCount }} retries wait <strong>{{ fmt(total) }}</strong> in total before the connect attempts themselves are counted.
      <template v-if="mode === 'HalfSquare'"> HalfSquare retries once immediately, then backs off.</template>
      <template v-if="mode === 'Squaring'"> Squaring is classic exponential backoff.</template>
    </figcaption>
  </figure>
</template>

<style scoped>
.chips {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}
.def {
  font-weight: 400;
  opacity: 0.7;
}
.controls {
  display: grid;
  grid-template-columns: 1fr 1fr auto;
  gap: 12px 24px;
  align-items: center;
  margin-bottom: 18px;
  font-size: 13px;
}
.controls label {
  display: grid;
  grid-template-columns: auto 1fr auto;
  gap: 10px;
  align-items: center;
  color: var(--vp-c-text-2);
}
.controls input[type='range'] {
  accent-color: var(--vp-c-brand-2);
  width: 100%;
}
.controls code,
.formula code {
  font-family: var(--as-font-mono);
  font-size: 12.5px;
  color: var(--vp-c-brand-1);
  background: var(--vp-c-brand-soft);
  border-radius: 5px;
  padding: 2px 7px;
}
.formula {
  color: var(--vp-c-text-2);
  white-space: nowrap;
}
.bars {
  list-style: none;
  margin: 0;
  padding: 0;
  display: grid;
  gap: 8px;
}
.bars li {
  display: grid;
  grid-template-columns: 64px 1fr 72px 110px;
  gap: 12px;
  align-items: center;
  margin: 0;
  font-size: 13px;
}
.label {
  color: var(--vp-c-text-2);
}
.track {
  height: 14px;
  border-radius: 7px;
  background: var(--vp-c-divider);
  overflow: hidden;
}
.fill {
  display: block;
  height: 100%;
  border-radius: 7px;
  background: linear-gradient(90deg, var(--as-gold-2), var(--as-gold-4));
  transition: width 0.35s ease;
  min-width: 2px;
}
.value {
  font-variant-numeric: tabular-nums;
  font-weight: 600;
  text-align: right;
}
.at {
  font-variant-numeric: tabular-nums;
  color: var(--vp-c-text-3);
}
@media (max-width: 640px) {
  .controls {
    grid-template-columns: 1fr;
  }
  .bars li {
    grid-template-columns: 56px 1fr 64px;
  }
  .at {
    display: none;
  }
}
</style>
