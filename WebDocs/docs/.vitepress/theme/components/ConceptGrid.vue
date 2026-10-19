<script setup lang="ts">
const concepts = [
  { t: 'Databases & shard sets', h: '#databases-and-shard-sets', d: 'Two injectable collections hold every connection your app can use.' },
  { t: 'Read / write pairs', h: '#read-and-write-connections', d: 'Every database and shard has two endpoints. You pick one; ArgentSea never guesses.' },
  { t: 'Query', h: '#queries', d: 'A named handle for a stored procedure or a deployed SQL file. SQL is never generated.' },
  { t: 'The Mapper', h: '#the-mapper', d: 'Attributes on your model, compiled once into delegates that run at ADO.NET speed.' },
  { t: 'ShardKey', h: '#shardkey', d: 'A record identity that knows which shard holds the record.' },
  { t: 'Fan-out', h: '#fan-out', d: 'ReadAll, ReadFirst and Write run one query across every shard concurrently.' },
  { t: 'Resilience', h: '#resilience', d: 'Retries with backoff and a circuit breaker on every endpoint, logged.' },
  { t: 'Batches', h: '#batches', d: 'The one place a client transaction is opened, for multi-step work.' }
]
</script>

<template>
  <div class="cg">
    <a v-for="c in concepts" :key="c.t" :href="c.h" class="as-card item">
      <strong>{{ c.t }}</strong>
      <span>{{ c.d }}</span>
    </a>
  </div>
</template>

<style scoped>
.cg {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
  gap: 10px;
  margin: 16px 0 28px;
}
.item {
  padding: 14px 16px;
  text-decoration: none !important;
  color: inherit;
  display: flex;
  flex-direction: column;
  gap: 4px;
}
.item strong {
  font-size: 14px;
}
.item span {
  font-size: 13px;
  color: var(--vp-c-text-2);
  line-height: 1.45;
}
</style>
