<template>
  <section class="plat">
    <div class="as-section">
      <span class="as-eyebrow">Runs on</span>
      <h2 class="as-section-title">Your databases. Your cloud. Your .NET.</h2>
      <div class="grid">
        <div class="as-card">
          <img src="/images/sql-server-h.svg" alt="Microsoft SQL Server" class="logo on-light" />
          <img src="/images/sql-server-h-dark.svg" alt="Microsoft SQL Server" class="logo on-dark" />
          <ul>
            <li>SQL Server 2017 and later, SQL Server Express</li>
            <li>Azure SQL Database, including read scale-out and geo-replicas</li>
            <li>Availability Group readable secondaries</li>
          </ul>
          <a href="https://www.nuget.org/packages/ArgentSea.Sql/" target="_blank" rel="noreferrer">ArgentSea.Sql on NuGet →</a>
        </div>
        <div class="as-card">
          <img src="/images/postgresql-h.svg" alt="PostgreSQL" class="logo on-light" />
          <img src="/images/postgresql-h-dark.svg" alt="PostgreSQL" class="logo on-dark" />
          <ul>
            <li>PostgreSQL with hot standbys</li>
            <li>Amazon RDS and Aurora, Azure Database for PostgreSQL</li>
            <li>Google Cloud SQL for PostgreSQL</li>
          </ul>
          <a href="https://www.nuget.org/packages/ArgentSea.Pg/" target="_blank" rel="noreferrer">ArgentSea.Pg on NuGet →</a>
        </div>
        <div class="as-card">
          <img src="/images/OrleansLogo.svg" alt="Microsoft Orleans" class="logo on-light" />
          <img src="/images/OrleansLogo-dark.svg" alt="Microsoft Orleans" class="logo on-dark" />
          <ul>
            <li>Grain persistence in normal relational tables</li>
            <li>Clustering membership and gateway discovery</li>
            <li>Reminders, on SQL Server or PostgreSQL</li>
          </ul>
          <a href="/guides/orleans">Read the Orleans guide →</a>
        </div>
      </div>
      <p class="fine">
        Built on .NET 10 with Microsoft.Data.SqlClient and Npgsql. Version 1 of ArgentSea ran in production for years and handled
        billions of dollars in transactions; version 2 is the current release. Open source under the MIT license.
      </p>
    </div>
  </section>
</template>

<style scoped>
.plat {
  background: var(--vp-c-bg-alt);
  border-top: 1px solid var(--vp-c-divider);
}
.grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(280px, 1fr));
  gap: 18px;
  margin-top: 28px;
}
/* Original colour logos. The -dark copies differ only in the wordmark colour, so the text stays readable on dark
   cards. */
.logo {
  display: block;
  height: 40px;
  margin-bottom: 12px;
}
.logo.on-dark,
.dark .logo.on-light {
  display: none;
}
.dark .logo.on-dark {
  display: block;
}
ul {
  margin: 0 0 14px;
  padding-left: 18px;
  color: var(--vp-c-text-2);
  font-size: 14.5px;
  line-height: 1.6;
}
a {
  font-weight: 600;
  color: var(--vp-c-brand-1);
  font-size: 14px;
}
.fine {
  margin-top: 28px;
  font-size: 14px;
  color: var(--vp-c-text-3);
  max-width: 820px;
}
</style>
