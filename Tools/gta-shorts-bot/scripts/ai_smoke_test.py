"""Check real local TTS, Whisper and vision without footage/accounts or uploads."""
import argparse
import json
from pathlib import Path
import time

from shorts.core import load, save_json
from shorts import ai, media


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--config", type=Path, default=Path("config.json"))
    args = parser.parse_args()
    cfg = load(args.config)
    folder = cfg.data_dir / "smoke-test"
    folder.mkdir(parents=True, exist_ok=True)
    voice = folder / "piper-test.wav"
    start = time.perf_counter()
    media.voice(cfg, "The getaway car has applied for a desk job.", voice)
    voice_seconds = media.duration(cfg, voice)
    transcript = ai.transcribe(cfg, voice)
    result = ai.chat(cfg, "This image is a generated test pattern. Return no moments: "
                     "it is not GTA gameplay. This checks the local vision/JSON interface only.",
                     ai.Analysis, [folder / "preview.jpg"])
    if result.moments:
        raise ValueError("Model incorrectly selected a test pattern")
    if not transcript or not any("car" in s["text"].lower() for s in transcript):
        raise ValueError("Whisper did not recover the test speech")
    report = {"tts_duration": voice_seconds, "transcript": transcript, "vision_rejected_pattern": True,
              "seconds": round(time.perf_counter() - start, 1)}
    save_json(folder / "ai-result.json", report)
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
