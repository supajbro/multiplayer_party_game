# Procedural Neighbourhood Implementation

## Overview

`NeighbourhoodGenerator` builds the first Couch Guys suburban greybox. The layout is generated on an integer grid before any visual object is chosen. Assigned modular prefabs and generated placeholders therefore represent the same logical roads and properties.

The generator owns only the `ProceduralNeighbourhood` child marked by `GeneratedNeighbourhoodRoot`. Clearing or regenerating never searches for or deletes unrelated scene objects.

No delivery, buyer, money, shop, couch spawning, vehicle, traffic, terrain or interior systems are included.

## Quick Start With No Art

1. Create an empty GameObject in the gameplay scene and name it `NeighbourhoodGenerator`.
2. Add `NeighbourhoodGenerator`.
3. Leave Starting Area, Road Prefabs and House Prefabs unassigned.
4. Press **Generate** in its Inspector, or enable **Generate On Start** and enter Play Mode.

This creates a placeholder Starting Area, Plane road tiles and Cube houses. The Plane and Cube primitives retain their standard colliders, so the greybox supplies static collision without Rigidbody, network or per-frame behaviours.

For a networked scene, also add `NeighbourhoodSeedSynchroniser` to the same GameObject. Its required FishNet `NetworkObject` is added automatically. Save the object in the scene so FishNet registers it as a scene network object.

## Architecture

- `NeighbourhoodGenerator` validates configuration, selects or accepts a seed, clears its previous result, creates the Starting Area, generates roads, calculates connections, places properties, creates visuals and validates occupancy.
- `GridCoordinate` is the value type used by road, property and exclusion-zone `HashSet` lookups.
- `RoadTile` stores prefab connection flags. Geometry is never inspected to infer connections.
- `StartingArea` exposes its `RoadConnection`, four or more optional player spawn markers and grid footprint.
- `GeneratedRoad` exposes each result's grid coordinate and final logical connections.
- `GeneratedProperty` is both the House prefab definition and the generated property data exposed to future delivery systems. It contains a `RoadConnection`, `DeliveryPoint`, grid footprint and generated anchor.
- `GeneratedNeighbourhoodRoot` marks the one hierarchy that the generator is allowed to clear.
- `NeighbourhoodSeedSynchroniser` is the optional FishNet authority layer. It synchronises one host-selected seed rather than networking static environment objects.
- `NeighbourhoodGeneratorEditor` adds Generate, Regenerate and Clear buttons.

Future code can read `GeneratedStartingArea`, `GeneratedRoads`, `GeneratedProperties` and `CurrentSeed` directly from the generator. The collections are exposed read-only.

## Grid System

The RoadConnection position is the grid origin and the first road tile centre. `GridToWorld` and `WorldToGrid` are the sole public coordinate conversions. Grid X maps to world X and grid Y maps to world Z, using `Tile Size`.

The Starting Area footprint is reserved before roads or properties. The explicit RoadConnection cell is removed from that exclusion set and is the only intended entry. Roads and properties use separate occupied-cell sets; placement does not depend on Physics overlap queries.

Property footprints are rectangular collections of cells. The front-centre anchor is adjacent to a road and remaining depth extends away from it. This supports larger future properties without assuming that one House always equals one road tile.

## Road Generation

The main road length is chosen within Minimum/Maximum Road Length. It advances in segments within Minimum/Maximum Segment Length and can turn left or right at segment boundaries according to Turn Chance. When a requested cell is blocked, the algorithm tries safe perpendicular directions and stops if none is valid.

After the main route, road cells are considered deterministically for limited branches. Branch Chance, Maximum Branches and the branch length range control their size. Every placement checks the road and Starting Area occupancy sets. Maximum Road Tiles is a hard budget shared by the main route and branches.

Once all road coordinates exist, connection flags are calculated from neighbouring cells. The first tile also receives a logical connection back to the Starting Area. The resulting flag set selects a matching Road prefab and rotation. The supported sets naturally include dead ends, straights, corners, T-junctions and crossroads.

