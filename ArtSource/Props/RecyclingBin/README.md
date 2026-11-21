# Couch Guys recycling bin

Standalone prop matching `depot_02`'s grey metal, teal trim, warm yellow bumper and broad softened industrial edges. Existing depot materials are shared read-only; new green emission materials are independent.

- Place `Assets/_Project/Prefabs/Props/RecyclingBin.prefab` directly in Unity.
- Source: `ArtSource/Props/RecyclingBin/recycling_bin.blend` (editable component meshes, UVs, materials, hidden collision collection).
- Model: `Assets/_Project/Models/Props/RecyclingBin/recycling_bin.fbx` (three consolidated LOD meshes, nine material slots each).
- Collision: `Assets/_Project/Models/Props/RecyclingBin/recycling_bin_collision.fbx` (eight convex wall prisms plus floor). The saved prefab already has convex MeshColliders configured, with no collision surface spanning the mouth.
- Independent green materials: `Assets/_Project/Models/Props/RecyclingBin/Materials/RB_GreenEmission.mat` and `RB_InteriorGlow.mat`. Edit HDR emission in the Inspector. Bloom depends on the scene's existing URP post-processing settings.

Metre scale; approximately 3.24 × 3.25 m footprint, 1.63 m height, 2.48 m clear opening across flats. Bottom-centre origin. Front is Unity +Z. Recessed collision floor at 0.25 m. Visual floor is slightly above it; rendered rim and liner have decorative bevels while collision stays simple and convex.

LOD triangles: 19,548 / 8,796 / 3,518; collision: 124. LOD screen-height thresholds: 0.45 / 0.20 / 0.035. Source components remain separate for editing; FBX render geometry is combined by LOD to reduce renderers. Smart-projected UV0 uses no external textures. LOD0 has closed topology after accounting for FBX shading seam splits; reduced LODs are intended for rendering, with separate watertight collision geometry used at every distance.

Verification: Blender FBX roundtrip, UVs, scale, origin, material slots, nine watertight collision parts, Unity mouth/wall raycasts, a weapon-sized rigidbody drop, and saved prefab reload. See `validation.json` and `unity_validation.txt`. Previews show the source, open interior, and lowest LOD. No gameplay scripts, scene placements, or modifications to depot assets.

Authoring is explicit: `Tools/Blender/create_recycling_bin.py`. It is not registered as a Unity callback and will only overwrite this bin when manually run. `Tools/Unity/AuthorRecyclingBin.cs` is an isolated Editor authoring/validation utility outside `Assets`; it is not installed as runtime code. The prefab remains editable without rerunning either tool.
