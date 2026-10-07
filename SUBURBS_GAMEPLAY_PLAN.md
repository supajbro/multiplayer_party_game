# Couch Guys — Complete Suburbs Gameplay Implementation Plan

## Purpose and Definition of Done

This is the master implementation plan for turning the current Suburbs prototype into a complete, replayable, multiplayer-safe 20–30 minute chapter. It is intentionally a plan only: no feature described here is implemented by this document.

The chapter is complete when a new party can spawn at the Suburbs depot, learn the delivery loop from the Couch Seller, complete seven paced deliveries, unlock and learn the van, survive escalating thief encounters, recover from player and party failure, complete a bespoke-feeling final delivery, unlock the next biome, and transition there without breaking the network session.

The core loop must remain:

`Carry → Load → Drive → Park → Unload → Carry → Deliver`

The van shortens the long middle of a route; it must not remove couch carrying, cooperative positioning, awkward approaches, or unloading risk. All Suburbs couches use the existing `CouchCarryController` behaviour and one consistent weight/handling profile. Couch weights and couch-stat progression are explicitly out of scope.

## Development Principles

1. Preserve the existing FishNet/Steam host-client architecture and server-authoritative couch simulation.
2. Extend `DeliveryManager`, `NeighbourhoodGenerator`, `DeliveryDestination`, `EnemySpawnManager`, `PlayerHealth`, and `NeighbourhoodMap` instead of replacing working systems.
3. Make chapter rules data-driven where tuning is expected, but do not build a general quest framework or dialogue graph.
4. Treat the server as the authority for every consequential gameplay decision.
5. Keep generated geometry deterministic on all peers; synchronise the seed, region, selections, and dynamic objects rather than every road or house.
6. Build recovery paths at the same time as couch/van mechanics, not as late polish.
7. Tune for cooperation and readable chaos. Thieves interrupt deliveries; they do not turn the game into a shooter.
8. Profile and validate with one to four real network clients. Host-only success is not sufficient.

## Audited Project Baseline

The project uses Unity `6000.3.11f1`, FishNet, FishySteamworks, Steamworks.NET, Unity Input System, Unity AI Navigation, URP, and the Unity vehicle physics module. There is currently one configured gameplay scene, `Assets/Scenes/SampleScene.unity`. No project gameplay test assemblies were found.

| Area | Existing implementation to retain | Current limitation relevant to this plan |
| --- | --- | --- |
| Player movement | `ThirdPersonPlayerController` uses an owner-driven `CharacterController`, camera-relative movement, jumping, external impulses, movement locks, carrying resistance, and slope-dependent uphill slowdown. `PlayerInputReader` owns cloned per-player actions. | No vehicle input/actions, interaction routing, incapacitated state, or transition freeze state. |
| Multiplayer/session | FishNet over FishySteamworks/Steamworks. `SteamClientBootstrap`, `SteamLobbyController`, and `SteamMultiplayerAutoLoader` provide a 1–4 player host/client session. `NetworkPlayerOwnership` enables controls/camera only for the owner. | Host migration is deliberately absent. Current player movement is owner-authoritative; gameplay state still needs server validation. |
| Player spawning | `NeighbourhoodPlayerSpawner` waits for `NeighbourhoodSeedSynchroniser.EnsureAuthoritativeGeneration()` and spawns owned players at `StartingArea.PlayerSpawnPoints`. | It only handles initial connection spawning, not party-wipe respawn or an in-session biome regeneration. |
| Couch spawning/delivery | `DeliveryManager` is a `NetworkBehaviour`; it selects a destination, spawns `Couch.prefab`, detects a carried couch within a collection radius, despawns it, and increments a team currency `SyncVar<int>`. `DeliveryNPC` starts a job. | Only one implicit state exists. A destination is uniformly random, NPC interaction starts immediately, rewards are fixed, and there is no stage, retry, failure, final delivery, or completion state. |
| Couch carrying | `PlayerCouchCarrier`, `CouchCarryController`, and `CouchCarryPoint` support four independent carry points. Owners request grabs/releases and send throttled movement intent; the server validates range and simulates the dynamic couch Rigidbody. Disconnect/despawn releases an occupied point. | `PlayerCouchCarrier` also directly searches NPCs and owns the general Interact key, which will conflict with revive/van/transition interactions unless input is routed. |
| Multiple carriers/physics | `CouchCarryController` applies per-point horizontal/lift forces, cooperation scaling, torque, velocity limits, slope gravity/resistance, and server-authoritative Rigidbody simulation. FishNet `NetworkTransform` replicates the result. | Van loading needs an explicit secured mode without altering normal carry physics. |
| Slope/elevation | `ThirdPersonPlayerController` slows uphill movement. `CouchCarryController` reacts to contacted slopes. `NeighbourhoodGenerator` assigns deterministic row elevations and rotates road visuals between height bands. | Elevation is generated, but no route analysis or progression difficulty metadata is produced. |
| Procedural Suburbs | `NeighbourhoodGenerator` builds a deterministic grid road lattice, properties/lots, delivery points, elevation, world bounds, region props/hazards, the depot, NPC, and one runtime NavMesh. `GridRoadGenerationStrategy`, `RegionDefinition`, and `LotDefinition` already separate region configuration from generation. | The generator does not classify routes, reserve vehicle clearance, validate turning/parking, or guarantee staged destination pools. |
| Current Suburbs data | `Region_Suburbs.asset` is a 6×6 block region with 4×4-cell blocks, 8 m road tiles, five lots per block, 2–4 m elevation steps, 24 m maximum elevation, and a 45° configured maximum road slope. | It currently has no road prefabs, ground prefab, starting-area prefab, props, hazards, or landmarks, so it falls back to generated placeholders. Vehicle metrics must be tuned against authored road width, not the full placeholder plane. |
| Houses/destinations | `GeneratedProperty` exposes `RoadConnection`, `DeliveryPoint`, footprint, and grid anchor. `GeneratedLot` points to `LotDefinition`. `DeliveryDestination` stores type, display name, delivery/reward modifiers, and eligibility. | No parking/unload anchor, final carry distance, route score, accessibility classification, or per-run usage history. |
| NavMesh/enemy navigation | `NeighbourhoodGenerator.BuildNavMeshOnce()` builds from physics colliders. `ThiefNavigator` is a server-only `NavMeshAgent` pursuit component. | Agents retain their initial target and do not recover from unreachable/stale targets. Vehicle occupants and delivery encounter zones are unknown to AI. |
| Enemy spawning/combat | `EnemySpawnManager` server-spawns 2–5 thieves at timed intervals, prefers a player carrying a couch, validates world bounds/NavMesh paths, and caps active enemies. `EnemyWeapon` performs server hitscan damage with replicated visual tracers. Pistol/shotgun/assault-rifle prefabs exist. | Spawning is global and time-based rather than delivery-stage/encounter-driven. Thieves have no gameplay health/death system. There is no public pause/despawn/encounter API. |
| Health | `PlayerHealth` has server-owned health, replicated health/knockdown state, damage, visual falling, movement lock, knockback, and forced couch release. | The current knockdown is a short automatic hit reaction. At zero health it can unlock movement after the timer, and there is no revive, healing/reset, incapacitation, wipe detection, or UI. This must be corrected rather than reused as-is. |
| Map/markers | `NeighbourhoodMap` builds an owner-only runtime map showing generated roads, houses, depot, local player, teammates, and the active destination. | No van marker, destination route/label, downed teammate distinction, transition point, or always-visible direction cue. UI is runtime primitives only. |
| Currency/progress | `DeliveryManager.m_currency` is server-owned team currency and rewards are adjusted by `DeliveryDestination.RewardModifier`. | Currency has no UI, persistence, purchasing, or campaign meaning. Use it as visible team earnings/van-fund feedback, not an RPG economy. |
| Regions/biomes | `RegionDefinition` contains identity, difficulty, unlock cost, layout, lots, hazards, and NavMesh settings. Multiple region assets already exist. `IRegionUnlockProvider` is an unused seam. `NeighbourhoodSeedSynchroniser` synchronises region index and seed. | There is no unlock provider implementation, save data, safe runtime region switch, readiness handshake, or transition flow. The synchroniser currently chooses initial state only. |
| Vehicles/saving/dialogue | Unity’s vehicle module is installed, and the current roads/couch already use Rigidbody physics. | No van prefab, vehicle controller, vehicle networking, cargo mode, save system, dialogue UI/data, or chapter/session manager exists. |

### Baseline Decisions

- Keep `DeliveryManager` as the authority for the single active delivery, but give it an explicit state machine and stage request supplied by a new Suburbs progression component.
- Keep team currency. Van acquisition is unlocked by completing Stage 3, with earnings presented as the narrative “van fund”; it is not a spendable purchase that can be missed or soft-lock the chapter.
- Add a small `PlayerInteractionController` to arbitrate the one Interact action in this priority order: revive teammate, current van/cargo action, couch point, Couch Seller, biome transition. `PlayerCouchCarrier` remains the couch specialist and exposes validated requests to the router.
- Keep locally generated static worlds. Dynamic objects—players, couch, van, and thieves—remain network spawned.
- Use a secured cargo state for the couch. Loading is a single validated action once the couch is in the van’s load zone; it is not a fiddly physical-joint minigame.
- Use coarse save checkpoints: chapter not started, van checkpoint reached, and Suburbs complete. Never persist a live delivery, spawned enemies, arbitrary object transforms, or health.