If no assigned `RoadTile` can match the exact final connection set, a Unity Plane is created at the same logical coordinate. Its scale is `Tile Size / 10`, because Unity's built-in Plane is 10 metres square. A warning is logged once per generation rather than once per tile.

## Creating Road Prefabs

1. Build a road whose root pivot is the centre of one tile and whose root scale is `(1, 1, 1)`.
2. Make the road occupy one `Tile Size` square. Keep any collider and visual geometry beneath the root.
3. Add `RoadTile` to the root.
4. Tick the directions to which the unrotated prefab connects. North is local `+Z`, East is local `+X`, South is local `-Z`, and West is local `-X`.
5. Save the prefab beneath `Assets/_Project/Prefabs/Environment/Roads/` and add it to Road Prefabs on the generator.

Only one orientation of an art piece is required. The generator tests all four 90-degree rotations. A prefab is valid only when its rotated flags exactly match the logical tile. Missing types fall back individually to Planes, so straights can be introduced before corners or junctions.

## Starting Area

The Starting Area is always instantiated before road generation. An assigned prefab is valid when its root has `StartingArea` and a RoadConnection reference.

The RoadConnection must be positioned at the intended centre of the first road tile. Its forward direction must point away from the base along one cardinal grid direction. Generation uses this Transform directly; there is no generator-side arbitrary start offset. Set Grid Footprint to the cells occupied by the base, centred on the prefab root.

### Creating a Starting Area Prefab

1. Create a root at scale `(1, 1, 1)` and add `StartingArea`.
2. Add visuals/colliders beneath a `Visual` child.
3. Add a `RoadConnection` Transform at the centre of the first road tile and point its local forward away from the base.
4. Add a `PlayerSpawnPoints` child and four markers such as `PlayerSpawnPoint01` to `PlayerSpawnPoint04`.
5. Assign the connection and spawn markers on `StartingArea` and set the base's grid footprint.
6. Save it beneath `Assets/_Project/Prefabs/Environment/StartingArea/` and assign it to Starting Area Prefab.

If the assignment is empty or invalid, the generator creates a low Cube base, RoadConnection and four spawn markers. With the default 10-metre tile and `3 x 3` footprint, its base is `30 x 0.5 x 30` metres and its road connection is placed one road-cell centre beyond the footprint edge.

## House and Property Generation

After roads are complete, each unconnected road edge is a potential property anchor. House Spawn Chance is evaluated deterministically. The full property footprint must avoid roads, Starting Area cells, other properties and the Minimum House Spacing halo.

A valid assigned House prefab is selected deterministically and its footprint is used for occupancy. The root is placed at the footprint centre and rotated so its RoadConnection faces the source road. Houses remain independent of Road prefabs.

If no valid House prefab is available, a property root with a Cube `Visual`, `RoadConnection`, `DeliveryPoint` and `GeneratedProperty` is created. Default Cube dimensions are approximately `10 x 6 x 10` metres and are configurable with Placeholder House Size. The horizontal size is clamped to its logical footprint. A warning is logged once per generation.

### Creating House Prefabs

1. Create a root at scale `(1, 1, 1)` and add `GeneratedProperty`.
2. Put meshes and colliders beneath a `Visual` child. Do not add the property to a Road prefab.
3. Add a `RoadConnection` marker on the property's road-facing edge. Its forward direction must point towards the road.
4. Add a `DeliveryPoint` Transform at the intended future couch destination.
5. Set Grid Footprint to the property's width and depth in cells. Depth extends away from the road; width runs along it.
6. Assign both marker references, save beneath `Assets/_Project/Prefabs/Environment/Houses/`, and add it to House Prefabs.

The current generator uses the rectangular grid footprint rather than a Physics bounds component. PropertyBounds geometry may be added for authoring reference, but occupancy is controlled by Grid Footprint.

## Seed Behaviour

