# Couch Carrying Prototype Implementation

## Architecture

The prototype extends the existing FishNet and Steamworks.NET multiplayer implementation. It does not introduce another networking layer.

- `PlayerCouchCarrier` is a `NetworkBehaviour` on each Player. Only the owning client reads Interact, selects a nearby point, and sends grab/release requests. While carrying, it also sends throttled world-space movement intent to the server on FishNet's unreliable channel.
- `CouchCarryController` is a `NetworkBehaviour` on the Couch. It owns four independently synchronised occupancy slots and applies all carrying forces during the server's `FixedUpdate`.
- `CouchCarryPoint` identifies one attachment transform and its stable point index. Occupancy remains centralised on the couch.
- `DebugCouchBotController` optionally drives one unowned network Player on the server. It approaches an available point, uses the normal validated grab path, and supplies movement intent through `PlayerCouchCarrier`; it does not contain alternate couch physics.
- `DebugCouchBotSettings` is the serialised debug configuration shown on `PlayerCouchCarrier` under **Debug Bot**.
- FishNet `NetworkTransform` synchronises the server-authoritative couch position and rotation. The existing Player remains owner-authoritative.
- `ThirdPersonPlayerController` exposes a general external movement-speed multiplier. Carrying uses it without moving input or networking responsibilities into the controller.

The authoritative simulation never parents or teleports the couch. The couch remains a dynamic Rigidbody on the server and a synchronised, kinematic replica on remote clients.

## Couch Prefab

Prefab: `Assets/_Project/Prefabs/Couch.prefab`

Hierarchy:

```text
Couch
├── Visual
│   ├── Base
│   ├── Back
│   ├── LeftArm
│   └── RightArm
└── CarryPoints
    ├── CarryPointFrontLeft
    ├── CarryPointFrontRight
    ├── CarryPointRearLeft
    └── CarryPointRearRight
```

The root scale is `(1, 1, 1)`. The four visual pieces use cube meshes and have no individual colliders. A root `BoxCollider` covers the approximately `2.1 m × 0.9 m × 0.9 m` temporary couch.

Important Rigidbody and weight settings:

| Setting | Value |
| --- | ---: |
| Mass | 35 kg |
| Linear damping | 0.4 |
| Angular damping | 1.5 |
| Interpolation | Interpolate |
| Collision detection | Continuous Dynamic |
| Use gravity | Yes |
| Is kinematic on server | No |
| Maximum angular velocity | 7 rad/s |

`CouchCarryController.m_weight` is the authoritative gameplay weight and applies its value directly to `Rigidbody.mass` during initialisation and Inspector validation. Changing Weight therefore changes real acceleration, inertia, gravity load, lift requirements, rotation response, and the carrying movement penalty without creating an unrelated second mass value.

Network components are `NetworkObject`, server-authoritative `NetworkTransform` configured for a Rigidbody, and `CouchCarryController`. The couch is also registered after the Player in `NetworkSpawnablePrefabs.asset`, so it can be spawned by the server as well as used as a FishNet scene object.

The prefab is not automatically placed into an existing scene. Add `Couch.prefab` to the test scene and position its root at floor height. FishNet processes the instance as a scene network object.

## Carry Points

The four point indices are stable:

| Index | Name | Local position |
| ---: | --- | --- |
| 0 | FrontLeft | `(-0.85, 0.35, -0.58)` |
| 1 | FrontRight | `(0.85, 0.35, -0.58)` |
| 2 | RearLeft | `(-0.85, 0.35, 0.58)` |
| 3 | RearRight | `(0.85, 0.35, 0.58)` |

`CouchCarryController` has one `SyncVar<int>` per point. Its value is the occupying Player's FishNet object ID, or `-1` when free. The server also keeps direct `PlayerCouchCarrier` references for physics. Because every slot is independent, up to four different Players can influence the same couch and one Player releasing does not affect the others.

Carry points draw green wire spheres when available and red wire spheres when occupied. Selecting the Couch also draws point markers and lines from the root. Optional force debugging is described below.

## Interaction

`PlayerInputReader` exposes `InteractPressedThisFrame`. `PlayerCouchCarrier.Update` immediately returns for non-owners, so remote Player objects never process local interaction input.