## Target Chapter Pace and Seven-Stage Progression

The stage number is the canonical progression gate. Currency communicates reward and the van fund, but stage completion—not current balance—unlocks content. Times are first-play targets and include brief seller interactions/returns.

| Stage | Cumulative target | Delivery design | New lesson/escalation | Suggested reward |
| --- | ---: | --- | --- | ---: |
| 1 — “Just Around the Corner” | 0–3 min | Closest valid easy house; flat road, one simple turn, 10–25 m final carry, no thieves. | Meet seller, accept job, map/waypoint, grab/release, multiple carriers, delivery/reward. | 100 |
| 2 — “This Looked Closer on the Map” | 3–6 min | Medium-short route with 2–3 turns, modest ascent, awkward driveway/fence approach, still on foot. | Coordinated cornering, slope handling, reading the map, returning to depot. Seller explicitly foreshadows the van fund. | 125 |
| 3 — “Definitely a Safe Neighbourhood” | 6–9 min | Medium route, meaningful ascent/final carry, one small scripted thief encounter (1–2 easy ranged thieves) near the approach. | Drop couch under pressure, revive if necessary, finish the last all-foot job. Completion fills the van fund and unlocks the van. | 175; van unlock at 400 total |
| 4 — “Van With a Plan” | 9–13 min | Long but easy, wide and mostly flat road; large parking area; short unload carry; low/no enemy pressure. | Enter/exit, driver and passengers, load/secure, drive, park, unload, carry. Deliberate power spike: obviously faster than walking. | 200 |
| 5 — “Hill Start” | 13–17 min | Long route with several turns, rolling elevation, a longer driveway and tighter parking; small load/unload ambush. | Vehicle turning, braking/parking, choosing an unload position, protecting loaders. | 250 |
| 6 — “The Scenic Route” | 17–22 min | Very long route with steep-but-valid road sections, 5+ turns/intersections, constrained parking, 35–60 m final carry, two moderate encounter windows. | Van recovery, long ascent, coordinated unload, revive under pressure, couch recovery. | 300 |
| 7 — “The House on Absolutely the Wrong Hill” | 22–28 min | Curated template mapped onto the hardest validated generated region: long drive, steep climb, road ambush, forced parking cutoff, uphill manual carry, final thief wave, ridiculous destination. | Culmination of every system; completion celebration and next-biome unlock. | 500 + completion reward |

Target total is 22–28 minutes, leaving a two-minute tolerance for party size, mistakes, and comedy. Stage time instrumentation must record accept-to-deliver, depot return time, wipes, recoveries, and total chapter duration. Adjust route bands and encounter size before changing couch physics.

The intended perceived curve is:

`Learning → coordination strain → first danger → VAN POWER SPIKE → driving skill → combined pressure → finale`

## Suburbs Progression and Delivery State Machine

### Goal

Represent the full chapter and each delivery explicitly so any client, late joiner, UI, enemy spawner, or recovery system can reconstruct what is happening from replicated state.

### Existing Systems

- `DeliveryManager` already owns the active couch, destination index, team currency, server start validation, proximity completion, reward, and collection marker.
- `DeliveryNPC.InteractServer()` already routes an NPC interaction to `DeliveryManager`.
- `NeighbourhoodSeedSynchroniser` and `NeighbourhoodGenerator` already establish deterministic world readiness before players spawn.

### New/Modified Systems

- Add `SuburbsChapterDefinition` as a ScriptableObject containing the seven ordered stage records: IDs, route/destination filters, reward, expected duration, van requirement, enemy encounter profile, final-carry band, and dialogue cue IDs.
- Add `SuburbsChapterManager : NetworkBehaviour` beside `DeliveryManager`. It owns replicated chapter stage, completed-delivery count, van-unlocked flag, chapter-completed flag, and coarse checkpoint state.
- Expand `DeliveryManager` with a replicated `DeliveryState` enum and delivery attempt/retry counter. Recommended states:

  `Unavailable → Available → NPCInteraction → Accepted → CouchSpawned → Transport → DestinationReached → Completed → Reward → ReturnToNPC`

  Failure branch:

  `Transport → PartyWipe → Failed → Resetting → Available`

  Van metadata does not need separate top-level states; use replicated couch transport mode (`Free/Carried/SecuredInVan`) and objective steps (`Load`, `Drive`, `Unload`, `ManualCarry`, `Deliver`) while the delivery remains in `Transport`.
- Add an immutable replicated delivery snapshot: stage ID/index, state, destination index, active couch reference, attempt number, reward, objective step, and optional active van reference. Use SyncVars or a small serialisable struct supported by FishNet.
- Make `SuburbsChapterManager` request a stage-appropriate destination from `DeliveryManager`; do not let the manager independently roll a random destination.

### Implementation Steps

1. Define stable enum values and string stage IDs. Never use display text as save/network identity.
2. Create `SuburbsChapterDefinition` and populate the seven stages from the progression table.
3. Add `SuburbsChapterManager` to the generator’s network scene object through `ProceduralGameplaySceneBuilder`; update its FishNet behaviour ordering verification.
4. Refactor `DeliveryManager.TryStartDeliveryServer` into guarded transitions: begin seller interaction, accept, select destination, spawn couch, enter transport.
5. Centralise all state changes in server-only methods. Each method validates the expected current state so duplicate RPCs become harmless no-ops.
6. Separate “delivery completed” from “chapter stage advanced.” Complete the couch handoff, award once, record timing, then let `SuburbsChapterManager` advance and select the next seller cue.
7. Track used destination indices and avoid repeats within the seven-stage run unless no alternative satisfies the stage.
8. Unlock the van atomically when Stage 3’s reward is committed. Spawn it only after state is saved/replicated.
9. Gate Stage 7 behind successful validation of a final-route candidate during world generation. If none exists, use the documented fallback candidate rather than blocking progression.
10. Remove the temporary teleport action from release builds; retain it behind `UNITY_EDITOR` or a development-build debug flag.
11. Expose read-only events for state, stage, objective, reward, and chapter completion so UI and audio do not poll every frame.
12. Add state transition logging in development builds with stage ID, attempt, previous/next state, destination, and initiating connection.

### Multiplayer Considerations

- Only the server selects destinations, spawns/despawns the couch, awards currency, changes stage, unlocks the van, or completes the biome.
- Clients send intent only. Revalidate distance, life state, chapter state, and object references on the server.
- Late joiners receive the replicated snapshot and see the current objective. They do not replay old dialogue or rewards.
- Deduplicate NPC accept, completion, reward, wipe, and retry using current state plus attempt ID.
- Keep generated destination ordering deterministic across peers; still synchronise the selected index because authority must not depend on each client making the same random call.

### Edge Cases

- Two players accept simultaneously: the first valid server transition wins; the second receives the already-active objective.
- No candidate destination: relax only the stage’s optional score bounds in a fixed order, log the fallback, and never choose an unreachable destination.
- Couch despawns unexpectedly: treat it as recoverable failure and respawn the same attempt at the depot; never grant a reward.
- Host leaves: retain the project’s existing rule that the session ends; do not invent host migration in this chapter work.
- Late join after completion: spawn at the depot/transition area and receive completed/unlocked state immediately.

### Testing

- EditMode tests for every legal/illegal state transition and exactly-once reward/stage advancement.
- PlayMode tests for double accept, double delivery trigger, retry, Stage 3 van unlock, and Stage 7 completion.
- Host plus three-client test with a join during `Transport`, `Failed`, and `ReturnToNPC`.
- Automated timing telemetry assertions should warn, not fail, when content is outside tuning targets.

### Dependencies

- Existing `DeliveryManager`, `DeliveryNPC`, `CouchCarryController`, `NeighbourhoodGenerator`, and FishNet scene object.
- Destination analysis described in the next feature.

## Procedural Difficulty Zones and Destination Scoring

### Goal

Allow each seed to look different while guaranteeing a coherent outward progression from depot through easy, medium, hilly, difficult, and final-delivery regions.

### Existing Systems

- `NeighbourhoodGenerator` knows every `GeneratedRoad`, `GeneratedProperty`, `GeneratedLot`, `DeliveryDestination`, block elevation, grid coordinate, world bound, and depot road connection.
- `GeneratedProperty.RoadConnection` and `DeliveryPoint` provide the two ends of the house approach.
- The grid road lattice and elevations are deterministic. `Region_Suburbs.asset` currently tends to raise/lower whole rows, naturally creating an outward elevation axis.

### New/Modified Systems

