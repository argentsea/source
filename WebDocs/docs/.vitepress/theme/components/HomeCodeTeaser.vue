<template>
  <section class="as-section">
    <div class="cols">
      <div class="copy">
        <span class="as-eyebrow">What it looks like</span>
        <h2 class="as-section-title">Attributes in, models out, SQL where it belongs.</h2>
        <p>
          A model declares how its properties map to parameters and columns. A <code>Query</code> names a stored
          procedure or a deployed SQL file. One call on a <code>Read</code> or <code>Write</code> connection, or on a
          whole shard set, does the rest: open, parameterize, execute, retry if needed, map, log.
        </p>
        <ol class="steps">
          <li>Configure connections in <code>appsettings.json</code>.</li>
          <li>Register with <code>AddSqlServices</code> or <code>AddPgServices</code>.</li>
          <li>Decorate a model. Name a query. Call a method.</li>
        </ol>
        <a class="link" href="/getting-started/quickstart-database">Follow the quickstart →</a>
      </div>
      <div class="as-codepanel">
        <slot />
      </div>
    </div>
  </section>
</template>

<style scoped>
.cols {
  display: grid;
  grid-template-columns: 0.9fr 1.1fr;
  gap: 40px;
  align-items: start;
}
@media (max-width: 900px) {
  .cols {
    grid-template-columns: 1fr;
  }
}
.copy p {
  color: var(--vp-c-text-2);
  line-height: 1.65;
}
.steps {
  margin: 16px 0 22px;
  padding-left: 20px;
  color: var(--vp-c-text-2);
  line-height: 1.7;
}
.link {
  font-weight: 600;
  color: var(--vp-c-brand-1);
}
</style>
