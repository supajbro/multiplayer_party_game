# Couch Carrying Prototype Implementation

## Architecture

The prototype extends the existing FishNet and Steamworks.NET multiplayer implementation. It does not introduce another networking layer.

- `PlayerCouchCarrier` is a `NetworkBehaviour` on each Player. Only the owning client reads Interact, selects a nearby point, and sends grab/release requests. The server stores that player's accepted couch and point in SyncVars.
- `CouchCarryController` is a `NetworkBehaviour` on the Couch. It owns four independently synchronised occupancy slots and applies all carrying forces during the server's `FixedUpdate`.
- `CouchCarryPoint` identifies one attachment transform and its stable point index. Occupancy remains centralised on the couch.
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

Important Rigidbody settings:

| Setting | Value |
| --- | ---: |
| Mass | 35 kg |
| Linear damping | 0.4 |
| Angular damping | 1.5 |
| Interpolation | Interpolate |
| Collision detection | Continuous Dynamic |
| Use gravity | Yes |
| Is kinematic on server | No |

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

Carry points draw green wire spheres when available and red wire spheres when occupied. Selecting the Couch also draws point markers and lines from the root.

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

Every occupied point contributes one spring force during the server's `FixedUpdate`:

```text
desired point = player position
              + horizontal direction towards couch × carry distance
              + up × carry height

force = position error × carry force
      - Rigidbody point velocity × damping
```

The force is clamped and applied with `Rigidbody.AddForceAtPosition`. The application position is blended between the centre of mass and the carry point using Rotation Influence. Multiple forces therefore combine naturally: agreement translates the couch, unequal/off-axis pulls generate torque, and opposing movement fights through the shared body. Gravity, inertia, contacts, and wall collisions remain active throughout.

Default tuning values:

| Setting | Value |
| --- | ---: |
| Carry Force | 500 N/m |
| Damping | 65 |
| Maximum Force per Player | 900 N |
| Carry Distance | 0.45 m |
| Carry Height | 0.45 m |
| Rotation Influence | 0.85 |
| Carrying Speed Multiplier | 0.72 |
| Maximum Carry Separation | 3 m |

The speed reduction lets the couch constrain the Player without locking locomotion. Excessive separation releases that Player rather than stretching the spring indefinitely.

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

### Multiple Player Influence

The server loops over all four occupied points each physics tick and adds a separately calculated force at each point. No carrier is designated leader and couch ownership is not assigned to any client.

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

### One Player

1. Start a host session.
2. Approach one of the four point gizmo positions and press E.
3. Walk around and confirm the couch lags, collides, and rotates from the off-centre pull.
4. Move more than 3 m away and confirm automatic release, then grab again.
5. Press E and confirm the couch releases and continues as an ordinary Rigidbody.

### Two Steam Players

1. Start the same build on two machines with two Steam accounts and connect both users to one lobby.
2. Approach the same couch from different corners.
3. Player 1 presses E near one point.
4. Player 2 presses E near a different point.
5. Move together and confirm both clients see the same couch motion.
6. Move in opposite directions and confirm the couch twists/rotates rather than choosing one Player.
7. Player 1 presses E and confirms only Player 1 releases.
8. Confirm Player 2 can still move the couch.
9. Player 2 presses E and confirms the couch returns to ordinary Rigidbody behaviour.

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
- [x] Couch is never parented to a Player.
- [x] Runtime carrying code never writes the couch transform.
- [x] No coroutines or `IEnumerator` implementations were introduced.
- [x] Existing Player ownership and owner-authoritative transform settings remain in place.
- [x] Existing third-person movement remains the movement implementation.
- [x] Unity compiled the runtime/editor assemblies and FishNet IL post-processing completed without errors after the source changes.

Runtime checks still requiring Play Mode/hardware:

- [ ] One-Player feel and collision tuning completed in Play Mode.
- [ ] Two distinct Steam users completed the full carry/release test.
- [ ] Existing Steam lobby behaviour re-verified in a two-user build.

## Known Limitations

- Two-account Steam testing and final feel tuning have not been performed in this implementation session; the runtime items above are deliberately not claimed as verified.
- There is no client-side couch prediction. Non-host carriers may perceive transform latency at higher network latency, although only one server simulation exists.
- The prototype uses one simple box collider rather than compound cushions/arms, so collision shape is intentionally coarse.
- A rejected request has no UI feedback. The Player can immediately press E again after moving closer or selecting another point.
- Occupancy has gizmo-only feedback; there is no runtime UI or hand animation.
- A scene must contain a FishNet scene instance of the Couch, or server code must spawn the registered Couch prefab. Automatic couch placement is intentionally not included.
- Spring tuning is shared by all points and couch mass is fixed for this single prototype type.
