// Generates narration phrases via ElevenLabs, one MP3 per line.
// Each phrase can then be placed at a specific data-start in the composition
// so audio lines up tightly with visual beats (no more drift).

import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));

const API_KEY = fs.readFileSync('E:/MyWork/Stream _Freedom/apikey.txt', 'utf8').trim();

// User's locked-in English narrator (Ian Cartwell — Suspense)
const VOICE_ID = 'e5WNhrdI30aXpS2RSGm1';

// Golden voice settings from CLAUDE.md
const VOICE_SETTINGS = {
  stability: 0.40,
  similarity_boost: 0.85,
  style: 0.30,
  use_speaker_boost: true,
};

// Phrase-per-scene: each maps to a specific timed scene in the composition.
// Keeping each phrase tight keeps the audio honest to the visuals.
const phrases = [
  { id: 'p01-intro',    text: 'Tunnel Vision.' },
  { id: 'p02-problem',  text: 'Too many windows. Too many distractions.' },
  { id: 'p03-shortcut', text: 'Press Control, Alt, T.' },
  { id: 'p04-focus',    text: 'Your focus window lights up. Everything else fades away.' },
  { id: 'p05-intensity',text: 'Adjust intensity on the fly.' },
  { id: 'p06-settings', text: 'All from a Fluent settings panel.' },
  { id: 'p07-blur',     text: 'Even with a blurred backdrop.' },
  { id: 'p08-outro',    text: 'Free. Open source. Download now.' },
];

async function generate(phrase) {
  const url = `https://api.elevenlabs.io/v1/text-to-speech/${VOICE_ID}?output_format=mp3_44100_128`;
  const res = await fetch(url, {
    method: 'POST',
    headers: {
      'xi-api-key': API_KEY,
      'Content-Type': 'application/json',
      'Accept': 'audio/mpeg',
    },
    body: JSON.stringify({
      text: phrase.text,
      model_id: 'eleven_v3',
      voice_settings: VOICE_SETTINGS,
    }),
  });

  if (!res.ok) {
    const err = await res.text();
    throw new Error(`[${phrase.id}] ${res.status}: ${err}`);
  }

  const outPath = path.join(__dirname, 'assets', `${phrase.id}.mp3`);
  const buf = Buffer.from(await res.arrayBuffer());
  fs.writeFileSync(outPath, buf);
  console.log(`  wrote ${outPath} (${(buf.length / 1024).toFixed(1)} KB)`);
}

(async () => {
  console.log(`Generating ${phrases.length} phrases via ElevenLabs (voice=${VOICE_ID}, model=eleven_v3)...`);
  for (const p of phrases) {
    try {
      await generate(p);
    } catch (e) {
      console.error(e.message);
      process.exit(1);
    }
  }
  console.log('Done.');
})();
