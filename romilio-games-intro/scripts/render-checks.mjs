import {bundle} from '@remotion/bundler';
import {renderStill, selectComposition} from '@remotion/renderer';
import {mkdir} from 'node:fs/promises';

const serveUrl = await bundle({entryPoint: 'src/index.ts'});
const composition = await selectComposition({serveUrl, id: 'RomilioGamesIntro'});
await mkdir('out/checks', {recursive: true});
for (const frame of [0, 18, 36, 68, 74, 119]) {
  await renderStill({
    serveUrl, composition, frame, imageFormat: 'png',
    output: `out/checks/frame-${frame}.png`,
  });
}
console.log('Rendered entrance, fade, and final-frame checks.');
