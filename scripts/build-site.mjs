// 소개 페이지(docs/)의 가이드·개인정보 쪽과 사이트맵을 굽는다.
//
//   site-src/<lang>/guide/<slug>.html   가이드 원고 한 편. 맨 위 <!--meta {…} --> 주석에 JSON 으로
//                                       제목·요약·날짜·순서를 적고, 그 아래에 본문(h2 부터)을 그대로 쓴다.
//                                       en·ko 가 같은 slug 면 서로 hreflang 으로 묶인다.
//   site-src/<lang>/privacy.html        개인정보 처리방침 (원고 형식은 가이드와 같다)
//
//   docs/guide/<slug>/index.html, docs/ko/guide/<slug>/index.html   완성 쪽
//   docs/guide/index.html, docs/ko/guide/index.html                 가이드 목차
//   docs/privacy/index.html, docs/ko/privacy/index.html
//   docs/sitemap.xml
//   docs/index.html, docs/ko/index.html   손으로 쓴 쪽이지만 아래 표시 사이만 다시 채운다
//     <!--guides--> … <!--/guides-->     가이드 목록
//     <!--ad--> … <!--/ad-->             광고 한 자리
//     <!--faq-ld--> … <!--/faq-ld-->     FAQ 의 <details> 를 그대로 옮긴 FAQPage 구조화 데이터
//
//   node scripts/build-site.mjs

import { readFileSync, writeFileSync, readdirSync, mkdirSync, existsSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..');
const SRC = join(ROOT, 'site-src');
const DOCS = join(ROOT, 'docs');
const SITE = 'https://claude-overlay.jsuk.cloud';
const VERSION = '0.2.5';

/* 광고: 애드센스는 jsuk.cloud 도메인 단위로 심사·지급된다(디지몬 사이트와 같은 계정).
   slot 을 비우면 광고 자리를 만들지 않고, client 까지 비우면 애드센스 스크립트도 넣지 않는다.
   다운로드 버튼 가까이에는 두지 않는다 — 잘못 누르기 쉬운 자리는 애드센스 정책 위반이다. */
const ADS = { client: 'ca-pub-9204832944396914', slot: '1976878299' };

const L = {
  en: {
    prefix: '/', locale: 'en_US', ad: 'Advertisement',
    guides: 'Guides', guidesTitle: 'Claude Code Session Overlay guides',
    guidesLead: 'Practical notes on running Claude Code: context, usage limits, juggling several sessions, and installing the overlay on Windows and macOS.',
    home: 'Home', updated: 'Updated', related: 'More guides', privacy: 'Privacy',
    other: { lang: 'ko', label: '한국어' },
    ctaTitle: 'Try the overlay', ctaText: 'Free and open source (MIT). Windows 10/11 and macOS 11+.',
    ctaWin: 'Download for Windows', ctaMac: 'Download for Mac',
    footer: (privacy) => `MIT License · <a href="https://github.com/jsuk10/claude-overlay">github.com/jsuk10/claude-overlay</a> · <a href="${privacy}">Privacy</a> · More projects at <a href="https://home.jsuk.cloud/">home.jsuk.cloud</a><br>
    Not affiliated with or endorsed by Anthropic. "Claude" and "Claude Code" are trademarks of Anthropic.`,
    date: (iso) => new Date(iso + 'T00:00:00Z').toLocaleDateString('en-US', { year: 'numeric', month: 'long', day: 'numeric', timeZone: 'UTC' }),
  },
  ko: {
    prefix: '/ko/', locale: 'ko_KR', ad: '광고',
    guides: '가이드', guidesTitle: '클로드 코드 세션 오버레이 가이드',
    guidesLead: 'Claude Code를 쓰면서 자주 막히는 것들을 정리했습니다. 컨텍스트, 사용량 한도, 세션 여러 개 다루기, Windows·macOS 설치까지.',
    home: '처음', updated: '고친 날', related: '다른 가이드', privacy: '개인정보 처리방침',
    other: { lang: 'en', label: 'English' },
    ctaTitle: '오버레이 써 보기', ctaText: '무료 오픈소스(MIT)입니다. Windows 10/11, macOS 11 이상.',
    ctaWin: 'Windows용 내려받기', ctaMac: 'macOS용 내려받기',
    footer: (privacy) => `MIT 라이선스 · <a href="https://github.com/jsuk10/claude-overlay">github.com/jsuk10/claude-overlay</a> · <a href="${privacy}">개인정보 처리방침</a> · 다른 프로젝트는 <a href="https://home.jsuk.cloud/">home.jsuk.cloud</a><br>
    Anthropic과 관련이 없거나 Anthropic이 보증하지 않는 개인 프로젝트입니다. "Claude"와 "Claude Code"는 Anthropic의 상표입니다.`,
    date: (iso) => { const [y, m, d] = iso.split('-').map(Number); return `${y}년 ${m}월 ${d}일`; },
  },
};
const LANGS = Object.keys(L);

const DL_WIN = 'https://github.com/jsuk10/claude-overlay/releases/latest/download/ClaudeSessionOverlay.exe';
const esc = (s) => String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
const text = (html) => html.replace(/<[^>]+>/g, '').replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&quot;/g, '"').replace(/&amp;/g, '&').replace(/\s+/g, ' ').trim();
const write = (file, body) => { mkdirSync(dirname(file), { recursive: true }); writeFileSync(file, body); };

