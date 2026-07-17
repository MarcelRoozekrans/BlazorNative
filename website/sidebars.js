/** @type {import('@docusaurus/plugin-content-docs').SidebarsConfig} */
const sidebars = {
  docs: [
    'intro',
    {
      type: 'category',
      label: 'Getting Started',
      items: [
        'getting-started/installation',
        'getting-started/quick-start',
      ],
    },
    {
      type: 'category',
      label: 'Architecture',
      items: [
        'architecture/overview',
        'architecture/the-wire',
        'architecture/layout-and-yoga',
        'architecture/parity',
      ],
    },
    {
      type: 'category',
      label: 'Components',
      items: [
        'components/overview',
      ],
    },
    {
      type: 'category',
      label: 'Shells',
      items: [
        'shells/android',
        'shells/ios',
      ],
    },
    'analyzers',
    'contributing',
  ],
};

export default sidebars;
