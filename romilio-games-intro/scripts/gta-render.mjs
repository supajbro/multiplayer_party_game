import { bundle } from "@remotion/bundler";
import {
  renderStill,
  renderMedia,
  selectComposition,
} from "@remotion/renderer";
import { mkdirSync, writeFileSync } from "node:fs";
const serveUrl = await bundle({ entryPoint: "src/index.ts" });
const composition = await selectComposition({
  serveUrl,
  id: "GTA6RecordShort",
});
mkdirSync("out/gta6/checks", { recursive: true });
for (const frame of [60, 570, 880, 1150, 1410, 1760])
  await renderStill({
    serveUrl,
    composition,
    frame,
    imageFormat: "png",
    output: `out/gta6/checks/${frame}.png`,
  });
console.log("CHECK_FRAMES_READY");
if (!process.argv.includes("--stills")) {
  let last = -1;
  await renderMedia({
    serveUrl,
    composition,
    codec: "h264",
    crf: 18,
    pixelFormat: "yuv420p",
    audioCodec: "aac",
    audioBitrate: "320k",
    outputLocation: "out/gta6/GTA6RecordShort-voiced.mp4",
    concurrency: 4,
    onProgress: ({ progress }) => {
      const p = Math.floor(progress * 10);
      if (p !== last) {
        console.log(`Render ${p * 10}%`);
        last = p;
      }
    },
  });
  writeFileSync(
    "out/gta6/render-metadata.json",
    JSON.stringify(
      {
        id: composition.id,
        width: composition.width,
        height: composition.height,
        fps: composition.fps,
        durationInFrames: composition.durationInFrames,
        durationSeconds: composition.durationInFrames / composition.fps,
        codec: "h264",
        audio: "AAC stereo, Andrew conversational neural narration with speech-driven music ducking",
      },
      null,
      2,
    ),
  );
  console.log("RENDER_COMPLETE");
}
