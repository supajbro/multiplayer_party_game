NPC_FamilySUV - CityHatch-style surface and LOD update

The yellow SUV keeps its taller/longer proportions, roof rack, bull bar,
running boards, larger wheels and rear receiver. Uneven generated surfaces
are replaced with clean editable mesh parts and flat window surfaces.
Nine texture-free Blender materials cover paint, glass, rubber, trim, alloy,
headlights, tail lights, indicators and the number plate accent. Paint uses
the same metallic/roughness treatment as CityHatch; Unity uses standard URP
Lit materials. Glass is opaque/tinted because no cabin interior is modelled.

Saved source: NPC_FamilySUV.blend
Runtime meshes/materials: Assets/_Project/Models/Vehicles/NPCTraffic/
Saved prefab: Assets/_Project/Prefabs/Vehicles/NPCTraffic/NPC_FamilySUV.prefab

LOD0: 11,862 imported triangles, screen-height threshold 0.22
LOD1:  5,934 imported triangles, screen-height threshold 0.09
LOD2:  2,162 imported triangles, screen-height threshold 0.018
Hard switches match CityHatch; settings remain editable in the LODGroup.
The lowest LOD retains the roof-rack silhouette. Source length remains 4.5m;
the original prefab scale, ground pivot and existing object/asset identities
are preserved. Physics, traffic settings and runtime code are not changed.

NPC_FamilySUV_UnityValidation.txt records imported triangle counts, aligned
bounds, material/shader references and damage/reset tint checks for every
material slot at every LOD. Blender and actual Unity preview PNGs are saved
alongside this file. These are isolated asset/render checks, not a gameplay
or multiplayer playtest. FamilySUV_BeforeCleanup/ archives original assets.

Explicit authoring tools only; no import/reload/Play mode hooks:
Tools/Blender/rebuild_family_suv_surfaces.py
Tools/stage_family_suv.py
Tools/Unity/AuthorFamilySUVLODs.cs
Tools/install_family_suv_lods.py
The Blender tool reuses only the CityHatch modelling helper functions and
does not execute or overwrite CityHatch's source, materials or exports.
Preserve later source/Inspector customizations before deliberate reauthoring.
