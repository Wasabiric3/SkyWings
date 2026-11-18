// One-time converter: SkyWings asset kit SVGs -> PNGs at 1x / 1.5x / 2x, embedded by WinFormsApp3/SkyWings.csproj.
// Usage: node convert.mjs "<path to 'SkyWings Assets' folder>" "<output folder, e.g. ../../WinFormsApp3/Assets>"
import { Resvg } from '@resvg/resvg-js';
import { readdirSync, readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { join } from 'node:path';

const [kit, out] = process.argv.slice(2);
if (!kit || !out) { console.error('usage: node convert.mjs <kit folder> <output folder>'); process.exit(1); }
mkdirSync(out, { recursive: true });

const buckets = { x1: 1, x15: 1.5, x2: 2 };
let count = 0;

function render(svgPath, name, logicalWidth) {
  const svg = readFileSync(svgPath);
  for (const [bucket, scale] of Object.entries(buckets)) {
    const png = new Resvg(svg, {
      fitTo: { mode: 'width', value: Math.round(logicalWidth * scale) },
      font: { loadSystemFonts: false },
    }).render().asPng();
    writeFileSync(join(out, `${name}_${bucket}.png`), png);
    count++;
  }
}

// Icons: 24 px logical, green and white only.
const iconDir = join(kit, '02_Icons');
for (const file of readdirSync(iconDir)) {
  const m = /^(.+)_(Green|White)\.svg$/.exec(file);
  if (m) render(join(iconDir, file), `icon_${m[1]}_${m[2].toLowerCase()}`, 24);
}

// Logo mark: 160 px logical, three colours.
for (const colour of ['Green', 'White', 'Black'])
  render(join(kit, '01_Logos', 'Solid', `SkyWings_Logo_${colour}_Solid.svg`), `logo_${colour.toLowerCase()}`, 160);

// Sage hero background (1440 wide in the kit -> 720 logical) and wing watermark (640 -> 320 logical).
render(join(kit, '05_Decorative', 'Sage_Hero_Background.svg'), 'hero', 720);
render(join(kit, '05_Decorative', 'Wing_Watermark.svg'), 'watermark', 320);

console.log(`wrote ${count} PNGs to ${out}`);
