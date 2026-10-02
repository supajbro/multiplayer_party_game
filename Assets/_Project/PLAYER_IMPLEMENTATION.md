# Initial Third-Person Player Implementation

## Overview

The `Player` prefab is a self-contained third-person prototype built on Unity's `CharacterController` and New Input System. `PlayerInputReader` owns device bindings, `ThirdPersonPlayerController` consumes movement intent, and `ThirdPersonCameraController` independently orbits a target near the player's upper torso. The separation keeps device input and movement authority replaceable when networking is introduced.

## Files Created

- `Assets/_Project/Prefabs/Player.prefab` — configured player, temporary capsule, camera target, and camera rig.
- `Assets/_Project/Input/PlayerControls.inputactions` — focused keyboard and mouse actions for the player foundation.
- `Assets/_Project/Scripts/Input/PlayerInputReader.cs` — enables the Player action map and exposes input intent.
- `Assets/_Project/Scripts/Player/ThirdPersonPlayerController.cs` — camera-relative locomotion, rotation, jumping, gravity, and grounding.
- `Assets/_Project/Scripts/Camera/ThirdPersonCameraController.cs` — smoothed orbit, shoulder framing, angle clamps, and camera collision.
- `Assets/_Project/Scripts/Editor/PlayerPrefabBuilder.cs` — repeatable editor builder and structural verification for the prefab.
- `Assets/_Project/README.md` — custom asset organisation and Australian English/C# naming conventions.
- `Assets/_Project/PLAYER_IMPLEMENTATION.md` — this implementation and verification reference.

Unity generates matching `.meta` files for imported folders and assets.

If the prefab is ever deleted, the editor builder recreates it after the next script/domain load. It can also be run manually from **Couch Guys > Build Player Prefab**.

## Player Prefab

Hierarchy:

```text
Player
├── Visual
│   └── Capsule
├── CameraTarget
└── CameraRig
    └── PlayerCamera
```

Important components:

- `Player`: `CharacterController`, `PlayerInputReader`, `ThirdPersonPlayerController`, `NetworkObject`, `NetworkTransform`, and `NetworkPlayerOwnership`. Root scale is `(1, 1, 1)`.
- `Visual/Capsule`: temporary mesh only. Local position `(0, 0.9, 0)`, local scale `(0.6, 0.9, 0.6)`, giving an approximately 0.6 m wide by 1.8 m tall visual. Its primitive collider is removed so it cannot conflict with the character controller.
- `CameraTarget`: transform at local position `(0, 1.5, 0)` near the upper torso/head.
- `CameraRig`: `ThirdPersonCameraController`.
- `CameraRig/PlayerCamera`: `Camera` and `AudioListener`, tagged `MainCamera`, with 65-degree field of view.

`CharacterController` configuration:

- Height: `1.8`
- Radius: `0.3`
- Centre: `(0, 0.9, 0)`
- Slope Limit: `50`
- Step Offset: `0.3`
- Skin Width: `0.08`
- Minimum Move Distance: `0`

## Player Controller

- Movement reads a normalised `Vector2`, accelerates/decelerates using `Speed Change Rate`, and moves through `CharacterController.Move`.
- The camera's forward and right vectors are projected onto the horizontal plane. WASD therefore remains relative to camera yaw without camera pitch adding vertical movement.
- The root smoothly turns only towards a non-zero movement direction. Orbiting the camera alone never rotates the player.
- Left Shift switches from walking to running; analogue input magnitude is retained for future stick input.
- Jump velocity is calculated from the configured jump height and gravity.
- Gravity accumulates every frame and is limited by terminal velocity. A small downward grounded force keeps the controller settled on ordinary slopes.
- Ground detection uses `CharacterController.isGrounded`, which is updated by collision during controller movement.

## Camera

The camera rig follows `CameraTarget` in world space. Mouse delta changes independent yaw and pitch values; horizontal yaw is unrestricted, while pitch is clamped. The camera uses a rotated offset of `(shoulder offset, 0, -distance)`, so its facing direction remains the orbit direction and the player appears slightly off-centre instead of being aimed at dead-centre.

Position and rotation use frame-rate-independent exponential smoothing. A sphere cast shortens the boom when geometry lies between the pivot and desired camera position, ignoring the player's own hierarchy. Movement uses the final camera transform's horizontal forward/right directions.

