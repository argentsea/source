<script setup lang="ts">
import { computed, onBeforeUnmount, ref } from 'vue'

type Mode = 'ReadAll' | 'ReadFirst' | 'Write'
type ShardState = 'idle' | 'running' | 'done' | 'cancelled' | 'empty'

const shards = [0, 1, 2, 3]
const mode = ref<Mode>('ReadAll')
const states = ref<ShardState[]>(['idle', 'idle', 'idle', 'idle'])
const results = ref<string[]>([])
const running = ref(false)
const log = ref<string[]>([])
let timers: ReturnType<typeof setTimeout>[] = []

const description = computed(() => {
  switch (mode.value) {
    case 'ReadAll':
      return 'Runs the query on the read connection of every shard, waits for all of them, and returns every non-null result combined into one list.'
    case 'ReadFirst':
      return 'Runs the query on the read connection of every shard and returns the first non-null result. The remaining shard queries are cancelled.'
    case 'Write':
      return 'Runs the command on the write connection of every shard. Use it for changes that must be applied across the whole set.'
  }
})

const call = computed(() => {
  switch (mode.value) {
    case 'ReadAll':
      return 'var users = await shardSet.ReadAll.MapListAsync<User>(Queries.UsersByRole, prms, cancellation);'
    case 'ReadFirst':
      return 'var user = await shardSet.ReadFirst.MapReaderAsync<User>(Queries.UserByLogin, prms, cancellation);'
    case 'Write':
      return 'await shardSet.Write.RunAsync(Queries.ExpireSessions, prms, cancellation);'
  }
})

function reset() {
  timers.forEach(clearTimeout)
  timers = []
  states.value = ['idle', 'idle', 'idle', 'idle']
  results.value = []
  log.value = []
  running.value = false
}

function later(ms: number, fn: () => void) {
  timers.push(setTimeout(fn, ms))
}

function run() {
  reset()
  running.value = true
  const latencies = shards.map(() => 500 + Math.random() * 1100)
  states.value = shards.map(() => 'running')
  log.value.push(`${mode.value}: parameters copied to ${shards.length} connections, queries started concurrently`)

  if (mode.value === 'ReadFirst') {
    // the shard that holds the record answers first
    const winner = shards[Math.floor(Math.random() * shards.length)]
    latencies[winner] = Math.min(...latencies) - 150
    shards.forEach((s) => {
      later(latencies[s], () => {
        if (states.value[s] !== 'running') return
        if (s === winner) {
          states.value[s] = 'done'
          results.value = [`shard ${s}: 1 row`]
          log.value.push(`shard ${s} returned a result; cancelling the others`)
          shards.forEach((o) => {
            if (states.value[o] === 'running') states.value[o] = 'cancelled'
          })
          running.value = false
        }
      })
    })
    return
  }

  let remaining = shards.length
  shards.forEach((s) => {
    later(latencies[s], () => {
      const rows = mode.value === 'Write' ? null : Math.floor(Math.random() * 4)
      if (rows === 0) {
        states.value[s] = 'empty'
        log.value.push(`shard ${s}: no rows (ignored)`)
      } else {
        states.value[s] = 'done'
        results.value.push(mode.value === 'Write' ? `shard ${s}: committed` : `shard ${s}: ${rows} rows`)
        log.value.push(`shard ${s}: ${mode.value === 'Write' ? 'command completed' : rows + ' row(s) mapped on its own thread'}`)
      }
      if (--remaining === 0) {
        log.value.push(mode.value === 'Write' ? 'all shards completed' : `combined list: ${results.value.length} shard result(s) merged`)
        running.value = false
      }
    })
  })
}

onBeforeUnmount(reset)
</script>

<template>
  <div class="fan as-anim">
    <div class="toolbar">
      <div class="modes" role="tablist">
        <button v-for="m in (['ReadAll', 'ReadFirst', 'Write'] as Mode[])" :key="m" role="tab" :aria-selected="mode === m"
          :class="{ on: mode === m }" @click="mode = m; reset()">
          {{ m }}
        </button>
      </div>
      <button class="run" :disabled="running" @click="run">{{ running ? 'Running…' : 'Run query' }}</button>
    </div>
    <p class="desc">{{ description }}</p>
    <pre class="call"><code>{{ call }}</code></pre>
    <div class="stage">
      <div class="caller">
        <div class="node app">caller<br /><small>one thread, one ParameterCollection</small></div>
      </div>
      <div class="lanes">
        <div v-for="s in shards" :key="s" class="lane">
          <div class="wire" :class="states[s]"><i></i></div>
          <div class="node shard" :class="states[s]">
            <strong>shard {{ s }}</strong>
            <small>{{ mode === 'Write' ? 'Write' : 'Read' }} connection · own retry policy</small>
            <span class="state">{{ states[s] === 'idle' ? '' : states[s] }}</span>
          </div>
        </div>
      </div>
      <div class="result">
        <div class="node out" :class="{ has: results.length }">
          <strong>{{ mode === 'Write' ? 'completion' : mode === 'ReadFirst' ? 'first result' : 'combined list' }}</strong>
          <ul>
            <li v-for="r in results" :key="r">{{ r }}</li>
            <li v-if="!results.length" class="dim">—</li>
          </ul>
        </div>
      </div>
    </div>
    <ol class="log">
      <li v-for="(l, i) in log" :key="i">{{ l }}</li>
    </ol>
  </div>