- Add `DeliveryRouteMetrics` data to each generated `DeliveryDestination` after all roads/properties exist.
- Build a lightweight road graph from `GeneratedRoad.Coordinate` and `RoadConnections`, rooted at `StartingArea.RoadConnection`/`GridCoordinate.Zero`.
- Compute a configurable `DeliveryDifficultyScore` and zone:

  `Depot`, `Easy`, `Medium`, `Hilly`, `DifficultOuter`, `FinalCandidate`.
- Recommended normalised score components:

  - 25% shortest road-route length from depot;
  - 15% straight-line distance as a secondary spread signal;
  - 20% cumulative elevation gain;
  - 10% maximum road grade;
  - 10% turn count and intersection decisions;
  - 10% road-to-delivery final carry distance/driveway complexity;
  - 5% constrained parking score;
  - 5% configured `DeliveryDestination.DifficultyModifier` and encounter opportunity.
- Store raw metrics as well as the aggregate score so stages can request semantic constraints such as “long but flat,” not only a score band.
- Add `DeliveryDestinationQuery`/filter logic to `DeliveryManager`: score range, route length, ascent, turn range, vehicle access, final carry range, destination type, exclude-used, and final-candidate requirement.

### Implementation Steps

1. Expose a read-only road lookup/height query from `NeighbourhoodGenerator`; do not let delivery code reflect into its private collections.
2. Map each destination’s `GeneratedProperty.RoadConnection` to its adjacent/nearest road cell and reject mappings beyond a small configured tolerance.
3. Run Dijkstra from the depot over road edges. Edge cost begins as horizontal distance, with separate accumulated ascent/grade metadata.
4. Reconstruct the best route to each destination and count turns, intersections, length, ascent, descent, and maximum grade.
5. Measure property approach length from road connection to delivery point and sample slope/clearance along it.
6. Normalise metrics against configured Suburbs target ranges, not the maximum found in one seed; this prevents an unusually flat seed from labelling a mild hill “extreme.”
7. Assign zones using score plus hard gates. For example, `Easy` must also have low ascent and short final carry; `FinalCandidate` must be vehicle-reachable to its parking cutoff and contain a hard manual finish.
8. Validate minimum candidate counts after generation: at least two easy, two medium, two van-easy/long, two hard van, and one final candidate.
9. If a seed lacks a required pool, first use deterministic relaxed bounds; if still invalid, have the host retry generation with a derived seed up to a small cap before players spawn.
10. Surface route metrics and zone colours in editor gizmos and `NeighbourhoodGeneratorEditor` for rapid content tuning.
11. Keep scoring allocation-free after generation. Cache the metrics for the entire run.

### Multiplayer Considerations

- All peers calculate the same metrics from the same region/seed/configuration, but only the server queries and selects a destination.
- Generation validity and any seed reroll happen on the server before player spawning; the final authoritative seed is what gets synchronised.
- Do not synchronise all metrics unless a client UI needs them; synchronise selected stage/destination and reconstruct presentation locally.

### Edge Cases

- Disconnected road graph: mark affected destinations invalid and report their coordinates.
- A house has no road connection/delivery point: retain current `CanReceiveDelivery` rejection and log once.
- Too few houses due to placement density: reroll the seed or fall back to a documented lower candidate-count threshold, never repeat one house seven times.
- Equal candidates: select deterministically from a server RNG derived from world seed, stage index, and attempt count.
- Destination becomes invalid after generation: fail safely back to seller and choose another unused candidate.

### Testing

- EditMode tests on hand-authored miniature graphs for route length, ascent, turn count, disconnected nodes, and score bands.
- Seed sweep (at least 100 Suburbs seeds) asserting required candidate pools and generation time.
- Visual gizmo inspection for near/flat versus far/hilly properties.
- Regression test that adding decorative prefabs does not change logical metrics for a fixed seed.

### Dependencies

- `NeighbourhoodGenerator`, `GeneratedRoad`, `GeneratedProperty`, `DeliveryDestination`, `RegionDefinition`, and the Stage definitions.

## Van-Aware Procedural Generation

### Goal

Guarantee that important van routes are driveable and recoverable while still creating increasingly demanding roads, parking cutoffs, and manual final approaches.

### Existing Systems

- The generator has an 8 m tile grid in the Suburbs asset, exact road connectivity, maximum road slope configuration, property road connections, elevation bands, world bounds, and collider-based NavMesh generation.
- `RoadTile` already describes connectivity independently of mesh appearance.
- Current Suburbs roads/ground/depot use placeholders, so authored vehicle clearance has not yet been proven.

### New/Modified Systems

- Extend road authoring metadata (on `RoadTile` or a sibling `VehicleRoadMetadata`) with driveable width, edge clearance, recommended speed, and optional no-van/cutoff flags.
- Extend `GeneratedRoad` with cached grade and vehicle accessibility.
- Extend `DeliveryDestination` with `ParkingPoint`, `VehicleAccessible`, `FinalCarryDistance`, `ParkingDifficulty`, and a reference to the approach/cutoff road cell.
- Add a `VehicleRouteValidator` used after generation to test connected width, maximum supported van grade, turn clearance, spawn/recovery clearance, and destination parking access.
- Add Suburbs-specific limits to `RegionDefinition` or a compact `SuburbsGenerationProfile`: van width/length, safety margin, maximum drive grade, minimum turn envelope, and parking dimensions.

### Implementation Steps

1. Build the production van footprint first as a greybox and use its measured collider/wheelbase values as generator inputs.
2. Author actual Suburbs road prefabs whose visible road and colliders meet the width metadata. Do not treat the whole 8 m placeholder plane as driveable road.
3. Clamp generation’s road grade to the lower of region maximum and van-tested maximum. The current 45° setting is a generation ceiling, not a proven vehicle-safe value; expect playtesting to reduce it substantially.
4. Validate every edge used by Stage 4–7 route pools. Exclude a route if any segment lacks clearance or exceeds the drive grade.
5. Generate or derive a parking point near each destination’s road connection. Use the road shoulder/driveway, not the front-door `DeliveryPoint`.
6. Sphere/box-cast the van footprint at the parking point and along critical turns. Reject blocked positions.
7. Classify approaches separately: vehicle route ends at `ParkingPoint`; couch route continues to `DeliveryPoint`. Cache that final carry length/ascent.
8. Reserve wider/easier route requirements for Stage 4; gradually allow tighter turns, steeper valid grades, and smaller parking zones for Stages 5–7.
9. For Stage 7, require an authored `VanCutoff` pattern or destination metadata that makes the last 45–75 m deliberately couch-only while remaining NavMesh reachable to players/thieves.
10. Add roadside recovery nodes every few road tiles and at major intersections. These are candidate safe reset positions, not visible checkpoints.
11. Validate depot van spawn, cargo loading area, and exit road before accepting a seed.
12. Keep environmental obstacles outside the swept vehicle path unless explicitly tagged as breakaway/non-blocking.

### Multiplayer Considerations

- Geometry and validation remain deterministic. Only the server decides which valid route/destination is active.
- Dynamic hazards that can block a van must be server spawned and either recoverable or excluded from critical routes.
- Clients may render parking/cutoff hints from selected destination metadata; the server validates actual proximity.

### Edge Cases

- No vehicle-valid Stage 4 route: reroll before spawn; never unlock a van into an unusable map.
- Visual road is narrower than metadata: editor validation fails the prefab/region configuration.
- Van cannot reach the front door: expected; the parking point remains reachable and the delivery point remains manually reachable.
- Procedural prop blocks a route: reserve critical corridors before prop/hazard placement or reject the prop placement.

### Testing

- Automated seed sweep with a simplified van-volume traversal over all required stage pools.
- Drive every connection type, grade band, parking size, depot exit, and recovery node with one and four clients.
- Physics tests at low/high frame rates and simulated latency.
- Editor validation that each road prefab’s declared width fits its collider bounds.

### Dependencies

- Difficulty scoring, a greybox van footprint, authored road/depot prefabs, and `Region_Suburbs.asset` tuning.

## Couch Seller, Dialogue, and Basic Delivery Loop

### Goal

Give players clear, comedic context and a low-friction accept/return loop without building an RPG conversation system.

### Existing Systems

- `DeliveryNPC` is spawned at `StartingArea.DeliveryNpcSpawnPoint` and already knows `DeliveryManager`.
- `PlayerCouchCarrier` currently finds the nearest NPC and sends a server RPC.
- The active delivery already spawns a couch at an NPC-relative offset and marks the destination.

### New/Modified Systems

- Add `CouchSellerDialogueDefinition` containing cue ID, speaker, 1–3 short lines, optional one-button prompt, and next cue. No branching conditions beyond chapter stage/result.
- Add a simple owner-local `DialoguePresenter` and shared toast/banner presenter.
- Extend `DeliveryNPC` with interaction range, couch/depot/van spawn references, and stage-aware server request methods.
- Add `PlayerInteractionController` as the sole consumer of `InteractPressedThisFrame`; it calls direct typed handlers rather than introducing a large generic interaction framework.
- Required cues: first meeting, first job, first success, carrying tip, thief warning, harder jobs, van foreshadow, van available, van tutorial, failed attempt, late-game job, final job, Suburbs complete, next biome unlocked.

