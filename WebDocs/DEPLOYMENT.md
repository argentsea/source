# Deploying the documentation site

The VitePress site in `WebDocs/` is published to **https://docs.argentsea.com** by the GitHub Actions workflow
[`.github/workflows/docs.yml`](../.github/workflows/docs.yml). This document covers what the workflow does and the
one-time switchover a repository owner performs to move the domain from the old DocFx site to this one.

## How deployment works

The workflow follows the [VitePress GitHub Pages recipe](https://vitepress.dev/guide/deploy#github-pages):

- **Trigger.** A push to `main` that changes anything under `WebDocs/**` (or the workflow file itself), or a manual
  run from the Actions tab (`workflow_dispatch`).
- **Build job.** `ubuntu-latest`, Node 20 with the npm cache keyed on `WebDocs/package-lock.json`; runs `npm ci` and
  `npm run docs:build` in `WebDocs`, then uploads `WebDocs/docs/.vitepress/dist` with `actions/upload-pages-artifact`.
- **Deploy job.** Runs `actions/deploy-pages` with the `pages: write` and `id-token: write` permissions against the
  `github-pages` environment. Nothing is committed to a `gh-pages` branch; the artifact is served directly.

Two files in the source tree make the deployed site work on the custom domain:

- `docs/public/CNAME` contains `docs.argentsea.com`. VitePress copies `docs/public` into the output root, so the
  deployed artifact carries the custom-domain marker and GitHub Pages keeps the domain across deployments.
- `docs/.vitepress/config.mts` sets `sitemap.hostname` to `https://docs.argentsea.com` and `cleanUrls: true`. GitHub
  Pages serves `guides/mapping.html` for `/guides/mapping`, so no rewrite rules are needed.

Dead internal links fail the build, which is deliberate: a deployment never ships a broken link.

## Current state (before the switchover)

| What | Where it is served from | Notes |
| --- | --- | --- |
| https://docs.argentsea.com | Repository `argentsea/docs`, branch `master`, GitHub Pages | Old DocFx output: `api/`, `api-sql/`, `api-pg/`, `tutorials/`, `CNAME` = `docs.argentsea.com`. Last pushed 2025-02-27. |
| https://argentsea.com and https://www.argentsea.com | Repository `argentsea/argentsea.github.io`, GitHub Pages | Landing page; mirrors the `ArgentSea.Home` folder in this repository (`ArgentSea.Home/CNAME` = `argentsea.com`). |
| DNS | `docs`, `www` and the apex all resolve to `argentsea.github.io` | No DNS change is needed for the switchover below. |

GitHub Pages allows **one custom domain per repository**, and a given domain can be attached to only one repository
at a time. That is why the old repository must release `docs.argentsea.com` before this repository can claim it.

## One-time switchover (repository owner)

Do these steps in order. Steps 1 and 2 need admin access to the two repositories; step 3 needs write access to this
one.

### 1. Release the domain from `argentsea/docs`

Either of these works; the first keeps the old output browsable at `https://argentsea.github.io/docs/` as a fallback.

- **Remove the CNAME.** In `argentsea/docs` (branch `master`) delete the root `CNAME` file and push. GitHub Pages
  drops the custom domain within a minute or two. Confirm on that repository's **Settings → Pages**: the
  "Custom domain" box must be empty.
- **Or disable Pages.** In `argentsea/docs` **Settings → Pages → Build and deployment → Source**, choose **None**.

Until this step is done, GitHub refuses to attach `docs.argentsea.com` to `argentsea/source` with the message
"The custom domain is already taken".

### 2. Point `argentsea/source` Pages at the workflow

In `argentsea/source` → **Settings → Pages**:

1. Under **Build and deployment → Source**, choose **GitHub Actions** (not "Deploy from a branch").
2. Under **Custom domain**, enter `docs.argentsea.com` and click **Save**. GitHub runs a DNS check; because the
   `docs` CNAME record already points at `argentsea.github.io` it should pass straight away. If it shows
   "Domain's DNS record could not be retrieved", wait a few minutes and click **Check again**.
3. Tick **Enforce HTTPS**. The checkbox is greyed out until GitHub has provisioned the certificate for the domain,
   which can take from a few minutes up to an hour after step 2. Come back and tick it if it is not yet available.

No `github-pages` environment needs to be created by hand: the first run of the workflow creates it. If the
repository restricts which branches may deploy to environments, allow `main` to deploy to `github-pages`
(**Settings → Environments → github-pages → Deployment branches and tags**).

### 3. Run the workflow

- **Actions → docs → Run workflow** (branch `main`), or push any change under `WebDocs/` to `main`.
- The `deploy` job prints the page URL in its summary. The first deployment after setting the domain can take a
  couple of minutes longer than later ones.

### 4. Verify

- `https://docs.argentsea.com/` shows the VitePress home page (the dark hero, not the DocFx table of contents).
- `https://docs.argentsea.com/guides/mapping` resolves (clean URL) and `https://docs.argentsea.com/sitemap.xml`
  lists `https://docs.argentsea.com/...` URLs.
- `https://docs.argentsea.com/CNAME` returns `docs.argentsea.com`.
- The padlock is present and `http://docs.argentsea.com/` redirects to `https://`.
- In **Settings → Pages** the banner reads "Your site is live at https://docs.argentsea.com/".

If the site renders but assets 404, the artifact was deployed under a sub-path; the workflow uploads `dist` as the
site root and the site has no `base` setting, so this should not happen unless the Pages source was left on
"Deploy from a branch".

## Retiring the landing page (argentsea.com)

The VitePress home page replaces the `ArgentSea.Home` landing page. Because GitHub Pages allows one custom domain
per repository, **pick one** of the following; do not do both.

### Option A (recommended): keep docs.argentsea.com here, redirect the apex

Leave `argentsea/argentsea.github.io` as the owner of `argentsea.com` / `www.argentsea.com` and replace its
`index.html` with a redirect. A meta refresh plus a canonical link is enough for browsers and for search engines:

```html
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <title>ArgentSea</title>
  <link rel="canonical" href="https://docs.argentsea.com/">
  <meta http-equiv="refresh" content="0; url=https://docs.argentsea.com/">
  <meta name="robots" content="noindex">
</head>
<body>
  <p>ArgentSea has moved to <a href="https://docs.argentsea.com/">docs.argentsea.com</a>.</p>
</body>
</html>
```

Keep that repository's `CNAME` (`argentsea.com`) so the apex and `www` continue to resolve. Once the redirect is in
place, delete the rest of the old landing-page files there and remove the `ArgentSea.Home` folder from this
repository (it is a gitlink to a nested checkout of `argentsea/argentsea.github.io`, not a submodule, so
`git rm ArgentSea.Home` is all that is required).

### Option B: move the apex onto this site

Attach `argentsea.com` to `argentsea/source` instead of `docs.argentsea.com`:

1. Release `argentsea.com` from `argentsea/argentsea.github.io` (delete its `CNAME` or disable Pages there).
2. Change `WebDocs/docs/public/CNAME` and `sitemap.hostname` in `config.mts` to `argentsea.com`, and update the
   Pages custom domain to match. GitHub Pages serves `www.argentsea.com` as a redirect to the apex automatically
   when the apex is the custom domain.
3. Because this repository can then no longer own `docs.argentsea.com`, repurpose `argentsea/docs` as a redirect
   repository (its `CNAME` stays `docs.argentsea.com`; replace its content with a `404.html` and `index.html` that
   redirect to `https://argentsea.com/`).

Option B costs an extra redirect for every old `docs.argentsea.com` deep link, which is why Option A is preferred.

## Old `/api/...` URLs

The DocFx API reference (`/api/`, `/api-sql/`, `/api-pg/`) and the `/tutorials/...` pages are not reproduced in
this site, so those URLs return 404 after the switchover. Two ways to soften that, either of which can be done
later:

- **Generate an API reference into the site.** Run `docfx` against the XML documentation comments (the
  `ArgentSea.DocFx` folder has the old configuration) with its output directory set to `WebDocs/docs/public/api`.
  VitePress copies `public/` verbatim, so the generated HTML would be served at `/api/...` alongside the site. A
  workflow step before `npm run docs:build` can do this on every deployment, at the cost of a .NET SDK setup in the
  build job.
- **Add redirects.** GitHub Pages has no server-side redirect support, so a redirect is a static HTML page per old
  URL (the same meta-refresh pattern as above) placed under `WebDocs/docs/public/`, or a custom `404.html` in
  `public/` that maps known old paths to new pages with a small script. Either is cheap for the handful of
  `/tutorials/...` pages; for the hundreds of `/api/...` pages, generating the reference is the better answer.
