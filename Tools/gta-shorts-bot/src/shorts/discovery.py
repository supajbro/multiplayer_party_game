"""Official API discovery and authorized Twitch acquisition only."""
from datetime import datetime, timedelta, timezone
import math
import json
import os
from pathlib import Path
import tempfile
from urllib.parse import urlparse

import requests
from .core import Store, now, save_json
from .rights import Rights, import_file


def acquire_queue(cfg, store):
    """User-authored jobs, never rights inferred from discovery labels."""
    inbox = cfg.data_dir / "inbox"
    inbox.mkdir(exist_ok=True)
    done = inbox / "done"
    done.mkdir(exist_ok=True)
    for job_path in sorted(inbox.glob("*.json"))[:cfg.max_sources_per_run]:
        try:
            job = json.loads(job_path.read_text(encoding="utf-8-sig"))
            rights = Rights.model_validate(job["rights"])
            rights.check()
            if job.get("method") == "twitch":
                acquire_twitch(cfg, store, rights)
            elif job.get("method") == "local":
                path = Path(job["file"])
                if not path.is_absolute():
                    path = job_path.parent / path
                import_file(cfg, store, path, rights)
            else:
                raise ValueError("Acquisition method must be local or twitch")
            job_path.replace(done / job_path.name)
        except Exception as e:
            store.event("acquisition", f"{job_path.name}: {type(e).__name__}; inspect job and authorization")


def get_json(url, **kwargs):
    r = requests.get(url, timeout=45, **kwargs)
    # Avoid logging full response/URL; API keys may appear in errors.
    if not r.ok:
        raise RuntimeError(f"API {urlparse(url).hostname} returned HTTP {r.status_code}")
    return r.json()


def twitch_headers(cfg, *, download=False):
    cid = os.getenv("TWITCH_CLIENT_ID")
    if not cid:
        raise ValueError("Set TWITCH_CLIENT_ID")
    token = None
    if download:
        saved = json.loads(cfg.twitch_token.read_text(encoding="utf-8")) if cfg.twitch_token.is_file() else {}
        token = saved.get("access_token") or os.getenv("TWITCH_USER_TOKEN")
        refresh = saved.get("refresh_token") or os.getenv("TWITCH_REFRESH_TOKEN")
        if not token:
            raise ValueError("Download requires TWITCH_USER_TOKEN with broadcaster/editor scope")
        validate = requests.get("https://id.twitch.tv/oauth2/validate",
                                headers={"Authorization": "OAuth " + token}, timeout=30)
        if validate.status_code == 401 and refresh:
            response = requests.post("https://id.twitch.tv/oauth2/token", timeout=45, data={
                "client_id": cid, "client_secret": os.getenv("TWITCH_CLIENT_SECRET", ""),
                "grant_type": "refresh_token", "refresh_token": refresh})
            if not response.ok:
                raise ValueError("Twitch token refresh failed; reauthorize the broadcaster/editor")
            saved = response.json()
            save_json(cfg.twitch_token, saved)
            token = saved["access_token"]
            validate = requests.get("https://id.twitch.tv/oauth2/validate",
                                    headers={"Authorization": "OAuth " + token}, timeout=30)
        if not validate.ok:
            raise ValueError("Twitch user token invalid; configure a refresh token or reauthorize")
        grant = validate.json()
        if grant.get("client_id") != cid or grant.get("user_id") != os.getenv("TWITCH_EDITOR_ID"):
            raise ValueError("Twitch token client/user does not match configured client/editor")
        if not {"channel:manage:clips", "editor:manage:clips"}.intersection(grant.get("scopes", [])):
            raise ValueError("Twitch token lacks clip management scope")
    else:
        r = requests.post("https://id.twitch.tv/oauth2/token", timeout=45, data={
            "client_id": cid, "client_secret": os.getenv("TWITCH_CLIENT_SECRET", ""),
            "grant_type": "client_credentials"})
        if not r.ok:
            raise RuntimeError(f"Twitch authentication returned HTTP {r.status_code}")
        token = r.json()["access_token"]
    return {"Client-Id": cid, "Authorization": f"Bearer {token}"}