### Implementation Steps

1. Move Interact polling/nearest-choice ownership out of `PlayerCouchCarrier` into `PlayerInteractionController`; retain all couch distance/server validation inside existing couch classes.
2. Implement deterministic interaction priority and a small local prompt showing the selected action.
3. Add stage/result-to-cue mapping in `SuburbsChapterDefinition`.
4. First seller interaction opens the first-meeting cue; a subsequent E/confirm accepts Stage 1. Later visits show the next job and explicit accept.
5. Broadcast milestone barks (“The van is finally mostly road legal.”) to all party members; keep ordinary accept dialogue local to the interacting player while updating everyone’s objective HUD.
6. Keep dialogue non-modal for remote players and pause neither the server nor enemies. At the depot, enemy spawning should already be suppressed.
7. Add concise van tutorial objectives: enter, load, drive, park, unload. Do not front-load every recovery rule in dialogue; show contextual prompts.
8. On failure, set one of several humorous retry cues but leave the same stage available.
9. On completion, require a final return to the seller for celebration and transition unlock, so completion feels larger than an ordinary doorstep reward.
10. Author text in Australian English and keep each line readable during multiplayer movement.

### Multiplayer Considerations

- Server validates seller range and chapter state. Dialogue presentation is client-side; acceptance/progression is server-side.
- Multiple players may talk to the seller, but only one stage accept transition can succeed.
- A player joining mid-delivery skips introduction dialogue and receives the current shared objective.
- Dialogue completion cannot be trusted as a gameplay gate; only server state transitions matter.

### Edge Cases

- Player walks away mid-dialogue: close the local panel; the delivery remains available.
- Seller is generated late: interaction prompt waits for generator readiness.
- Multiple UI panels compete: dialogue closes map, and opening map closes dialogue; neither changes server state.
- No dialogue asset/cue: log and continue with objective text so progression never blocks.

### Testing

- Walk through all cues in order, including failure/retry and late join.
- Spam Interact from two clients and verify only one couch/delivery starts.
- Verify prompts choose revive/van/couch before seller when ranges overlap.
- Check controller/keyboard navigation once gamepad bindings are added.

### Dependencies

- Progression state machine, interaction router, UI foundation, and existing `DeliveryNPC`.

## Player Health, Downing, Reviving, and Party Wipe

### Goal

Turn enemy damage into recoverable cooperative pressure. A player goes `Alive → Downed → Revived`; all connected human players downed causes the current delivery to restart without losing completed Suburbs progress.

### Existing Systems

- `PlayerHealth` already owns server health, SyncVars, knockback, couch release, movement lock, events, and a falling visual.
- `NetworkPlayerOwnership.ServerPlayerStarted/Stopped` provides a server-side player registry seam.
- `ThirdPersonPlayerController` can lock movement and accept external impulses.

### New/Modified Systems

- Replace the temporary `m_knockedDown` timer semantics with replicated `PlayerLifeState { Alive, Downed }` plus optional separate short `HitStunned` timing if severe nonlethal impacts still need a tumble.
- Add `PlayerReviveController` or revive logic coordinated by `PlayerInteractionController`: server-validated hold interaction, progress, cancellation, and health restoration.
- Add `PartyStateMonitor` responsibilities to `SuburbsChapterManager` (or one focused component beside it) to count connected human players and initiate a wipe after a short confirmation delay.
- Add owner/teammate downed UI and world marker hooks.

### Implementation Steps

1. Correct `PlayerHealth`: reaching zero enters `Downed` indefinitely; it never auto-unlocks while health is zero.
2. On downing, force couch release, exit the van, cancel loading/revive/dialogue interactions, stop normal input, and replicate the state/direction.
3. Keep the downed player visible and collidable enough for revive targeting, but prevent normal movement, jumping, carrying, driving, accepting jobs, or biome transition readiness.
4. Add server methods `ReviveServer(healthFraction)` and `ResetAtDepotServer()`; clamp health and make repeated calls idempotent.
5. Make revive selection highest interaction priority within a configurable radius. Send begin/cancel intent to the server; calculate duration on the server (target 2.5–3.5 seconds).
6. Cancel revive if reviver is downed, disconnects, leaves radius, releases Interact, enters a vehicle, or takes a configured interrupting hit.
7. Replicate revive target/progress sufficiently for the reviver, target, and teammates to read it. Do not send per-frame reliable RPCs.
8. `PartyStateMonitor` registers spawned/stopped players and evaluates only valid human-owned player objects. If all are downed for 1–2 seconds during an active delivery, transition once to `PartyWipe`.
9. Wipe sequence: stop encounter scheduling and enemy fire, release/unsecure active interactions, show/fade a humorous failure card, despawn all thieves, reset couch and van, teleport players to distinct depot points, restore health, increment attempt, and return the same stage to `Available`/retry.
10. Preserve completed stages, currency, van unlock, biome unlocks, and coarse save checkpoint.
11. Decide solo behaviour explicitly: a solo down is an immediate wipe after the same grace period; no self-revive.

### Multiplayer Considerations

- Damage, downing, revive progress, health restoration, wipe detection, and reset are server authoritative.
- Owner movement remains client-driven, but the server-owned life state gates server interactions and `NetworkPlayerOwnership`/controller locks local input.
- Wipe teleports must call the established `ThirdPersonPlayerController.Teleport` plus FishNet `NetworkTransform.Teleport`, following `PlayerCouchCarrier.TeleportWithCouchServer` precedent.
- A disconnect can change “all players down” truth; recompute after the player registry updates.

### Edge Cases

- Last alive player disconnects while teammates are downed: remaining party wipes.
- Downed player disconnects: remove them from the required count and release any revive/carry/seat reference.
- Join during wipe: hold the player in transition lock and include them in the depot reset, not the failed attempt.
- Everyone downed at the instant of delivery: a committed delivery completion wins if its server transition occurred first; otherwise wipe wins. State guards make the order deterministic.
- Reviver and target on moving/steep geometry: use horizontal plus limited vertical range checks.

### Testing

- Unit tests for damage-to-down, revive health, cancellation, and no automatic recovery at zero health.
- Network tests for simultaneous revives, reviver disconnect, solo wipe, four-player wipe, and delivery/wipe race.
- Verify couch release and van exit for driver/passenger downing.
- Repeatedly wipe Stage 6 and confirm stage/currency/van do not regress or duplicate.

### Dependencies

- Interaction router, chapter/delivery state machine, player registry, UI, and enemy pause/cleanup API.

## Thief Progression and Encounter Direction

### Goal

Use existing thieves as short delivery disruptions whose intensity and placement follow stage design, without letting continuous combat dominate couch transport.

### Existing Systems

- `EnemySpawnManager` can server-spawn network enemy groups on NavMesh, prefers carriers, enforces distance/world/path checks, tracks active thieves, and clears them during region changes.
- `ThiefNavigator` server-navigates to a `PlayerHealth`; `EnemyWeapon` applies server hitscan damage and knockback.

### New/Modified Systems

- Add `DeliveryEncounterProfile` per stage: ambient spawning enabled, maximum active, group sizes, weapon mix, encounter windows/triggers, cooldown, and protected safe zones.
- Give `EnemySpawnManager` public server APIs: configure profile, pause/resume, spawn encounter near a route anchor, retarget, and despawn all.
- Add generated `EnemyEncounterAnchor` candidates based on route distance, NavMesh, sightlines, distance from depot/front door, and safe spawn clearance.
- Add target selection that ignores downed players and van occupants, periodically retargeting valid exposed players.

### Implementation Steps

1. Disable current free-running timed spawning until a stage profile explicitly enables it.
2. Mark a no-spawn/no-fire safe radius around depot and the immediate post-delivery celebration area.
3. Stage 1: zero thieves. Stage 2: normally zero, optional distant visual foreshadow only. Stage 3: one scripted group of 1–2 lower-pressure thieves near the final approach.
4. Stage 4: zero or one tiny unload encounter after players enjoy the van. Stage 5: 2–3 thieves triggered during load/unload. Stage 6: two windows with a 3–4 active cap. Stage 7: a road ambush and final-carry wave, never both at full strength simultaneously.
5. Select encounter anchors along the active route and validate a complete NavMesh path to an exposed player/approach area.
6. Update target selection every few seconds and on target down/disconnect/van entry. Prefer couch carriers/loaders, then nearest alive exposed player.
7. Add an unreachable timeout: repath/retarget, warp to a hidden valid anchor if safe, or despawn. An enemy must not stall an objective or consume the active cap forever.
8. Pause spawning during seller dialogue at depot, reward, wipe/reset, and biome transition. Clear all active thieves on wipe and region clear.
9. Keep enemies away from the secured couch itself; they attack/knock down players, indirectly causing drops and chaos.
10. Use weapon mix as texture, not raw escalation. Limit assault-rifle enemies in the final wave until revive/down timings are proven fair.

