# Getaway crew appearance

Appearance-only update based on the approved burgundy/plum/yellow concept: a lightning shirt, paired yellow sleeve bands, plum cap with burgundy band and yellow trim, dark metal mask, plum trousers and burgundy/charcoal shoes. The face, hands and original body dimensions are retained.

The three existing PNG maps in `Assets/_Project/Textures/Thieves/ThiefEnemy01/` supply the new appearance to the shared visual and all seven production thief prefabs, across all four LODs. The existing material remains editable in the Inspector. Texture GUIDs, material settings, prefab customizations, weapon mounts and controllers are preserved. No runtime or automatic asset-building code was added.

The gameplay FBX is unchanged, including all vertex positions, UVs, weights, bones, bind poses and 25 clips. The editable root `thief_enemy_01.blend` has the updated packed material, with its geometry, rig, UVs, weights and animation curves verified before/after saving. `Source_OcclusionRoughnessMetallic.png` is a Blender/glTF channel-layout companion; Unity continues using its original metallic/AO/smoothness packing.

## Verification

- `UnityCrewValidation.txt`: actual saved prefabs imported into an isolated Unity 6000.3.11f1 project. Checks the shared material/maps and all 25 imported clips; exercises Idle, Walk, Shoot, Frustration and Death at 13 points each on the shared visual and seven enemy prefabs. All four LODs are baked per pose to check finite skinning and saved bounds. Existing weapon grip anchors, Speed transitions and Death priority are checked.
- `AssetPreservationValidation.json` and `PreservedAssetHashes.json`: byte-for-byte preservation of the gameplay FBX/importer, enemy prefabs, controllers, material and texture metadata.
- `SourcePreservationValidation.json`: matching source geometry/UV/weights/rig/animation fingerprints before and after material replacement and source reload.
- Front, back, quarter, LOD and action PNGs: renders of the existing Blender meshes and original animation poses with the staged crew maps. The 700-triangle LOD is intended for its existing small screen-size threshold.
- `Before/`: original maps and editable Blender master. `InstalledTextureHashes.json` records the delivered runtime maps.

These are isolated asset/playback checks and rendered previews; a full multiplayer encounter was not run.

## Explicit authoring tools

`Tools/Blender/inspect_thief_reskin.py` extracts the existing UV/rest-surface mapping. `Tools/author_thief_crew_texture.py` bakes the graphics into that atlas without editing geometry. `Tools/Blender/preview_thief_crew.py -- --full` renders the existing poses. `Tools/stage_thief_crew_validation.py` and `Tools/Unity/ValidateThiefCrew.cs` validate the saved production assets with candidate maps in the isolated project. `Tools/install_thief_crew.py` installs only validated maps and refuses to overwrite production edits made during authoring. `Tools/Blender/install_thief_crew_source.py` updates only the packed source material. These are one-shot offline tools, never import/reload/Play-mode builders.