def discover_twitch(cfg, store):
    headers = twitch_headers(cfg)
    games = get_json("https://api.twitch.tv/helix/games", headers=headers,
                     params={"name": cfg.twitch_game_name})["data"]
    if not games:
        store.event("twitch", "Configured game category not found; skipped")
        return
    start = (datetime.now(timezone.utc) - timedelta(days=cfg.lookback_days)).isoformat()
    params = {"game_id": games[0]["id"], "started_at": start, "first": 100}
    for _ in range(3):
        result = get_json("https://api.twitch.tv/helix/clips", headers=headers, params=params)
        for clip in result["data"]:
            if clip["view_count"] < cfg.min_views:
                continue
            clip["engagement_score"] = math.log1p(clip["view_count"])
            store.discover("twitch:" + clip["id"], "twitch", clip)
        cursor = result.get("pagination", {}).get("cursor")
        if not cursor:
            break
        params["after"] = cursor


def discover_youtube(cfg, store):
    key = os.getenv("YOUTUBE_API_KEY")
    if not key:
        raise ValueError("Set YOUTUBE_API_KEY")
    start = (datetime.now(timezone.utc) - timedelta(days=cfg.lookback_days)).isoformat()
    for query in cfg.youtube_queries:
        result = get_json("https://www.googleapis.com/youtube/v3/search", params={
            "key": key, "part": "snippet", "q": query, "type": "video", "maxResults": 25,
            "publishedAfter": start, "order": "relevance"})
        ids = [x["id"]["videoId"] for x in result.get("items", [])]
        if not ids:
            continue
        details = get_json("https://www.googleapis.com/youtube/v3/videos", params={
            "key": key, "part": "snippet,statistics,contentDetails,status", "id": ",".join(ids)})
        for video in details.get("items", []):
            stats = video.get("statistics", {})
            views = int(stats.get("viewCount", 0))
            if views < cfg.min_views:
                continue
            video["url"] = "https://www.youtube.com/watch?v=" + video["id"]
            likes = int(stats.get("likeCount", 0))
            video["engagement_score"] = math.log1p(views) + min(likes / max(views, 1), .2) * 10
            # Duration is metadata, not a reliable Shorts classifier.
            store.discover("youtube:" + video["id"], "youtube", video)


def discover(cfg, store):
    enabled = []
    if os.getenv("TWITCH_CLIENT_ID"):
        enabled.append(("twitch", discover_twitch))
    if os.getenv("YOUTUBE_API_KEY"):
        enabled.append(("youtube", discover_youtube))
    for name, fn in enabled:
        try:
            fn(cfg, store)
        except Exception as e:
            store.event(name, f"Discovery failed ({type(e).__name__}); check credentials/quota")
    if not enabled:
        store.event("discovery", "No credentials; using authorized local imports only")
    # Purge cached API metadata after 30 days; retain independent rights/hash/history.
    cutoff = (datetime.now(timezone.utc) - timedelta(days=30)).isoformat()
    store.db.execute("UPDATE sources SET metadata='{}' WHERE platform IN ('youtube','twitch') AND discovered<?",
                     (cutoff,))
    store.db.commit()


def acquire_twitch(cfg, store, rights: Rights):
    rights.check()
    if not rights.source_id.startswith("twitch:"):
        raise ValueError("Expected twitch:CLIP_ID rights record")
    headers = twitch_headers(cfg, download=True)
    clip_id = rights.source_id.split(":", 1)[1]
    clips = get_json("https://api.twitch.tv/helix/clips", headers=headers,
                     params={"id": clip_id})["data"]
    if not clips:
        raise ValueError("Clip unavailable")
    clip = clips[0]
    result = get_json("https://api.twitch.tv/helix/clips/downloads", headers=headers, params={
        "broadcaster_id": clip["broadcaster_id"], "editor_id": os.getenv("TWITCH_EDITOR_ID", ""),
        "clip_id": clip_id})["data"]
    if not result or not result[0].get("landscape_download_url"):
        raise ValueError("Official download unavailable for this authorization")
    url = result[0]["landscape_download_url"]
    if urlparse(url).scheme != "https":
        raise ValueError("Expected HTTPS download")
    with tempfile.TemporaryDirectory(dir=cfg.data_dir) as tmp:
        path = Path(tmp) / "clip.mp4"
        with requests.get(url, stream=True, timeout=(15, 90)) as response:
            if not response.ok:
                raise RuntimeError(f"Download returned HTTP {response.status_code}")
            total = 0
            with path.open("wb") as f:
                for chunk in response.iter_content(1024 * 1024):
                    total += len(chunk)
                    if total > 2 * 1024**3:
                        raise ValueError("Download exceeds 2 GiB limit")
                    f.write(chunk)
        store.discover(rights.source_id, "twitch", clip)
        import_file(cfg, store, path, rights)
