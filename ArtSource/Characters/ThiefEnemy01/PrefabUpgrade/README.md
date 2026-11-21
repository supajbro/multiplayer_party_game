# Production thief prefab upgrade

Updated the seven character prefabs under `Assets/_Project/Prefabs/Enemies/`: Thief, Thief_Pistol, Thief_AssaultRifle, Thief_Shotgun, Thief_RocketLauncher, GrapplerThief and SpeakerThief. RogueCar and the shared thief_enemy_01 visual prefab were left intact.

Each uses the saved thief_enemy_01 visual prefab with its four body LODs and retargeted controller. Pistol/base/grappler/speaker use the Pistol animation set, AssaultRifle/rocket use AssaultRifle, and Shotgun uses Shotgun. Original visual height is retained independently of customized gameplay collider sizes. All prefab GUIDs and gameplay-root component IDs are preserved. Root serialized gameplay fields retain their original values, changing only visual, muzzle, renderer and ragdoll references.

Existing firearms are retained as saved rigid attachment meshes under `Assets/_Project/Models/Thieves/ThiefEnemy01Attachments/`; they use the original weapon materials. Ungrouped held renderers are included in every body LOD so they cull with the character. Existing rocket launcher and grapple gun hierarchies are preserved, including their references, effects and prop LODs. The speaker keeps its three prop LODs and shoulder attachment, moved outward to clear the larger head.

Ragdolls have 11 kinematic bodies, 11 fitted disabled sphere colliders and 10 connected joints. Body masses, damping, collision modes and joint limits are retained. The new colliders match the new proportions; health and animation driver references are remapped. No runtime hierarchy-building code or automatic authoring hooks were added.

`SavedPrefabValidation.txt` records Unity 6000.3.11f1 checks on all saved prefabs: no missing scripts, correct body LOD budgets and skeletons, valid attachment/weapon references, fitted ragdolls, original visual height, and all five Animator states playing. `GameplaySettingsPreservation.json` records the restricted root-reference changes. `OriginalPrefabs/` and OriginalPrefabHashes.json preserve the pre-migration assets. `InstalledPrefabHashes.json` records the installed outputs.

`EnemyPrefabComparison.png` and individual PNGs show the actual posed meshes captured from the upgraded Unity hierarchies and rendered in Blender with simplified material lighting. No full multiplayer encounter or crowd frame-time benchmark was run in this task.

Explicit offline tools: `Tools/stage_thief_prefab_upgrade.py`, `Tools/Unity/UpgradeThiefEnemyPrefabs.cs`, `Tools/preserve_thief_prefab_root_settings.py`, `Tools/Unity/ValidateUpgradedThiefPrefabs.cs`, `Tools/Blender/preview_thief_prefab_upgrade.py` and `Tools/install_upgraded_thief_prefabs.py`. The installer refuses to overwrite prefabs that changed during authoring. Later Inspector customization remains supported; deliberate reauthoring should preserve those changes.
