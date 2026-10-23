import { defineConfig } from 'vitepress'

export default defineConfig({
  title: 'ArgentSea',
  description:
    'ArgentSea is a .NET open source data-access framework built on ADO.NET for simplicity, high performance and global scale: sharding, tenant isolation, read scale-out, compiled mapping, resilience and Microsoft Orleans.',
  lang: 'en-US',
  cleanUrls: true,
  lastUpdated: false,
  appearance: 'dark',
  head: [
    ['link', { rel: 'icon', href: '/images/favicon.ico' }],
    ['link', { rel: 'preconnect', href: 'https://fonts.googleapis.com' }],
    ['link', { rel: 'preconnect', href: 'https://fonts.gstatic.com', crossorigin: '' }],
    [
      'link',
      {
        rel: 'stylesheet',
        href: 'https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700&family=JetBrains+Mono:wght@400;600&family=Source+Serif+4:opsz,wght@8..60,500;8..60,600;8..60,700&display=swap'
      }
    ],
    ['meta', { name: 'theme-color', content: '#0b0b0d' }],
    ['meta', { property: 'og:type', content: 'website' }],
    ['meta', { property: 'og:title', content: 'ArgentSea' }],
    [
      'meta',
      {
        property: 'og:description',
        content:
          'A .NET data-access framework built for simplicity, high performance and global scale.'
      }
    ],
    ['meta', { property: 'og:image', content: '/images/argentsea-logo.png' }]
  ],
  markdown: {
    lineNumbers: false,
    theme: 'github-dark-dimmed'
  },
  sitemap: { hostname: 'https://docs.argentsea.com' },
  themeConfig: {
    logo: { light: '/images/argentsealogo-black.svg', dark: '/images/argentsealogo-white.svg', alt: 'ArgentSea' },
    siteTitle: false,
    nav: [
      { text: 'Introduction', link: '/introduction/what-is-argentsea', activeMatch: '^/introduction/' },
      { text: 'Get started', link: '/getting-started/installation', activeMatch: '^/getting-started/' },
      { text: 'Guides', link: '/guides/configuration', activeMatch: '^/guides/' },
      {
        text: 'Reference',
        activeMatch: '^/reference/',
        items: [
          { text: 'Glossary', link: '/reference/glossary' },
          { text: 'FAQ', link: '/reference/faq' },
          { text: "What's new in v2", link: '/reference/whats-new' },
          { text: 'Supported platforms', link: '/reference/platforms' }
        ]
      },
      {
        text: 'v2.0',
        items: [
          { text: 'NuGet: ArgentSea.Sql', link: 'https://www.nuget.org/packages/ArgentSea.Sql/' },
          { text: 'NuGet: ArgentSea.Pg', link: 'https://www.nuget.org/packages/ArgentSea.Pg/' },
          { text: 'NuGet: ArgentSea.Orleans.Sql', link: 'https://www.nuget.org/packages/ArgentSea.Orleans.Sql/' },
          { text: 'Source on GitHub', link: 'https://github.com/argentsea/source' }
        ]
      }
    ],
    sidebar: [
      {
        text: 'Introduction',
        items: [
          { text: 'What is ArgentSea?', link: '/introduction/what-is-argentsea' },
          { text: 'Why ArgentSea?', link: '/introduction/why-argentsea' },
          { text: 'Core concepts', link: '/introduction/core-concepts' },
          { text: 'Design principles', link: '/introduction/principles' },
          { text: 'Architecture', link: '/introduction/architecture' }
        ]
      },
      {
        text: 'Getting started',
        items: [
          { text: 'Installation', link: '/getting-started/installation' },
          { text: 'Quickstart: one database', link: '/getting-started/quickstart-database' },
          { text: 'Quickstart: sharding', link: '/getting-started/quickstart-sharding' },
          { text: 'Testing without a database', link: '/getting-started/testing' }
        ]
      },
      {
        text: 'Guides',
        items: [
          { text: 'Configuration', link: '/guides/configuration' },
          { text: 'Resilience', link: '/guides/resilience' },
          { text: 'Logging', link: '/guides/logging' },
          { text: 'Queries', link: '/guides/queries' },
          { text: 'Mapping', link: '/guides/mapping' },
          { text: 'Sharding & tenanting', link: '/guides/sharding' },
          { text: 'Batches & transactions', link: '/guides/batches' },
          { text: 'Microsoft Orleans', link: '/guides/orleans' }
        ]
      },
      {
        text: 'Reference',
        items: [
          { text: 'Glossary', link: '/reference/glossary' },
          { text: 'FAQ', link: '/reference/faq' },
          { text: "What's new in v2", link: '/reference/whats-new' },
          { text: 'Supported platforms', link: '/reference/platforms' }
        ]
      }
    ],
    outline: { level: [2, 3], label: 'On this page' },
    search: { provider: 'local' },
    socialLinks: [{ icon: 'github', link: 'https://github.com/argentsea/source' }],
    editLink: {
      pattern: 'https://github.com/argentsea/source/edit/main/WebDocs/docs/:path',
      text: 'Improve this page'
    },
    footer: {
      message: 'Released under the MIT License.',
      copyright: '© John Hicks · ArgentSea'
    },
    docFooter: { prev: 'Previous', next: 'Next' }
  }
})