### Multiplayer Considerations

- Server owns spawn rolls, enemy targets, navigation, hits, and cleanup.
- Replicate only normal enemy network transforms/combat visuals and encounter UI cues.
- Scale group caps modestly by alive party size, with stage-defined hard caps to protect CPU/network budgets.

### Edge Cases

- Target enters van: immediately retarget an exposed player; if none exist, enemies move toward an unload/ambush anchor but do not shoot through the vehicle.
- Enemy has no complete path: retry another anchor/target, then despawn after timeout.
- Players retreat to depot: enemies disengage/despawn at the safe boundary.
- Delivery completes with enemies alive: freeze/despawn encounter members before reward/return state.

### Testing

- Stage-by-stage spawn count/weapon mix assertions.
- NavMesh tests across slopes and final manual approaches.
- Four-client target churn: down, revive, enter van, disconnect.
- Profile CPU, active network objects, path updates, and tracer creation during the final wave.

### Dependencies

- Delivery stage profiles, route/encounter analysis, corrected `PlayerHealth`, and van occupancy.

## Networked Delivery Van

### Goal

Create a simple, funny, reliable mid-chapter vehicle that feels dramatically powerful on Stage 4, then becomes a skill requirement on harder roads without introducing a complex damage/repair simulation.

### Existing Systems

- Unity vehicle physics support is installed.
- FishNet network spawning/ownership and `NetworkTransform` patterns already exist.
- `ThirdPersonPlayerController` provides movement locking and `NetworkPlayerOwnership` handles owner-local controls/camera.
- `StartingArea` provides a natural depot hierarchy, though it needs an authored van spawn/recovery marker.

### New/Modified Systems

- Add a networked `DeliveryVan.prefab` with `Rigidbody`, four `WheelCollider`s (or an equally small tested Rigidbody controller), colliders, `NetworkObject`, server-authoritative `NetworkTransform`, and:

  - `VanController` — server physics and input application;
  - `VanNetworkInput` — current driver submits throttled steering/throttle/brake on unreliable RPC;
  - `VanOccupancy` — replicated driver/passenger object IDs and enter/exit validation;
  - `VanCargoBay` — load zone, cargo anchor, unload anchor;
  - `VanRecovery` — flip/stuck/out-of-bounds detection and reset request/cooldown.
- Add vehicle actions to `PlayerInputReader`: accelerate/brake/steer can reuse Move while seated; add exit, handbrake, and reset as appropriate without affecting on-foot bindings.
- The server owns the van object for the whole session. “Driver authority” means exclusive input permission, not transferring physics ownership to the client.

### Implementation Steps

1. Greybox the van at realistic Couch Guys scale and tune stable low-speed arcade handling before networking it. Cap speed and angular velocity; bias centre of mass low.
2. Add depot `VanSpawnPoint`, `VanRecoveryPoint`, and enough load-zone clearance to the starting-area prefab. Until final art exists, create explicit markers in the generated placeholder.
3. Spawn the van server-side when Stage 3 commits its unlock. If the saved van checkpoint is loaded, spawn it as soon as the Suburbs region is ready.
4. Implement server-validated entry: player alive, not carrying/reviving, seat free, within range, delivery/transition state allows it.
5. Assign the first entrant to driver and later entrants to passenger seats (up to party size). Lock on-foot movement/carrying while seated and attach the visual to a seat presentation anchor.
6. Driver sends bounded input samples with a sequence/timestamp. The server applies the latest valid sample and zeros input on timeout/disconnect/downing.
7. Adapt the local camera to follow a vehicle camera target while seated; return it to the player on exit.
8. Place exiting players at validated left/right exit points sampled against collision/ground; if blocked, try ordered fallbacks.
9. Do not implement van hit points, fuel, repairs, or part damage in Suburbs. Enemy bullets do not damage the van or occupants; thieves pressure players while they are outside loading/unloading.
10. Server collision with a thief above a minimum speed triggers a comic knock-aside/defeat presentation and delayed despawn with per-thief cooldown. The van cannot damage players.
11. Detect flipped state by up-vector angle plus low speed over time. Allow a hold-reset after a short delay when no normal recovery is possible.
12. Reset to the nearest valid recovery node behind current route progress, or depot as final fallback. Zero velocity, clear driver input, preserve seats only if safe, and preserve secured cargo.
13. Detect out-of-bounds via expanded generated world bounds and reset immediately after a warning/fade.
14. Add anti-spam reset cooldown and server audit logs. Reset is a recovery tool, not a faster travel option; never choose a node ahead of route progress.

### Multiplayer Considerations

- Server simulates Rigidbody/WheelCollider physics and validates driver input. Clients interpolate the server transform.
- Replicate seat IDs, lights/brake state if needed, and recovery state. Keep wheel visuals cosmetic/local.
- On driver disconnect/down: zero controls, safely eject/clear the driver seat, stop with braking, and allow another alive player to enter.
- Passenger transforms must derive from replicated seat membership, not independent owner movement.

### Edge Cases

- Van flips/sticks: manual or automatic recovery to a behind-route node with cargo retained.
- Van falls out of world: immediate server reset to depot/nearest safe node.
- Driver exits at speed: require low speed or perform a safe forced stop before exit.
- Two players enter the driver seat: first valid server request wins; the other may take a passenger seat.
- Van blocks the delivery point: completion still requires couch at the delivery point; parking guidance discourages this.

### Testing

- Physics matrix: flat, maximum grade, curb, tight turn, reverse, collision, flip, and out-of-bounds.
- Host/client latency and packet-loss tests for input timeout and interpolation.
- Driver disconnect/down, seat races, four passengers, blocked exits, and repeated recovery.
- Confirm Stage 4 route is substantially faster by van than on foot and feels stable rather than fragile.

### Dependencies

- Van-compatible roads/depot, interaction router, corrected health states, FishNet prefab registration, and camera handoff.

## Couch Loading, Securing, Transporting, and Unloading

### Goal

Integrate the existing cooperative couch with the van in one-button, low-friction cargo interactions while preserving manual handling before and after driving.

### Existing Systems

- `CouchCarryController` has reliable server grab/release, dynamic physics, carrier occupancy, teleport/reset support, and a server-authoritative network transform.
- `DeliveryManager` owns exactly one active delivery couch.

### New/Modified Systems

- Add replicated `CouchTransportMode { Free, SecuredInVan }` and secured van reference to `CouchCarryController` or a focused `CouchVanCargo` sibling.
- `VanCargoBay` exposes a generous trigger/load volume, cargo anchor, unload anchor, and blocked-unload probes.
- Add objective prompts and server RPCs for load/unload. No manual straps, joints, or inventory grid.

### Implementation Steps

1. Permit loading only for the active delivery couch, into the active team van, during a van-required stage, while the van is nearly stationary and the couch centre is inside/near the cargo load volume.
2. Require carriers to bring the couch to the rear/side load zone. Any carrier may press Interact to secure; the server releases all carry points atomically.
3. On secure, set the couch Rigidbody kinematic, disable collision that would fight the van, set transport mode/reference, and keep its server transform at the cargo anchor each physics tick. Let `NetworkTransform` replicate the result; do not rely on unsynchronised hierarchy parenting.
4. Prevent grabbing a secured couch and prevent driving before the load transition finishes.
5. Unload only below a speed threshold. Choose the first collision-free unload anchor, place the couch upright with zero velocity, restore collision/dynamic Rigidbody, clear secured state, and advance objective to manual carry.
6. If all unload anchors are obstructed, show “Move the van somewhere less ridiculous” and keep cargo secure.
7. Allow unload away from the intended parking area, but retain the map/waypoint and let players bear the extra carry distance. This supports improvisation without soft-locking.
8. During van recovery, move secured cargo with the van and keep it secured. During a party wipe, reset couch to the depot spawn and van to its depot/recovery marker, then clear cargo mode.
9. Add a watchdog: if secured state references a missing van, restore the couch at the last safe cargo/unload position; if the transform diverges beyond tolerance, snap server-side once and log.
10. Do not vary couch weight, load capacity, straps, or cargo damage in Suburbs.

### Multiplayer Considerations

- Server validates and performs load/unload atomically. All clients render from replicated transport state and transforms.
- Simultaneous load/unload requests are idempotent through mode and delivery attempt checks.
- Carriers disconnecting during load are already released by existing couch lifecycle; the load request revalidates occupancy/range.

### Edge Cases

- Couch falls out visually: authoritative secured mode wins and corrects it to the cargo anchor; if actually `Free`, use normal couch recovery.
- Van and couch become separated: secured couch follows recovery; free couch remains recoverable independently.
- Couch is upside down in load zone: securing normalises it at the cargo anchor.
- Van disappears: respawn/recover van, then restore cargo or place couch at a safe unload point.
- Couch leaves playable area while free: warning, then reset it to the last safe couch checkpoint (depot, unload point, or route recovery point).

