# Couch Guys NPC traffic vehicle assets

Finished Unity assets: `Assets/_Project/Models/Vehicles/NPCTraffic/`.

| FBX | Triangles | Length |
| --- | ---: | ---: |
| NPC_CityHatch.fbx | 9998 | 3.7 m |
| NPC_FamilySUV.fbx | 9996 | 4.5 m |
| NPC_UtilityPickup.fbx | 8664 | 4.9 m |
| NPC_SportCoupe.fbx | 10000 | 4.3 m |

All four imported successfully in Unity 6000.3.11f1. See `UnityValidation.txt` for actual imported bounds and checks. Blender mesh checks: zero non-manifold edges, open boundary edges, or loose vertices on every final model.

One unit is one metre. Each pivot is at ground level at the centre of the vehicle. Unity uses +Z forward and +Y up, following the existing car authoring convention. The models contain four physical tyres, minimal interiors and opaque tinted windows. They are static traffic visuals: wheels are baked into the mesh and are not independently rigged.

## Materials and textures

Each FBX has separate `Details` and `BodyPaint` material slots, remapped to saved materials in `Materials/`. To recolour a car, edit **Body Paint Colour** on its BodyPaint material (or use `_BaseColor` on a material instance/property block later). The included paint shader is derived from the existing Couch Guys Toon Lit shader and uses the saved paint mask to keep white stripes and other details. No gameplay code was added. Original paint references are retained internally; the default colours reproduce the generated atlas without recolour patches.

`Textures/` contains the generated BaseColor, ORM (R occlusion / G roughness / B metallic), Normal and PaintMask PNGs for each vehicle. The toon materials use BaseColor and the body paint mask; the normal/ORM maps are retained for optional later material configuration.

## Generation and cleanup

The four image crops in `References/` came directly from the supplied concept sheet. The existing local Pixal3D INT8 graph was copied from the Flamecouch workflow, with the triangle target changed to 10,000. `Workflows/`, `GenerationJobs.json`, generated GLBs and editable Blender files preserve the source work. The Delivery Van was not generated.

Blender cleanup standardised scale, orientation and pivots, removed degenerate geometry, repaired touching-shell junctions, separated body material regions, and exported FBX. The pickup generation invented cargo shapes; these were removed and the bed interior cleaned to leave an open cargo bed. Single-view generation necessarily inferred rear and underside details, and some surfaces retain reconstruction softness. These are background traffic assets rather than close-up hero models.

`VehiclePreviewSheet.png` and the individual front/rear PNGs show the final Blender visual checks. Unity validation ran in an isolated temporary project using the same editor version and cached URP packages; validated import settings and material assets were copied back with their GUIDs. The initial model-generation task left existing traffic behaviour, spawning, physics, prefabs and scenes unchanged.

## Traffic prefab integration

The follow-up integration uses `Assets/_Project/Prefabs/Vehicles/TrafficCar.prefab`, moved from Resources with its original GUID. `Resources/Traffic/TrafficSettings.asset` now holds a serialized reference to it. The four editable visual prefabs live in `Assets/_Project/Prefabs/Vehicles/NPCTraffic/`. Each wraps its generated FBX, uniformly scaled to fit the existing traffic footprint while preserving proportions and the ground pivot.

The traffic prefab contains four saved visual variants. The existing server-side `Random.Range(0, VisualVariantCount)` selects one on every traffic spawn, and the existing buffered observer RPC keeps clients consistent. No randomisation code, spawning rules or physics settings were changed. Exactly one variant is active at a time. Wheels remain baked into these generated meshes and do not animate independently.

`TrafficPrefabValidation.txt` records successful Unity authoring and checks for all four selections, index wrapping, active-object count, renderer-cache updates, serialized settings references, and preserved Rigidbody/collider/FishNet settings. Automatic traffic rebuild hooks were removed so script reloads cannot restore the old hatchback or overwrite Inspector changes. Explicit traffic builder and direct editor/test asset paths now use the saved prefab location.
