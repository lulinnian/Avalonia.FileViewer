import fs from 'node:fs';
import path from 'node:path';

// Keep the exact production dependency notices next to the bundled assets.
const lock = JSON.parse(fs.readFileSync('package-lock.json', 'utf8'));
const target = '../ThirdPartyNotices';
fs.mkdirSync(target, { recursive: true });
let notice = 'Flyfish File Viewer production dependencies\nGenerated from package-lock.json.\n'
  + 'Dual licenses: DOMPurify is used under Apache-2.0; JSZip under MIT.\n\n';
for (const [directory, metadata] of Object.entries(lock.packages)) {
  if (!directory || metadata.dev) continue;
  const manifest = JSON.parse(fs.readFileSync(path.join(directory, 'package.json'), 'utf8'));
  notice += `\n${'='.repeat(72)}\n${manifest.name}@${manifest.version}\nLicense: ${metadata.license}\n`;
  const files = fs.readdirSync(directory).filter(file => /^(license|licence|notice|copying|ofl)(\.|$)/i.test(file));
  if (!files.length && manifest.name === 'isarray') files.push('README.md'); // This package embeds its full MIT text in its README.
  if (!files.length) throw new Error(`Missing license notice: ${directory}`);
  for (const file of files) notice += `\n--- ${file} ---\n${fs.readFileSync(path.join(directory, file), 'utf8')}\n`;
}
fs.writeFileSync(path.join(target, 'THIRD-PARTY-NOTICES.txt'), notice);