/* 원고 읽기 */
function readDoc(file) {
  const raw = readFileSync(file, 'utf8');
  const m = raw.match(/^\s*<!--meta\s*([\s\S]*?)-->\s*/);
  if (!m) throw new Error(`${file}: 맨 위에 <!--meta {…} --> 가 없다`);
  const meta = JSON.parse(m[1]);
  for (const k of ['title', 'description', 'date']) if (!meta[k]) throw new Error(`${file}: meta.${k} 가 비었다`);
  return { ...meta, updated: meta.updated || meta.date, body: raw.slice(m[0].length).trim() };
}

const guides = {};
for (const lang of LANGS) {
  const dir = join(SRC, lang, 'guide');
  guides[lang] = readdirSync(dir).filter((f) => f.endsWith('.html')).map((f) => {
    const slug = f.replace(/\.html$/, '');
    return { slug, lang, path: `${L[lang].prefix}guide/${slug}/`, ...readDoc(join(dir, f)) };
  }).sort((a, b) => (a.order ?? 99) - (b.order ?? 99) || a.slug.localeCompare(b.slug));
}
const counterpart = (g) => guides[L[g.lang].other.lang].find((o) => o.slug === g.slug);

/* 공통 머리 */
function head(lang, { title, description, path, alternates = {}, type = 'article', image, jsonld = [] }) {
  const alt = Object.entries(alternates);
  return `<!doctype html>
<html lang="${lang}">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>${esc(title)}</title>
<meta name="description" content="${esc(description)}">
<meta name="theme-color" content="#f7f6f3" media="(prefers-color-scheme: light)">
<meta name="theme-color" content="#1b1a18" media="(prefers-color-scheme: dark)">
<link rel="canonical" href="${SITE}${path}">
${alt.map(([l, p]) => `<link rel="alternate" hreflang="${l}" href="${SITE}${p}">`).join('\n')}${alt.length ? `\n<link rel="alternate" hreflang="x-default" href="${SITE}${alternates.en || path}">` : ''}
<meta property="og:type" content="${type}">
<meta property="og:site_name" content="Claude Session Overlay">
<meta property="og:title" content="${esc(title)}">
<meta property="og:description" content="${esc(description)}">
<meta property="og:url" content="${SITE}${path}">
<meta property="og:image" content="${SITE}/${image || `screenshot-${lang}-dark.png`}">
<meta property="og:locale" content="${L[lang].locale}">
<meta name="twitter:card" content="summary_large_image">
<script>
  if (location.hostname.endsWith('github.io')) location.replace('${SITE}/' + location.pathname.replace(/^\\/claude-overlay\\//, '') + location.hash);
</script>
<link rel="icon" type="image/png" href="/favicon.png">
<link rel="apple-touch-icon" href="/apple-touch-icon.png">
<link rel="stylesheet" href="/site.css">
${adsHead()}
<script async src="https://www.googletagmanager.com/gtag/js?id=G-0XK79SYGFF"></script>
<script>
  window.dataLayer = window.dataLayer || [];
  function gtag(){dataLayer.push(arguments);}
  gtag('js', new Date());
  gtag('config', 'G-0XK79SYGFF', { anonymize_ip: true });
  document.addEventListener('click', function (e) {
    var a = e.target.closest('a[data-track]');
    if (a) gtag('event', a.getAttribute('data-track'), { link_url: a.href });
  });
</script>
${jsonld.map((o) => `<script type="application/ld+json">\n${JSON.stringify(o, null, 2)}\n</script>`).join('\n')}
</head>`;
}

