# Hotwire — shotgun thief

Hotwire is the exposed-face mechanic from `ApprovedConcept.png`: sideways burgundy cap, dark hair, brown eyes, asymmetric grin, cheek grease, mustard shirt, plum/burgundy overalls, yellow-backed gloves and reinforced shoes. The reference was isolated with built-in imagegen (`HotwireReference.png`), then converted locally with the saved Pixal3D INT8 workflow. No paid generation nodes were used. The back graphics and eye details were finished in the UV atlas.

Use the existing `Assets/_Project/Prefabs/Enemies/Thief_Shotgun.prefab`. It retains its original GUID, root/component IDs, gameplay settings, network components, shotgun weapon/muzzle/grip, ragdoll, Animator avatar and Shotgun controller. Only this prefab's body mesh/material overrides and sampled skin bounds change. Other thief variants and the original character's model, shared textures and 25 animation clips are preserved.

`Assets/_Project/Models/Thieves/Hotwire/Hotwire.fbx` contains four skinned meshes (10,000 / 4,000 / 1,500 / 700 triangles) and the original 22-bone rest skeleton. There are no new animation clips or additional bones. Skin weights are transferred from the original body, with uniform arm blends to support tightly bent shotgun poses. The new surface is fitted to the original 1.65m model height, shoulder plane and hand span. The existing scene skeleton drives the new meshes using bone-name remapping and their imported bind poses. The original 25-clip FBX and its controller remain the animation source.

The standalone editable `Hotwire.blend` retains four LOD meshes, packed finished material and the existing 25 preview Actions, saved in T-pose. Unity uses a separate Inspector-editable URP material and 1024px mipmapped maps under `Assets/_Project/Materials/Thieves/Hotwire/` and `Assets/_Project/Textures/Thieves/Hotwire/`. All assets and prefab overrides are saved; no runtime hierarchy reconstruction or import/reload/Play-mode builders are installed.

## Verification and previews

- `UnityHotwireValidation.txt` evaluates the saved shotgun prefab through its original Animator: Idle (47 poses), Walk (24), Shoot (24), Frustration (30), Death (51), on all four Hotwire LODs. It checks finite skinning, pose-aware bounds, valid bone references, original avatar/controller, grip anchoring, muzzle alignment, walking transitions and Death priority.
- `Unity_*.png` are Blender renders of actual posed body and shotgun surfaces captured from the saved Unity prefab. These include all five states, T-pose and the three distant LODs. Front/back/quarter source previews show the editable model.
- `ModelBuildValidation.json` records the rig, source fit, four budgets and normalized influence limits.
- `Before/Thief_Shotgun.prefab`, `BeforeHashes.json`, `InstallationValidation.json` and `InstalledAssetHashes.json` record the original prefab, preserved existing assets and installed outputs. Existing gameplay serialization is explicitly restored after Unity saves the new visual overrides, then validated.

This verifies saved assets and animation playback in isolated Unity 6000.3.11f1. It does not constitute a complete multiplayer encounter or performance profile.

## Explicit offline workflow

`Tools/run_hotwire_pixal.py` submits the saved input/workflow to the existing local conversion server. `Tools/Blender/build_hotwire.py` fits, skins and exports the independent model; `Tools/import_hotwire_assets.py` extracts its maps and creates separate Unity assets. `Tools/Blender/inspect_hotwire_surface.py` and `Tools/finish_hotwire_texture.py` finish the eyes/back; `Tools/Blender/pack_hotwire_material.py` packs the final atlas into the master. `Tools/stage_hotwire.py` stages current dependencies in the private Unity project. `Tools/Unity/AuthorHotwireShotgun.cs` explicitly authors the prefab overrides and validates saved playback. `Tools/Blender/preview_hotwire.py -- --unity` renders captured poses. `Tools/install_hotwire_shotgun.py` installs only the validated shotgun prefab and refuses to overwrite changes made during authoring.
