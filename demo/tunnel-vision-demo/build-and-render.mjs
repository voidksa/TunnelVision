// Multilingual build + render.
// Reads i18n.json, substitutes data-i18n strings in index.html, swaps narration
// audio, sets <html lang>/<body dir>/font, then invokes hyperframes render.
// Restores the English base file at the end.

import fs from 'fs';
import path from 'path';
import { spawnSync } from 'child_process';
import { fileURLToPath } from 'url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));

const i18n = JSON.parse(fs.readFileSync(path.join(__dirname, 'i18n.json'), 'utf8'));
const originalHtml = fs.readFileSync(path.join(__dirname, 'index.html'), 'utf8');

const langsArg = process.argv[2];
const langs = langsArg ? langsArg.split(',') : Object.keys(i18n);

function buildHtml(langKey) {
  const cfg = i18n[langKey];
  let html = originalHtml;

  // Swap <html lang=..">
  html = html.replace(/<html\s+lang="[^"]*"/, `<html lang="${cfg.lang}"`);

  // Swap/insert <body dir=...>
  if (/<body[^>]*dir=/.test(html)) {
    html = html.replace(/<body([^>]*)dir="[^"]*"/, `<body$1dir="${cfg.dir}"`);
  } else {
    html = html.replace('<body>', `<body dir="${cfg.dir}">`);
  }

  // Swap body font via inline style tag at end of <head>
  const fontInject = `    <style>body, html { font-family: ${cfg.bodyFont}; }</style>\n  </head>`;
  html = html.replace('</head>', fontInject);

  // Swap narration audio src
  html = html.replace(/assets\/narration-[a-z]+\.mp3/g, `assets/${cfg.narrationFile}`);

  // Substitute data-i18n strings
  for (const [key, value] of Object.entries(cfg.strings)) {
    const re = new RegExp(`(data-i18n="${key}"[^>]*>)[^<]*(<)`, 'g');
    html = html.replace(re, `$1${value}$2`);
  }

  return html;
}

function lintAndRender(langKey) {
  const outputFile = `tunnel-vision-demo-${langKey}.mp4`;
  const indexPath = path.join(__dirname, 'index.html');

  const translated = buildHtml(langKey);
  fs.writeFileSync(indexPath, translated, 'utf8');
  console.log(`\n[${langKey}] wrote translated index.html`);

  const lint = spawnSync('npx', ['hyperframes', 'lint'], {
    cwd: __dirname, stdio: 'inherit', shell: true
  });
  if (lint.status !== 0) {
    console.error(`[${langKey}] lint failed`);
    process.exit(1);
  }

  const render = spawnSync('npx', ['hyperframes', 'render', '--output', outputFile, '--quiet'], {
    cwd: __dirname, stdio: 'inherit', shell: true
  });
  if (render.status !== 0) {
    console.error(`[${langKey}] render failed`);
    process.exit(1);
  }

  console.log(`[${langKey}] rendered → ${outputFile}`);
}

try {
  for (const lang of langs) {
    if (!i18n[lang]) {
      console.error(`Unknown lang: ${lang}`);
      continue;
    }
    lintAndRender(lang);
  }
} finally {
  // Restore English base
  fs.writeFileSync(path.join(__dirname, 'index.html'), originalHtml, 'utf8');
  console.log('\nRestored base index.html (English)');
}