function adsHead() {
  if (!ADS.client) return '';
  return `<meta name="google-adsense-account" content="${ADS.client}">
<script async src="https://pagead2.googlesyndication.com/pagead/js/adsbygoogle.js?client=${ADS.client}" crossorigin="anonymous"></script>`;
}

/* 광고 한 자리. 채워지지 않으면 CSS(.ad:has(…unfilled))가 자리째 접는다. */
function adSlot(lang) {
  if (!ADS.client || !ADS.slot) return '';
  return `<aside class="ad" aria-label="${L[lang].ad}" data-label="${L[lang].ad}">
  <ins class="adsbygoogle" style="display:block" data-ad-client="${ADS.client}" data-ad-slot="${ADS.slot}" data-ad-format="auto" data-full-width-responsive="true"></ins>
  <script>(adsbygoogle = window.adsbygoogle || []).push({});</script>
</aside>`;
}

function top(lang, otherPath) {
  const t = L[lang];
  return `<header class="top">
    <a class="brand" href="${t.prefix}">Claude Session Overlay</a>
    <nav>
      <a href="${t.prefix}guide/">${t.guides}</a>
      <a href="${otherPath}" hreflang="${t.other.lang}" lang="${t.other.lang}">${t.other.label}</a>
      <a href="https://github.com/jsuk10/claude-overlay" data-track="github_click">GitHub</a>
    </nav>
  </header>`;
}

const foot = (lang) => `<footer>
    ${L[lang].footer(`${L[lang].prefix}privacy/`)}
  </footer>`;

const crumbs = (lang, trail) => `<nav class="crumbs" aria-label="breadcrumb">${trail.map(([name, path], i) =>
  i === trail.length - 1 ? `<span aria-current="page">${esc(name)}</span>` : `<a href="${path}">${esc(name)}</a>`).join(' <span aria-hidden="true">›</span> ')}</nav>`;

const crumbsLd = (trail) => ({
  '@context': 'https://schema.org', '@type': 'BreadcrumbList',
  itemListElement: trail.map(([name, path], i) => ({ '@type': 'ListItem', position: i + 1, name, item: SITE + path })),
});

const guideList = (lang, list) => `<ul class="guide-list">
${list.map((g) => `  <li><a href="${g.path}"><strong>${esc(g.title)}</strong><span>${esc(g.description)}</span></a></li>`).join('\n')}
</ul>`;

/* 본문 가운데에 광고 한 자리: 세 번째 h2 앞(짧은 글이면 넣지 않는다) */
function withMidAd(body, lang) {
  const h2 = [...body.matchAll(/<h2[\s>]/g)];
  if (h2.length < 4) return body;
  const at = h2[2].index;
  return body.slice(0, at) + adSlot(lang) + '\n' + body.slice(at);
}

