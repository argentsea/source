<script setup lang="ts">
import { ref } from 'vue'

// The query paradigm as five stages. Click a stage to read what happens there.
interface Stage {
  key: string
  name: string
  code: string
  body: string
}

const stages: Stage[] = [
  {
    key: 'parameters',
    name: 'Build parameters',
    code: 'var prms = new ParameterCollection()\n    .AddSqlIntInputParameter("@CustomerId", id);',
    body:
      'Parameters are built on the caller thread before any connection exists. The collection is provider-neutral; the typed helper methods, or the Mapper from a model, fix each parameter’s database type.'
  },
  {
    key: 'endpoint',
    name: 'Choose Read or Write',
    code: 'var endpoint = _customers[key].Read;',
    body:
      'The caller names the database or shard and picks the Read or Write endpoint explicitly. ArgentSea never routes or falls back on its own; the configuration hierarchy supplies everything the endpoint needs.'
  },
  {
    key: 'execute',
    name: 'Run the Query',
    code: 'endpoint.MapReaderAsync<Customer>(Queries.GetCustomer, prms, cancellation)',
    body:
      'The parameters are copied onto the connection’s own command, the procedure or statement file runs, and the whole call sits inside the endpoint’s retry and circuit-breaker policy, so a transient failure re-runs it from the start.'
  },
  {
    key: 'handle',
    name: 'Handle the results',
    code: 'rdr => Mapper.ToList<Customer>(rdr, logger)   // or your own handler',
    body:
      'A result handler turns the reader and output parameters into objects. The Mapper is the built-in handler; a custom handler gets the same reader. On a shard set this stage runs once per shard, concurrently, so handlers stay stateless.'
  },
  {
    key: 'model',
    name: 'Receive the model',
    code: 'Customer customer = await ...;',
    body:
      'The caller gets a model, a list, a value or a key, identical in shape whether one database or forty shards answered. Fan-out methods combine per-shard results before returning them.'
  }
]

const active = ref(2)
</script>

<template>
  <figure class="as-figure as-anim">
    <div class="as-figure-head">
      <h4 class="as-figure-title">The query paradigm, stage by stage</h4>
      <span class="hint">Select a stage</span>
    </div>
    <ol class="rail" role="tablist">
      <li v-for="(s, i) in stages" :key="s.key" :class="{ on: i === active, done: i < active }">
        <button type="button" role="tab" :aria-selected="i === active" @click="active = i">
          <span class="num">{{ i + 1 }}</span>
          <span class="name">{{ s.name }}</span>
        </button>
        <span v-if="i < stages.length - 1" class="link" aria-hidden="true"></span>
      </li>
    </ol>
    <div class="panel" role="tabpanel">
      <pre class="code"><code>{{ stages[active].code }}</code></pre>
      <p class="body">{{ stages[active].body }}</p>
    </div>
    <figcaption class="as-figure-caption">
      Sharded and single-database calls have the same five stages; only the endpoint differs.
    </figcaption>
  </figure>
</template>

<style scoped>
.hint {
  font-size: 12px;
  letter-spacing: 0.1em;
  text-transform: uppercase;
  color: var(--vp-c-text-3);
}
.rail {
  list-style: none;
  display: grid;
  grid-template-columns: repeat(5, 1fr);
  gap: 0;
  margin: 0 0 18px;
  padding: 0;
}
.rail li {
  position: relative;
  display: flex;
  align-items: center;
  margin: 0;
  padding: 0;
}
.rail button {
  flex: 1;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 8px;
  background: transparent;
  border: none;
  font: inherit;
  color: var(--vp-c-text-2);
  cursor: pointer;
  padding: 4px 4px;
  z-index: 1;
}
.num {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 34px;
  height: 34px;
  border-radius: 50%;
  border: 2px solid var(--vp-c-divider);
  background: var(--vp-c-bg);
  font-family: var(--as-font-display);
  font-size: 16px;
  font-weight: 600;
  transition: border-color 0.2s, background 0.2s, color 0.2s, transform 0.2s;
}
.name {
  font-size: 12.5px;
  font-weight: 600;
  text-align: center;
  line-height: 1.25;
}
.rail li.done .num {
  border-color: var(--vp-c-brand-2);
  color: var(--vp-c-brand-1);
}
.rail li.on .num {
  border-color: var(--vp-c-brand-2);
  background: linear-gradient(120deg, var(--as-gold-3), var(--as-gold-4));
  color: #141210;
  transform: scale(1.08);
}
.rail li.on .name {
  color: var(--vp-c-text-1);
}
.link {
  position: absolute;
  top: 21px;
  left: calc(50% + 20px);
  right: calc(-50% + 20px);
  height: 2px;
  background: var(--vp-c-divider);
}
.rail li.done .link {
  background: var(--vp-c-brand-2);
}
.panel {
  border-top: 1px solid var(--vp-c-divider);
  padding-top: 16px;
}
.code {
  margin: 0 0 12px;
  padding: 12px 16px;
  border-radius: 10px;
  background: var(--vp-code-block-bg);
  color: var(--vp-code-block-color);
  font-family: var(--as-font-mono);
  font-size: 13px;
  line-height: 1.6;
  overflow-x: auto;
  white-space: pre;
}
.body {
  margin: 0;
  font-size: 15px;
  line-height: 1.65;
}
@media (max-width: 640px) {
  .rail {
    grid-template-columns: 1fr;
    gap: 6px;
  }
  .rail button {
    flex-direction: row;
    justify-content: flex-start;
    gap: 12px;
  }
  .link {
    display: none;
  }
  .name {
    text-align: left;
  }
}
</style>
