"""Generate local test media and exercise the real FFmpeg render path. Never uploads."""
from pathlib import Path
import argparse
import json
import time

from shorts.core import load, save_json
from shorts import media
from shorts.rights import Rights


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--config", type=Path, default=Path("config.json"))
    args = parser.parse_args()
    cfg = load(args.config)
    cfg.payoff_sound = True
    folder = cfg.data_dir / "smoke-test"
    folder.mkdir(parents=True, exist_ok=True)
    source = folder / "source.mp4"
    narration = folder / "narration.wav"
    media.ffmpeg(cfg, ["-f", "lavfi", "-i", "testsrc2=size=1280x720:rate=30", "-f", "lavfi",
                       "-i", "sine=frequency=220:sample_rate=48000", "-t", "12", "-c:v",
                       "libx264", "-preset", "ultrafast", "-c:a", "aac", source])
    media.ffmpeg(cfg, ["-f", "lavfi", "-i", "sine=frequency=440:sample_rate=22050", "-t", "2", narration])
    rights = Rights(source_id="local:smoke", creator="Test", basis="owned", evidence="Generated test pattern",
                    commercial_use=True, editing=True, youtube_distribution=True, third_party_audio_cleared=True,
                    game_content_cleared=True, access_authorized=True, attribution="Generated locally")
    parts = []
    start = time.perf_counter()
    for i in range(3):
        part = folder / f"part-{i}.mp4"
        # Exercise fit, crop, zoom, subtitles, freeze tail, narration and original audio.
        r = rights.model_copy(update={"layout": "crop" if i == 1 else "fit"})
        media.render_segment(cfg, source, 0, 11, narration, 9, r,
                             [(0, 4, "Local video test: readable captions"), (9, 11, "Narration follows action")],
                             folder, part, zoom=(i == 2), action_length=9)
        parts.append(part)
    output = folder / "smoke.mp4"
    media.assemble(cfg, parts, folder, output)
    result = media.quality_check(cfg, output)
    result["render_seconds"] = round(time.perf_counter() - start, 1)
    save_json(folder / "result.json", result)
    media.frame(cfg, output, 3, folder / "preview.jpg")
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    main()