/* 가이드 한 편 */
function renderGuide(g) {
  const t = L[g.lang];
  const other = counterpart(g);
  const alternates = other ? { [g.lang]: g.path, [other.lang]: other.path } : {};
  const trail = [[t.home, t.prefix], [t.guides, `${t.prefix}guide/`], [g.title, g.path]];
  const related = guides[g.lang].filter((o) => o.slug !== g.slug);
  const article = {
    '@context': 'https://schema.org', '@type': 'TechArticle',
    headline: g.title, description: g.description, inLanguage: g.lang,
    datePublished: g.date, dateModified: g.updated,
    mainEntityOfPage: SITE + g.path,
    image: `${SITE}/screenshot-${g.lang}-dark.png`,
    author: { '@type': 'Person', name: 'jsuk10', url: 'https://github.com/jsuk10' },
    about: { '@type': 'SoftwareApplication', name: 'Claude Session Overlay', url: SITE + t.prefix },
  };
  const title = g.title.includes('Claude Session Overlay') ? g.title : `${g.title} · Claude Session Overlay`;
  return `${head(g.lang, { title, description: g.description, path: g.path, alternates, jsonld: [article, crumbsLd(trail)] })}
<body>
<div class="wrap">
  ${top(g.lang, other ? other.path : L[t.other.lang].prefix + 'guide/')}

  <main class="article">
    ${crumbs(g.lang, trail)}
    <h1>${esc(g.title)}</h1>
    <p class="meta">${t.updated}: <time datetime="${g.updated}">${t.date(g.updated)}</time></p>
${withMidAd(g.body, g.lang)}

    <div class="cta-box">
      <strong>${t.ctaTitle}</strong>
      <p>${t.ctaText}</p>
      <div class="cta">
        <a class="btn primary" href="${DL_WIN}" data-track="download_click">${t.ctaWin}</a>
        <a class="btn" href="${t.prefix}#mac" data-track="download_mac_click">${t.ctaMac}</a>
      </div>
    </div>

    ${adSlot(g.lang)}

    <section class="related">
      <h2>${t.related}</h2>
      ${guideList(g.lang, related)}
    </section>
  </main>

  ${foot(g.lang)}
</div>
</body>
</html>
`;
}

/* 가이드 목차 */
function renderHub(lang) {
  const t = L[lang];
  const path = `${t.prefix}guide/`;
  const otherPath = `${L[t.other.lang].prefix}guide/`;
  const trail = [[t.home, t.prefix], [t.guides, path]];
  const list = {
    '@context': 'https://schema.org', '@type': 'ItemList',
    itemListElement: guides[lang].map((g, i) => ({ '@type': 'ListItem', position: i + 1, url: SITE + g.path, name: g.title })),
  };
  return `${head(lang, { title: `${t.guidesTitle}`, description: t.guidesLead, path, alternates: { [lang]: path, [t.other.lang]: otherPath }, type: 'website', jsonld: [list, crumbsLd(trail)] })}
<body>
<div class="wrap">
  ${top(lang, otherPath)}

  <main class="article">
    ${crumbs(lang, trail)}
    <h1>${t.guidesTitle}</h1>
    <p class="lead">${t.guidesLead}</p>
    ${guideList(lang, guides[lang])}
    ${adSlot(lang)}
  </main>

  ${foot(lang)}
</div>
</body>
</html>
`;
}

/* 개인정보 처리방침 */
function renderPrivacy(lang) {
  const t = L[lang];
  const doc = readDoc(join(SRC, lang, 'privacy.html'));
  const path = `${t.prefix}privacy/`;
  const otherPath = `${L[t.other.lang].prefix}privacy/`;
  return `${head(lang, { title: `${doc.title} · Claude Session Overlay`, description: doc.description, path, alternates: { [lang]: path, [t.other.lang]: otherPath }, type: 'website' })}
<body>
<div class="wrap">
  ${top(lang, otherPath)}

  <main class="article">
    <h1>${esc(doc.title)}</h1>
    <p class="meta">${t.updated}: <time datetime="${doc.updated}">${t.date(doc.updated)}</time></p>
${doc.body}
  </main>

  ${foot(lang)}
</div>
</body>
</html>
`;
}

