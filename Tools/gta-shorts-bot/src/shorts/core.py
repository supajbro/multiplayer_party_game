from __future__ import annotations

import contextlib
import hashlib
import json
import logging
import logging.handlers
import os
from pathlib import Path
import sqlite3
import subprocess
from datetime import datetime, timezone

from dotenv import load_dotenv
from pydantic import BaseModel, ConfigDict, Field, model_validator


def now() -> str:
    return datetime.now(timezone.utc).isoformat()


def digest(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for block in iter(lambda: f.read(1024 * 1024), b""):
            h.update(block)
    return h.hexdigest()


def save_json(path: Path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_suffix(path.suffix + ".tmp")
    tmp.write_text(json.dumps(value, indent=2, ensure_ascii=False), encoding="utf-8")
    tmp.replace(path)


def command(args, *, timeout=600, input=None) -> str:
    result = subprocess.run([str(a) for a in args], input=input, capture_output=True,
                            encoding="utf-8", errors="replace", timeout=timeout)
    if result.returncode:
        # Do not include command arguments: they may contain temporary download credentials.
        raise RuntimeError(f"{Path(str(args[0])).name} failed: {result.stderr[-3000:]}")
    return result.stdout


class Settings(BaseModel):
    model_config = ConfigDict(extra="forbid")
    data_dir: Path = Path("data")
    ffmpeg: str = "ffmpeg"
    ffprobe: str = "ffprobe"
    ollama_url: str = "http://127.0.0.1:11434"
    model: str = "qwen3-vl:4b"
    whisper_model: str = "small"
    whisper_device: str = "cpu"
    whisper_compute: str = "int8"
    voice_model: Path = Path("models/en_US-lessac-medium.onnx")
    voice_license_evidence: str = ""
    font_file: Path = Path("C:/Windows/Fonts/arialbd.ttf")
    youtube_queries: list[str] = ["GTA 6 funny gameplay", "GTA 6 fails glitches"]
    twitch_game_name: str = "Grand Theft Auto VI"
    lookback_days: int = Field(7, ge=1, le=30)
    min_views: int = Field(100, ge=0)
    max_sources_per_run: int = Field(8, ge=1, le=50)
    max_source_seconds: int = Field(180, ge=15, le=3600)
    min_score: float = Field(7.5, ge=0, le=10)
    upload_enabled: bool = False
    auto_publish: bool = False
    testing_complete: bool = False
    expected_channel_id: str = ""
    contains_synthetic_media: bool = False
    oauth_client: Path = Path("secrets/client_secret.json")
    oauth_token: Path = Path("secrets/youtube_token.json")

    @model_validator(mode="after")
    def local_only(self):
        from urllib.parse import urlparse
        u = urlparse(self.ollama_url)
        if u.hostname not in {"localhost", "127.0.0.1", "::1"}:
            raise ValueError("Use a local Ollama endpoint")
        if self.auto_publish and not (self.testing_complete and self.upload_enabled):
            raise ValueError("auto_publish requires testing_complete and upload_enabled")
        return self


def load(path: Path) -> Settings:
    load_dotenv(path.resolve().parent / ".env")
    cfg = Settings.model_validate_json(path.read_text(encoding="utf-8"))
    # Paths are relative to config, independent of Task Scheduler's working directory.
    for name in ("data_dir", "voice_model", "font_file", "oauth_client", "oauth_token"):
        value = getattr(cfg, name)
        if not value.is_absolute():
            setattr(cfg, name, path.resolve().parent / value)
    cfg.data_dir.mkdir(parents=True, exist_ok=True)
    logdir = cfg.data_dir / "logs"
    logdir.mkdir(exist_ok=True)
    handler = logging.handlers.RotatingFileHandler(logdir / "bot.log", maxBytes=2_000_000,
                                                    backupCount=5, encoding="utf-8")
    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s",
                        handlers=[handler, logging.StreamHandler()])
    return cfg


class Store:
    def __init__(self, cfg: Settings):
        self.db = sqlite3.connect(cfg.data_dir / "history.sqlite3")
        self.db.row_factory = sqlite3.Row
        self.db.execute("PRAGMA journal_mode=WAL")
        self.db.executescript('''
          CREATE TABLE IF NOT EXISTS sources (
            id TEXT PRIMARY KEY, platform TEXT NOT NULL, metadata TEXT NOT NULL,
            discovered TEXT NOT NULL, rights TEXT, path TEXT, sha256 TEXT UNIQUE,
            analysis TEXT, used_by TEXT);
          CREATE TABLE IF NOT EXISTS productions (
            id TEXT PRIMARY KEY, day TEXT NOT NULL UNIQUE, status TEXT NOT NULL,
            manifest TEXT NOT NULL, upload_id TEXT, error TEXT, updated TEXT NOT NULL);
          CREATE TABLE IF NOT EXISTS events (
            time TEXT NOT NULL, stage TEXT NOT NULL, detail TEXT NOT NULL);
        ''')
        self.db.commit()

    def event(self, stage, detail):
        self.db.execute("INSERT INTO events VALUES (?,?,?)", (now(), stage, detail))
        self.db.commit()
        logging.info("%s: %s", stage, detail)

    def discover(self, source_id, platform, metadata):
        self.db.execute('''INSERT INTO sources(id,platform,metadata,discovered) VALUES(?,?,?,?)
          ON CONFLICT(id) DO UPDATE SET metadata=excluded.metadata,discovered=excluded.discovered''',
                        (source_id, platform, json.dumps(metadata), now()))
        self.db.commit()


@contextlib.contextmanager
def pipeline_lock(folder: Path):
    """OS-backed lock releases after a crash; no stale lock file recovery needed."""
    import msvcrt
    with (folder / "pipeline.lock").open("a+b") as f:
        f.seek(0)
        f.write(b"0")
        f.flush()
        f.seek(0)
        try:
            msvcrt.locking(f.fileno(), msvcrt.LK_NBLCK, 1)
        except OSError as e:
            raise RuntimeError("Another pipeline process is running") from e
        try:
            yield
        finally:
            f.seek(0)
            msvcrt.locking(f.fileno(), msvcrt.LK_UNLCK, 1)
