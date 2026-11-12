from datetime import datetime
import json
from pathlib import Path
import uuid

from .core import now, digest, save_json, pipeline_lock
from .rights import check_source
from . import ai, media, discovery, youtube


def select(candidates, previous_hashes):
    chosen = []
    for candidate in sorted(candidates, key=lambda x: x["moment"]["score"], reverse=True):
        fp = candidate["moment"]["fingerprint"]
        if any(media.similar(fp, p) for p in previous_hashes):
            continue
        if any(x["source_id"] == candidate["source_id"] or
               media.similar(fp, x["moment"]["fingerprint"]) for x in chosen):
            continue
        chosen.append(candidate)
        if len(chosen) == 3:
            # Save the strongest payoff for last.
            return [chosen[1], chosen[2], chosen[0]]
    return []


def captions(transcript, start, end):
    return [(max(s["start"], start)-start, min(s["end"], end)-start, s["text"])
            for s in transcript if s["end"] > start and s["start"] < end]


def render(cfg, store, production_id):
    row = store.db.execute("SELECT * FROM productions WHERE id=?", (production_id,)).fetchone()
    manifest = json.loads(row["manifest"])
    if row["status"] not in {"planned", "render_failed"}:
        raise ValueError("Only planned/failed renders can be resumed")
    folder = cfg.data_dir / "productions" / production_id
    folder.mkdir(parents=True, exist_ok=True)
    for sid in manifest["source_ids"]:
        check_source(store.db.execute("SELECT * FROM sources WHERE id=?", (sid,)).fetchone())
    try:
        script = manifest["script"]
        voices = []
        for i, text in enumerate([script["hook"], *script["jokes"]]):
            path = folder / f"voice-{i}.wav"
            media.voice(cfg, text, path)
            voices.append(path)
        durations = [media.duration(cfg, path) for path in voices]
        if durations[0] > 3 or any(d > 4.5 for d in durations[1:]):
            raise ValueError("Voice delivery exceeds budget; regenerate a shorter script")
        parts = []
        lengths = [c["moment"]["end"]-c["moment"]["start"]+d+.25
                   for c, d in zip(manifest["clips"], durations[1:])]
        padding = max(0, 30.25 - (3 + sum(lengths))) / 3
        total = 3.0
        first = manifest["clips"][0]
        source = store.db.execute("SELECT * FROM sources WHERE id=?", (first["source_id"],)).fetchone()
        rights = check_source(source)
        part = folder / "part-0.mp4"
        media.render_segment(cfg, Path(source["path"]), first["moment"]["start"], 3, voices[0], 0,
                             rights, [(0, durations[0], script["hook"])], folder, part, action_length=.1)
        parts.append(part)
        for i, clip in enumerate(manifest["clips"]):
            source = store.db.execute("SELECT * FROM sources WHERE id=?", (clip["source_id"],)).fetchone()
            rights = check_source(source)
            m = clip["moment"]
            action = m["end"] - m["start"]
            length = lengths[i] + padding
            total += length
            transcript = json.loads(source["analysis"])["transcript"]
            entries = captions(transcript, m["start"], m["end"])
            entries.append((action, action + durations[i+1], script["jokes"][i]))
            part = folder / f"part-{i+1}.mp4"
            media.render_segment(cfg, Path(source["path"]), m["start"], length, voices[i+1], action,
                                 rights, entries, folder, part, zoom=(i == 2), action_length=action)
            parts.append(part)
        if not 30 <= total <= 45:
            raise ValueError(f"Timeline is {total:.1f}s; outside 30-45s requirement")
        output = folder / "short.mp4"
        media.assemble(cfg, parts, folder, output)
        manifest["qc"] = media.quality_check(cfg, output)
        manifest["output"] = str(output)
        manifest["output_sha256"] = digest(output)
        save_json(folder / "manifest.json", manifest)
        store.db.execute("UPDATE productions SET status='rendered',manifest=?,error=NULL,updated=? WHERE id=?",
                         (json.dumps(manifest), now(), production_id))
        store.db.commit()
        return output
    except Exception as e:
        store.db.execute("UPDATE productions SET status='render_failed',error=?,updated=? WHERE id=?",
                         (str(e), now(), production_id))
        store.db.commit()
        raise


def run(cfg, store):
    with pipeline_lock(cfg.data_dir):
        # The scheduler uses the PC's local calendar date (Brisbane on this PC).
        day = datetime.now().date().isoformat()
        existing = store.db.execute("SELECT * FROM productions WHERE day=?", (day,)).fetchone()
        if existing:
            pid = existing["id"]
            if existing["status"] in {"planned", "render_failed"}:
                render(cfg, store, pid)
            current = store.db.execute("SELECT * FROM productions WHERE id=?", (pid,)).fetchone()
            if cfg.upload_enabled and current["status"] == "rendered":
                youtube.upload(cfg, store, pid)
            elif cfg.auto_publish and current["status"] == "uploaded":
                youtube.publish(cfg, store, pid)
            store.event("daily", f"Existing production {pid}: {current['status']}")
            return pid
        discovery.discover(cfg, store)
        discovery.acquire_queue(cfg, store)
        previous = [json.loads(r[0]) for r in store.db.execute("SELECT manifest FROM productions")]
        previous_hashes = [c["moment"]["fingerprint"] for p in previous for c in p["clips"]]
        rows = store.db.execute("SELECT * FROM sources WHERE path IS NOT NULL AND used_by IS NULL "
                                "ORDER BY attempted IS NOT NULL, attempted, discovered DESC").fetchall()
        candidates = []
        attempts = 0
        for row in rows:
            try:
                check_source(row)
                if row["analysis"]:
                    analysis = json.loads(row["analysis"])
                else:
                    if attempts >= cfg.max_sources_per_run:
                        continue
                    attempts += 1
                    store.db.execute("UPDATE sources SET attempted=? WHERE id=?", (now(), row["id"]))
                    store.db.commit()
                    analysis = ai.analyse(cfg, row, cfg.data_dir / "analysis" / row["sha256"])
                    store.db.execute("UPDATE sources SET analysis=? WHERE id=?", (json.dumps(analysis), row["id"]))
                    store.db.commit()
                for m in analysis["moments"]:
                    if m["score"] >= cfg.min_score:
                        candidates.append({"source_id": row["id"], "moment": m})
            except Exception as e:
                store.event("analysis", f"{row['id']}: {type(e).__name__}: {str(e)[:300]}")
        clips = select(candidates, previous_hashes)
        if not clips:
            store.event("skip", "Need three distinct authorized, high-confidence funny moments")
            return None
        script = ai.write_script(cfg, [c["moment"] for c in clips], [p["script"] for p in previous])
        pid = day + "-" + uuid.uuid4().hex[:8]
        credits = []
        for clip in clips:
            source = store.db.execute("SELECT * FROM sources WHERE id=?", (clip["source_id"],)).fetchone()
            rights = check_source(source)
            credits.append(rights.attribution + (" — " + rights.source_url if rights.source_url else ""))
        manifest = {"id": pid, "day": day, "clips": clips, "source_ids": [c["source_id"] for c in clips],
                    "script": script.model_dump(), "credits": credits}
        with store.db:
            store.db.execute("INSERT INTO productions VALUES(?,?,'planned',?,NULL,NULL,?)",
                             (pid, day, json.dumps(manifest), now()))
            for sid in manifest["source_ids"]:
                store.db.execute("UPDATE sources SET used_by=? WHERE id=?", (pid, sid))
        output = render(cfg, store, pid)
        store.event("render", str(output))
        if cfg.upload_enabled:
            youtube.upload(cfg, store, pid)
        return pid