/* 손으로 쓴 소개 페이지에서 표시 사이만 다시 채운다 */
function fillHome(lang) {
  const file = join(DOCS, lang === 'en' ? 'index.html' : 'ko/index.html');
  let html = readFileSync(file, 'utf8');
  const fill = (name, body) => {
    const re = new RegExp(`(<!--${name}-->)[\\s\\S]*?(<!--/${name}-->)`);
    if (!re.test(html)) throw new Error(`${file}: <!--${name}--> 표시가 없다`);
    html = html.replace(re, (_, a, b) => `${a}\n${body}\n${b}`);
  };
  fill('guides', guideList(lang, guides[lang]));
  fill('ad', adSlot(lang));
  fill('ads-head', adsHead());

  const faq = [...html.matchAll(/<details><summary>([\s\S]*?)<\/summary>([\s\S]*?)<\/details>/g)]
    .map(([, q, a]) => ({ '@type': 'Question', name: text(q), acceptedAnswer: { '@type': 'Answer', text: text(a) } }));
  fill('faq-ld', `<script type="application/ld+json">\n${JSON.stringify({ '@context': 'https://schema.org', '@type': 'FAQPage', inLanguage: lang, mainEntity: faq }, null, 2)}\n</script>`);
  writeFileSync(file, html);
  return faq.length;
}

/* 사이트맵 */
function sitemap() {
  const newest = (list) => list.map((g) => g.updated).sort().at(-1);
  const entries = [];
  const pair = (paths, lastmod) => {
    for (const p of Object.values(paths)) entries.push({ loc: p, lastmod, alternates: paths });
  };
  const homeDate = [HOME_UPDATED, newest(guides.en), newest(guides.ko)].sort().at(-1);
  pair({ en: '/', ko: '/ko/' }, homeDate);
  pair({ en: '/guide/', ko: '/ko/guide/' }, homeDate);
  for (const lang of LANGS) for (const g of guides[lang]) {
    const other = counterpart(g);
    if (other && lang !== 'en') continue; // 짝이 있으면 en 쪽에서 둘 다 넣었다
    if (other) pair({ en: g.path, ko: other.path }, [g.updated, other.updated].sort().at(-1));
    else entries.push({ loc: g.path, lastmod: g.updated, alternates: {} });
  }
  pair({ en: '/privacy/', ko: '/ko/privacy/' }, PRIVACY_UPDATED);

  return `<?xml version="1.0" encoding="UTF-8"?>
<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9" xmlns:xhtml="http://www.w3.org/1999/xhtml">
${entries.map((e) => `  <url>
    <loc>${SITE}${e.loc}</loc>
    <lastmod>${e.lastmod}</lastmod>
${Object.entries(e.alternates).map(([l, p]) => `    <xhtml:link rel="alternate" hreflang="${l}" href="${SITE}${p}"/>`).join('\n')}
  </url>`).join('\n').replace(/\n\n/g, '\n')}
</urlset>
`;
}

/* 소개 페이지 본문을 손봤으면 이 날짜를 올린다 (사이트맵 lastmod) */
const HOME_UPDATED = '2026-10-20';
let PRIVACY_UPDATED = HOME_UPDATED;

for (const lang of LANGS) {
  for (const g of guides[lang]) write(join(DOCS, g.path, 'index.html'), renderGuide(g));
  write(join(DOCS, L[lang].prefix, 'guide', 'index.html'), renderHub(lang));
  write(join(DOCS, L[lang].prefix, 'privacy', 'index.html'), renderPrivacy(lang));
  PRIVACY_UPDATED = readDoc(join(SRC, lang, 'privacy.html')).updated;
  const faq = fillHome(lang);
  const chars = guides[lang].map((g) => text(g.body).replace(/\s/g, '').length);
  console.log(`${lang}: 가이드 ${guides[lang].length}편 (본문 ${Math.min(...chars)}~${Math.max(...chars)}자), FAQ ${faq}개`);
}
write(join(DOCS, 'sitemap.xml'), sitemap());
if (!existsSync(join(DOCS, 'ads.txt')) && ADS.client) {
  write(join(DOCS, 'ads.txt'), `google.com, ${ADS.client.replace('ca-', '')}, DIRECT, f08c47fec0942fa0\n`);
}
console.log('docs/sitemap.xml 갱신');