### Testing

- Load from all sides/rotations, reject moving/remote/wrong couch, and unload with blocked anchors.
- Simultaneous requests from four clients and disconnect during transition.
- Flip/recover/out-of-bounds with cargo secured.
- Confirm carrying physics before/after cargo mode is unchanged.

### Dependencies

- Networked van, delivery objective steps, couch recovery checkpoints, and interaction prompts.

## Map, Waypoints, and Progress Feedback

### Goal

Keep every player oriented and make chapter progress legible without adding RPG-heavy menus.

### Existing Systems

- `NeighbourhoodMap` already displays roads, houses, depot, players/teammates, and current destination on an owner-only full map.
- `DeliveryManager` already creates a world-space collection marker and exposes currency changes.

### New/Modified Systems

- Extend the map with van, downed teammate, parking/cutoff, and biome transition markers.
- Add a compact HUD: current delivery title/number, one-line objective, van-fund/unlock milestone, team currency, downed/revive feedback, failure/completion banners.
- Add an optional screen-edge destination direction indicator so players need not repeatedly open the map.

### Implementation Steps

1. Refactor runtime marker creation into reusable local marker records while retaining the current owner-only map approach.
2. Add marker sources for depot, active destination, recommended parking point, active van, alive/downed teammates, and unlocked transition.
3. Show parking and front-door destination as distinct icons during van stages: drive to parking, then carry to door.
4. Bind HUD events to replicated chapter/delivery state, currency, van unlock, health, and revive progress.
5. Display `Delivery 4/7 — Load the couch`, then update to drive/unload/carry without exposing internal enum names.
6. Present the first three rewards as van-fund progress; on Stage 3 completion use a prominent `VAN UNLOCKED` moment.
7. On wipe, fade and failure message must cover reset latency; on completion, use a clearly distinct `SUBURBS COMPLETE` presentation.
8. Avoid drawing the full calculated road route initially. If navigation testing shows confusion, add a simple highlighted road-cell route using cached metrics.
9. Pool/update dynamic marker objects rather than finding all players every frame. The current 0.5-second refresh is acceptable as a starting point.

### Multiplayer Considerations

- UI is local presentation of replicated authoritative state.
- Never let a UI animation delay server progression/reset.
- Late joiners build current markers/objectives immediately and do not replay unlock animations already acknowledged in the session.

### Edge Cases

- Van/couch temporarily missing: hide marker and show recovery objective.
- Destination replaced on retry/fallback: update marker atomically with attempt ID.
- Map opened while entering van/downed: close or disable interaction cleanly.
- Marker outside current bounds during transition: clear old map before generating the next region.

### Testing

- One-to-four client marker identity/colour tests.
- Stage-by-stage HUD snapshot checklist, including every objective step.
- Late join, wipe, van recovery, and biome transition marker cleanup.
- Readability at supported resolutions/aspect ratios.

### Dependencies

- Replicated delivery snapshot, van occupancy/cargo state, health/revive events, and transition state.

## Final Suburbs Delivery

### Goal

Deliver a memorable 4–6 minute finale that composes learned mechanics into a sequence, rather than merely maximising every numeric difficulty value.

### Existing Systems

- Generated route metrics can identify long, elevated routes and hard manual approaches.
- Van, cargo, thieves, revive, map, and recovery systems from earlier phases supply all required verbs.

### New/Modified Systems

- Add a `FinalDeliveryTemplate` entry inside `SuburbsChapterDefinition` with ordered beats and trigger conditions, mapped to validated generated anchors rather than fixed world coordinates.
- Beats: depot load, long drive, steep route segment, road ambush, cutoff parking, unload, uphill/manual traversal, final ambush, delivery, celebration.

### Implementation Steps

1. Select a `FinalCandidate` whose road route is long, has meaningful ascent/turns, contains two separated encounter anchors, and ends at a 45–75 m manually reachable approach.
2. Seller introduces an absurd oversized-status couch narratively, but use the standard couch prefab/physics.
3. At accept, mark the drive route, recommended parking/cutoff, and destination; spawn the normal couch at depot.
4. Let the first 45–60 seconds be loading and a readable long drive, not immediate combat.
5. Trigger a small road ambush at a wide/low-risk section. Enemies pressure a stop or sloppy driving, but cannot damage the van or create an unavoidable blockade.
6. Lead players through the steepest validated road section and a tight but fair final turn.
7. Make the road beyond the cutoff visibly unsuitable/blocked for the van. Parking there creates a defensible unload moment.
8. Trigger the final wave only after the couch is unloaded or crosses the manual approach threshold. Cap it so at least one player can carry/revive while others draw pressure.
9. Use approach geometry with corners/elevation that demands couch coordination but does not require precision jumping.
10. Deliver to a ridiculous landmark/house presentation if authored; otherwise use the highest-scoring special-looking property and completion effects.
11. On delivery, immediately stop enemies, secure players from new damage, award once, and trigger celebration. Require return to seller only for the completion conversation/transition activation, not another dangerous trek; offer a party return prompt/short safe transport.
12. Record beat timing and failure location for balancing.

### Multiplayer Considerations

- The server advances each beat from authoritative triggers and attempt ID.
- A late joiner receives the current beat/objective and spawns at the safest relevant position: depot before departure, near an alive teammate/van during drive, or a validated approach catch-up point during manual carry.
- Encounter triggers fire once per attempt and reset on wipe.

### Edge Cases

- Players bypass cutoff with the van: physical/readable obstacle and route validation should prevent it; if they still do, allow creative parking but require manual couch delivery.
- Van reset during road ambush: restore behind progress with cargo; do not retrigger an already-cleared beat.
- Couch reset during final approach: reset to the last manual checkpoint, not all the way to depot, unless party wipe occurs.
- Final destination unreachable: generation validation should prevent this; runtime watchdog falls back to the best valid difficult destination and logs the seed.

### Testing

- Ten complete runs across varied seeds and party sizes, targeting 4–6 minutes.
- Intentional van reset, couch loss, wipe, late join, and disconnect at every beat.
- Confirm finale differs structurally from Stage 1 and does not become continuous gunfire.

### Dependencies

- All core delivery, procedural, van, couch cargo, health/revive, enemy, map, and recovery phases.

## Biome Completion, Unlocks, Saving, and Transition

### Goal

Make Suburbs completion permanent and celebratory, unlock exactly the next biome, and move the whole connected party into newly generated terrain through an explicit multiplayer interaction.

### Existing Systems

- `RegionDefinition` already provides stable region IDs and a configured available-region order.
- `IRegionUnlockProvider` already defines the generator-facing unlock check.
- `NeighbourhoodSeedSynchroniser` replicates region index/seed and each peer regenerates static geometry locally.
- `NeighbourhoodGenerator.RegionClearing/RegionGenerated` and `EnemySpawnManager` cleanup hooks exist.

### New/Modified Systems

- Add `BiomeProgressionService` implementing `IRegionUnlockProvider`, with a minimal ordered region list and replicated session unlocks.
- Add host-owned `ProgressSaveService` with versioned JSON stored under `Application.persistentDataPath` using write-temp/replace semantics.
- Add `BiomeTransitionController : NetworkBehaviour` and an interactable depot exit/road gate activated after the completion conversation.
- Extend `NeighbourhoodSeedSynchroniser` with one atomic server transition API carrying region index, seed, and generation revision; avoid reacting to half-updated region/seed pairs.
- Add per-client generation-ready acknowledgement for the new revision before fade-in/unlock controls.

### Implementation Steps

1. On final reward commit, set `SuburbsCompleted = true`, unlock the configured next region ID, set chapter completion state, and save once on the host.
2. Present completion reward/UI, clear enemies, and set the seller’s completion cue.
3. After the completion conversation, enable a visible transition point at the depot (for example, the outbound road gate/van departure marker).
4. Block transition while any delivery is active. Downed players are restored before readiness begins.
5. First interaction opens a shared ready check. Alive players opt in; host may cancel. Require all connected players or use a short clearly displayed host-start countdown after all are at the gate.
6. Server enters `Transitioning`, freezes gameplay input, secures/ejects seats as chosen, stops enemy systems, fades clients, and despawns delivery/enemy dynamic objects.
7. Decide van persistence through data. For the first implementation, carry the permanent `VanUnlocked` flag forward but respawn a biome-appropriate van only if the next region supports it; do not physically preserve the old Rigidbody through regeneration.
8. Clear the old generated region, set the atomic new region/seed/revision, and generate on every peer using existing deterministic generation.
9. Server waits for its own generation/NavMesh and each connected client’s ready acknowledgement, with timeout/disconnect handling.
10. Teleport existing player network objects to new `StartingArea.PlayerSpawnPoints`, restore health/life state, clear old map data, initialise new biome state, and fade in.
11. Late join during transition receives the current transition revision, remains input-locked, generates the target region, acknowledges ready, then spawns/teleports with the party.
12. Implement only the architecture needed for `Suburbs → next configured biome`; defer generic biome campaigns, cross-biome quest data, and unique future rules.

