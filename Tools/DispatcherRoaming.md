# Dispatcher roaming

Edit Assets/_Project/Prefabs/DeliveryNPC.prefab > Dispatcher Roaming for timing. Defaults: Roaming Interval 5-10 seconds at home, Roaming Duration 45-75 seconds per outing including return travel, Pause Duration 0.5-1.5 seconds at each stop, speed 0.8 m/s. Saved Inspector values take precedence over script defaults.

## Change the path in the Editor

1. Open Assets/_Project/Prefabs/World/depot_02.prefab in Prefab Mode (the active Suburbs depot). CouchGuysDepotV2.prefab is the scene fallback and has its own independent points.
2. Expand DispatcherRoamingWaypoints. Move SafeRoamingPoint_1, _2 and _3 using the Move tool. Keep their height at the depot floor/spawn-marker height.
3. Select the depot root's Dispatcher Roaming Area component. Its Waypoints array must reference every point you want to use. Duplicate/create an empty child marker to add a stop, then add its Transform to this array. Remove array entries when deleting stops.
4. Enable Scene Gizmos and select the root: green spheres are points, cyan bounds are the permitted interior, and red bounds are exclusions. Expand Interior deliberately if extending the walking area; keep truck bays and door lanes excluded. Allow at least 0.65 m clearance around the TV and a supporting floor.
5. Save the prefab outside Play mode. Update the fallback prefab separately if it should use a matching route.

Destinations are chosen randomly; the array order does not define an ordered patrol. A small waypoint graph finds a route through intermediate markers when a direct segment is blocked. Each segment uses the full TV footprint and checks current colliders, interior bounds, exclusions and supporting destination floor. Place intermediate points around furniture/corners to provide a connected detour. This is waypoint routing, not a NavMesh. All five authored points are preserved, but points inside the truck exclusion remain unavailable.

Roaming now keeps choosing destinations until the outing's return budget is reached. The former one-to-three-visit cap is removed. Revisiting a point removes closed loops from the return trail, so memory stays bounded by home plus the authored waypoint count and the actor can retrace a safe path home. If all destinations are blocked he retries after five seconds. A blocked return waits or replans through available points; it never teleports across colliders. Conversation pauses movement; dialogue release triggers a return home.

The existing pose SyncVar remains server-authoritative and capped at 10 Hz while moving. Clients interpolate the pose; no new network messages or objects were added. No model assets or LODs were changed.

Couch Guys > Dispatcher > Validate Roaming checks the saved layout, collisions, more than three waypoint visits, dialogue pause/release and return behavior in an isolated Editor scene. Use outside Play mode. Validation source now reads the configured interval instead of assuming the old 45-second minimum.

Latest checks: Unity compiled the runtime and Editor scripts. Validate Roaming passed against both saved depot prefabs, including collider-safe departure, desk detour for depot_02, six destinations, bounded trail, conversation pause/release, return-home deadline, truck/exterior rejection, and blocked-return waiting/recovery without teleportation. Multiplayer playtesting remains outstanding.

Desk-route fix: depot_02 home was moved behind the desk to (-4.4, 0.08, -5.99), outside the previous bounds. The saved interior now includes that rear aisle and SafeRoamingPoint_5 on the left. Home -> Point 5 -> Point 1 -> Point 2 supplies a clear detour around the desk. Existing point positions, desk colliders and truck exclusions were preserved.
