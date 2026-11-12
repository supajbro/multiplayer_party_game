from __future__ import annotations

import json
from pathlib import Path
import re
import subprocess
import wave

from .core import command


def probe(cfg, path):
    return json.loads(command([cfg.ffprobe, "-v", "error", "-show_format", "-show_streams",
                               "-of", "json", path], timeout=60))


def duration(cfg, path):
    return float(probe(cfg, path)["format"]["duration"])


def ffmpeg(cfg, args, timeout=900):
    return command([cfg.ffmpeg, "-hide_banner", "-loglevel", "error", "-nostdin", "-y", *args],
                   timeout=timeout)


def frame(cfg, source, time, dest):
    ffmpeg(cfg, ["-ss", str(time), "-i", source, "-frames:v", "1", "-vf", "scale=640:-2", dest])


def audio(cfg, source, dest):
    ffmpeg(cfg, ["-i", source, "-vn", "-ac", "1", "-ar", "16000", dest])


def reaction_cues(cfg, source, folder, transcript):
    """Bounded audio measurements; peaks are cues, never a humour decision."""
    import numpy as np
    path = folder / "analysis.wav"
    audio(cfg, source, path)
    with wave.open(str(path), "rb") as f:
        rate = f.getframerate()
        samples = np.frombuffer(f.readframes(f.getnframes()), dtype="<i2").astype(np.float32) / 32768
    rms = [float(np.sqrt(np.mean(samples[i:i+rate]**2))) for i in range(0, len(samples), rate)]
    baseline = max(float(np.median(rms)), .001)
    cues = [{"second": i, "rms_db": round(20*float(np.log10(max(x, 1e-6))), 1),
             "cue": "energy spike; could be reaction, music or game sound"}
            for i, x in enumerate(rms) if x > baseline*2.5 and x > .03]
    for s in transcript:
        if re.search(r"\b(laugh\w*|scream\w*|ha ha|oh my god|what the|no way)\b", s["text"], re.I):
            cues.append({"second": s["start"], "cue": "possible reaction in transcript", "text": s["text"]})
    return cues


def fingerprint(cfg, source, start, length, folder):
    """Three difference hashes: catches many resizes/re-encodes, not all edits."""
    from PIL import Image
    hashes = []
    for i, fraction in enumerate((.2, .5, .8)):
        path = folder / f"hash-{i}.png"
        frame(cfg, source, start + length * fraction, path)
        with Image.open(path) as image:
            image = image.convert("L").resize((9, 8))
            p = list(image.getdata())
        bits = [p[y * 9 + x] > p[y * 9 + x + 1] for y in range(8) for x in range(8)]
        hashes.append(f"{sum(int(b) << i for i, b in enumerate(bits)):016x}")
    return hashes


def similar(a, b):
    return len(a) == len(b) == 3 and sum(
        (int(x, 16) ^ int(y, 16)).bit_count() <= 7 for x, y in zip(a, b)) >= 2


def voice(cfg, text, dest):
    if not cfg.voice_license_evidence.strip():
        raise ValueError("Set voice_license_evidence after reviewing voice model commercial terms")
    if not cfg.voice_model.is_file() or not Path(str(cfg.voice_model) + ".json").is_file():
        raise ValueError("Piper voice .onnx and .onnx.json files are required")
    import sys
    command([sys.executable, "-m", "piper", "-m", cfg.voice_model, "-f", dest], input=text,
            timeout=180)


def ass_time(seconds):
    ticks = max(0, round(seconds * 100))
    return f"{ticks // 360000}:{ticks // 6000 % 60:02}:{ticks // 100 % 60:02}.{ticks % 100:02}"


def subtitles(path, entries):
    header = """[Script Info]
ScriptType: v4.00+
PlayResX: 1080
PlayResY: 1920
WrapStyle: 0
[V4+ Styles]
Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
Style: Default,Arial,58,&H00FFFFFF,&H000000FF,&H00101010,&H80000000,-1,0,0,0,100,100,0,0,1,4,1,2,80,80,260,1
[Events]
Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
"""
    lines = [header]
    for start, end, text in entries:
        if end <= start:
            continue
        # Remove ASS control characters from untrusted transcription/model output.
        text = re.sub(r"[{}\\\r\n]", " ", text).strip()
        words = text.split()
        # Short timed phrases, not paragraphs across the gameplay.
        for n in range(0, len(words), 7):
            a = start + (end - start) * n / max(1, len(words))
            b = start + (end - start) * min(n + 7, len(words)) / max(1, len(words))
            lines.append(f"Dialogue: 0,{ass_time(a)},{ass_time(b)},Default,,0,0,0,,{' '.join(words[n:n+7])}\n")
    path.write_text("".join(lines), encoding="utf-8")


def escaped_filter_path(path):
    return str(path.resolve()).replace("\\", "/").replace(":", "\\:").replace("'", "\\'")


