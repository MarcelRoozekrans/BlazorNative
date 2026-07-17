import clsx from 'clsx';
import Link from '@docusaurus/Link';
import useBaseUrl from '@docusaurus/useBaseUrl';
import useDocusaurusContext from '@docusaurus/useDocusaurusContext';
import Layout from '@theme/Layout';
import styles from './index.module.css';

function HeroBanner() {
  const {siteConfig} = useDocusaurusContext();
  return (
    <header className={clsx('hero hero--primary', styles.heroBanner)}>
      <div className="container">
        {/* useBaseUrl, not a bare relative 'img/logo.svg': the relative form resolves
            against whatever path the page is served from, and this site's baseUrl is
            the one thing no local build can verify (U2). */}
        <img src={useBaseUrl('img/logo.svg')} alt="BlazorNative" className={styles.heroLogo} />
        <h1 className="hero__title">{siteConfig.title}</h1>
        <p className="hero__subtitle">{siteConfig.tagline}</p>
        <div className={styles.buttons}>
          <Link className="button button--secondary button--lg" to="/docs/getting-started/quick-start">
            Get Started
          </Link>
          <Link className="button button--outline button--lg" to="/docs/intro" style={{marginLeft: '1rem', color: 'white', borderColor: 'white'}}>
            Learn More
          </Link>
        </div>
      </div>
    </header>
  );
}

const features = [
  {
    title: 'Real widgets, not a WebView',
    description: 'Your components render to a real TextView and a real UILabel. There is no browser, no JavaScript bridge, and no HTML anywhere in the process.',
  },
  {
    title: 'Compiled, not interpreted',
    description: 'Your UI and logic compile ahead-of-time into a platform-native library — a .NET NativeAOT binary per platform and ABI. No JIT, no IL interpreter, nothing to download at boot.',
  },
  {
    title: 'One layout engine, two platforms',
    description: 'Both shells delegate placement to the same C++ flexbox engine, so both compute the same frames. It is asserted by a cross-shell drift test, not promised in a README.',
  },
  {
    title: 'Typed structs on the wire',
    description: 'The renderer emits fixed-size struct patches through a C-ABI frame callback. Nothing on the frame path is serialized, parsed, or allocated per frame.',
  },
  {
    title: 'It is just Blazor',
    description: 'Components, @bind, EventCallback, cascading values, keyed lists, real disposal and DI. The render tree is the real one — only the thing at the end of it changed.',
  },
  {
    title: 'Analyzers that know the runtime',
    description: 'The BN rules catch what a native shell makes illegal — a blocking sleep on the dispatch lane, an exception escaping the C-ABI — at compile time, not at 3am.',
  },
];

function Feature({title, description}) {
  return (
    <div className={clsx('col col--4')}>
      <div className="padding-horiz--md padding-vert--lg">
        <h3>{title}</h3>
        <p>{description}</p>
      </div>
    </div>
  );
}

function FeaturesSection() {
  return (
    <section className={styles.features}>
      <div className="container">
        <div className="row">
          {features.map((props, idx) => (
            <Feature key={idx} {...props} />
          ))}
        </div>
      </div>
    </section>
  );
}

function CodeExample() {
  return (
    <section className={styles.codeExample}>
      <div className="container">
        <div className="row">
          <div className="col col--6">
            <h2>You write this</h2>
            <pre className={styles.codeBlock}>
{`<BnColumn Gap="16" Padding="16">

  <BnRow Justify="FlexJustify.SpaceBetween">
    <BnText Text="Left" />
    <BnText Text="Right" />
  </BnRow>

  <BnButton Label="Tap me" OnClick="OnTap" />

</BnColumn>`}
            </pre>
          </div>
          <div className="col col--6">
            <h2>This runs</h2>
            <pre className={styles.codeBlock}>
{`Android                 iOS
───────────────         ───────────────
FrameLayout             UIView
├─ FrameLayout          ├─ UIView
│  ├─ TextView          │  ├─ UILabel
│  └─ TextView          │  └─ UILabel
└─ Button               └─ UIButton

Real controls — and every one of
them placed at the frame Yoga
computed, which is the SAME frame
on both sides.`}
            </pre>
          </div>
        </div>
      </div>
    </section>
  );
}

function PackagesSection() {
  return (
    <section className={styles.packages}>
      <div className="container">
        <h2 style={{textAlign: 'center', marginBottom: '2rem'}}>The packages</h2>
        <div className="row">
          <div className="col col--4">
            <div className={styles.packageCard}>
              <h3>BlazorNative.Core</h3>
              <p>The IMobileBridge contract and the bridge implementations. A pure library.</p>
            </div>
          </div>
          <div className="col col--4">
            <div className={styles.packageCard}>
              <h3>BlazorNative.Renderer</h3>
              <p>The headless NativeRenderer and the RenderPatch model.</p>
            </div>
          </div>
          <div className="col col--4">
            <div className={styles.packageCard}>
              <h3>BlazorNative.Runtime</h3>
              <p>The NativeAOT composition root and the C-ABI export surface.</p>
            </div>
          </div>
          <div className="col col--4">
            <div className={styles.packageCard}>
              <h3>BlazorNative.Components</h3>
              <p>The Bn* component library — the flex surface, the controls, the list.</p>
            </div>
          </div>
          <div className="col col--4">
            <div className={styles.packageCard}>
              <h3>BlazorNative.Http</h3>
              <p>BridgeHttpHandler + DI — a plain HttpClient, over the shell's fetch.</p>
            </div>
          </div>
          <div className="col col--4">
            <div className={styles.packageCard}>
              <h3>BlazorNative.Analyzers</h3>
              <p>Compile-time guards for the native runtime and the C-ABI boundary.</p>
            </div>
          </div>
        </div>
      </div>
    </section>
  );
}

function Caveat() {
  return (
    <section className={styles.caveat}>
      <div className="container">
        <div className={styles.caveatInner}>
          <p>
            <strong>This is a pre-release proof of concept.</strong> The API surface is unstable
            and changes without notice. iOS is simulator-only.
          </p>
          <p>
            This site deliberately states no test counts, no version numbers and no milestone
            status — those facts are asserted by CI on every pull request, and a copy of them
            here would only be a copy that goes stale. For the current state of the project,
            read{' '}
            <a href="https://github.com/MarcelRoozekrans/BlazorNative#readme">the repository</a>
            {' '}and{' '}
            <a href="https://github.com/MarcelRoozekrans/BlazorNative/actions/workflows/ci.yml">its CI</a>.
          </p>
        </div>
      </div>
    </section>
  );
}

export default function Home() {
  const {siteConfig} = useDocusaurusContext();
  return (
    <Layout
      title="Blazor as real native mobile UI"
      description="Blazor components compiled with NativeAOT and rendered as real Android and iOS views — no WebView, no JavaScript, no wasm.">
      <HeroBanner />
      <main>
        <FeaturesSection />
        <CodeExample />
        <PackagesSection />
        <Caveat />
      </main>
    </Layout>
  );
}