### Saving Policy

Save these host-profile fields at minimum:

- save schema version;
- completed region IDs;
- unlocked region IDs;
- permanent unlocks such as `VanUnlocked` if intended across biomes;
- Suburbs coarse checkpoint: `Start`, `VanUnlocked`, or `Completed`.

Do not save a live delivery, destination, couch/van transform, enemy state, health, dialogue panel, or party roster. If the host quits before the van checkpoint, restart Stage 1. If they quit after Stage 3, resume at Stage 4 with the van at depot. After completion, load with Suburbs complete and the next biome unlocked. The host save is canonical for the hosted session; per-Steam-player cloud progression and party merge rules are future work.

### Multiplayer Considerations

- Server/host owns completion, unlock decisions, save writes, transition target, seed, and revision.
- Clients cannot claim completion or alter save data. They receive session unlock state for UI/access.
- Treat disconnects during ready/generation as removal from the wait set. Treat new joins as additions that must acknowledge the active revision.
- Never regenerate the world while players retain live local control.

### Edge Cases

- Save missing/corrupt/newer version: back it up, use safe defaults, and do not block play.
- Save write failure: keep session progression, show/log a non-fatal warning, and retry at safe milestones.
- Player attempts transition during delivery: reject with current objective.
- Transition begins while someone is downed: restore/eject everyone before freeze.
- Next biome asset is incomplete: leave the gate locked with a clear development error; in production builds only expose configured, validated targets.
- Generation timeout: remain faded, remove disconnected clients, and return the party safely to Suburbs/depot if the server cannot generate the target.

### Testing

- Save round-trip, schema migration/defaults, corrupt file, and write-failure tests.
- Complete Suburbs, quit/reload at all three checkpoints, and verify exact resume behaviour.
- Two-to-four client ready check, cancellation, disconnect, and late join during transition.
- Repeated Suburbs/next-region generation verifies old NavMesh, enemies, markers, and dynamic objects are removed.

### Dependencies

- Final delivery completion, UI/fade, region validation, player reset/teleport, and `NeighbourhoodSeedSynchroniser` changes.

## Consolidated Multiplayer Authority Matrix

| System/action | Authority | Client responsibility |
| --- | --- | --- |
| Chapter stage/delivery state/reward | Server `SuburbsChapterManager`/`DeliveryManager` | Display replicated snapshot; request allowed actions. |
| NPC interaction/accept | Server validates distance/state and commits first request | Select prompt, show dialogue, send intent. |
| Destination selection | Server queries cached generated metrics and syncs index | Render waypoint/route from selected index. |
| Couch spawn/despawn/completion | Server | Request grab/load; render/interpolate. |
| Couch carrying/physics | Existing server `CouchCarryController` | Owners send throttled movement intent. |
| Couch loading/unloading | Server atomic transition | Send interact intent and show prompt. |
| Van spawn/physics/recovery | Server | Driver sends bounded unreliable input; all clients interpolate. |
| Van seats | Server assigns replicated slots | Request enter/exit; local camera/presentation. |
| Enemy spawn/navigation/combat | Server | Render network motion/tracers/UI. |
| Player movement | Existing owner-authoritative transform, locally gated by replicated state | Owner simulates on foot; server still validates interactions/life state. |
| Health/down/revive | Server | Show state; reviver sends begin/hold/cancel intent. |
| Party wipe/reset | Server | Fade/UI and accept authoritative teleports. |
| Currency/progression/unlocks | Server; host persists | Display only. |
| Static region generation | Host chooses atomic region/seed/revision; every peer deterministically generates | Generate and acknowledge readiness. |
| Biome transition | Server coordinates | Ready response, fade, generation acknowledgement. |

## Consolidated Recovery and Edge-Case Policy

| Case | Required simple behaviour |
| --- | --- |
| Player disconnects while carrying | Existing player stop releases its carry point; remaining carriers continue. If no carriers remain, couch stays physical and recoverable. |
| Player disconnects while driving | Zero vehicle input, clear driver seat, apply brakes, allow takeover. Cargo stays secured. |
| Driver is downed | Same as disconnect, then eject/place at safe side point for revive. |
| Player joins mid-delivery | Spawn at a safe depot/catch-up point, receive current stage/objective, and do not replay rewards. |
| Player joins after van unlock | Van state/marker/seat availability replicate normally; no unlock replay required. |
| Couch stuck | Hold recovery after stationary/unreachable timeout; reset to last safe couch checkpoint behind progress. |
| Couch outside playable area | Server bounds watchdog resets it; warning/fade hides snap. |
| Van stuck/flipped | Timed hold reset to nearest behind-route recovery node; preserve secured cargo. |
| Van outside playable area | Immediate authoritative recovery with velocity cleared. |
| Van/couch separated | If secured, watchdog rejoins them; if free, recover independently. |
| Couch falls out of van | Secured state corrects to cargo anchor. A free couch uses normal recovery. |
| Destination unreachable | Pre-generation validation excludes it; runtime fallback replaces it within the same stage/attempt without reward. |
| Enemy cannot reach player | Repath, retarget, hidden safe warp, then despawn after timeout. |
| Everyone dies/is downed | One server wipe transition; cleanup, depot reset, same stage retry, no progression loss. |
| Multiple NPC/start interactions | First valid server state transition wins; all duplicates see current shared state. |
| Biome change during delivery | Rejected. Finish or wipe/retry the delivery first. |
| Transition while player is downed | Restore all players before transition freeze/readiness. |
| Late join during transition | Lock input, apply target revision, generate/acknowledge, then place with party. |
| Host disconnects | Existing session-ending behaviour remains; no host migration scope. |

## Performance and Configuration Targets

- Perform route graph/scoring, vehicle validation, encounter-anchor selection, and NavMesh construction once per generated region—not per frame.
- Cache component/object references; remove new gameplay dependence on repeated `FindObjectsByType` polling where registries/events already exist.
- Pool or cap frequently created visuals such as bullet tracers, map markers, prompts, and completion effects if profiling shows allocation spikes.
- Hard-cap active thieves per stage and scale carefully by party size.
- Keep van input unreliable/throttled, revive progress compact, and state changes reliable/rare.
- Put tuning values in `SuburbsChapterDefinition`, encounter profiles, and region/vehicle profiles. Avoid scattered magic numbers.
- Add a development overlay/export for stage, route metrics, encounter count, elapsed time, recovery count, and network state.

## Actionable Development Order

Each phase should end in a playable, testable increment. A future request to “Implement the next phase from `SUBURBS_GAMEPLAY_PLAN.md`” should take the first incomplete phase below, honour its exit criteria, and update a checklist/implementation notes section in the same change.

### Phase 1 — Suburbs Progression Foundation

**Implementation status: Complete (2026-10-07).** The seven-stage definition,
server-authoritative chapter state, replicated delivery/objective state machine,
stage rewards, van-unlock milestone flag, and chapter-completion flag are implemented.
As an intentionally temporary bridge to Phase 4, player death currently releases the
couch, waits five seconds on the server, restores health, and teleports the same network
player object to its assigned depot spawn without reloading the scene or resetting the
active delivery.

1. Add `SuburbsChapterDefinition`, seven configured stages, stable IDs, and `SuburbsChapterManager`.
2. Expand `DeliveryManager` into the guarded state machine and replicated delivery snapshot.
3. Separate reward commit, stage advancement, retry, and completion events.
4. Wire components into `ProceduralGameplaySceneBuilder` and FishNet network behaviour ordering.
5. Add state-machine EditMode tests.

Exit criteria: server can advance/retry seven debug deliveries exactly once each, and late clients reconstruct the current state.

### Phase 2 — Procedural Difficulty

**Implementation status: Complete (2026-10-07).** Generated properties now retain
their exact connected road coordinate. Every destination caches deterministic route
metrics from the depot (road distance, direct distance, uphill gain, maximum grade,
turns, intersections, final carry distance/elevation, score, and difficulty zone).
`DeliveryManager` uses stage-specific envelopes with unused-destination preference
and deterministic relaxed fallbacks. Stage 1 explicitly ranks short, low-ascent,
low-grade routes first. The generator exposes read-only road height/connection lookup,
validates early-stage pools, draws zone-coloured gizmos, and reports all seven strict
stage pool sizes in its custom inspector. The inspector also provides an opt-in
100-seed validator which reports analysis failures, progression failures, and minimum
strict pool sizes before restoring the original seed.

**Progressive terrain extension (2026-10-07).** The Suburbs region now uses a dedicated
progressive road strategy: a dense flat depot/easy grid transitions into sparser,
less-symmetrical medium, hilly, and outer streets around a guaranteed vehicle-safe
primary route. Elevation is a continuous seeded field with an upward progression trend,
multi-scale hills and valleys, a flat depot radius, smooth road-gradient repair, tilted
ground/road surfaces, and separate primary/residential slope limits. Roads, properties,
and destinations carry `SuburbZone` metadata; final-stage selection requires an Outer
candidate. Validation now rejects disconnected roads, excessive gradients, missing zone
pools, insufficient outer elevation, missing late-route descents, and unreachable houses,
with deterministic bounded seed retries. Zone/primary/elevation gizmos and destination
Inspector diagnostics make the full progression directly inspectable.

