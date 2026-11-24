NPC_UtilityPickup - matching traffic vehicle PBR and LOD update

The red utility pickup keeps its yellow side stripe, genuinely open bed,
bed liner and wheel wells, roll bar with three spotlights, chrome bumpers,
large wheels and running boards. Uneven generated surfaces are replaced
with editable mesh parts and nine texture-free Blender surface materials.
Paint, glass, rubber, trim, alloy, lights and stripe use the same PBR treatment
as the updated CityHatch, FamilySUV and SportCoupe. Unity uses URP Lit.
Glass remains opaque/tinted because no cab interior is modelled.

Source: NPC_UtilityPickup.blend
Runtime meshes/materials: Assets/_Project/Models/Vehicles/NPCTraffic/
Saved prefab: Assets/_Project/Prefabs/Vehicles/NPCTraffic/NPC_UtilityPickup.prefab

Three saved LODs use the existing traffic-vehicle screen-height thresholds:
0.22, 0.09, 0.018. The distant LOD uses lower-resolution wheels/lights and
simpler component bevels; it does not collapse the assembled bed/cab mesh.
The open bed, cab glass, roll bar and three lights remain intact at every LOD.
NPC_UtilityPickup_UnityValidation.txt records imported triangle counts,
aligned bounds, shader/material references and per-material damage/reset
tint checks for every LOD. The original prefab scale, ground pivot, object
identities and GUIDs are preserved. Traffic/physics settings are unchanged.

Original source and existing assets: UtilityPickup_BeforeCleanup/
Blender/Unity preview PNGs are alongside this file. These are isolated asset
and render checks; a full gameplay or multiplayer session was not run.

Explicit offline authoring only; no import/reload/Play mode builders:
Tools/Blender/rebuild_utility_pickup_surfaces.py
Tools/stage_utility_pickup.py
Tools/Unity/AuthorUtilityPickupLODs.cs
Tools/install_utility_pickup_lods.py
Only pure CityHatch modelling helpers are reused; its build/export code is
not executed. Preserve later source/Inspector edits before reauthoring.
