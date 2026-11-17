# Capture & Promote

Game view screenshots, continuous gameplay recording and frame-aligned gameplay bookmarks in the Unity Editor.

## Setup and shortcuts

Open **Tools > Capture & Promote** and enter Play Mode. Keep the Game view rendering. Capture works with the plugin window closed.

- **F12** saves a PNG at the current rendered resolution. The multiplier scales both dimensions.
- **F9** starts/stops recording. Defaults are 1920×1080, 60 FPS, High quality and gameplay audio enabled.
- The window provides the same controls, settings, recording indicator, elapsed wall-clock time and folder buttons.
- Rebind commands in **Edit > Shortcuts > Capture & Promote**. The focused Game view fallback supports single-chord function-key bindings. Unity's normal shortcut dispatcher handles Editor bindings outside Game view.

This project uses Unity **6000.3.11f1**, Recorder **5.1.8** and Input System **1.19**. To reuse the plugin, copy `Assets/CaptureAndPromote` and install `com.unity.recorder` version 5.1 using **Window > Package Management > Package Manager > Install package by name**. Missing/unsupported Recorder versions display setup guidance while screenshots remain available.

## Output and settings

Default output folders are `Captures/Screenshots/` and `Captures/Videos/`, relative to the project root. Filenames contain a timestamp and random suffix. Absolute paths are also supported. Choose folders outside `Assets`. This repository ignores `Captures/`. Add an ignore rule for custom in-project folders or when installing in another repository.

Video uses Recorder's Game View input and Unity Media Encoder. Its platform default is H.264 MP4 on Windows/macOS and WebM on Linux. Video dimensions must be even, between 16×16 and 4096×2160, with FPS between 1 and 120. Platform encoder limits still apply. Recorder forces Game view to the requested resolution. Restore the previous resolution using the Game view toolbar afterwards if needed.

Audio capture requires Mono or Stereo speaker mode. Other modes generate a warning and video-only recording. Listener state, Editor audio muting and platform support affect captured audio.

## Lifecycle and limitations

Stopping, exiting Play Mode, assembly reload and normal Editor shutdown request Recorder finalisation. Repeated starts share one plugin session. Existing active Recorder sessions prevent plugin startup. Failed/interrupted output paths appear in status and media files are retained. Crashes, forced termination or storage failures can leave incomplete containers.

Keep Game view rendering. Pausing/hiding it can delay capture. Supersampling and encoding increase memory/GPU load. Constant-frame-rate recording can slow simulation, particularly in networked sessions. The timer measures real elapsed time.

## Architecture

The generic Editor assembly owns settings, orchestration, shortcuts and the window. Separate optional Editor assemblies integrate Recorder and Input System. Legacy-only projects have a legacy input fallback. User settings persist in `UserSettings/CaptureAndPromote.asset`.

The project-specific `CaptureAndPromoteIntegration.cs` gives the plugin Editor F12 ownership and suppresses overlapping Couch Guys captures. The original runtime handler retains its standalone capture and writable-directory fallback. Copy the generic plugin folder when reusing it in another game.

## Validation

Tested on Windows in Unity 6000.3.11f1.

- Project compilation passed. An isolated project compiled and exited successfully with Recorder and Input System absent.
- Unity Input System keyboard-state injection tested F12/F9 with focused Game view, held-key duplicate suppression and repeated recording starts.
- Verified PNGs at native and 2× resolution, including 1920×1080 and 3840×2160. The original Couch Guys capture coroutine also produced a valid PNG.
- FFmpeg decoded 1080p/60 FPS and 720p/30 FPS MP4s with non-silent stereo AAC audio. Play Mode exit and assembly reload finalised playable videos.

Native OS key delivery and visual Editor-window QA remain unverified because desktop capture/input geometry timed out. Accessibility confirmed the window opened. Standalone execution, other operating systems and legacy-input hotkeys remain manual checks.

Play Mode reload exposed an existing argument-null error in `RobotFaceExpressionController.ApplyShaderProperties` at line 178. Capture finalisation completed first. Existing third-party/authoring deprecation warnings remain.

