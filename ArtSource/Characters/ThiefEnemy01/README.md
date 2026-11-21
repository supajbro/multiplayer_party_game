# thief_enemy_01 — optimized LODs and animations

Use `Assets/_Project/Prefabs/Enemies/thief_enemy_01.prefab` for gameplay. Its Inspector-configurable LODGroup selects one skinned mesh at a time. The FBX at `Assets/_Project/Models/Thieves/thief_enemy_01.fbx` contains all four meshes and all 15 clips. The editable Blender master and saved prefab retain a T-pose.

| LOD | Triangles | Unity vertices (including seams) | Bone influences | Screen-height cutoff | Cast shadows |
| --- | ---: | ---: | ---: | ---: | --- |
| 0 | 10,000 | 7,878 | 4 | 35% | Yes |
| 1 | 4,000 | 3,975 | 4 | 16% | Yes |
| 2 | 1,500 | 1,966 | 2 | 7% | No |
| 3 | 700 | 1,184 | 2 | 2.5% | No |

The character is culled below 2.5% screen height. Distances depend on camera FOV and Unity LOD bias. Hard switches avoid rendering two skinned meshes during a crossfade. Settings remain editable in the prefab Inspector.

The 1.65 m character faces Unity +Z and uses one shared skeleton, Animator and URP Lit material, with mipmapped 1024px textures. The existing material/texture folders under `Assets/_Project/Materials/Thieves/ThiefEnemy01/` and `Assets/_Project/Textures/Thieves/ThiefEnemy01/` are retained. The 22-bone Generic rig has normalized weights. CPU mesh readability, offscreen skin updates and skinned motion vectors are disabled. Animator culling is Cull Completely. Saved skin bounds include sampled poses from every clip, with padding, avoiding continuous bounds updates.

## Animations and use

Character01Thief's base Blender file has no Actions. Its Pistol, AssaultRifle and Shotgun masters each have Idle, Walk, Shoot, Death and Frustration. All 15 were retargeted to this character and embedded in the FBX as `<variant>_Thief_<action>`, at the original 30 fps and durations. Idle/Walk loop; the remaining clips play once. Root motion is disabled and floor contact was adjusted for the target proportions.

Three controllers are saved under `Assets/_Project/Animations/Thieves/ThiefEnemy01/`, named `thief_enemy_01_Pistol.controller`, `thief_enemy_01_AssaultRifle.controller` and `thief_enemy_01_Shotgun.controller`. The prefab uses Pistol by default; select another in its Animator Inspector.

Controllers expose Speed, Shoot, Dead, Frustration and FrustrationSpeed. State names are compatible with the existing ThiefAnimationDriver. Speed above 0.08 enters Walk; Shoot/Frustration are triggers; Dead takes priority and holds Death. Frustration also supports the driver's CrossFade call. This is a visual prefab with empty hands. Weapons, gameplay/network components and spawner installation are separate configuration. Simplified fingers have no independent articulation; holding poses transfer body motion without adding finger grips.

The seven production character prefabs in Prefabs/Enemies now use this LOD visual: Thief, Thief_Pistol, Thief_AssaultRifle, Thief_Shotgun, Thief_RocketLauncher, GrapplerThief and SpeakerThief. Their original prefab GUIDs, root component IDs and gameplay settings are preserved, with animation, ragdoll and weapon references remapped to the new hierarchy. Weapons and specialist props are retained; ragdoll colliders are fitted to the new body. See PrefabUpgrade/ for original backups, validation and posed previews. RogueCar, Character01Thief source assets, scenes, runtime scripts and spawn configuration were preserved. Crowd frame times and a complete multiplayer session were not profiled or tested as part of this visual migration.

## Verification

- `FBXRoundtripValidation.json`: reimported the delivered FBX; checked all four budgets, UVs, unit scale, normalized weights, and closed topology. Every frame of all 15 actions was evaluated on every LOD for finite positions and reasonable bounds.
- `UnityLODAnimationValidation.txt`: Unity 6000.3.11f1 imported all meshes, URP material, textures, Generic avatar and clips, and verified budgets, weights, loop settings, references and saved optimization flags.
- `UnityAnimatorPlaybackValidation.txt`: all 15 states played through Animator on every LOD within saved bounds. Speed transitions and Death priority passed for all controllers.
- `LODAnimationPreviews/LODComparison.png`: equal-size LOD comparison; coarse versions are intended for small screen sizes.
- `LODAnimationPreviews/AnimationComparison.png`: representative transferred poses. Additional back/walk renders are in that folder.
- `InstalledAssetManifest.json`: hashes of installed model, importer, prefab and controller files.

## Sources and reproduction

`thief_enemy_01.blend` is the packed final master with four LODs and 15 Actions, saved in T-pose. `thief_enemy_01_20000_source.blend` and `thief_enemy_01_20000_source.glb` preserve the original asset. `OriginalGeometryValidation.json` preserves its report; GeometryValidation.json and LODAnimationValidation.json describe the final version. Original root-folder turntable images show the earlier 20,000-triangle source.

The original reference was created with built-in imagegen from the supplied burglar: rounded cartoon forms, front T-pose, tan skin, brown eyes, black domino mask, black/ivory striped beanie and shirt, charcoal trousers, oversized brown shoes, no props and a white background. The saved Pixal3DWorkflow.json records local ComfyUI generation with Pixal3D INT8, seed 42, 1024 generation/atlas and the original 20,000-triangle output. The raw GLB remains in Generated/. No paid API nodes were used.

Explicit offline workflow:

1. `Tools/Blender/optimize_thief_enemy_01.py`: decimate from preserved source, retain UVs and interpolated weights, reduce distant influences, retarget Actions and export FBX.
2. `Tools/Blender/preview_thief_enemy_01_lods.py` and `validate_thief_enemy_01_lods.py`: render and validate actual exported data.
3. `Tools/Unity/AuthorThiefEnemy01.cs`: explicitly author the prefab/controllers in an isolated Unity project; refuses to overwrite existing outputs.
4. `Tools/Unity/ValidateThiefEnemy01Playback.cs`: validate saved controllers, skinned poses and bounds.
5. `Tools/install_thief_enemy_01_lods.py`: install validated outputs while preserving the model GUID; refuses to overwrite the delivered prefab/controller folder.

No builder is installed in Unity Assets or runs on import, script reload or Play mode. Preserve later Inspector/source changes before deliberately reauthoring. Older prepare/finish scripts document the original generation stage and are not part of this final workflow.
