# Expressive thief pose revision

The earlier Character01 weapon retarget placed both wrists too close together for this character's large hands. The base thief and speaker also inherited the pistol idle. This revision changes the saved animations and controller assignments, preserving the existing meshes, 22-bone rig, T-pose, material, LOD budgets and gameplay-root settings.

- Unarmed: asymmetric relaxed arms, independent walk swings, head glance/nod and shoulder motion.
- Pistol/grappler: dominant hand holds the weapon; free hand rests or swings separately.
- Assault rifle/shotgun/rocket: separate trigger and supporting-hand positions, with the support palm turned upward and elbow kept open.
- Speaker: right hand raised beneath the speaker, free hand relaxed/swinging. The retained speaker is aligned to the carrying shoulder's actual imported side and clears the head.
- Frustration: free-hand gesture returns to each variant's own ready pose.

All 15 original clip names and timings remain available. Ten additional Unarmed/Speaker clips bring the total to 25. Original death motion is retained. Five saved controllers remain configurable in the Animator Inspector. The shared visual and Thief use Unarmed, SpeakerThief uses Speaker, and armed enemies use their existing weapon sets.

`ArmClearanceValidation.json` measures the heavily hand-weighted surface vertices at every authored frame, excluding the preserved death motion. Minimum clearance is greater than 3.5 cm in model space. `FBXRoundtripValidation.json` checks the actual exported 25-clip FBX, its T-pose, 22 bones, four budgets (10,000 / 4,000 / 1,500 / 700), UVs, normalized weights and sampled animated geometry. `UnityPoseValidation.txt` checks all five states on the shared visual and seven production prefabs, their avatar, separate wrist positions and updated bounds on every LOD.

`EnemyActingComparison.png` shows posed meshes captured from the actual upgraded Unity prefabs and rendered in Blender. `ActingComparison.png` compares idle/walk poses without props. Individual pose and prefab PNGs are also provided. These are simplified-lighting previews; a full multiplayer encounter was not run.

`Before/` preserves the previous master and affected asset files. `BeforeHashes.json`, `InstalledPoseHashes.json` and `PrefabIdentityPreservation.json` record installation and retained root identities. Root gameplay MonoBehaviour serialization is preserved; prefab GUIDs, model GUID, original animation identifiers and attachment meshes are retained. Only animation data/import definitions, saved controllers, controller assignments, skin bounds and the speaker's fitted position are updated.

Explicit offline tools: `Tools/Blender/expressive_thief_enemy_01.py`, `preview_expressive_thief.py`, `validate_expressive_thief.py`, `Tools/Unity/AuthorExpressiveThiefPoses.cs` and `Tools/install_expressive_thief_poses.py`. The installer refuses to replace assets changed during authoring. No automatic authoring hooks or runtime pose-building code are installed.
