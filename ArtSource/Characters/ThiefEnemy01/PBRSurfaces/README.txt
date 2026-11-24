THIEF ENEMY 01 - COUCH GUYS PBR SURFACES

The current Getaway Crew design is retained: burgundy/plum/gold outfit,
lightning graphic, dark mask, warm skin and burgundy shoes. Generated skin
and leather illumination is replaced with clean colour regions. Generated
normal relief is disabled. No emission or baked shadow/AO darkening is used.

Blender source: ../thief_enemy_01.blend
CG_Thief_* materials and CG Thief / * node groups hold named surface colours,
metallic and roughness settings. The shared thief_enemy_01 material selects
those lit Principled surface parameters using the existing UV material mask.
It uses one Principled closure; the mask contains material IDs, not lighting.

Unity: the same surface colours and physical parameters are baked into the
existing colour and metallic/smoothness maps. The standard URP Lit material
responds to scene lights and shadows. Graphic details such as the lightning
logo remain in the colour atlas. No custom unlit shader is used. The shared
atlas keeps the original single material per LOD and adds no draw calls.

This update does not re-export the FBX or alter mesh positions, UVs, weights,
bones, animation curves, controllers, prefabs, saved LOD settings or mounts.
All four LODs and 25 clips are retained. Before/ contains the previous surface
assets and source. InstallationValidation.json records preservation hashes.

UnityPBRValidation.txt exercises five controllers, five states per controller,
13 poses per state and every LOD. Unity_Light_Left/Right and Ambient_Only are
actual Unity renders; pixel checks verify response to direction and intensity.
Blender lighting comparisons and source preservation checks are included.
These are isolated asset checks, not a full multiplayer encounter playtest.

Explicit offline tools (never import/reload/Play mode hooks):
Tools/author_thief_pbr_surfaces.py
Tools/Blender/author_thief_pbr_materials.py
Tools/stage_thief_pbr_surfaces.py
Tools/Unity/ValidateThiefPBRSurfaces.cs
Tools/install_thief_pbr_surfaces.py
Preserve later artist/Inspector customizations before deliberately reauthoring.