</template>

<style scoped>
.fan {
  border: 1px solid var(--vp-c-divider);
  border-radius: 14px;
  padding: 18px;
  margin: 20px 0 28px;
  background: var(--vp-c-bg-soft);
}
.toolbar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 12px;
  flex-wrap: wrap;
}
.modes {
  display: inline-flex;
  border: 1px solid var(--vp-c-divider);
  border-radius: 999px;
  overflow: hidden;
}
.modes button {
  padding: 6px 14px;
  font-size: 13px;
  font-weight: 600;
  color: var(--vp-c-text-2);
  background: transparent;
}
.modes button.on {
  background: var(--vp-c-brand-1);
  color: var(--vp-c-bg);
}
.run {
  padding: 7px 16px;
  border-radius: 999px;
  background: var(--vp-button-brand-bg);
  color: var(--vp-button-brand-text);
  font-weight: 600;
  font-size: 13px;
}
.run:disabled {
  opacity: 0.6;
}
.desc {
  margin: 12px 0 8px;
  font-size: 14px;
  color: var(--vp-c-text-2);
}
.call {
  margin: 0 0 16px;
  padding: 10px 12px;
  border-radius: 8px;
  background: var(--vp-code-block-bg);
  font-size: 12.5px;
  overflow-x: auto;
}
.call code {
  color: var(--vp-c-text-1);
}
.stage {
  display: grid;
  grid-template-columns: 150px 1fr 170px;
  gap: 12px;
  align-items: center;
}
@media (max-width: 640px) {
  .stage {
    grid-template-columns: 1fr;
  }
  .wire {
    display: none;
  }
}
.node {
  border-radius: 10px;
  padding: 10px 12px;
  font-size: 13px;
  line-height: 1.3;
  border: 1px solid var(--vp-c-divider);
  background: var(--vp-c-bg);
}
.node small {
  display: block;
  font-size: 11px;
  color: var(--vp-c-text-3);
  margin-top: 2px;
}
.node.app {
  background: linear-gradient(120deg, #c9a85a, #ebdc9e);
  color: #141210;
  font-weight: 700;
  text-align: center;
}
.node.app small {
  color: #3b3222;
  font-weight: 500;
}
.lanes {
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.lane {
  display: grid;
  grid-template-columns: 1fr 230px;
  align-items: center;
  gap: 8px;
}
.wire {
  position: relative;
  height: 2px;
  background: var(--vp-c-divider);
  border-radius: 2px;
  overflow: hidden;
}
.wire i {
  position: absolute;
  top: 0;
  left: -40%;
  width: 40%;
  height: 100%;
  background: var(--vp-c-brand-1);
  opacity: 0;
}
.wire.running i {
  opacity: 1;
  animation: pulse 0.9s linear infinite;
}
.wire.done {
  background: var(--vp-c-brand-1);
}
.wire.cancelled {
  background: repeating-linear-gradient(90deg, var(--vp-c-divider) 0 6px, transparent 6px 12px);
}
@keyframes pulse {
  to {
    left: 100%;
  }
}
.node.shard {
  position: relative;
  transition: border-color 0.2s, box-shadow 0.2s;
}
.node.shard.running {
  border-color: var(--vp-c-brand-2);
  box-shadow: 0 0 0 3px var(--vp-c-brand-soft);
}
.node.shard.done {
  border-color: var(--vp-c-brand-1);
}
.node.shard.cancelled,
.node.shard.empty {
  opacity: 0.55;
}
.state {
  position: absolute;
  right: 10px;
  top: 8px;
  font-size: 10px;
  text-transform: uppercase;
  letter-spacing: 0.08em;
  color: var(--vp-c-brand-1);
}
.node.out ul {
  margin: 6px 0 0;
  padding-left: 16px;
  font-size: 12px;
  color: var(--vp-c-text-2);
}
.node.out.has {
  border-color: var(--vp-c-brand-1);
}
.dim {
  list-style: none;
  margin-left: -16px;
  color: var(--vp-c-text-3);
}
.log {
  margin: 14px 0 0;
  padding-left: 18px;
  font-size: 12.5px;
  color: var(--vp-c-text-2);
  font-family: var(--vp-font-family-mono);
  min-height: 1.2em;
}
.log li {
  margin: 2px 0;
}
</style>
