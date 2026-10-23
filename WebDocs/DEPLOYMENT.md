# Deploying the website

The VitePress site in `WebDocs/` is the ArgentSea website: home page and documentation in one. It is published to
**https://www.argentsea.com** by the GitHub Actions workflow
[`.github/workflows/docs.yml`](../.github/workflows/docs.yml). This document covers what the workflow does and the
one-time switchover a repository owner performs to move the domain from the old landing page to this site.

## How deployment works

The workflow follows the [VitePress GitHub Pages recipe](https://vitepress.dev/guide/deploy#github-pages):

- **Trigger.** A push to `main` that changes anything under `WebDocs/**` (or the workflow file itself), or a manual
  run from the Actions tab (`workflow_dispatch`). GitHub lists the workflow in the Actions tab only once the file is
  on `main` on GitHub.
- **Build job.** `ubuntu-latest`, Node 20 with the npm cache keyed on `WebDocs/package-lock.json`; runs `npm ci` and
  `npm run docs:build` in `WebDocs`, then uploads `WebDocs/docs/.vitepress/dist` with `actions/upload-pages-artifact`.
- **Deploy job.** Runs `actions/deploy-pages` with the `pages: write` and `id-token: write` permissions against the
  `github-pages` environment. Nothing is committed to a `gh-pages` branch; the artifact is served directly.

The custom domain is decided by **Settings → Pages → Custom domain** in `argentsea/source`. When Pages deploys from
a workflow, GitHub ignores any `CNAME` file in the artifact. `docs/public/CNAME` (`www.argentsea.com`) is kept only
as a record of the intended domain and for hosts that read it; changing it does not move the site.

Other host-related files:

- `docs/.vitepress/config.mts` sets `sitemap.hostname` to `https://www.argentsea.com` and `cleanUrls: true`. GitHub
  Pages serves `guides/mapping.html` for `/guides/mapping`, so no rewrite rules are needed.
- `docs/public/BingSiteAuth.xml` is the Bing Webmaster Tools verification file, copied from the old landing page so
  the verification survives the move.

Dead internal links fail the build, which is deliberate: a deployment never ships a broken link.

## Domains before and after

| Domain | Before | After |
| --- | --- | --- |
| `www.argentsea.com` | Old landing page, repository `argentsea/argentsea.github.io` (its `CNAME` is `argentsea.com`, so `www` redirects to the apex) | **This site**, repository `argentsea/source` |
| `argentsea.com` | Old landing page | Redirects to `www.argentsea.com` (GitHub does this automatically) |
| `docs.argentsea.com` | Old DocFx site, repository `argentsea/docs` (`CNAME` already deleted) | Redirect repository, see step 4 |

DNS needs no change: `www`, the apex and `docs` all already resolve to GitHub Pages.

GitHub Pages allows **one custom domain per repository**, and a domain can be attached to only one repository at a
time. When a repository's custom domain is `www.argentsea.com` and the apex also points at GitHub Pages, GitHub
serves `argentsea.com` as a redirect to `www`. That is why the old landing-page repository must release
`argentsea.com` first.

## One-time switchover (repository owner)

Do these steps in order. They need admin access to the repositories involved.

### 1. Release the domain from the old landing page

In `argentsea/argentsea.github.io` → **Settings → Pages**, under **Custom domain**, click **Remove**. For a site that
deploys from a branch, GitHub commits the deletion of the `CNAME` file for you. Reload the page and check the box is
empty.

Until this is done, GitHub refuses to attach `www.argentsea.com` to `argentsea/source` with "The custom domain is
already taken".

### 2. Point `argentsea/source` Pages at the workflow

In `argentsea/source` → **Settings → Pages**:

1. Under **Build and deployment → Source**, choose **GitHub Actions** (not "Deploy from a branch").
2. Under **Custom domain**, enter `www.argentsea.com` and click **Save**. If `docs.argentsea.com` is already there
   from an earlier attempt, replace it. GitHub runs a DNS check; if it shows "Domain's DNS record could not be
   retrieved", wait a few minutes and click **Check again**.
3. Tick **Enforce HTTPS**. The checkbox stays greyed out until GitHub has issued the certificate, which can take from
   a few minutes up to an hour after step 2. Come back and tick it if it is not yet available.

The first workflow run creates the `github-pages` environment. If the repository restricts which branches may
deploy, allow `main` under **Settings → Environments → github-pages → Deployment branches and tags**.

### 3. Run the workflow

- Push `main` with the workflow on it. The push itself starts a run because it changes `WebDocs/`.
- Later, use **Actions → docs → Run workflow** (branch `main`) to redeploy without a change.
- If a run's deploy job failed because step 2 was not done yet, open the run and click **Re-run failed jobs**.

### 4. Keep `docs.argentsea.com` working

Old links and search results point at `docs.argentsea.com`. A DNS name that points at GitHub Pages but is claimed by
no repository serves a GitHub 404, and anyone who claims it first could serve their own content there. So claim it
again with a redirect. In `argentsea/docs`:

1. Re-add a root `CNAME` file containing `docs.argentsea.com`, or enter it again in **Settings → Pages → Custom
   domain**.
2. Replace `index.html` with a redirect to the new home page (below).
3. Add a `404.html` with the same redirect, so any other old path lands on the new site instead of a 404.
4. Decide what to do with the old pages. Leaving `api/`, `api-sql/` and `api-pg/` in place keeps the old DocFx API
   reference reachable at its old URLs, since the new site has no API reference. Deleting `tutorials/` and
   `articles/` sends those old paths to the new home page through `404.html`, instead of serving outdated content.

```html
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <title>ArgentSea</title>
  <link rel="canonical" href="https://www.argentsea.com/">
  <meta http-equiv="refresh" content="0; url=https://www.argentsea.com/">
  <meta name="robots" content="noindex">
</head>
<body>
  <p>ArgentSea has moved to <a href="https://www.argentsea.com/">www.argentsea.com</a>.</p>
</body>
</html>
```

To make the step even safer, verify `argentsea.com` for the organization under **Organization settings → Pages →
Add a domain**. A verified domain and its subdomains can then only be claimed by repositories in the organization.

### 5. Verify

- `https://www.argentsea.com/` shows the new home page with the moon over the sea.
- `https://argentsea.com/` redirects to `https://www.argentsea.com/`.
- `https://www.argentsea.com/guides/mapping` resolves (clean URL) and `https://www.argentsea.com/sitemap.xml` lists
  `https://www.argentsea.com/...` URLs.
- `https://www.argentsea.com/BingSiteAuth.xml` returns the verification file.
- The padlock is present and `http://www.argentsea.com/` redirects to `https://`.
- `https://docs.argentsea.com/` redirects to the new home page.
- In `argentsea/source` **Settings → Pages** the banner reads "Your site is live at https://www.argentsea.com/".

If the site renders but scripts and styles 404, the Pages source was left on "Deploy from a branch". The workflow
uploads `dist` as the site root and the site has no `base` setting.

## Retiring the old sources

Once the new site is live:

- `ArgentSea.Home` in this repository can be removed. It is a gitlink to a nested checkout of
  `argentsea/argentsea.github.io` with no `.gitmodules` entry, so `git rm ArgentSea.Home` is all that is required.
- `argentsea/argentsea.github.io` can be archived, or left as the organization's default Pages site with no custom
  domain.
- `ArgentSea.DocFx` can stay until an API reference decision is made (next section).

## Old API reference URLs

The DocFx API reference (`/api/`, `/api-sql/`, `/api-pg/`) is not reproduced in this site. Step 4 keeps the old
pages reachable on `docs.argentsea.com`, but they describe the old release. Two ways to replace them later:

- **Generate an API reference into this site.** Run `docfx` against the XML documentation comments (the
  `ArgentSea.DocFx` folder has the old configuration) with its output directory set to `WebDocs/docs/public/api`.
  VitePress copies `public/` verbatim, so the generated HTML would be served at `/api/...` alongside the site. A
  workflow step before `npm run docs:build` can do this on every deployment, at the cost of a .NET SDK setup in the
  build job. The `docs.argentsea.com` 404 page could then send `/api/...` paths to the same path on `www`.
- **Redirect instead.** GitHub Pages has no server-side redirects, so this means a static meta-refresh page per old
  URL, or a `404.html` script that maps known old paths to new pages.