All logical random decisions use local `System.Random` instances; Unity's global random state is not read or changed. The same seed, generator settings and prefab metadata produce the same logical layout and prefab selections. Road visual selection uses a separately derived random stream, so adding Road art does not move Houses. House selection consumes a stable random value even when no House art is assigned.

When Use Random Seed is disabled, Seed is used exactly. When enabled, the authoritative caller chooses a fresh integer and `CurrentSeed` records it. **Regenerate** reuses `CurrentSeed`; **Generate** selects according to the Inspector setting.

## Multiplayer Behaviour

`NeighbourhoodSeedSynchroniser` follows the existing FishNet authority model:

1. The FishNet server/Steam host selects the seed from the generator.
2. A FishNet `SyncVar<int>` distributes it through the existing connection.
3. Host and clients call `Generate(seed)` locally.
4. Roads, Houses and Starting Area remain ordinary static objects and are not individually network spawned.

Do not enable separate generator-only random execution in a multiplayer scene. Adding the synchroniser automatically suppresses `NeighbourhoodGenerator`'s standalone Generate On Start path. The network scene object must use identical generator configuration and prefab metadata in the same build on every peer. Host migration remains outside the existing multiplayer implementation.

## Inspector Configuration

- **Generation:** Generate On Start, Use Random Seed, Seed, Maximum Road Tiles and Tile Size.
- **Roads:** Road Prefabs, main length, segment length, Turn Chance, Branch Chance, Maximum Branches and branch length.
- **Houses:** House Prefabs, House Spawn Chance, Minimum House Spacing, standard fallback footprint and placeholder dimensions.
- **Starting Area:** Starting Area Prefab and placeholder footprint.
- **Debug:** optional grid wires, road connection lines and coloured occupied cells.

Selecting the generator displays Gizmos only; there is no runtime debug UI and no per-frame generation work. Blue cells are roads, green cells are properties, and orange cells are the Starting Area exclusion zone.

## Generated Hierarchy

```text
NeighbourhoodGenerator
└── ProceduralNeighbourhood
    ├── StartingArea
    │   └── Placeholder_StartingArea (when required)
    ├── Roads
    │   ├── Road_000 / Placeholder_Road_000
    │   └── ...
    └── Houses
        ├── House_000 / Placeholder_House_000
        └── ...
```

## Files Created

- `Assets/_Project/Scripts/ProceduralGeneration/GridCoordinate.cs`
- `Assets/_Project/Scripts/ProceduralGeneration/RoadConnections.cs`
- `Assets/_Project/Scripts/ProceduralGeneration/RoadTile.cs`
- `Assets/_Project/Scripts/ProceduralGeneration/StartingArea.cs`
- `Assets/_Project/Scripts/ProceduralGeneration/GeneratedRoad.cs`
- `Assets/_Project/Scripts/ProceduralGeneration/GeneratedProperty.cs`
- `Assets/_Project/Scripts/ProceduralGeneration/GeneratedNeighbourhoodRoot.cs`
- `Assets/_Project/Scripts/ProceduralGeneration/NeighbourhoodGenerator.cs`
- `Assets/_Project/Scripts/ProceduralGeneration/NeighbourhoodSeedSynchroniser.cs`
- `Assets/_Project/Scripts/Editor/NeighbourhoodGeneratorEditor.cs`
- `Assets/_Project/PROCEDURAL_GENERATION_IMPLEMENTATION.md`

No existing Player, couch, camera, scene or Steam multiplayer files were modified.

## Known Limitations

- Roads are flat, one-cell modules on the generator's horizontal plane; slopes, bridges and terrain conformance are not supported.
- The algorithm deliberately produces a small street network rather than blocks, zoning or a city simulation.
- Property footprints are rectangular grid regions. Arbitrary polygonal bounds are not yet supported.
- An even-width property has one deterministic centre bias because no single grid cell can be its exact centre.
- Prefab metadata and generator settings must match across multiplayer peers; configuration is not serialised over the network.
- Runtime multiplayer verification still requires a host/client Steam session in the Unity project. The implementation does not alter the existing Steam lobby, Player spawn or couch authority code.
