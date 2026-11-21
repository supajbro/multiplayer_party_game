import { readFileSync, writeFileSync } from "node:fs";
import { spawnSync } from "node:child_process";
const dir = "node_modules/@remotion/compositor-win32-x64-msvc/";
const path = "out/gta6/GTA6RecordShort-voiced.mp4";
const probe = spawnSync(
  dir + "ffprobe.exe",
  ["-v", "error", "-show_streams", "-show_format", "-of", "json", path],
  { encoding: "utf8" },
);
if (probe.status !== 0) throw new Error(probe.stderr);
const info = JSON.parse(probe.stdout),
  v = info.streams.find((s) => s.codec_type === "video"),
  a = info.streams.find((s) => s.codec_type === "audio");
if (
  v.width !== 1080 ||
  v.height !== 1920 ||
  v.r_frame_rate !== "30/1" ||
  v.codec_name !== "h264" ||
  Math.abs(Number(info.format.duration) - 60) > 0.1 ||
  a?.codec_name !== "aac" ||
  a.channels !== 2
)
  throw new Error("Unexpected format");
const decode = spawnSync(
  dir + "ffmpeg.exe",
  [
    "-v",
    "error",
    "-i",
    path,
    "-c:v",
    "rawvideo",
    "-c:a",
    "pcm_s16le",
    "-f",
    "null",
    "NUL",
  ],
  { encoding: "utf8" },
);
if (decode.status !== 0) throw new Error(decode.stderr);
const wav = readFileSync("public/gta6/voiced-mix.wav");
let peak = 0,
  sum = 0,
  count = 0;
let audioStart = 12, audioBytes = 0;
while (audioStart + 8 <= wav.length) {
  const size = wav.readUInt32LE(audioStart + 4);
  if (wav.toString('ascii', audioStart, audioStart + 4) === 'data') {
    audioStart += 8;
    audioBytes = size;
    break;
  }
  audioStart += 8 + size + (size % 2);
}
if (!audioBytes) throw new Error('Missing WAV audio data');
for (let i = audioStart; i < audioStart + audioBytes; i += 2) {
  const x = wav.readInt16LE(i) / 32768;
  peak = Math.max(peak, Math.abs(x));
  sum += x * x;
  count++;
}
const report = {
  file: path,
  width: v.width,
  height: v.height,
  fps: v.r_frame_rate,
  frames: v.nb_frames,
  duration: info.format.duration,
  videoCodec: v.codec_name,
  pixelFormat: v.pix_fmt,
  audioCodec: a.codec_name,
  audioChannels: a.channels,
  audioSampleRate: a.sample_rate,
  bytes: info.format.size,
  fullDecode: "passed",
  mixPeakDbFS: 20 * Math.log10(peak),
  mixRmsDbFS: 20 * Math.log10(Math.sqrt(sum / count)),
  narration:
    "en-US-AndrewMultilingualNeural, measured word timings, sidechain-ducked music",
};
writeFileSync("out/gta6/verification.json", JSON.stringify(report, null, 2));
console.log(JSON.stringify(report, null, 2));
