<script setup lang="ts">
import { ref } from 'vue'
const arity = ref(1)
const parts = [
  { name: 'ShardId', type: 'short', note: 'Which database holds the record. Assigned by client configuration.' },
  { name: 'RecordId', type: 'TRecord', note: 'The record\'s own key within its shard: int, long, Guid, string, DateTime…' },
  { name: 'ChildId', type: 'TChild', note: 'Second key column, for a compound primary key (an order and its line).' },
  { name: 'GrandChildId', type: 'TGrandChild', note: 'Third key column.' },
  { name: 'GreatGrandChildId', type: 'TGreatGrandChild', note: 'Fourth key column.' }
]
const decl = () => {
  const g = ['TRecord', 'TChild', 'TGrandChild', 'TGreatGrandChild'].slice(0, arity.value)
  return `ShardKey<${g.join(', ')}>`
}
const example = () => ['int', 'short', 'byte', 'byte'].slice(0, arity.value)
</script>

<template>
  <div class="anat">
    <div class="head">
      <code class="decl">{{ decl() }}</code>
      <label>
        key columns
        <input type="range" min="1" max="4" v-model.number="arity" />
        <b>{{ arity }}</b>
      </label>
    </div>
    <div class="boxes">
      <div class="box meta" title="Compact code for the .NET types of the key columns, embedded in every serialized form">
        <span class="n">type metadata</span><span class="t">embedded</span>
      </div>
      <div v-for="(p, i) in parts.slice(0, arity + 1)" :key="p.name" class="box" :class="{ shard: i === 0 }">
        <span class="n">{{ p.name }}</span>
        <span class="t">{{ i === 0 ? 'short' : example()[i - 1] }}</span>
        <span class="note">{{ p.note }}</span>
      </div>
    </div>
    <div class="forms">
      <div><b>Equality</b> compares the shard id and the key values. The struct cannot be null: an absent key is <code>Empty</code>.</div>
      <div><b>Serialized forms:</b> binary (<code>ToArray()</code>), UTF-8 (<code>ToUtf8()</code>) and the URL-safe external string (<code>ToExternalString()</code>), each prefixed with the type metadata so bytes for one key shape cannot be read as another.</div>
    </div>
  </div>
</template>

<style scoped>
.anat {
  border: 1px solid var(--vp-c-divider);
  border-radius: 14px;
  padding: 18px;
  margin: 20px 0 28px;
  background: var(--vp-c-bg-soft);
}
.head {
  display: flex;
  justify-content: space-between;
  align-items: center;
  flex-wrap: wrap;
  gap: 12px;
  margin-bottom: 14px;
}
.decl {
  font-size: 15px;
  font-weight: 700;
  color: var(--vp-c-brand-1);
  background: var(--vp-c-bg);
  padding: 6px 10px;
  border-radius: 8px;
}
label {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  font-size: 13px;
  color: var(--vp-c-text-2);
}
.boxes {
  display: flex;
  gap: 6px;
  flex-wrap: wrap;
}
.box {
  flex: 1 1 140px;
  min-width: 120px;
  border: 1px solid var(--vp-c-divider);
  border-radius: 10px;
  padding: 10px 12px;
  background: var(--vp-c-bg);
  display: flex;
  flex-direction: column;
  gap: 2px;
}
.box.shard {
  border-color: var(--vp-c-brand-2);
  background: var(--vp-c-brand-soft);
}
.box.meta {
  flex: 0 0 110px;
  min-width: 100px;
  border-style: dashed;
}
.n {
  font-weight: 700;
  font-size: 13px;
}
.t {
  font-family: var(--vp-font-family-mono);
  font-size: 12px;
  color: var(--vp-c-brand-1);
}
.note {
  font-size: 11.5px;
  color: var(--vp-c-text-2);
  line-height: 1.4;
  margin-top: 4px;
}
.forms {
  margin-top: 14px;
  display: grid;
  gap: 6px;
  font-size: 13px;
  color: var(--vp-c-text-2);
}
</style>
