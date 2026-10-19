<script setup lang="ts">
import { computed, ref } from 'vue'

// A small, realistic configuration: values set at different levels.
type Level = 'Global' | 'Shard set' | 'Read' | 'Write' | 'Shard' | 'Endpoint'
interface Setting {
  level: Level
  values: Record<string, string>
}

const global: Setting = { level: 'Global', values: { RetryCount: '6', ConnectTimeout: '15', Encrypt: 'true' } }
const shardSet: Setting = { level: 'Shard set', values: { UserName: 'app_user', Password: '(secret store)', InitialCatalog: 'Orders' } }
const readSection: Setting = { level: 'Read', values: { ApplicationIntent: 'ReadOnly', UserName: 'app_reader' } }
const writeSection: Setting = { level: 'Write', values: { ApplicationIntent: 'ReadWrite' } }
const shards: Record<number, Setting> = {
  0: { level: 'Shard', values: { DataSource: 'ord-us-east.db.local' } },
  1: { level: 'Shard', values: { DataSource: 'ord-eu-west.db.local', ConnectTimeout: '30' } }
}
const endpoints: Record<string, Setting> = {
  '0-Read': { level: 'Endpoint', values: { DataSource: 'ord-us-east-replica.db.local' } },
  '0-Write': { level: 'Endpoint', values: {} },
  '1-Read': { level: 'Endpoint', values: {} },
  '1-Write': { level: 'Endpoint', values: {} }
}

const shardId = ref(0)
const kind = ref<'Read' | 'Write'>('Read')

const chain = computed<Setting[]>(() => [
  global,
  shardSet,
  kind.value === 'Read' ? readSection : writeSection,
  shards[shardId.value],
  endpoints[`${shardId.value}-${kind.value}`]
])

const keys = ['DataSource', 'InitialCatalog', 'UserName', 'Password', 'ApplicationIntent', 'ConnectTimeout', 'RetryCount', 'Encrypt']

const resolved = computed(() =>
  keys.map((k) => {
    let from: Level | '—' = '—'
    let value = '(not set)'
    for (const s of chain.value) {
      if (k in s.values) {
        from = s.level
        value = s.values[k]
      }
    }
    return { key: k, value, from }
  })
)

const levelClass = (l: Level | '—') => `lv-${l.toLowerCase().replace(' ', '')}`
</script>

<template>
  <div class="hier">
    <div class="pick">
      <span>Resolve the settings for</span>
      <select v-model.number="shardId"><option :value="0">shard 0</option><option :value="1">shard 1</option></select>
      <select v-model="kind"><option>Read</option><option>Write</option></select>
      <span>endpoint</span>
    </div>
    <div class="cols">
      <ol class="levels">
        <li v-for="s in chain" :key="s.level" :class="levelClass(s.level)">
          <div class="lvl">{{ s.level }}<small v-if="s.level === 'Shard'"> {{ shardId }}</small><small v-if="s.level === 'Endpoint'"> {{ kind }}Connection</small></div>
          <div class="vals">
            <code v-for="(v, k) in s.values" :key="k">{{ k }} = {{ v }}</code>
            <span v-if="!Object.keys(s.values).length" class="none">inherits everything</span>
          </div>
        </li>
      </ol>
      <table class="res">
        <thead><tr><th>Setting</th><th>Resolved value</th><th>Comes from</th></tr></thead>
        <tbody>
          <tr v-for="r in resolved" :key="r.key">
            <td><code>{{ r.key }}</code></td>
            <td>{{ r.value }}</td>
            <td><span class="tag" :class="levelClass(r.from)">{{ r.from }}</span></td>
          </tr>
        </tbody>
      </table>
    </div>
    <p class="note">Each level may set any connection property. The most specific level that sets a property wins; everything else is inherited.</p>
  </div>
</template>

<style scoped>
.hier {
  border: 1px solid var(--vp-c-divider);
  border-radius: 14px;
  padding: 18px;
  margin: 20px 0 28px;
  background: var(--vp-c-bg-soft);
}
.pick {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  align-items: center;
  font-size: 14px;
  margin-bottom: 14px;
}
select {
  border: 1px solid var(--vp-c-divider);
  border-radius: 8px;
  padding: 4px 8px;
  background: var(--vp-c-bg);
  color: var(--vp-c-text-1);
  font-weight: 600;
}
.cols {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 18px;
}
@media (max-width: 760px) {
  .cols {
    grid-template-columns: 1fr;
  }
}
.levels {
  margin: 0;
  padding: 0;
  list-style: none;
  display: flex;
  flex-direction: column;
  gap: 6px;
}
.levels li {
  border-left: 4px solid var(--c, var(--vp-c-divider));
  background: var(--vp-c-bg);
  border-radius: 0 8px 8px 0;
  padding: 8px 10px;
}
.levels li + li {
  margin-left: 14px;
}
.lvl {
  font-weight: 700;
  font-size: 13px;
}
.lvl small {
  font-weight: 500;
  color: var(--vp-c-text-2);
}
.vals {
  display: flex;
  flex-wrap: wrap;
  gap: 4px 8px;
  margin-top: 4px;
}
.vals code {
  font-size: 11.5px;
  background: var(--vp-c-bg-soft);
  padding: 1px 6px;
  border-radius: 4px;
}
.none {
  font-size: 12px;
  color: var(--vp-c-text-3);
  font-style: italic;
}
.res {
  width: 100%;
  border-collapse: collapse;
  font-size: 13px;
}
.res th,
.res td {
  text-align: left;
  padding: 6px 8px;
  border-bottom: 1px solid var(--vp-c-divider);
}
.res th {
  font-size: 12px;
  color: var(--vp-c-text-2);
}
.tag {
  display: inline-block;
  font-size: 11px;
  font-weight: 600;
  padding: 1px 8px;
  border-radius: 999px;
  color: #141210;
  background: var(--c, var(--vp-c-divider));
  white-space: nowrap;
}
.lv-global { --c: #9aa1ab; }
.lv-shardset { --c: #c9a85a; }
.lv-read { --c: #7fb8d8; }
.lv-write { --c: #e2a26b; }
.lv-shard { --c: #a8c686; }
.lv-endpoint { --c: #ebdc9e; }
.lv-— { --c: var(--vp-c-divider); }
.note {
  margin: 12px 0 0;
  font-size: 13px;
  color: var(--vp-c-text-2);
}
</style>
