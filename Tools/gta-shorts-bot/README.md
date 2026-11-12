# Local GTA Shorts Bot

A Windows Python application: official API discovery → explicit permission/import queue → Whisper + local vision analysis → three distinct moments → contextual commentary → Piper → FFmpeg vertical edit → technical QC → optional private YouTube upload. SQLite records sources, rights, source hashes, analyses, production reservations, upload outcomes and errors.

No paid AI services. API accounts, local disk, bandwidth and electricity are still required. Discovery is optional: authorized local recordings work without Twitch/YouTube discovery credentials.

## Current status

The application and setup scripts are implemented. This is an initial implementation requiring calibration with your footage, not an established unattended production service. See `VALIDATION.md` for actual test results and limits. OAuth consent, source permissions, real gameplay evaluation, and review of initial private uploads remain account/content setup steps. Uploading and public publishing are disabled by default.

## Hardware and model choice

Detected 11 November 2026: Intel i7-10700K, RTX 3060 Ti (8192 MiB VRAM), 31.9 GiB usable RAM. Initial storage: C: 201.8 GiB free; D: 1192.8 GiB free, before installations. Use `doctor` for current figures.

Start with `qwen3-vl:4b` (Ollama quantized download about 3.3 GB), 8192-token context, six 640px frames per window. Run Whisper `small` with CPU/int8 so its CUDA libraries and GPU memory do not compete with the vision model. Ollama keeps the model warm for two minutes between requests. If vision runs out of memory, try `qwen3-vl:2b`; benchmark quality before accepting it. `whisper_device=cuda` requires compatible CUDA/cuDNN dependencies described by Faster-Whisper and is not needed for the default setup.

Allow 15–25 GB initially for tools, models and tests, plus media storage. Set `data_dir` to an absolute D: folder if desired; the application creates it. Rendering uses CPU/libx264 for compatibility. No speed or comedy-quality claims are made without benchmarking representative footage.

## Install / reproduce

Open PowerShell as your normal Windows user:

```powershell
cd 'C:\Users\Jaden\Documents\MyUnityProjects\multiplayer_party_game\Tools\gta-shorts-bot'
winget install --id Python.Python.3.12 --exact --scope user --silent --accept-package-agreements --accept-source-agreements
winget install --id Gyan.FFmpeg --exact --silent --accept-package-agreements --accept-source-agreements
winget install --id Ollama.Ollama --exact --silent --accept-package-agreements --accept-source-agreements
# Open Ollama from Start if it is not already running.
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\setup.ps1 -DownloadModels
```

The setup script creates `.venv`, installs this project, copies configuration only if absent, and resolves WinGet FFmpeg paths. It preserves existing config and credentials. `-InstallTools` can perform the three WinGet installs. Network is needed for installation and initial model downloads; AI inference is local thereafter.

Exact individual environment commands:

```powershell
& "$env:LOCALAPPDATA\Programs\Python\Python312\python.exe" -m venv .venv
.\.venv\Scripts\python.exe -m pip install -e .
Copy-Item config.example.json config.json  # only on first setup
Copy-Item .env.example .env                # only on first setup
& "$env:LOCALAPPDATA\Programs\Ollama\ollama.exe" pull qwen3-vl:4b
.\.venv\Scripts\python.exe -m piper.download_voices --download-dir models en_US-ljspeech-high
.\.venv\Scripts\python.exe -m shorts.cli doctor
```

If the Python launcher points at a missing Store installation, use the explicit Python path above. If `doctor` cannot find FFmpeg, set `ffmpeg` and `ffprobe` in `config.json` to their absolute executable paths, or rerun setup after opening a fresh shell. Config paths resolve relative to the config file, not the shell's current directory.

Dependencies are bounded in `pyproject.toml`; `requirements.lock.txt` records the tested Windows environment. To reproduce it in a fresh venv, run `python -m pip install -r requirements.lock.txt`, then `python -m pip install --no-deps -e .`. The lock excludes the machine-specific editable-project path.

## Voice licensing

