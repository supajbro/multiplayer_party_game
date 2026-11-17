# Capture & Promote

Phase 1 provides Game view screenshots and continuous gameplay recording in the Unity Editor.

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
