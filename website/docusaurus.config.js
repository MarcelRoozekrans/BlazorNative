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
  // AND ANCHORS ARE THE OTHER HALF OF THAT SURFACE (8.4 review, S2-1). The
  // default is 'warn', which means a dead `#anchor` prints a WARNING and deploys
  // — `[WARNING] Docusaurus found broken anchors!` followed by `[SUCCESS]`, exit
  // 0. `onBrokenLinks: 'throw'` above does NOT cover them: it decides pages, not
  // fragments.
  //
  // THIS SITE IS THE CASE THAT NEEDS IT. The component reference is GENERATED
  // from `<see cref="..."/>`, which xmldoc2md renders as intra-page anchor links
  // — so the largest body of anchors here is machine-written from C# doc
  // comments that no one proofreads. Gate 2 fixed five broken anchors at the
  // source cref; with the default nothing keeps them fixed, and the next bad
  // cref rots a link that reports SUCCESS.
  //
  // Docusaurus agrees, and says so in its own default:
  //   configValidation.js:123 — `onBrokenAnchors: 'warn', // TODO Docusaurus v4:
  //   change to throw`
  // This is v4's behaviour, adopted early rather than inherited late.
  //
  // MUTATION (Gate 4): a link to /docs/analyzers#bn9999 ->
  //   [ERROR] Error: Unable to build website for locale en.
  //     [cause]: Error: Docusaurus found broken anchors!
  //        -> linking to /BlazorNative/docs/analyzers#bn9999
  //   exit 1
  onBrokenAnchors: 'throw',

  markdown: {
    // Divergence 4, and it is FORCED BY THE GENERATED REFERENCE (8.4 decision 3).
    // Docusaurus 3 parses every .md file as MDX by default, and MDX is JSX: `<br>`
    // must be `<br/>`, and a bare `{` opens an expression. xmldoc2md emits BOTH —
    // `<br>` after every type line, and C# XML docs routinely carry braces
    // (`System.Nullable{T}` is in the tool's own output). Under the default, the
    // reference does not build:
    //
    //   MDX compilation failed ... Expected a closing tag for `<br>` (20:151-20:155)
    //
    // 'detect' means .md is CommonMark and .mdx is MDX — which is simply the truth
    // about these files: not one page on this site uses JSX, an import or an
    // export. Admonitions, headings and links all run through remark either way,
    // so the prose is unaffected (verified: the same 12 pages build clean).
    //
    // ESCAPING THE `<br>`s IN THE SCRIPT WAS THE ALTERNATIVE AND IS WORSE: it
    // patches the one construct that happens to break TODAY, and leaves the next
    // doc comment containing a brace to fail the deploy for a reason no one will
    // connect to a `<summary>`. This setting removes the whole class.
    format: 'detect',
    hooks: {
      // Divergence 3 from the mirror, and the only one the mirror would want back:
      // AdoNet.Async still sets the top-level `onBrokenMarkdownLinks`, which 3.10
      // deprecates with a warning on every build and removes in v4. Mirroring the
      // warning into a repo that holds a zero-warning bar would import a defect,
      // not a convention.
      onBrokenMarkdownLinks: 'warn',
    },
  },

  // NO og:image HERE, DELIBERATELY (8.4 review, S2-3). A handwritten
  // `og:image` full URL used to sit at the top of this list, and it was a THIRD
  // copy of the baseUrl inside the very file the one-home rule is about —
  // widening U2's blast radius for nothing. `themeConfig.image` below already
  // emits a baseUrl-aware `og:image`, so the handwritten one was pure
  // duplication: the built HTML carries exactly one og:image tag either way
  // (verified in build/index.html).
  headTags: [
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
      // PNG, NOT THE SVG (8.4 review, S2-4) — and this is decision 6's lesson
      // arriving a second time. NuGet taught it ("JPEG/PNG only, SVG
      // unsupported") and it did not transfer to the social card: X, Facebook,
      // LinkedIn and Slack ALL require a raster og:image, so an SVG here means
      // `twitter:card: summary_large_image` renders NO preview at all — a link
      // to this site posts as a bare URL. Nothing reds; the tag is present and
      // the file resolves 200. The unfurl is simply blank.
      //
      // social-card.svg stays as the SOURCE and social-card.png is rendered
      // from it, mirroring the icon's story (both encodings from one vector).
      image: 'img/social-card.png',
      navbar: {
        title: 'BlazorNative',
        logo: {
          alt: 'BlazorNative Logo',
          src: 'img/logo.svg',
          // `href: '/'`, NOT the hardcoded '/BlazorNative/' (8.4 review, S2-3).
          // Docusaurus resolves this through useBaseUrl — theme-classic's
          // Logo/index.js is literally `useBaseUrl(logo?.href || '/')` — so '/'
          // becomes /BlazorNative/ from the ONE baseUrl above, and follows it if
          // it ever changes.
          //
          // The old hardcoded copy WORKED, and only by luck: addBaseUrl skips
          // prefixing when the url already startsWith(baseUrl). Change baseUrl to
          // '/Foo/' and that guard stops matching — the logo link becomes
          // /Foo/BlazorNative/ and 404s. That is U2's blast radius, and this is
          // one of the two copies inside the file the one-home rule is about.
          //
          // NOT `to:` — the review prescribed it, and the build refuses it:
          // `"navbar.logo.to" is not allowed`. `to` is for navbar ITEMS; the logo
          // schema takes `href` only. The build is the pin that said so.
          href: '/',
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