Default voice: `en_US-ljspeech-high`. Review its [model card](https://huggingface.co/rhasspy/piper-voices/blob/main/en/en_US/ljspeech/high/MODEL_CARD) and [dataset terms](https://keithito.com/LJ-Speech-Dataset/), retain a local copy of the evidence, and set `voice_license_evidence` to that record or URL. The dataset page identifies public-domain status in the US and notes territorial considerations; review the applicable terms for your use. This field records your review; code cannot determine legal validity. The initial Lessac voice was replaced because its model card points to research-only dataset terms. Do not assume all Piper voices have the same commercial permissions. Piper software is GPL-3.0; review obligations if distributing the software.

## Import footage with established rights

Copy `rights.example.json` for each source. Fill the creator, actual evidence, attribution, and source URL. Change permission flags to true only when established. Owning a recording does not establish rights to licensed music, third-party content, or publisher-controlled content. Remove/clear in-game radio music when necessary. An optional expiry must include a timezone.

```powershell
.\.venv\Scripts\python.exe -m shorts.cli import 'D:\Recordings\moment-one.mp4' '.\permissions\moment-one.json'
```

Import at least three distinct recordings, each containing a self-contained 6–9 second setup/payoff. Sources default to a 180 second maximum and at least 480p with audio. Trim longer footage first. Keep source IDs stable, e.g. `local:session-42-clip-01` or `youtube:VIDEO_ID` for a creator-provided authorized original. The importer copies media to managed storage, checks SHA-256 duplicates and preserves any discovery metadata already associated with the ID.

For unattended ingestion, place jobs in `data/inbox/*.json`:

```json
{
  "method": "local",
  "file": "D:/Recordings/authorized.mp4",
  "rights": { "...": "the complete filled rights.example.json object" }
}
```

For official Twitch acquisition use `method: "twitch"`, omit `file`, and supply a complete `twitch:CLIP_ID` rights record. Completed jobs move to `inbox/done`; failed jobs remain for correction and retry. This folder is input from you or an authorized source workflow; the bot never turns a search result or CC label into permission automatically.

Permission checks run again before analysis, rendering, upload and publication; changed/missing source files and expired permissions stop processing. Evidence is an auditable user assertion, not automated verification of legal rights. Revoke with `python -m shorts.cli revoke SOURCE_ID`; review existing YouTube uploads separately. Correct or renew permission by reimporting the same source with an updated valid rights record. Do not set permissions merely to make a job pass.

## Discovery credentials

Edit `.env` locally. Never paste secrets into chat or commit them.

**YouTube:** create a project in [Google Cloud Console](https://console.cloud.google.com/), enable **YouTube Data API v3**, and create an API key restricted to this API. Set `YOUTUBE_API_KEY`. Queries, freshness and view threshold are configurable. Search uses two queries by default, followed by video details/statistics; inspect your project's current quota before increasing frequency. Short durations are collected, but the Data API does not provide a dependable `isShort` flag. Category/title metadata cannot prove GTA VI identity.

**Twitch:** register an application in the [Twitch developer console](https://dev.twitch.tv/console/apps), then set `TWITCH_CLIENT_ID` and `TWITCH_CLIENT_SECRET`. The app obtains a client-credentials token for discovery. An exact configured game-category match is required; absent categories skip cleanly. Clips are filtered by views and recent publication. Engagement scores are stored for inspection; AI selection prioritizes comedy rather than raw popularity.

```powershell
.\.venv\Scripts\python.exe -m shorts.cli discover
```

Discovery stores metadata only. API metadata is refreshed by rediscovery or cleared after 30 days; independently supplied permission records, local hashes and production history remain.

YouTube discovery never downloads videos. Obtain original files from the rights holder or import your recordings. Twitch official downloading additionally requires a user token with `channel:manage:clips` or `editor:manage:clips`, and `TWITCH_EDITOR_ID` matching the token subject. Use Twitch's [authorization-code flow](https://dev.twitch.tv/docs/authentication/getting-tokens-oauth/#authorization-code-grant-flow); do not use unofficial media URL extraction. Set `TWITCH_USER_TOKEN` and `TWITCH_REFRESH_TOKEN` locally. The bot validates client, subject and scope before downloading, refreshes an expired token when possible and saves rotated tokens in `secrets/twitch_token.json`. Missing/revoked authorization fails acquisition safely while local imports remain usable. Discovery uses a separate app token.

```powershell
.\.venv\Scripts\python.exe -m shorts.cli acquire-twitch '.\permissions\twitch-clip.json'
```

Twitch broadcaster/editor access establishes API access, not the commercial rights to the broadcast or its music. See the [official download endpoint](https://dev.twitch.tv/docs/api/reference/clips).

## Run and inspect

```powershell
.\.venv\Scripts\python.exe -m shorts.cli run
.\.venv\Scripts\python.exe -m shorts.cli status
# Reanalyse an imported source after changing the model or analysis settings:
.\.venv\Scripts\python.exe -m shorts.cli analyse SOURCE_ID
```

The daily run discovers metadata, processes queued authorized imports, transcribes sources, examines overlapping 16-second windows and six timestamped frames, and requests structured setup/payoff explanations and scores from local Ollama. It requires high-confidence GTA VI identity and rejects weak/ambiguous scenes. A model can still mistake another game for GTA VI; manually validate your source pool. Three different sources are selected, with the highest scoring payoff last. Perceptual hashes reject many re-encodes/resizes; significant crops/reordered excerpts can evade them. SHA-256 detects exact copies.

Whisper dialogue and the vision model's observed reactions inform the comedy analysis. Acoustic energy and transcript cues are supporting heuristics, not a reliable laughter/scream classifier. Sparse frames can miss fast events; humour scores are model estimates and require real-footage calibration. Do not interpret an automated score as proof of entertainment quality.

Original hooks and three contextual jokes are constrained to the speech budget, checked against past wording, and saved in a manifest. Invalid or repeated model output stops the run. The editor creates a 3-second hook, three 6–9 second action sections and short freeze-frame commentary endings, totaling 30–45 seconds. Timing adapts to actual Piper speech lengths. Full-frame fit preserves facecam; optional `layout: crop` uses the permission record's `focus_x`. A small final punch-in and short fades are included. Set `payoff_sound: true` for a quiet locally synthesized chirp on the final joke; it defaults off. Narration follows the action to preserve reactions, ducks gameplay while speaking, burns phrase subtitles and normalizes audio. This version has no automatic face tracking; inspect composition. No third-party music or meme sound effects are fetched.

Inspect `data/productions/ID/short.mp4`, `manifest.json`, source transcripts under `data/analysis`, SQLite `events`, and rotating `data/logs/bot.log`. Technical QC checks codec, dimensions, duration, full decoding, audio level, extended silence and black frames; it does not assess joke taste, rights, misleading game identification, or all audio/caption defects. Subtitles use Whisper segment timing and proportional phrase timing, so check accuracy. A bad day skips rather than filling with weak content. Disk retention is manual in this version; archive reviewed output/intermediates periodically.

At most one production is reserved per PC-local calendar day, including when uploads are disabled. A failed render reserves its sources and can resume on the next run that day or explicitly:

```powershell
.\.venv\Scripts\python.exe -m shorts.cli render PRODUCTION_ID
```

## Connect YouTube and upload privately

1. In the Google Cloud project, configure OAuth consent / Google Auth Platform. Add your account as a test user if the app is in testing.
2. Create **OAuth client ID → Desktop app**; download its JSON to `secrets/client_secret.json`.
3. Run the interactive command and select the intended channel:

```powershell
.\.venv\Scripts\python.exe -m shorts.cli auth
```

4. Copy the printed channel ID into `config.json` → `expected_channel_id`. OAuth tokens stay in `secrets/youtube_token.json`; restrict folder access to your Windows account.
5. After inspecting rendered videos, set `upload_enabled: true`. Leave `auto_publish: false` for testing.

```powershell
.\.venv\Scripts\python.exe -m shorts.cli upload PRODUCTION_ID
```

Every insert is private. Resumable chunks retry transient failures; durable `uploading` state is written before remote insertion. A lost final response or crash leaves `upload_uncertain` / `uploading`, blocking another insert. Find the production marker in YouTube Studio, then reconcile a confirmed video:

```powershell
.\.venv\Scripts\python.exe -m shorts.cli reconcile-upload PRODUCTION_ID VIDEO_ID
```

If no upload exists, investigate the remote session before manually resetting that SQLite job to `rendered`; automatic retries cannot prove whether an uncertain insert created a video. Confirmed upload IDs are idempotent. Testing-mode OAuth refresh tokens can expire; run `auth` again if required. Channel destination is checked before upload.

For later auto-publication: validate representative private outputs, set `testing_complete`, `upload_enabled` and `auto_publish` to true, rerun `auth` to consent to the broader YouTube scope needed for status updates, then use `publish PRODUCTION_ID`. Publication requires successful YouTube processing and fresh source rights checks. A subsequent daily run retries publication for that day's existing private upload. Processing success is **not** a Content ID or monetization clearance; the API provides no universal rights-clearance endpoint. Account/project restrictions can still prevent public publication. Review current [upload documentation](https://developers.google.com/youtube/data/reference/rest/v3/videos/insert).

## Schedule after testing

Register a disabled task for inspection:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\register-task.ps1 -At '18:00'
```

Enable after reviewing its action/configuration:

```powershell
Enable-ScheduledTask -TaskName 'GTA Shorts Bot'
# Or register enabled explicitly:
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\register-task.ps1 -At '18:00' -Enable
```

Runs daily at PC-local time (Australia/Brisbane on this PC), as the logged-in user, without administrator privileges or stored password. Missed starts run when available. PC must be awake and you must be logged in. Overlapping scheduler instances are ignored; an OS file lock also guards manual runs. The runner starts Ollama hidden if needed, uses absolute Python/config paths, and returns a nonzero exit code on failure. No task is enabled by installation alone.

## Tests and evaluation

```powershell
.\.venv\Scripts\python.exe -m unittest discover -s tests -v
.\.venv\Scripts\python.exe -m pip check
.\.venv\Scripts\python.exe scripts\smoke_test.py
.\.venv\Scripts\python.exe scripts\ai_smoke_test.py
```

Smoke test generates its own footage/audio, exercises the actual FFmpeg path and never uploads. Compare several authorized, manually labelled funny/ordinary clips before scheduling. Record selection precision, false positives, missed setups, narration relevance/repetition, subtitle accuracy, facecam visibility, render time and private upload processing. Keep `testing_complete` false until satisfied. A larger model or more frames may improve context, at a memory/time cost.

Permission, copyright and monetization are separate. YouTube evaluates reused and inauthentic content at channel level; AI narration and credits do not guarantee eligibility. Prioritize substantive commentary, selective editing and a recognizable original editorial contribution. No monetization approval is promised. Review [YouTube monetization policies](https://support.google.com/youtube/answer/1311392), [API developer policies](https://developers.google.com/youtube/terms/developer-policies), [Faster-Whisper requirements](https://github.com/SYSTRAN/faster-whisper), and [Piper](https://github.com/OHF-Voice/piper1-gpl).
