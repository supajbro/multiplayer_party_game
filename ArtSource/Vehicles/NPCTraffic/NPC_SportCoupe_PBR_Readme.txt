NPC_SportCoupe - CityHatch/FamilySUV-style PBR and LOD update

The purple coupe retains its 4.3m source length, white racing stripes, hood
scoop, low cabin, wide wheels and oversized rear wing. Stripes, scoop, lights,
wing, splitters and exhausts are modelled parts. Generated albedo/normal maps
are replaced by nine texture-free Blender surface materials. Paint uses the
same metallic/roughness treatment as CityHatch and FamilySUV. Unity uses
standard URP Lit materials with editable colour, metallic and smoothness.
Glass is opaque/tinted because no cabin interior is modelled.

Source: NPC_SportCoupe.blend
Runtime meshes/materials: Assets/_Project/Models/Vehicles/NPCTraffic/
Prefab: Assets/_Project/Prefabs/Vehicles/NPCTraffic/NPC_SportCoupe.prefab

Three saved LODs use the same screen-height thresholds as CityHatch/FamilySUV:
0.22, 0.09, 0.018. Hard switches avoid drawing two levels during a crossfade.
The wing silhouette and cabin/glass boundaries are protected in the lowest
LOD. See NPC_SportCoupe_UnityValidation.txt for actual imported triangle
counts, aligned bounds, material/shader checks and per-slot damage/reset tests.
The existing prefab scale, ground pivot, GUIDs and object identities remain.
Traffic physics, configuration and runtime code are not modified.

Blender and Unity PNGs show the saved surfaces and all three LODs. These are
isolated asset/render checks, not a gameplay or multiplayer playtest.
SportCoupe_BeforeCleanup/ archives the original source and existing assets.

Explicit offline tools only; no import/reload/Play mode builders:
Tools/Blender/rebuild_sport_coupe_surfaces.py
Tools/stage_sport_coupe.py
Tools/Unity/AuthorSportCoupeLODs.cs
Tools/install_sport_coupe_lods.py
Only pure modelling helpers are reused from CityHatch; its authoring/export
code is not executed. Preserve later source/Inspector edits before rerunning.
