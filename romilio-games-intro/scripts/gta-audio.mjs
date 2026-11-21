import { mkdirSync, writeFileSync, readFileSync } from "node:fs";
// Original deterministic synth score and designed effects. No sampled music.
const rate = 48000,
  length = rate * 60,
  data = Buffer.alloc(length * 4);
let seed = 42;
const noise = () => {
  seed = (Math.imul(seed, 1664525) + 1013904223) >>> 0;
  return seed / 2147483648 - 1;
};
const timing = JSON.parse(readFileSync("src/GTA6/voice-timing.json", "utf8"));
const hits = [
  0,
  2.4,
  7,
  timing.revenue / 30,
  timing.preorders / 30,
  timing.race / 30,
  timing.minute / 30,
  timing.minute / 30 + 3,
  timing.ending / 30,
  timing.black / 30,
  59.1,
];
for (let i = 0; i < length; i++) {
  const t = i / rate,
    beat = (t * 128) / 60,
    phase = beat % 1,
    env = Math.exp(-phase * 12);
  const notes = [55, 65.406, 49, 73.416],
    freq = notes[Math.floor(beat / 8) % 4];
  let music =
    (Math.sin(2 * Math.PI * freq * t) +
      0.3 * Math.sin(4 * Math.PI * freq * t)) *
    0.13 *
    env;
  music +=
    Math.sin(2 * Math.PI * (45 * t - 1.1 * Math.exp(-phase * 26))) *
    0.29 *
    Math.exp(-phase * 25);
  music += noise() * 0.055 * Math.exp(-((beat * 2) % 1) * 35);
  const arp = [0, 7, 12, 15, 12, 7, 19, 12][Math.floor(beat * 2) % 8];
  music +=
    Math.sin(2 * Math.PI * freq * 4 * Math.pow(2, arp / 12) * t) *
    0.045 *
    Math.exp(-((beat * 2) % 1) * 5);
  let fx = 0;
  for (const hit of hits) {
    const d = t - hit;
    if (d >= 0 && d < 1.5)
      fx +=
        Math.sin(2 * Math.PI * (43 * d + 30 * (1 - Math.exp(-d * 15)))) *
          0.4 *
          Math.exp(-d * 5) +
        noise() * 0.12 * Math.exp(-d * 14);
    const r = hit - t;
    if (r > 0 && r < 0.65) fx += noise() * 0.09 * (1 - r / 0.65);
  }
  if (t > timing.black / 30) music = 0;
  const fade = Math.min(1, t * 10, (60 - t) * 4);
  for (let c = 0; c < 2; c++) {
    const v = Math.tanh((music + fx) * 1.3) * fade;
    data.writeInt16LE(Math.round(v * 29000), i * 4 + c * 2);
  }
}
const header = Buffer.alloc(44);
header.write("RIFF");
header.writeUInt32LE(36 + data.length, 4);
header.write("WAVEfmt ", 8);
header.writeUInt32LE(16, 16);
header.writeUInt16LE(1, 20);
header.writeUInt16LE(2, 22);
header.writeUInt32LE(rate, 24);
header.writeUInt32LE(rate * 4, 28);
header.writeUInt16LE(4, 32);
header.writeUInt16LE(16, 34);
header.write("data", 36);
header.writeUInt32LE(data.length, 40);
mkdirSync("public/gta6", { recursive: true });
writeFileSync("public/gta6/voiced-score.wav", Buffer.concat([header, data]));
console.log("Original stereo score: 60s / 48kHz");
