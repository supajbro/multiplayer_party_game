import argparse
import ctypes
import importlib.metadata
import json
import logging
from pathlib import Path
import platform
import shutil
import sys

from .core import load, Store, command, save_json, pipeline_lock
from .rights import read_rights, import_file, check_source
from . import discovery, pipeline, youtube, ai


def doctor(cfg):
    report = {"python": sys.version, "platform": platform.platform(), "checks": {}}
    for name in ("ffmpeg", "ffprobe"):
        try:
            report["checks"][name] = command([getattr(cfg, name), "-version"], timeout=15).splitlines()[0]
        except (OSError, RuntimeError) as e:
            report["checks"][name] = f"MISSING: {e}"
    try:
        report["gpu"] = command(["nvidia-smi", "--query-gpu=name,memory.total", "--format=csv,noheader"], timeout=15).strip()
    except (OSError, RuntimeError):
        report["gpu"] = "NVIDIA unavailable; CPU fallback"
    if sys.platform == "win32":
        class Memory(ctypes.Structure):
            _fields_ = [("length", ctypes.c_ulong), ("load", ctypes.c_ulong)] + [
                (x, ctypes.c_ulonglong) for x in ("total", "avail", "page", "apage", "virt", "avirt", "ext")]
        mem = Memory()
        mem.length = ctypes.sizeof(mem)
        if ctypes.windll.kernel32.GlobalMemoryStatusEx(ctypes.byref(mem)):
            report["ram_gib"] = round(mem.total / 2**30, 1)
    disk = shutil.disk_usage(cfg.data_dir)
    report["data_disk_free_gib"] = round(disk.free / 2**30, 1)
    try:
        import requests
        response = requests.get(cfg.ollama_url + "/api/tags", timeout=10)
        response.raise_for_status()
        report["ollama_models"] = [m["name"] for m in response.json()["models"]]
        report["checks"]["model"] = "ok" if cfg.model in report["ollama_models"] else "MISSING: run ollama pull " + cfg.model
    except Exception:
        report["checks"]["model"] = "MISSING: Ollama not reachable"
    report["checks"]["voice"] = "ok" if cfg.voice_model.is_file() else "MISSING: download Piper voice"
    report["checks"]["voice_license"] = "recorded" if cfg.voice_license_evidence else "MISSING: review voice terms"
    report["checks"]["oauth"] = "configured" if cfg.oauth_client.is_file() else "MISSING: Google desktop OAuth client"
    report["packages"] = {n: importlib.metadata.version(n) for n in
                          ("faster-whisper", "piper-tts", "requests", "google-api-python-client")}
    save_json(cfg.data_dir / "doctor.json", report)
    print(json.dumps(report, indent=2))


def main():
    parser = argparse.ArgumentParser(description="Local, rights-gated GTA Shorts bot")
    parser.add_argument("--config", type=Path, default=Path("config.json"))
    sub = parser.add_subparsers(dest="command", required=True)
    for name in ("doctor", "discover", "run", "auth", "status"):
        sub.add_parser(name)
    p = sub.add_parser("import")
    p.add_argument("video", type=Path)
    p.add_argument("rights", type=Path)
    p = sub.add_parser("acquire-twitch")
    p.add_argument("rights", type=Path)
    for name in ("analyse", "revoke"):
        p = sub.add_parser(name)
        p.add_argument("source_id")
    for name in ("render", "upload", "publish"):
        p = sub.add_parser(name)
        p.add_argument("production_id")
    p = sub.add_parser("reconcile-upload", help="Record a verified remote upload after an uncertain outcome")
    p.add_argument("production_id")
    p.add_argument("video_id")
    args = parser.parse_args()
    try:
        cfg = load(args.config)
        store = Store(cfg)
        if args.command == "doctor":
            doctor(cfg)
        elif args.command == "run":
            print(pipeline.run(cfg, store) or "Skipped: insufficient suitable footage")
        elif args.command == "auth":
            print("Authorized channel:", youtube.channel_id(youtube.service(cfg, interactive=True)))
        elif args.command == "status":
            print(json.dumps([dict(r) for r in store.db.execute(
                "SELECT id,day,status,upload_id,error FROM productions ORDER BY day DESC")], indent=2))
            print("Sources:", store.db.execute("SELECT count(*) FROM sources").fetchone()[0])
        else:
            with pipeline_lock(cfg.data_dir):
                if args.command == "discover":
                    discovery.discover(cfg, store)
                elif args.command == "import":
                    import_file(cfg, store, args.video, read_rights(args.rights))
                elif args.command == "acquire-twitch":
                    discovery.acquire_twitch(cfg, store, read_rights(args.rights))
                elif args.command == "revoke":
                    row = store.db.execute("SELECT rights FROM sources WHERE id=?", (args.source_id,)).fetchone()
                    if not row or not row["rights"]:
                        raise ValueError("No rights record for that source")
                    record = json.loads(row["rights"])
                    record["commercial_use"] = False
                    store.db.execute("UPDATE sources SET rights=? WHERE id=?", (json.dumps(record), args.source_id))
                    store.db.commit()
                    store.event("revocation", args.source_id + "; review any existing YouTube uploads separately")
                elif args.command == "analyse":
                    row = store.db.execute("SELECT * FROM sources WHERE id=?", (args.source_id,)).fetchone()
                    if not row:
                        raise ValueError("Unknown source")
                    check_source(row)
                    result = ai.analyse(cfg, row, cfg.data_dir / "analysis" / row["sha256"])
                    store.db.execute("UPDATE sources SET analysis=? WHERE id=?", (json.dumps(result), args.source_id))
                    store.db.commit()
                    print(json.dumps(result["moments"], indent=2))
                elif args.command == "render":
                    print(pipeline.render(cfg, store, args.production_id))
                elif args.command == "upload":
                    print(youtube.upload(cfg, store, args.production_id))
                elif args.command == "publish":
                    youtube.publish(cfg, store, args.production_id)
                elif args.command == "reconcile-upload":
                    youtube.reconcile(cfg, store, args.production_id, args.video_id)
    except Exception as e:
        # Avoid traceback dumps of HTTP request URLs or tokens in scheduler logs.
        logging.error("%s: %s", type(e).__name__, str(e)[:1000])
        raise SystemExit(1)


if __name__ == "__main__":
    main()
