"""Explicit per-source permission records. Discovery never grants permission."""
from datetime import datetime, timezone
from pathlib import Path
import json
import shutil

from pydantic import BaseModel, ConfigDict, Field, model_validator
from .core import Store, digest


class Rights(BaseModel):
    model_config = ConfigDict(extra="forbid")
    source_id: str = Field(min_length=3)
    creator: str = Field(min_length=1)
    source_url: str = ""
    basis: str = Field(pattern=r"^(owned|written_permission|licensed)$")
    evidence: str = Field(min_length=8)
    commercial_use: bool
    editing: bool
    youtube_distribution: bool
    third_party_audio_cleared: bool
    game_content_cleared: bool
    access_authorized: bool
    attribution: str = Field(min_length=1)
    expires: str | None = None
    # Preserve full frame (including facecam) by default. Optional per-source focal crop.
    layout: str = Field("fit", pattern=r"^(fit|crop)$")
    focus_x: float = Field(0.5, ge=0, le=1)

    @model_validator(mode="after")
    def validate_expiry(self):
        if self.expires:
            d = datetime.fromisoformat(self.expires)
            if d.tzinfo is None:
                raise ValueError("expires must include timezone, e.g. +00:00")
        return self

    def check(self):
        for name in ("commercial_use", "editing", "youtube_distribution",
                     "third_party_audio_cleared", "game_content_cleared", "access_authorized"):
            if not getattr(self, name):
                raise ValueError(f"Rights check failed: {name}")
        if self.expires and datetime.fromisoformat(self.expires) <= datetime.now(timezone.utc):
            raise ValueError("Permission expired")


def read_rights(path: Path) -> Rights:
    rights = Rights.model_validate_json(path.read_text(encoding="utf-8"))
    rights.check()
    return rights


def import_file(cfg, store: Store, path: Path, rights: Rights):
    rights.check()
    if path.suffix.lower() not in {".mp4", ".mov", ".mkv", ".webm"}:
        raise ValueError("Import a video file")
    sha = digest(path)
    existing = store.db.execute("SELECT id FROM sources WHERE sha256=?", (sha,)).fetchone()
    if existing and existing["id"] != rights.source_id:
        raise ValueError(f"Exact duplicate already imported as {existing['id']}")
    old = store.db.execute("SELECT * FROM sources WHERE id=?", (rights.source_id,)).fetchone()
    if old and old["sha256"] and old["sha256"] != sha:
        raise ValueError("Source ID already belongs to different footage")
    folder = cfg.data_dir / "media"
    folder.mkdir(exist_ok=True)
    target = folder / (sha + path.suffix.lower())
    if not target.exists() or digest(target) != sha:
        shutil.copy2(path, target)
    if not old:
        store.discover(rights.source_id, "local", {"creator": rights.creator, "url": rights.source_url})
    store.db.execute("UPDATE sources SET rights=?,path=?,sha256=? WHERE id=?",
                     (rights.model_dump_json(), str(target), sha, rights.source_id))
    store.db.commit()
    store.event("import", rights.source_id)


def check_source(row):
    rights = Rights.model_validate_json(row["rights"] or "{}")
    rights.check()
    path = Path(row["path"] or "")
    if not path.is_file() or digest(path) != row["sha256"]:
        raise ValueError("Missing or changed footage; reimport required")
    return rights