See the [official Unity Recorder documentation](https://docs.unity3d.com/Packages/com.unity.recorder@5.1/manual/index.html).

## Smart gameplay capture

**F10** bookmarks the current recording frame and briefly confirms it in the Capture tab. The Capture tab also offers a bookmark button and a live highlight timeline. F10 is inactive when recording is stopped. F9 and F12 retain their existing controls.

Open **Capture Library** to browse the configured output folders. Filter screenshots, videos or videos containing highlights. Select a recording to view its timeline, select markers, edit labels, priority, category and description, delete markers, or add a marker at a time entered in seconds. Use **Save Changes** to commit marker edits. **Refresh** rescans files and metadata. Open/reveal buttons launch the default media app or Explorer/Finder.

Library scanning is incremental. Rows and marker lists are virtualised, and up to 24 small screenshot thumbnails are cached. One thumbnail is generated per Editor update. PNGs over 32 MB or 16 megapixels retain a file icon. Videos are listed from metadata without decoding their full contents. Earlier Phase 1 videos remain browsable with unknown duration/resolution until metadata exists.

### Reporting events from another game

Reference `CaptureAndPromote.Runtime` from your gameplay assembly definition and call the API on Unity's main thread.

```csharp
using CaptureAndPromote;

CaptureHighlight.Mark("Explosion", HighlightPriority.High);
CaptureHighlight.Mark("Delivery complete", HighlightPriority.Medium,
    category: "Delivery", description: "Delivered the final couch");
```

The API safely ignores events outside an active recording and in standalone players. It creates no scene objects. **Accept gameplay events** enables/disables automatic markers. The same label/category is deduplicated for one second of video time by default. Set the cooldown to zero to retain every event. The automatic marker limit defaults to 10,000 per recording. Manual bookmarks bypass the automatic cooldown and limit.

Couch Guys hooks report rocket and vehicle explosion notifications, successful delivery commits, knockdown transitions and couch theft notifications. Toggle these through **Couch Guys > Capture > Enable Automatic Highlights**. Their adapter lives under `_Project` and uses optional Editor bindings. Delivery and theft notifications currently run on the host/server where those events already exist. Client-only recordings receive the existing replicated explosion/knockdown notifications. Additional client delivery/theft reporting would require a separate gameplay notification hook.

### Metadata format and timing

Each new video has a companion `<video filename>.capture.json` with `schemaVersion` 1, a unique `recordingId`, the media filename, UTC start/end dates, `state`, dimensions, `frameRate`, `frameCount`, `durationSeconds` and a `highlights` array. Each marker stores its ID, `timestampSeconds`, zero-based video `frame`, label, category, priority name (`Low`, `Medium`, `High`), description and source (`manual` or `event`).

Timestamps use Recorder's frame callbacks and constant frame rate. A bookmark points to the current/next encoded frame, independent of wall-clock startup delays and simulation slowdown. Duration equals `frameCount / frameRate`. A metadata-only custom Recorder shares the movie session's scheduling through Unity's supported Recorder API. The window's main recording timer continues to show wall-clock elapsed time.

Metadata is checkpointed at most once every two seconds while recording and saved when recording stops, exits Play Mode or reloads. Atomic replacement retains a `.bak` snapshot. An unfinished sidecar is shown as `interrupted` when loaded after restart, preserving its last saved duration and markers. A crash can lose the most recent two seconds of metadata, and an unfinished video container may still need recovery. Unsupported future schema versions are preserved and reported as unavailable.

### Phase 2 validation

Verified in Unity 6000.3.11f1 with Recorder 5.1.8 on Windows.

- Injected F9/F10/F12 through Unity Input System. Held F10 produced one marker, a second press added another, and F12 produced one screenshot during recording.
- Tested multiple runtime events, same-event cooldown suppression, the optional Couch Guys adapter and inactive API behaviour. Labels, priorities, descriptions, manually entered times and marker deletion survived JSON round trips.
- Refreshed the library, loaded a saved recording's marker data and generated a screenshot thumbnail. Visual desktop QA and external application launch buttons remain manual checks because desktop capture/input geometry timed out.
- Decoded recordings stopped normally, on Play Mode exit and on assembly reload. Their 568, 1,732 and 938 encoded frames exactly matched JSON frame counts. The initial timing test also matched all 2,536 encoded frames.
- Tested interrupted metadata loading and recovery from an invalid primary JSON using its atomic backup. A separate minimal Unity project compiled successfully with Recorder and Input System absent.

Physical key delivery, all five gameplay incidents in a live multiplayer match, full Editor restart and large-library stress testing remain unverified. The adapter was exercised directly. Delivery/theft visibility follows the host/server limitation described above. Play Mode reload again exposed the existing `RobotFaceExpressionController` error documented in Phase 1, after capture metadata and media had finalised.