def render_segment(cfg, source, start, length, narration, narration_start, rights,
                   entries, folder, output, zoom=False, action_length=None):
    sub = folder / (output.stem + ".ass")
    subtitles(sub, entries)
    if rights.layout == "crop":
        vf = ("scale=1080:1920:force_original_aspect_ratio=increase,"
              f"crop=1080:1920:(iw-ow)*{rights.focus_x}:(ih-oh)/2")
    else:
        # Foreground keeps ALL gameplay and facecam; blurred background fills the vertical frame.
        vf = None
    action_length = action_length or length
    inputs = ["-ss", str(start), "-t", str(action_length), "-i", source, "-i", narration]
    base = (f"[0:v]{vf},setsar=1,fps=30[v0];" if vf else
            "[0:v]split=2[bg][fg];[bg]scale=1080:1920:force_original_aspect_ratio=increase,"
            "crop=1080:1920,boxblur=30:2[back];"
            "[fg]scale=1080:1920:force_original_aspect_ratio=decrease[front];"
            "[back][front]overlay=(W-w)/2:(H-h)/2,setsar=1,fps=30[v0];")
    if zoom:
        # A 4% punch-in for the final payoff clip.
        base += f"[v0]scale=1124:1998,crop=1080:1920[vz];"
        video_label = "vz"
    else:
        video_label = "v0"
    base += (f"[{video_label}]tpad=stop_mode=clone:stop_duration=6,ass=filename='{escaped_filter_path(sub)}',"
             "fade=t=in:st=0:d=0.08,format=yuv420p[v];")
    delay = int(narration_start * 1000)
    # Narration follows action, ducking only its own interval to preserve the original reaction.
    vd = duration(cfg, narration)
    base += (f"[0:a]apad,atrim=duration={length},asetpts=PTS-STARTPTS,"
             f"volume=0.3:enable='between(t,{narration_start},{narration_start+vd})'[game];"
             f"[1:a]aresample=48000,adelay={delay}|{delay}[voice];"
             "[game][voice]amix=inputs=2:duration=first:normalize=0[mix];")
    if zoom and cfg.payoff_sound:
        # A quiet, locally synthesized descending chirp; no external SFX licensing.
        base += ("aevalsrc=0.08*sin(2*PI*(700*t-1800*t*t))*exp(-35*t):s=48000:d=0.12,"
                 f"adelay={delay}|{delay}[sfx];"
                 "[mix][sfx]amix=inputs=2:duration=first:normalize=0,alimiter=limit=0.95[a]")
    else:
        base += "[mix]alimiter=limit=0.95[a]"
    ffmpeg(cfg, [*inputs, "-filter_complex", base, "-map", "[v]", "-map", "[a]",
                 "-t", str(length), "-c:v", "libx264", "-preset", "fast", "-crf", "20",
                 "-c:a", "aac", "-ar", "48000", "-ac", "2", "-movflags", "+faststart", output])


def assemble(cfg, parts, folder, output):
    concat = folder / "concat.txt"
    # Generated filenames contain no user-controlled text.
    concat.write_text("".join(f"file '{p.name}'\n" for p in parts), encoding="utf-8")
    ffmpeg(cfg, ["-f", "concat", "-safe", "1", "-i", concat, "-af",
                 "loudnorm=I=-16:TP=-1.5:LRA=11", "-c:v", "copy", "-c:a", "aac",
                 "-ar", "48000", "-movflags", "+faststart", output])


def quality_check(cfg, path):
    info = probe(cfg, path)
    v = next(s for s in info["streams"] if s["codec_type"] == "video")
    a = next(s for s in info["streams"] if s["codec_type"] == "audio")
    seconds = float(info["format"]["duration"])
    if not (30 <= seconds <= 45.2 and v["width"] == 1080 and v["height"] == 1920):
        raise ValueError("Output failed duration/resolution QC")
    if a["codec_name"] != "aac" or v["codec_name"] != "h264":
        raise ValueError("Output failed codec QC")
    # Full decode catches truncation/corruption, not humour or composition defects.
    ffmpeg(cfg, ["-xerror", "-i", path, "-f", "null", "-"], timeout=300)
    stats = subprocess.run([str(cfg.ffmpeg), "-hide_banner", "-nostdin", "-i", str(path),
                            "-af", "volumedetect,silencedetect=noise=-45dB:d=3",
                            "-vf", "blackdetect=d=2:pix_th=0.1", "-f", "null", "-"],
                           capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=300)
    if stats.returncode:
        raise ValueError("QC signal analysis failed")
    log = stats.stderr
    mean = re.search(r"mean_volume: ([\-\d.]+) dB", log)
    peak = re.search(r"max_volume: ([\-\d.]+) dB", log)
    if not mean or not peak or float(mean[1]) < -40 or float(peak[1]) > -.1:
        raise ValueError("QC audio is silent, too quiet, or near clipping")
    if "silence_start:" in log or "black_start:" in log:
        raise ValueError("QC detected extended silence or black frames")
    return {"duration": seconds, "width": v["width"], "height": v["height"], "decode": "passed",
            "mean_db": float(mean[1]), "peak_db": float(peak[1]), "black_silence": "passed"}
