# Character02 Thief

Derived in Blender from Hodaart Character 02 T-Pose and its separate eye/eyebrow meshes. The original body vertices, rig proportions and skin weights are preserved. The dark clothing, fitted mask, beanie and backpack follow Character01Thief's design, fitted to Character02.

Unity assets are in `Assets/_Project/Models/Thieves/Character02Thief/`. The main FBX includes the unarmed body; `Armed/` contains matching Pistol, AssaultRifle and Shotgun model variants. Editable Blender sources remain here outside Unity Assets.

Every FBX includes Idle (48 frames), Walk (24), Shoot (24), Death (52) and Frustration (31) at 30 FPS. Each armed variant copies the corresponding Character01 animation actions and weapon, with the weapon socket adapted to Character02's hand. Generic rig import, clip names, timing, loop settings and weapon material remaps follow the Character01 armed import configuration. The unarmed body includes the Pistol animation set and uses the same generic import configuration. No controller is assigned by a model asset.

`FBXValidation.json` records Blender FBX reimport checks: all five clips and frame ranges, 43 bones, fully weighted meshes, and finite evaluated bounds sampled at the start, midpoint and end of every clip. `Validation.json` records source-body preservation. All Blender sources and FBXs default to the original Character02 T-pose, with no active action saved. The preview shows that default T-pose. Unity playback has not been tested.

The current GetawayPalette update matches Character01's burgundy outfit/beanie, plum trousers/backpack, charcoal mask/gloves/straps and gold/off-white accents. It changes palette pixels and UV assignments only; FBX geometry, weights, rig, clips and metadata are preserved. All four editable final sources are saved in T-pose with no active action.

Only `Assets/_Project/Prefabs/Enemies/Thief_Pistol.prefab` now uses this model. Its existing gameplay settings and weapon/ragdoll references are preserved, with a small Character02 pistol clip override using the existing state machine. The saved prefab defaults to the mesh bind T-pose. Unity validated all five animation states, finite skinning and actual walk deformation. Other enemy prefabs are unchanged. Checks, the T-pose preview and backups are in `GetawayPalette/`.

Explicit tools: `Tools/Blender/apply_character02_getaway_palette.py`, `Tools/stage_character02_pistol.py`, `Tools/Unity/UseCharacter02Pistol.cs` and `Tools/install_character02_pistol.py`. They do not run automatically. The earlier `create_character02_thief.py` documents initial model generation; preserve later source/Inspector edits before deliberately reauthoring.