On Interact, an owner who is not carrying searches active `CouchCarryPoint` components, filters out unavailable or unspawned points, and submits the closest point within `1.8 m`. The server validates:

- the supplied `NetworkObject` exists and is spawned;
- it has a `CouchCarryController`;
- the point index resolves to a configured point;
- the point is unoccupied;
- the requesting Player is within `1.8 m + 0.35 m` server tolerance;
- the Player is not already carrying a couch or occupying another point on that couch.

If carrying, Interact sends a release request instead. The server also releases on Player despawn or if Player-to-point separation exceeds `3 m`.

## Physics

### Couch Weight

Weight defaults to `35 kg` and is assigned directly to the Rigidbody mass. Unity therefore supplies the fundamental mass effects: the same force produces less acceleration on a heavier couch, angular response follows the Rigidbody inertia tensor, gravity produces a larger load, and collisions retain physically meaningful momentum. The lift calculation uses `weight × gravity`, so a heavier couch also needs more support.

### Per-Player Horizontal Forces

Every occupied point independently calculates a desired grip position from that Player, the current couch centre, Carry Distance, and Carry Height. Horizontal force contains three terms:

```text
horizontal force = horizontal grip error × Carry Force
                 - horizontal point velocity × Carry Damping
                 + player world movement intent × Movement Force
```

The force is scaled by player-count efficiency, cooperation, and separation influence before being clamped. Each Player retains a distinct direction; the system never averages their directions into one generic couch direction.

The local Player sends movement intent at most every `0.1 s`, with a `0.5 s` heartbeat. The server discards intent older than `0.4 s`. This gives the authoritative simulation responsive intent without adding reliable per-frame traffic.

### Vertical Lifting

Each occupied point receives its own upward force at the exact CarryPoint:

```text
lift = share of couch weight support
     + positive grip-height error × Lift Force
     - vertical point velocity × Lift Damping
```

Lift is never allowed to become a downward carrier force and is clamped per point. Gravity remains active. A single corner receives partial weight support and therefore lifts and tilts around the couch portions still contacting the floor. Two points support their own locations independently; opposite corners tend towards a more level couch, while two front points naturally leave the rear lower. No code assigns or levels couch rotation.

### Player-Count Scaling

Total carrier efficiency uses diminishing returns:

```text
total efficiency = Single Carrier Efficiency
                 + Additional Carrier Efficiency × (carrier count - 1)^0.75
```

The result is capped and divided among the active points. Defaults produce approximately `0.45`, `0.80`, `1.04`, and `1.25` total efficiency for one through four carriers. This makes two cooperating Players substantially more capable than one without allowing four Players to generate four times the acceleration. Vertical static support follows the same curve but caps at `90%` of couch weight, so gravity always wins unless positive grip-height error contributes spring lift; the couch cannot passively float.

### Cooperation Calculation

The server compares every carrier pair. Moving pairs use the dot product of their normalised world-space movement intentions:

- same direction: dot `1`, agreement `1`;
- perpendicular: dot `0`, agreement `0.5`;
- opposite: dot `-1`, agreement `0`;
- either Player stationary: neutral agreement `0.5`.

Pair agreements are averaged into `CurrentCooperationEfficiency`. It scales carrying assistance between Minimum Conflict Efficiency and full strength. Opposing forces are still independently applied at their own points, so they primarily cancel translation while retaining enough force to rotate or twist the couch.

### Rotation and Torque

Horizontal forces use `Rigidbody.AddForceAtPosition` at a point blended towards the CarryPoint by Rotation Influence. Vertical forces are always applied at the exact CarryPoint. Off-centre forces therefore create real torque, and players can rotate the couch by moving different ends in suitable directions. The Transform rotation is never assigned by gameplay code.

### Carrying Movement Restrictions

Player speed changes continuously with couch weight, diminishing player-count assistance, and cooperation. The configured Single Carrier Speed Multiplier is the slow baseline, while Maximum Cooperative Speed Multiplier is approached only when additional carriers cooperate. Effective weight per unit of carrier efficiency adds a further penalty.

