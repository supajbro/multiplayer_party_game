# Validation record — 11 November 2026

Environment: Windows 10, Python 3.12.10, i7-10700K, 31.9 GiB RAM, RTX 3060 Ti 8 GiB. FFmpeg/FFprobe 9.0.2, local Ollama Qwen3-VL 4B Q4_K_M, Piper 1.8.0 and Faster-Whisper 1.2.1. Exact Python packages are recorded in `requirements.lock.txt`.

## Completed

- Twelve unittest cases pass: every required permission flag, expiry, exact duplicate/tamper detection, distinct/near-duplicate moment selection, strongest payoff order, public-publishing opt-in validation, private insert body, confirmed-upload idempotency, uncertain-upload persistence/retry blocking, wrong-channel prevention, Twitch token refresh/rotation and editor identity validation. All account API requests in tests are mocked.
- `pip check`: no broken requirements. Python compilation and PowerShell parser checks pass.
- Real generated-media render: full-frame fit, focal crop, final zoom, ASS captions, game audio, delayed voice track, freeze tail, concat, H.264/AAC and loudness processing. First pass: 33.021 seconds, 1080×1920, full decode passed, about 16.6 seconds rendering. Visual preview inspected for full-frame layout and readable outlined captions. Follow-up signal checks: mean -18.1 dB, peak -14.0 dB, no extended silence/black frames. The source is a test pattern and tones, not representative gameplay.
- Follow-up real render with the optional locally synthesized payoff sound also passed: 33.021 seconds, mean -18.1 dB, peak -11.4 dB, decode/black/silence checks passed, 14.7 seconds rendering.
- Real Piper synthesis produced 2.74 seconds of speech. Real Whisper-small CPU/int8 recovered the complete test sentence with word timestamps. Real local vision inference accepted the structured JSON interface and returned no gameplay moments for the generated test pattern. Combined initial AI smoke test took 138 seconds. This is a connectivity/rejection smoke test, not an accuracy benchmark; real multi-frame analysis may be slow.
- The speech test exposed PyAV 19 removing an argument Faster-Whisper 1.2.1 uses. Dependency now constrains PyAV to 16.x; actual successful test used 16.1.0.
- Empty-pool daily run skipped without creating/uploading a production.
- Windows Task Scheduler task `GTA Shorts Bot` registered at 18:00 local time, **disabled**, with logged-in-user execution and no stored password. PowerShell scripts parse successfully.

Generated outputs and runtime reports are ignored by Git under `data/smoke-test/` and `data/doctor.json`.

## Still requiring your account / footage

- Twitch and YouTube live discovery, quotas and authorized Twitch downloads need your credentials and applicable broadcaster/editor access. No live account API tests were performed.
- No Google OAuth client or channel authorization was supplied. No real uploads or publication occurred. Private upload reliability is tested through mocked responses only.
- No authorized GTA VI gameplay was supplied. Humour precision/recall, factual game identification, reaction classification, contextual joke quality, real gameplay audio balance, facecam composition, transcription accuracy and repetition over multiple daily outputs remain unvalidated.
- No universal copyright/monetization clearance can be inferred from technical QC or YouTube processing success. Rights records are explicit operator assertions backed by evidence; CC labels are not auto-approved.
- Reaction detection uses transcript cues, energy spikes and model context, not a trained acoustic laughter/scream classifier. Cropping uses full-frame fit or an operator-specified focal point, not automatic face tracking.
- Twitch user-token validation/refresh needs a real authorized account test; revoked grants require reauthorization. Local imports continue to work without Twitch. Storage retention is manual. Long vision runs may exceed the scheduled task's four-hour limit: use shorter sources/lower per-run counts, benchmark another local model, or adjust the task limit after testing.
- Optional public publication is disabled and requires both completed testing and broader OAuth consent. YouTube processing may finish after the upload command returns; explicitly retry `publish ID` after completion.

## Before enabling daily uploads

Fill `.env` and Google desktop OAuth JSON locally, run `auth`, pin the channel ID, and import at least three genuinely authorized source recordings with complete rights evidence. Validate a representative labelled source pool and inspect private outputs. Enable the task only after those checks. Keep `auto_publish` false during testing.
