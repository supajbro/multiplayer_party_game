"""OAuth and crash-aware private-first uploads. No blind retry of uncertain inserts."""
import json
from pathlib import Path

from .core import digest, now, save_json
from .rights import check_source
from . import media

SCOPES = ["https://www.googleapis.com/auth/youtube.upload",
          "https://www.googleapis.com/auth/youtube.readonly"]


def service(cfg, interactive=False):
    from google.oauth2.credentials import Credentials
    from google.auth.transport.requests import Request
    from google_auth_oauthlib.flow import InstalledAppFlow
    from googleapiclient.discovery import build
    scopes = SCOPES + (["https://www.googleapis.com/auth/youtube"] if cfg.auto_publish else [])
    creds = None
    if cfg.oauth_token.is_file():
        creds = Credentials.from_authorized_user_file(str(cfg.oauth_token))
        if not creds.has_scopes(scopes):
            creds = None
    if creds and creds.expired and creds.refresh_token:
        creds.refresh(Request())
    if not creds or not creds.valid:
        if not interactive:
            raise ValueError("Run auth interactively first; scheduled tasks cannot request consent")
        flow = InstalledAppFlow.from_client_secrets_file(str(cfg.oauth_client), scopes)
        creds = flow.run_local_server(port=0, access_type="offline", prompt="consent")
    cfg.oauth_token.parent.mkdir(parents=True, exist_ok=True)
    cfg.oauth_token.write_text(creds.to_json(), encoding="utf-8")
    return build("youtube", "v3", credentials=creds, cache_discovery=False)


def channel_id(client):
    channels = client.channels().list(part="id", mine=True).execute().get("items", [])
    if len(channels) != 1:
        raise ValueError("OAuth account must select exactly one YouTube channel")
    return channels[0]["id"]


def upload(cfg, store, production_id):
    if not cfg.upload_enabled:
        raise ValueError("Uploads disabled in config")
    if not cfg.expected_channel_id:
        raise ValueError("Set expected_channel_id from auth output to pin the upload destination")
    row = store.db.execute("SELECT * FROM productions WHERE id=?", (production_id,)).fetchone()
    if not row:
        raise ValueError("Unknown production")
    if row["upload_id"]:
        return row["upload_id"]
    if row["status"] != "rendered":
        raise ValueError("Only rendered jobs can upload. Uncertain uploads need manual reconciliation.")
    manifest = json.loads(row["manifest"])
    for source_id in manifest["source_ids"]:
        check_source(store.db.execute("SELECT * FROM sources WHERE id=?", (source_id,)).fetchone())
    output = Path(manifest["output"])
    if digest(output) != manifest["output_sha256"]:
        raise ValueError("Output changed after QC")
    media.quality_check(cfg, output)
    client = service(cfg)
    if channel_id(client) != cfg.expected_channel_id:
        raise ValueError("OAuth channel does not match expected_channel_id")
    from googleapiclient.http import MediaFileUpload
    # Commit before any remote side effect. A crash blocks duplicate uploads.
    store.db.execute("UPDATE productions SET status='uploading',updated=? WHERE id=?", (now(), production_id))
    store.db.commit()
    script = manifest["script"]
    description = (script["description"] + "\n\n" + "\n".join(manifest["credits"]) +
                   f"\n\nProduction: {production_id}\nOriginal locally generated comedy narration. #Shorts")
    try:
        request = client.videos().insert(part="snippet,status", body={
            "snippet": {"title": script["title"], "description": description[:5000],
                        "categoryId": "20", "tags": ["GTA 6", "gaming", "Shorts"]},
            "status": {"privacyStatus": "private", "selfDeclaredMadeForKids": False,
                       "containsSyntheticMedia": cfg.contains_synthetic_media}},
            media_body=MediaFileUpload(str(output), mimetype="video/mp4", chunksize=8*1024*1024,
                                       resumable=True))
        response = None
        while response is None:
            _, response = request.next_chunk(num_retries=3)
        video_id = response["id"]
        store.db.execute("UPDATE productions SET status='uploaded',upload_id=?,updated=? WHERE id=?",
                         (video_id, now(), production_id))
        store.db.commit()
    except Exception:
        store.db.execute("UPDATE productions SET status='upload_uncertain',error=?,updated=? WHERE id=?",
                         ("Remote outcome uncertain; reconcile in YouTube Studio", now(), production_id))
        store.db.commit()
        raise RuntimeError("Upload outcome uncertain. Inspect YouTube Studio; do not blindly retry.") from None
    # Publication requires a separate explicit opt-in and completed testing.
    # Publishing requires the broader scope, requested only when auto_publish is enabled.
    if cfg.auto_publish:
        publish(cfg, store, production_id)
    return video_id


def publish(cfg, store, production_id):
    if not (cfg.auto_publish and cfg.testing_complete and cfg.upload_enabled):
        raise ValueError("Enable testing_complete, upload_enabled and auto_publish first")
    row = store.db.execute("SELECT * FROM productions WHERE id=?", (production_id,)).fetchone()
    if not row or not row["upload_id"]:
        raise ValueError("Production must have a confirmed private upload")
    manifest = json.loads(row["manifest"])
    for sid in manifest["source_ids"]:
        check_source(store.db.execute("SELECT * FROM sources WHERE id=?", (sid,)).fetchone())
    client = service(cfg)
    if channel_id(client) != cfg.expected_channel_id:
        raise ValueError("Wrong channel")
    video = client.videos().list(part="status,processingDetails", id=row["upload_id"]).execute()["items"]
    if not video or video[0].get("processingDetails", {}).get("processingStatus") != "succeeded":
        raise ValueError("YouTube processing not complete; retry publish later")
    status = video[0]["status"]
    if status.get("uploadStatus") != "processed" or status.get("rejectionReason"):
        raise ValueError("YouTube has not accepted the upload")
    client.videos().update(part="status", body={"id": row["upload_id"],
        "status": {"privacyStatus": "public", "selfDeclaredMadeForKids": False,
                   "containsSyntheticMedia": cfg.contains_synthetic_media}}).execute()
    store.db.execute("UPDATE productions SET status='published',updated=? WHERE id=?", (now(), production_id))
    store.db.commit()


def reconcile(cfg, store, production_id, video_id):
    row = store.db.execute("SELECT * FROM productions WHERE id=?", (production_id,)).fetchone()
    if not row or row["status"] not in {"uploading", "upload_uncertain"}:
        raise ValueError("Only uncertain uploads can be reconciled")
    client = service(cfg)
    if channel_id(client) != cfg.expected_channel_id:
        raise ValueError("Wrong channel")
    items = client.videos().list(part="snippet", id=video_id).execute().get("items", [])
    if not items or items[0]["snippet"]["channelId"] != cfg.expected_channel_id:
        raise ValueError("Video not found on expected channel")
    if f"Production: {production_id}" not in items[0]["snippet"]["description"]:
        raise ValueError("Video description does not contain this production marker")
    store.db.execute("UPDATE productions SET status='uploaded',upload_id=?,error=NULL,updated=? WHERE id=?",
                     (video_id, now(), production_id))
    store.db.commit()
