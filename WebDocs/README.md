# ArgentSea WebDocs

The documentation website for ArgentSea, built with [VitePress](https://vitepress.dev). It replaces the
`ArgentSea.DocFx` tutorials and the `ArgentSea.Home` landing page with one site: a compelling introduction for
developers who have never seen ArgentSea, quickstarts, concept guides, and generated reference material.

## Run it locally

```bash
cd WebDocs
npm install
npm run docs:dev
```

Then open the URL VitePress prints (usually http://localhost:5173). Edits to Markdown and Vue files hot-reload.

## Build for deployment

```bash
npm run docs:build
```

The static site lands in `docs/.vitepress/dist`; `npm run docs:preview` serves it locally.

Production deployment is automated: the GitHub Actions workflow
[`.github/workflows/docs.yml`](../.github/workflows/docs.yml) builds this folder on every push to `main` that touches
`WebDocs/**` (or on a manual run) and publishes `dist` to GitHub Pages at https://www.argentsea.com. The custom
domain is set in the repository's Settings → Pages; `docs/public/CNAME` only records it. See
[`DEPLOYMENT.md`](DEPLOYMENT.md) for how the workflow works and for the one-time Pages switchover.

The site uses clean URLs (`/guides/mapping`, no `.html`), so any other static host must serve `guides/mapping.html`
for that path or be configured to rewrite; GitHub Pages and most static hosts do this by default.

## Layout

```
WebDocs/
  AUTHORING.md               the authoring brief: audience, site map, API facts, style rules
  README.md                  this file
  package.json
  scripts/build-glossary.mjs generates docs/reference/glossary.md from ../specs/glossary
  docs/
    index.md                 home page (custom Vue sections)
    introduction/            what/why/concepts/principles/architecture
    getting-started/         installation, two quickstarts, testing
    guides/                  configuration, resilience, logging, queries, mapping, sharding, batches, orleans
    reference/               glossary (generated), faq, whats-new, platforms
    public/CNAME             intended custom domain (www.argentsea.com); Settings → Pages is authoritative
    public/BingSiteAuth.xml  Bing Webmaster Tools verification (copied from ArgentSea.Home)
    public/images/           logos, icons and the hero moon photo (from ArgentSea.Home/images)
    .vitepress/
      config.mts             site title, nav, sidebar, search, footer
      theme/                 brand CSS and the custom components
```

## Editing pages

Read `AUTHORING.md` first. It lists the facts every page must agree on (verified against the current source, which
differs from the old DocFx docs in several places) and the style rules. Pages are plain Markdown with VitePress
extensions; use a code group to show SQL Server and PostgreSQL side by side:

````md
::: code-group
```csharp [SQL Server]
[MapToSqlInt("@CustomerId")]
```
```csharp [PostgreSQL]
[MapToPgInteger("customer_id")]
```
:::
````

Internal links are root-absolute without an extension (`/guides/mapping`). Dead links fail the build, which is the
point.

### Interactive components

Custom components registered in `docs/.vitepress/theme/index.ts` can be dropped into any page as a tag on its own
line:

| Tag | What it shows | Used on |
| --- | --- | --- |
| `<FanOutDemo />` | Animated ReadAll / ReadFirst / Write fan-out across four shards | Sharding guide |
| `<ConfigHierarchy />` | Pick a shard and endpoint, see which level each setting resolves from | Configuration guide |
| `<ShardKeyAnatomy />` | The parts of a `ShardKey`, with a slider for 1–4 key columns | Sharding guide |
| `<PackageMap />` | The package layering diagram | Architecture |
| `<ConceptGrid />` | Jump cards for the core concepts | Core concepts |
| `<GlossaryIndex :letters="[...]" />` | Letter jump bar | Glossary (generated) |

### Glossary

`docs/reference/glossary.md` is generated; do not edit it by hand. When the spec corpus changes, run:

```bash
npm run glossary
```

## Retiring the old docs

This site replaces both the `argentsea.com` landing page and the DocFx site at `docs.argentsea.com`, and is served
at `www.argentsea.com`. `ArgentSea.DocFx` and `ArgentSea.Home` remain in the repository until it is live.
[`DEPLOYMENT.md`](DEPLOYMENT.md) describes the switchover in order: release `argentsea.com` from the old
landing-page repository, point this repository's Pages settings at the workflow with `www.argentsea.com`, run the
workflow, turn `docs.argentsea.com` into a redirect, and verify.

The DocFx API reference (`/api/...`) is not reproduced here. The redirect step can keep the old pages reachable on
`docs.argentsea.com`. If a current API reference is wanted alongside this site, generate it with `docfx` from the XML
comments into `docs/public/api` and link it from the nav; `DEPLOYMENT.md` covers that and the redirect alternative.
