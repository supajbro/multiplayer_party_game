# Medium House 02

Added the concept-based modern medium house: offset skillion roofs, cream render,
limestone tower with two-storey glazing, upper timber facade, recessed porch,
horizontal door glazing, right-hand garage, entrance steps and garden beds.

## Saved assets and integration

- `Assets/_Project/Models/Houses/MediumHouses/medium_house_01.blend` and `.fbx`:
  relocated reference model, with original `.meta` GUIDs and backup files preserved.
- `Assets/_Project/Models/Houses/MediumHouses/medium_house_02.blend` and `.fbx`:
  new model and three separately authored mesh LODs, combined by material.
- `Assets/_Project/Prefabs/Houses/medium_house_02.prefab`: saved editable prefab,
  LODGroup, compound box colliders independent of the LODs, GeneratedProperty,
  delivery anchor and HouseExteriorModuleSet. Reuses the reference access templates.
- `Assets/_Project/Materials/Toon/MediumHouse02/`: ten shared project-toon materials.
- `RegionDefinition.cs`, `NeighbourhoodGenerator.cs`: an Inspector-configurable
  equal-weight medium pool, following the existing easy-house per-slot seeded stream.
  Excludes easy houses, nonresidential lots and mansions; does not consume layout RNG.
- `Region_Suburbs.asset`, `Region_Suburbs_Menu.asset`: register both prefabs, 50/50.
- `HousePrefabBuilder.cs` and existing Blender house authoring scripts: update only
  the reference-model path after its move.
- `generate_medium_house_02.py`, `MediumHouseVariantAuthoring.cs`: explicit offline
  model authoring and Unity editor authoring/validation. No routine import, reload
  or Play-mode rebuilding. A `Library/MediumHouseVariant.request` file explicitly
  requests a one-shot build/validation; `rebuild` overwrites only the new variant.
  Editor menu: **Couch Guys > Validation > Build Missing Medium House Variant And Validate**.

The original saved wrapper remains `House_01.prefab` to preserve its identity,
references and Inspector settings. Its source model is now `medium_house_01`.
The original model, FBX, metadata, prefab and combined LOD asset passed SHA-256
comparisons against their pre-task contents.

## LODs

| Level | Triangles | LOD0 fraction | Renderers/material batches |
| --- | ---: | ---: | ---: |
| LOD0 | 13,444 | 100% | 10 |
| LOD1 | 5,512 | 41.0% | 10 |
| LOD2 | 1,436 | 10.7% | 8 |

Transitions use screen heights 0.60 / 0.30 / 0.01. The existing house-distance
controller also works with three levels: its farthest level clamps to LOD2.
All levels retain identical overall bounds. No duplicated high-detail meshes
are used as simplified levels. Colliders stay active across LOD changes.

## Measurements and validation

Measured source reference bounds including authored access pieces:
17.6954 m wide × 15.0403 m deep × 8.2604 m high. Both wrappers retain the original
2× visual scale, pivot offset, +Z frontage and FBX coordinate convention. The
Blender garage centre stays at X=6.35 m (Unity local X=-6.35 m, street-view right).

With the original modular paths/driveway excluded from building render bounds,
the original prefab is approximately 35.39 × 22.36 m in X/Z; the variant is
35.39 × 23.40 m, a 4.65% depth difference. Height is 16.30 m versus 16.52 m.
Both use the existing 2×2 medium plot and normal terrain fitting/reservations.
The new entrance and path are aligned to the actual recessed doorway.

Unity 6000.3.11f1 compiled the changes and passed focused editor validation:

- Original GUID dependency and original-file hashes preserved.
- Three populated LODs, requested reduction ratios, matching silhouette bounds.
- Supported toon materials, structural colliders and delivery clearance.
- Seed 10000: 11 original / 5 new medium houses.
- Seed 10001: 6 original / 9 new medium houses.
- Seed 12345: 6 original / 9 new medium houses.
- Repeated seed 10000 reproduced assignments and delivery positions exactly.
- Easy/hard positions and drop points matched a disabled-medium-pool baseline.
- New variant excluded from easy and hard delivery eligibility.
- Three forced LOD renders verified as non-empty and visually inspected.

See `Previews/medium_house_02_validation.txt`, `medium_house_02_blender.png`
and `medium_house_02_LOD0.png` through `LOD2.png` for the saved report/previews.
Validation used the generator in a Unity preview scene; no manual multiplayer
delivery session was performed.
