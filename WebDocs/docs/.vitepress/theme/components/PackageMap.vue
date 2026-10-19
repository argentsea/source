<template>
  <div class="pm">
    <div class="row app">
      <div class="pkg you">Your application<small>models with mapping attributes · a <code>Queries</code> class · data classes that inject the collections</small></div>
    </div>
    <div class="row orleans">
      <div class="pkg">ArgentSea.Orleans.Sql<small>grain storage, clustering, reminders on SQL Server</small></div>
      <div class="pkg abstract">ArgentSea.Orleans<small>abstract Orleans provider over any ArgentSea provider</small></div>
    </div>
    <div class="row providers">
      <div class="pkg">ArgentSea.Sql<small>SQL Server: options, <code>MapToSql*</code> attributes, parameter helpers, TVPs, transient-error rules · Microsoft.Data.SqlClient</small></div>
      <div class="pkg">ArgentSea.Pg<small>PostgreSQL: options, <code>MapToPg*</code> attributes, parameter helpers, COPY, transient-error rules · Npgsql</small></div>
    </div>
    <div class="row core">
      <div class="pkg">ArgentSea (core)<small>configuration hierarchy · Databases &amp; ShardSets · fan-out · resilience (Polly) · batches · ShardKey · the Mapper · logging</small></div>
    </div>
    <div class="row ado">
      <div class="pkg plain">ADO.NET (System.Data.Common)</div>
    </div>
  </div>
</template>

<style scoped>
.pm {
  display: flex;
  flex-direction: column;
  gap: 8px;
  margin: 20px 0 28px;
}
.row {
  display: grid;
  gap: 8px;
}
.row.orleans,
.row.providers {
  grid-template-columns: 1fr 1fr;
}
@media (max-width: 640px) {
  .row.orleans,
  .row.providers {
    grid-template-columns: 1fr;
  }
}
.pkg {
  border: 1px solid var(--vp-c-divider);
  border-radius: 10px;
  padding: 12px 14px;
  background: var(--vp-c-bg-soft);
  font-weight: 700;
  font-size: 14px;
}
.pkg small {
  display: block;
  font-weight: 400;
  font-size: 12.5px;
  color: var(--vp-c-text-2);
  margin-top: 3px;
  line-height: 1.45;
}
.pkg.you {
  background: linear-gradient(120deg, #c9a85a, #ebdc9e);
  color: #141210;
  border-color: transparent;
}
.pkg.you small {
  color: #3b3222;
}
.pkg.you code {
  color: #141210;
  background: rgba(0, 0, 0, 0.08);
}
.pkg.abstract {
  border-style: dashed;
}
.row.core .pkg {
  border-color: var(--vp-c-brand-2);
  background: var(--vp-c-brand-soft);
}
.pkg.plain {
  text-align: center;
  font-weight: 600;
  color: var(--vp-c-text-2);
  background: transparent;
}
</style>
