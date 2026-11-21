# Character02 Thief

Derived in Blender from Hodaart Character 02 T-Pose and its separate eye/eyebrow meshes. The original body vertices, rig proportions and skin weights are preserved. The dark clothing, fitted mask, beanie and backpack follow Character01Thief's design, fitted to Character02.

Unity assets are in `Assets/_Project/Models/Thieves/Character02Thief/`. The main FBX includes the unarmed body; `Armed/` contains matching Pistol, AssaultRifle and Shotgun model variants. Editable Blender sources remain here outside Unity Assets.

Every FBX includes Idle (48 frames), Walk (24), Shoot (24), Death (52) and Frustration (31) at 30 FPS. Each armed variant copies the corresponding Character01 animation actions and weapon, with the weapon socket adapted to Character02's hand. Generic rig import, clip names, timing, loop settings and weapon material remaps follow the Character01 armed import configuration. The unarmed body includes the Pistol animation set and uses the same generic import configuration. No controller is assigned by a model asset.

`FBXValidation.json` records Blender FBX reimport checks: all five clips and frame ranges, 43 bones, fully weighted meshes, and finite evaluated bounds sampled at the start, midpoint and end of every clip. `Validation.json` records source-body preservation. All Blender sources and FBXs default to the original Character02 T-pose, with no active action saved. The preview shows that default T-pose. Unity playback has not been tested.

No prefabs, controllers, scenes, gameplay scripts or original third-party assets were changed. The explicit authoring script is `Tools/Blender/create_character02_thief.py`; it does not run automatically.

