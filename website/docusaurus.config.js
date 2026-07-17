// @ts-check
import {themes as prismThemes} from 'prism-react-renderer';

/** @type {import('@docusaurus/types').Config} */
const config = {
  title: 'BlazorNative',
  tagline: 'Blazor components rendered as real Android and iOS views — no WebView, no JavaScript',
  favicon: 'img/favicon.ico',
  url: 'https://marcelroozekrans.github.io',
  // The project-site shape, mirrored from AdoNet.Async — the trailing slash matters.
  // A WRONG baseUrl BUILDS GREEN and deploys a page with dead CSS and 404 links
  // (8.4 design, U2 — the quiet arrow). No local build can see it; the first check
  // after Pages is enabled is a LOOK at the page, not a look for a red.
  baseUrl: '/BlazorNative/',
  organizationName: 'MarcelRoozekrans',
  projectName: 'BlazorNative',
  trailingSlash: false,
  // Adopted as a PIN, not as decoration (8.4 design, decision 2): a dead internal
  // link fails the build. It is the only mechanism keeping this site's own
  // cross-references true, and it governs INTERNAL links only — external rot is
  // unpinnable and accepted.
  onBrokenLinks: 'throw',

  markdown: {
    hooks: {
      // Divergence 3 from the mirror, and the only one the mirror would want back:
      // AdoNet.Async still sets the top-level `onBrokenMarkdownLinks`, which 3.10
      // deprecates with a warning on every build and removes in v4. Mirroring the
      // warning into a repo that holds a zero-warning bar would import a defect,
      // not a convention.
      onBrokenMarkdownLinks: 'warn',
    },
  },

  headTags: [
    {
      tagName: 'meta',
      attributes: { property: 'og:image', content: 'https://marcelroozekrans.github.io/BlazorNative/img/social-card.svg' },
    },
    {
      tagName: 'meta',
      attributes: { property: 'og:title', content: 'BlazorNative' },
    },
    {
      tagName: 'meta',
      attributes: { property: 'og:description', content: 'Blazor components rendered as real Android and iOS views — no WebView, no JavaScript' },
    },
    {
      tagName: 'meta',
      attributes: { name: 'twitter:card', content: 'summary_large_image' },
    },
  ],

  i18n: {
    defaultLocale: 'en',
    locales: ['en'],
  },

  presets: [
    [
      'classic',
      /** @type {import('@docusaurus/preset-classic').Options} */
      ({
        docs: {
          sidebarPath: './sidebars.js',
          editUrl: 'https://github.com/MarcelRoozekrans/BlazorNative/tree/main/website/',
          routeBasePath: 'docs',
        },
        // Divergence 1 from the mirror (8.4 design, decision 2): explicit, because
        // the DoD says no blog. The reference leaves the key unset.
        blog: false,
        theme: {
          customCss: './src/css/custom.css',
        },
      }),
    ],
  ],

  themeConfig:
    /** @type {import('@docusaurus/preset-classic').ThemeConfig} */
    ({
      image: 'img/social-card.svg',
      navbar: {
        title: 'BlazorNative',
        logo: {
          alt: 'BlazorNative Logo',
          src: 'img/logo.svg',
          href: '/BlazorNative/',
        },
        items: [
          {
            type: 'docSidebar',
            sidebarId: 'docs',
            position: 'left',
            label: 'Docs',
          },
          {
            href: 'https://github.com/MarcelRoozekrans/BlazorNative',
            label: 'GitHub',
            position: 'right',
          },
          {
            href: 'https://www.nuget.org/packages/BlazorNative.Components',
            label: 'NuGet',
            position: 'right',
          },
        ],
      },
      footer: {
        style: 'dark',
        links: [
          {
            title: 'Docs',
            items: [
              { label: 'Introduction', to: '/docs/intro' },
              { label: 'Getting Started', to: '/docs/getting-started/installation' },
              { label: 'Components', to: '/docs/components/overview' },
            ],
          },
          {
            title: 'Community',
            items: [
              { label: 'GitHub', href: 'https://github.com/MarcelRoozekrans/BlazorNative' },
              { label: 'Issues', href: 'https://github.com/MarcelRoozekrans/BlazorNative/issues' },
              { label: 'CI', href: 'https://github.com/MarcelRoozekrans/BlazorNative/actions/workflows/ci.yml' },
            ],
          },
          {
            title: 'More',
            items: [
              { label: 'NuGet', href: 'https://www.nuget.org/packages/BlazorNative.Components' },
              { label: 'License', href: 'https://github.com/MarcelRoozekrans/BlazorNative/blob/main/LICENSE' },
            ],
          },
        ],
        copyright: `Copyright ${new Date().getFullYear()} Marcel Roozekrans. Built with Docusaurus.`,
      },
      prism: {
        theme: prismThemes.github,
        darkTheme: prismThemes.dracula,
        // Divergence 2 from the mirror (8.4 design, decision 2): this repo's
        // snippets are not C#-only — the shells are Kotlin and Swift, and the
        // components are Razor.
        //
        // 'cshtml', NOT 'razor', and the difference is load-bearing: Docusaurus
        // resolves each entry to `prismjs/components/prism-<lang>`, and there is
        // no prism-razor.js — the file is prism-cshtml.js, which REGISTERS
        // 'razor' as an alias. Asking for 'razor' here fails the build outright;
        // asking for 'cshtml' is what makes ```razor fences highlight.
        additionalLanguages: ['csharp', 'cshtml', 'kotlin', 'swift', 'markup', 'bash', 'json', 'yaml'],
      },
    }),
};

export default config;