Separation begins weakening both couch influence and Player speed at Soft Carry Separation. Influence fades to zero at Maximum Carry Separation, where the server releases that individual point. This avoids a hard movement lock while preventing a Player from sprinting away and dragging the couch through an extreme spring.

### Stability

Horizontal and vertical forces have separate limits. Lift damping removes upward oscillation, Rigidbody damping remains active, linear velocity is capped at `7 m/s`, and Rigidbody maximum angular velocity is `7 rad/s`. These limits prevent launches and unbounded spinning while preserving collisions, tilting, torque, and deliberate physical awkwardness.

## Inspector Tuning

| Group | Setting | Default |
| --- | --- | ---: |
| Weight | Weight | 35 kg |
| Horizontal Carrying | Carry Force | 500 N/m |
| Horizontal Carrying | Movement Force | 320 N |
| Horizontal Carrying | Damping | 65 |
| Horizontal Carrying | Maximum Horizontal Force | 700 N per point |
| Horizontal Carrying | Rotation Influence | 0.85 |
| Vertical Support | Lift Force | 450 N/m |
| Vertical Support | Lift Damping | 55 |
| Vertical Support | Maximum Lift Force | 350 N per point |
| Carrier Scaling | Single Carrier Efficiency | 0.45 |
| Carrier Scaling | Additional Carrier Efficiency | 0.35 |
| Carrier Scaling | Maximum Carrier Efficiency | 1.25 |
| Carrier Scaling | Minimum Conflict Efficiency | 0.45 |
| Stability | Maximum Linear Velocity | 7 m/s |
| Stability | Maximum Angular Velocity | 7 rad/s |
| Player Carrying | Carry Distance | 0.45 m |
| Player Carrying | Carry Height | 0.45 m |
| Player Carrying | Single Carrier Speed Multiplier | 0.52 |
| Player Carrying | Maximum Cooperative Speed Multiplier | 0.82 |
| Player Carrying | Soft Carry Separation | 1.6 m |
| Player Carrying | Maximum Carry Separation | 3 m |
| Networked Intent | Send Interval | 0.1 s |
| Networked Intent | Heartbeat Interval | 0.5 s |
| Networked Intent | Timeout | 0.4 s |

## Debug Visualisation

Enable **Show Force Gizmos** on `CouchCarryController` and select the Couch while running as host/server:

- green/red spheres show free/occupied CarryPoints;
- red rays show horizontal spring and movement force;
- cyan rays show vertical lift force;
- yellow rays show each Player's desired movement direction.

`ActiveCarrierCount`, `CurrentCooperationEfficiency`, and `CurrentVelocity` are public runtime diagnostic properties. Debug Force Scale controls only Gizmo length and does not affect physics.

## Multiplayer

### Authority Model

The FishNet server/host is the only authoritative couch physics peer. `CouchCarryController` ignores `FixedUpdate` unless `IsServerInitialized` is true. FishNet's Rigidbody-configured `NetworkTransform` makes the couch Rigidbody kinematic on non-server clients and sends authoritative position and rotation to observers.

The couch never transfers ownership to a carrier. This is important: several Players can occupy separate points without fighting over NetworkObject ownership.

### Grab Request Flow

1. The local owner finds the nearest locally available point.
2. `PlayerCouchCarrier.RequestGrabServerRpc` sends the Couch `NetworkObject` and point index.
3. The server performs object, point, occupancy, range, and current-carry validation.
4. On acceptance, the couch records the Player in that point's occupancy SyncVar.
5. The Player records the accepted Couch and point in its own SyncVars.
6. All observers receive the resulting state through FishNet SyncVars.

### Release Flow

The owning client sends `RequestReleaseServerRpc`. The server clears only the matching point and that Player's carried state. Other point occupants remain unchanged. Server-side automatic release uses the same path for separation or disconnect cleanup.

### Couch Transform Synchronisation

The Couch `NetworkTransform` synchronises position and rotation from the server. Scale is not synchronised. No custom per-frame transform RPCs are sent.

### Movement Intent Flow

