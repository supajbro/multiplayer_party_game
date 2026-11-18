# Player emotes

The saved `Player.prefab` is already wired. Hold **B**, point inside a wedge, and release to play. Release in the centre or outside the wheel to cancel. Movement, jump, attack, interact, reload, item selection, damage and incompatible gameplay states cancel playback. Dance, Shuffle and Celebration loop; Backflip, Finger Guns and Spin finish automatically.

The existing robot has no legs or individual finger bones. Finger Guns points both existing hands; the model, rig, meshes, proportions and materials are unchanged.

## Inspector configuration

- `PlayerEmoteController`: ordered wheel definitions, playback and saved wheel prefab, movement tolerance.
- Each emote `.asset`: unique nonzero byte ID, display name, preview label, clip, loop flag, blend time, existing face expression and six visual motion curves (metres/degrees, time in seconds).
- Each `.anim`: normal editable Unity clip targeting only the existing robot rig.
- `PlayerEmotePlayback`: existing Animator, visual root, body rotation centre and animated bone references.
- `Prefabs/UI/EmoteWheel.prefab`: editable canvas, segments, labels, font, colours and dead zone.
- `Input/PlayerControls.inputactions`: rebind the Player/Emote action (B by default).

To add an emote, create an Emote definition, assign an existing-rig clip and unused ID, and assign it to the player definitions. The current wheel intentionally has six slots; expanding it requires adjusting sector layout/selection together.

## Playback and networking

The existing Animator and its controller continue running. The emote samples saved clips after Animator, recoil and hover evaluation, blends the bones, and moves only the existing `Visual` child around its body centre. Early Update restores the underlying pose before normal gameplay/animation work. The CharacterController, NetworkTransform, player root and physics never receive emote motion. There is no runtime hierarchy builder.

FishNet owns a single atomic SyncVar containing ID (zero = stopped), sequence and server start tick. Owned ServerRPCs request start/stop; the server validates health, carrying, vehicles, movement locks, weapon actions and healing. Late observers sample the original tick phase. Repeated state notifications do not restart clips. Owner interruptions restore the pose immediately while the reliable stop request reaches the server. Server damage/attack hooks and displacement also stop persistent state.

## Editor tools and checks

- **Couch Guys > Player > Validate Emotes**: asset/rig bindings, all six clip samples, loop seams, pose restoration, radial selection, local UDP server/owner/late observer, authority rejection, damage/movement interruption and B-key wheel input. This explicitly enters and exits Play mode. Results: `Temp/PlayerEmoteValidation.txt`.
- **Couch Guys > Player > Render Emote Previews**: pose contact sheet and wheel PNG in `Temp`.
- **Couch Guys > Player > Create Missing Emote Assets**: explicit recovery tool. Existing definitions/clips/wheel assets are preserved; player bindings are updated. It never runs automatically on import, reload or Play mode. Test/authoring request files under `Temp` are explicit automation triggers, consumed once.

No model import, Animator-controller rebuild, external asset, or manual Editor setup is required. A final visual check in the actual gameplay camera and a separate-machine latency test remain useful alongside the included local UDP integration checks.

## Changed paths

- `Prefabs/Player.prefab`, `Prefabs/UI/EmoteWheel.prefab`, `Animations/Emotes/*`.
- `Scripts/Player/EmoteDefinition.cs`, `PlayerEmoteController.cs`, `PlayerEmotePlayback.cs`.
- `Scripts/UI/EmoteWheelView.cs`, `EmoteWheelSector.cs`.
- `Scripts/Input/PlayerInputReader.cs`, `Input/PlayerControls.inputactions`.
- Small integration hooks in `CouchGuyAnimationDriver.cs`, `RobotFaceExpressionController.cs` and `PlayerWeaponController.cs`, `.Melee.cs`, `.BaseballBat.cs`.
- `Scripts/Editor/PlayerEmoteAuthoring.cs`, `PlayerEmotePreview.cs`, `Tests/Editor/PlayerEmoteTests.cs`, with Unity metadata.