## Input

The `Player` action map contains:

| Action | Type | Default binding |
| --- | --- | --- |
| Move | Value / Vector2 | WASD 2D Vector composite |
| Look | Pass Through / Vector2 | Mouse delta |
| Jump | Button | Space |
| Sprint | Button | Left Shift |

Actions are isolated behind `PlayerInputReader`; gamepad bindings can be added to the action asset without changing the movement or camera classes. Every spawned Player clones its action asset at runtime, so disabling a remote reader cannot disable the local owner's action map. `NetworkPlayerOwnership` disables the reader, movement controller, camera controller, camera, and AudioListener on every remote player.

## Inspector Configuration

Player defaults:

| Setting | Value |
| --- | ---: |
| Walk Speed | 3.5 m/s |
| Run Speed | 6 m/s |
| Speed Change Rate | 12 m/s² |
| Rotation Smooth Time | 0.1 s |
| Jump Height | 1.2 m |
| Gravity | -25 m/s² |
| Grounded Force | 2 m/s² |
| Terminal Velocity | 50 m/s |

Camera defaults:

| Setting | Value |
| --- | ---: |
| Camera Distance | 4.5 m |
| Camera Target Height | 1.5 m |
| Pivot Height | 0.15 m |
| Shoulder Offset | 0.65 m |
| Mouse Sensitivity | 0.12 |
| Minimum Vertical Angle | -35° |
| Maximum Vertical Angle | 65° |
| Initial Vertical Angle | 12° |
| Follow Smoothing | 16 |
| Rotation Smoothing | 20 |
| Collision Radius | 0.2 m |
| Collision Padding | 0.1 m |
| Camera Field Of View | 65° |

## Scene Setup

1. Open or create a scene.
2. Add your own ground with a non-trigger 3D collider. No floor asset is supplied by this implementation.
3. Do not place the Player prefab in the scene. The persistent multiplayer bootstrap spawns one owned Player for each connection.
4. Remove or disable the scene's existing gameplay camera and AudioListener. The locally owned Player supplies both.
5. Enter Play Mode and use the Steam multiplayer development panel. Click the Game view if needed; the cursor locks after the local Player spawns. Press Escape in the editor to release it.

## Verification Checklist

- [x] Player prefab exists.
- [x] Player root scale is `(1, 1, 1)`.
- [x] Capsule is approximately human-sized at 1.8 m tall and 0.6 m wide.
- [x] CharacterController dimensions match the player.
- [ ] WASD movement works in Play Mode on user-supplied ground.
- [ ] Movement is camera-relative in Play Mode.
- [ ] Player rotates towards movement in Play Mode.
- [ ] Jump works in Play Mode.
- [ ] Gravity works in Play Mode.
- [ ] Player correctly detects user-supplied ground in Play Mode.
- [ ] Mouse rotates the camera in Play Mode.
- [ ] Camera can orbit completely around the player horizontally in Play Mode.
- [x] Vertical camera rotation is clamped in code and prefab configuration.
- [ ] Camera maintains the intended over-the-shoulder framing in Play Mode.
- [x] Rotating the camera does not invoke player rotation.
- [ ] Camera-relative movement still works after rotating the camera in Play Mode.
- [x] No legacy Unity input APIs are used.
- [x] No coroutines or `IEnumerator` implementations are used.
- [x] No floor asset was created.
- [x] All custom files are contained inside `Assets/_Project/`.
- [x] Prefab object references pass the editor builder's structural verification.
- [x] Remote player input, cameras, and AudioListeners are disabled by ownership.
- [x] Player position and rotation are configured for owner-authoritative synchronisation.

Items requiring physical input and collision with the user's future ground remain unchecked until they are exercised in Play Mode.

## Known Limitations / Future Work

- Gamepad bindings are not yet present, although the input boundary supports adding them without controller rewrites.
- Animation and an Animator are not included.
- The capsule is a temporary visual; no final character model is included.
- Couch interaction and couch physics are not included.
- Camera obstruction is handled with a simple sphere cast; advanced occlusion/fading is deferred.
- Host migration is not included; the session ends when the host leaves.

Steam-specific verification and limitations are documented in `STEAM_MULTIPLAYER.md`.