`ThirdPersonPlayerController` exposes its already-calculated camera-relative world movement intent. Only the owning `PlayerCouchCarrier` submits it, at a maximum rate of 10 messages per second and only while carrying. The RPC uses FishNet's unreliable channel; a heartbeat covers unchanged held input and a server timeout safely returns stale intent to zero. Intent is consumed only by the authoritative couch simulation and is not redundantly broadcast to observers.

The server synchronises the latest cooperation efficiency as one low-frequency SyncVar so each owner can apply the same weight/count/cooperation movement restriction. Occupancy remains represented by the existing four server-written SyncVars.

### Multiple Player Influence

The server loops over all four occupied points each physics tick and adds separately calculated horizontal and vertical forces at each point. It calculates pairwise movement agreement once per physics tick and applies diminishing count scaling. No carrier is designated leader and couch ownership is not assigned to any client.

## Networked Debug Bot

The Player prefab exposes **Debug Bot > Spawn Bot**. When this option is enabled, only the host-owned Player creates one bot during `OnStartServer`. The bot is another registered Player prefab instance spawned through FishNet with no owning connection. This makes FishNet's existing Player `NetworkTransform` server-controlled for that instance, so every client observes the same bot movement while no client can send input for it.

`DebugCouchBotController` is disabled on the prefab. The host initialises and enables only the server instance before its network spawn; client copies remain disabled. The bot searches for a target at the configured interval rather than every frame. It prioritises a couch that already has a carrier, then distance and its preferred point. It walks directly towards the outside of the selected CarryPoint using the Player's `CharacterController`, and its grab is accepted only if the normal server distance, spawn, availability, and current-carry checks pass.

While carrying, the bot writes its normalised movement intent directly into its server-side `PlayerCouchCarrier`. No bot intent RPC is required because the bot and authoritative couch simulation already run on the same peer. Couch occupancy SyncVars and the existing server-authoritative couch `NetworkTransform` continue to synchronise grab state and physics to all clients.

The available singular behaviours are:

| Behaviour | Intent |
| --- | --- |
| Cooperate With Player | Copies the first human carrier's server movement intent. |
| Pull Against Player | Uses the opposite of the first human carrier's intent. |
| Hold Position | Stops moving and acts as a physical anchor through the existing grip spring. |
| Rotate Clockwise | Walks tangentially around the couch in the clockwise direction. |
| Rotate Anticlockwise | Walks tangentially around the couch in the anticlockwise direction. |
| Wander | Chooses a random horizontal direction at the configured interval. |
| Move In Configured Direction | Uses the configured world-space direction. |

The bot still receives the normal couch weight, player-count, cooperation, separation, force, and automatic-release rules. Despawning the host Player also despawns its bot, and the bot's normal server shutdown releases only its own CarryPoint.

Debug Bot Inspector settings:

| Setting | Default | Purpose |
| --- | ---: | --- |
| Spawn Bot | Off | Enables one host-spawned network bot. |
| Behaviour | Cooperate With Player | Selects the bot's one movement state. |
| Preferred Carry Point | Any | Biases selection towards one of the four point indices. |
| Spawn Offset | `(2, 0, 2)` | Bot spawn offset in host Player-local space. |
| Approach Speed | 3.5 m/s | Speed before grabbing. |
| Carrying Speed | 3 m/s | Base movement speed while carrying; normal couch speed restrictions still apply. |
| Point Stand Off | 0.55 m | Target distance outside the CarryPoint. |
| Turn Speed | 540 degrees/s | Server-side Player turning speed. |
| Configured Direction | World forward | Direction used by Move In Configured Direction. |
| Target Search Interval | 0.75 s | Delay between scene point searches. |
| Retry Delay | 0.75 s | Delay after a failed grab attempt. |
| Wander Direction Interval | 2 s | Time between random Wander directions. |

### Debug Bot Test