**Continuous terrain extension (2026-10-07).** Per-block ground planes have been
replaced in generation by one shared-vertex `GeneratedTerrainMesh`. Its height sampler
starts with the seeded progressive elevation field, blends level pads beneath the depot
and generated properties, then cuts vehicle-width road beds with smooth shoulders along
the resolved road graph. Modular road pieces remain slightly raised visual overlays, so
existing road art can still be used without cracks exposing empty space. The mesh owns a
single collider for player/vehicle physics and NavMesh collection. Its vertex spacing,
border, road width, shoulder width, pad blends, material, texture scale, and road offset
are configured per region through `GeneratedTerrainSettings`; generation validation now
rejects missing terrain meshes and the inspector reports vertex/triangle counts.

1. Expose road graph/height data safely.
2. Compute and cache `DeliveryRouteMetrics`, score, and zones.
3. Implement stage-aware destination queries, used-destination exclusion, validation, and deterministic fallback/reroll.
4. Add gizmos/editor diagnostics and 100-seed validation tests.

Exit criteria: every accepted Suburbs seed supplies the required destination pools and Stage 1/7 metrics are observably different.

### Phase 3 — Basic Full Gameplay Loop

1. Add `PlayerInteractionController` and remove general Interact polling from `PlayerCouchCarrier`.
2. Implement seller dialogue data/presenter and explicit accept flow.
3. Complete NPC → couch → carry → destination → reward → return → next delivery for Stages 1–3.
4. Add objective/reward telemetry and remove release-build teleport shortcut.

Exit criteria: a multiplayer party can play the first three stages in order with clear prompts and no duplicate starts/rewards.

### Phase 4 — Death & Revive

1. Correct `PlayerHealth` life-state semantics.
2. Implement held revive, cancellation, health restoration, downed presentation, and interaction priority.
3. Implement party registry/wipe detection, enemy pause hook, depot reset, and same-stage retry.
4. Test delivery/wipe races and disconnects.

Exit criteria: players can revive each other, and a full wipe reliably restores the same delivery without losing progress.

### Phase 5 — Thief Integration

1. Add stage encounter profiles and public `EnemySpawnManager` control/cleanup APIs.
2. Generate/validate encounter anchors and safe zones.
3. Add retarget/unreachable recovery and stage-specific groups for Stages 3–7.
4. Tune enemies as short disruptions around carry/load/unload beats.

Exit criteria: enemy intensity follows stages, never spawns in tutorial/depot safety, and never stalls an attempt.

### Phase 6 — Van

1. Greybox and tune the van locally.
2. Implement server physics/input, seats, camera handoff, enter/exit, unlock spawn, and FishNet registration.
3. Implement flip/stuck/out-of-bounds recovery and driver disconnect/down handling.
4. Add van map marker and contextual prompts.

Exit criteria: four clients can share and recover the van; only the current driver controls it and Stage 4 travel is clearly faster.

### Phase 7 — Couch + Van

1. Add cargo load zone/anchors and replicated couch transport mode.
2. Implement atomic secure/unsecure, collision/physics switching, blocked unload checks, and objective steps.
3. Integrate cargo with van reset, couch reset, wipe, and disconnect handling.
4. Verify existing free-carry behaviour is unchanged.

Exit criteria: `Carry → Load → Drive → Unload → Carry → Deliver` works repeatedly for one to four players under latency.

### Phase 8 — Van-Aware Procedural Generation

1. Author Suburbs road/depot greybox prefabs and vehicle metadata.
2. Add route width/grade/turn/parking validation and recovery nodes.
3. Add distinct parking points and manual final-approach metrics.
4. Reserve vehicle corridors against props/hazards and run seed sweeps.

Exit criteria: every stage-required route is driveable, later routes remain challenging, and every destination retains a manual carry finish.

### Phase 9 — Progression Balancing

1. Tune Stage 1–3 distances and van-fund messaging to 7–10 minutes.
2. Make Stage 4 a deliberate low-pressure power spike.
3. Tune Stages 5–6 by route shape, parking, final carry, and encounter composition—not only distance.
4. Record stage times across party sizes/seeds and adjust data, not couch weight.

Exit criteria: median full progression is on track for 20–30 minutes and players report the van as a meaningful upgrade.

### Phase 10 — UI, Map & Feedback

1. Add objective, delivery count, currency/van fund, downed/revive, failure, unlock, and completion UI.
2. Extend `NeighbourhoodMap` with van, parking/cutoff, downed teammate, and transition markers.
3. Add destination direction feedback and polish seller prompts/dialogue.
4. Validate late-join reconstruction and screen resolutions.

Exit criteria: players can always state the current objective, destination, van location, teammate status, and chapter progress.

### Phase 11 — Final Delivery

1. Add final template/beat state and generated anchor mapping.
2. Build the long drive → road ambush → steep route → cutoff → unload → manual climb → final wave sequence.
3. Add celebration, safe return, instrumentation, and all beat recovery paths.
4. Tune to 4–6 minutes across party sizes.

Exit criteria: the finale is structurally distinct, memorable, recoverable, and reliably completes once.

### Phase 12 — Biome Completion

1. Add `BiomeProgressionService`/`IRegionUnlockProvider` implementation.
2. Commit `SuburbsCompleted`, next-biome unlock, reward, UI, and seller completion state.
3. Activate the depot transition point only after completion dialogue.

Exit criteria: final delivery visibly completes the area and unlocks exactly one configured next biome.

### Phase 13 — Biome Transition

1. Add ready check, input freeze/fades, cleanup, and `BiomeTransitionController`.
2. Make region/seed/revision switching atomic in `NeighbourhoodSeedSynchroniser`.
3. Add client generation acknowledgement, existing-player repositioning, late join, disconnect, timeout, and rollback handling.

Exit criteria: the whole party transitions to a newly generated region without reconnecting or retaining stale Suburbs state.

### Phase 14 — Saving

1. Implement versioned host-profile JSON and atomic writes.
2. Save/load completed/unlocked regions, van permanence, and the three Suburbs checkpoints.
3. Test missing/corrupt/newer files and reload at every checkpoint.

Exit criteria: restarts resume at the intended safe checkpoint and never restore a half-active delivery.

### Phase 15 — Polish & Balancing

1. Run repeated one-to-four-player, multi-seed full chapter playthroughs.
2. Fix soft locks, confusing prompts, unfair spawn lines, network races, and recovery exploits before adding content.
3. Profile generation, NavMesh, server physics, enemy AI, UI allocation, and bandwidth.
4. Tune stage durations to 22–28 minutes median, with 20–30 minutes as the acceptable first-run range.
5. Replace remaining essential placeholders (depot, roads, parking/cutoff readability, final destination) while preserving metadata/colliders.
6. Complete a release checklist with debug shortcuts disabled and save/transition failure paths verified.

Exit criteria: three consecutive full multiplayer runs on different valid seeds finish without manual developer intervention, progression loss, or unrecoverable couch/van/player states.

## Explicit Non-Goals for the Suburbs Milestone

- Different couch weights, couch rarity, couch stats, or couch upgrades.
- Character levels, skill trees, equipment, inventory, or an RPG economy.
- Branching dialogue trees, quests unrelated to delivery, or cinematic conversation tools.
- Vehicle fuel, repairs, deformable damage, tyre simulation, or a fleet/garage system.
- Player weapons or a combat progression loop.
- Host migration, competitive anti-cheat, or cross-host progression merging.
- Fully generic future-biome rule authoring. Only the seam required to unlock and enter the next biome is included.
- Replacing FishNet, current couch physics, deterministic local world generation, or working player movement.

## Final Acceptance Checklist

- [ ] Seven stages follow the documented timing and mechanic curve.
- [ ] Stages 1–3 are fully playable without a vehicle and teach cooperative carrying.
- [ ] The van unlock is guaranteed after Stage 3 and feels like a major power spike in Stage 4.
- [ ] Stages 5–7 challenge driving, parking, unloading, manual carry, enemies, and revives without changing couch weight.
- [ ] Generated seeds provide validated difficulty zones and van-compatible routes.
- [ ] The map shows players, depot, destination, van, parking/cutoff, downed teammates, and transition as applicable.
- [ ] Down/revive and party-wipe retry work for one to four players.
- [ ] Every couch/van/player/enemy disconnect, stuck, flip, out-of-bounds, and unreachable case has a tested recovery.
- [ ] The final delivery takes roughly 4–6 minutes and combines all learned systems.
- [ ] `SuburbsCompleted` and the next biome unlock are authoritative and saved.
- [ ] The party can intentionally transition to the next generated terrain in the same multiplayer session.
- [ ] A first-time complete run takes approximately 20–30 minutes.