1. Select `Assets/_Project/Prefabs/Player.prefab` and enable **Player Couch Carrier > Debug Bot > Spawn Bot**.
2. Choose **Cooperate With Player** or **Pull Against Player**.
3. Ensure a Couch prefab instance is in the scene, then start **Host Game**.
4. Confirm exactly one extra Player spawns, walks to an available CarryPoint, and grabs it.
5. Grab a different point with E and move. Cooperate should match the Player's direction; Pull Against should reverse it.
6. Change Behaviour at runtime on the spawned host Player to exercise Hold, turning, wander, or configured-direction states.
7. Confirm couch physics and both Player transforms are visible to a connected client when one is available.
8. Disable **Spawn Bot** on the Player prefab after testing when ordinary multiplayer behaviour is desired.

## Input

The existing `Player` action map now contains:

| Action | Type | Default binding |
| --- | --- | --- |
| Interact | Button | E |

Like all existing Player actions, each spawned Player gets a private runtime clone of the action asset. Existing Move, Look, Jump, and Sprint bindings are unchanged.

## Files Created / Modified

Created:

- `Assets/_Project/Prefabs/Couch.prefab`
- `Assets/_Project/Scripts/Gameplay/Couch/CouchCarryController.cs`
- `Assets/_Project/Scripts/Gameplay/Couch/CouchCarryPoint.cs`
- `Assets/_Project/Scripts/Player/PlayerCouchCarrier.cs`
- `Assets/_Project/Scripts/Player/DebugCouchBotController.cs`
- `Assets/_Project/Scripts/Player/DebugCouchBotSettings.cs`
- `Assets/_Project/Scripts/Editor/CouchPrototypeBuilder.cs`
- `Assets/_Project/COUCH_CARRYING_IMPLEMENTATION.md`
- matching Unity `.meta` files and Gameplay/Couch folder metadata

Modified:

- `Assets/_Project/Input/PlayerControls.inputactions`
- `Assets/_Project/Scripts/Input/PlayerInputReader.cs`
- `Assets/_Project/Scripts/Player/ThirdPersonPlayerController.cs`
- `Assets/_Project/Prefabs/Player.prefab`
- `Assets/_Project/Settings/NetworkSpawnablePrefabs.asset`
- `Assets/_Project/Scripts/Editor/PlayerPrefabBuilder.cs`
- `Assets/_Project/Scripts/Editor/SteamMultiplayerPrefabBuilder.cs`

The repeatable builders preserve the Player carrier component, both Player and Couch spawnable registrations, and the Couch prefab if generated again from the **Couch Guys** menu.

## Testing

Before either test, add `Assets/_Project/Prefabs/Couch.prefab` to a networked test scene on level ground. Do not instantiate a separate local-only copy at runtime. If generated assets need rebuilding, run **Couch Guys > Build Couch Prototype**.

### One Carrier

1. Start a host session.
2. Grab one corner and remain still briefly.
3. Confirm the grabbed side lifts while the opposite side remains lower and the Rigidbody naturally tilts.
4. Move and confirm the couch is heavy, slow, awkward, but still movable.
5. Move beyond 1.6 m separation and confirm influence and Player speed progressively weaken.
6. Move beyond 3 m and confirm automatic release, then grab again.
7. Press E and confirm the couch releases and continues as an ordinary Rigidbody.

### Two Cooperating Carriers

1. Start the same build on two machines with two Steam accounts and connect both users to one lobby.
2. Grab opposite CarryPoints and move in the same direction.
3. Confirm both clients see matching couch position, tilt, and rotation.
4. Confirm the couch accelerates more effectively and is more supported/stable than with one carrier.

### Two Conflicting Carriers

1. With both Players still attached, move in directly opposite directions.
2. Confirm translation is substantially reduced and neither Player overrides the other.
3. Confirm the couch remains physical and may rotate according to the occupied points.
4. Move one Player forward and the other sideways; confirm diagonal movement and/or rotation with reduced efficiency.

### Turning and Release

1. Have Players on different ends intentionally move in different suitable directions.
2. Confirm rotation is produced by off-centre forces and Rigidbody torque, not a Transform assignment.
3. Player 1 presses E and confirms only Player 1 releases.
4. Confirm Player 2 remains attached and can still influence the couch.
5. Player 2 presses E and confirms ordinary Rigidbody behaviour resumes.

## Verification Checklist

Code and asset inspection completed:

- [x] Couch prefab exists.
- [x] Couch uses Rigidbody physics.
- [x] Couch has a non-trigger collider for environment collisions.
- [x] E requests the nearest available carry point.
- [x] E requests release while carrying.
- [x] Carry points reject multiple occupants on the server.
- [x] Four independent Players can occupy one couch in the state model.
- [x] Couch position is configured for server-authoritative synchronisation.
- [x] Couch rotation is configured for server-authoritative synchronisation.
- [x] Grab state uses server-written SyncVars.
- [x] Remote Players return before reading interaction input.
- [x] Releasing one point leaves other occupants attached.
- [x] Couch remains a dynamic server Rigidbody while carried.
- [x] Weight directly configures Rigidbody mass and vertical support load.
- [x] Every occupied point applies an independent horizontal force and lift force.
- [x] Lift is applied at the actual CarryPoint rather than the centre of mass.
- [x] Player-count assistance uses a capped diminishing-returns curve.
- [x] Cooperation uses pairwise movement-intent dot products.
- [x] Opposing intent remains as separate off-centre force contributions.
- [x] Movement intent is owner-submitted, throttled, unreliable, and server-timeout protected.
- [x] Carry influence and Player speed weaken before automatic separation release.
- [x] Force, lift, linear-velocity, and angular-velocity stability limits are configured.
- [x] Optional Gizmos expose CarryPoints, horizontal force, lift, and movement intent.
- [x] Couch is never parented to a Player.
- [x] Runtime carrying code never writes the couch transform.
- [x] No coroutines or `IEnumerator` implementations were introduced.
- [x] Existing Player ownership and owner-authoritative transform settings remain in place.
- [x] Existing third-person movement remains the movement implementation.
- [x] The optional debug bot is a server-driven, unowned instance of the registered Player prefab.
- [x] Bot grabbing uses the existing server validation and independent occupancy state.
- [x] Bot intent enters the existing per-carrier couch-force calculation without an extra physics path.
- [x] Client bot copies do not run AI or local input.
- [x] Bot point discovery is throttled and no per-frame bot network RPC is sent.
- [x] The runtime and editor C# assemblies compile with zero warnings and zero errors after the debug-bot source changes.

Runtime checks still requiring Play Mode/hardware:

- [ ] One corner visibly lifts while the unsupported side stays lower.
- [ ] One carrier can move the couch slowly without effortless acceleration.
- [ ] Two cooperating carriers move and support the couch more effectively.
- [ ] Two opposing carriers substantially reduce translation while retaining physical torque.
- [ ] Coordinated differential movement rotates the couch physically.
- [ ] Two distinct Steam users completed the full influence and independent-release test.
- [ ] Existing Steam lobby behaviour re-verified in a two-user build.
- [ ] The host-spawned debug bot completed its approach, grab, cooperate, oppose, and independent-release Play Mode checks.
- [ ] A remote Steam client observed the same bot transform, occupancy, and couch response.

## Known Limitations

- Two-account Steam testing and final feel tuning have not been performed in this implementation session; the runtime items above are deliberately not claimed as verified.
- The debug bot walks directly towards its target with a CharacterController and does not use NavMesh pathfinding, so level geometry can block it.
- Cooperate and Pull Against follow the first human carrier found on that couch. They intentionally remain idle until a human on the same couch supplies movement intent.
- One debug bot is supported per host session; runtime spawning of teams or mixed bot behaviours is outside this focused test tool.
- There is no client-side couch prediction. Non-host carriers may perceive transform latency at higher network latency, although only one server simulation exists.
- The prototype uses one simple box collider rather than compound cushions/arms, so collision shape is intentionally coarse.
- A rejected request has no UI feedback. The Player can immediately press E again after moving closer or selecting another point.
- Occupancy has gizmo-only feedback; there is no runtime UI or hand animation.
- A scene must contain a FishNet scene instance of the Couch, or server code must spawn the registered Couch prefab. Automatic couch placement is intentionally not included.
- Horizontal/lift tuning is shared by all four points; the prototype does not model different grip quality or leverage per handle.
- Movement intent is intentionally sampled at 10 Hz rather than predicted, so very high latency can delay cooperation changes even though stale input safely expires.
