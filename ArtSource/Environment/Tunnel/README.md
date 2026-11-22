# Couch Guys tunnel

Editable source: `CouchGuys_Tunnel.blend`. Unity imports only the three FBX files in
`Assets/_Project/Models/Environment/Tunnel/`.

The saved prefab is `Assets/_Project/Prefabs/Environment/Tunnel/CouchGuys_Tunnel.prefab`.
It contains four linked 3 m lining bays, two entrance collars, static non-convex shell
colliders, a 212-triangle hill envelope, two inexpensive shadowless lights, and an inactive summit trigger.
There is no floor: the existing generated roads and their colliders remain the driving surface.
Pivot: road centre at ground level; Unity +Z runs along the tunnel. Nominal region
dimensions are 12 m width, 8 m height, 12 m length. The actual arch has 12.8 m
clear width, with yellow trim outside the traffic lanes. Side skirts extend to
7.86 m from centre, inside the existing 7.92 m reserved half-width, and bury below
ground to conceal small sampling differences. Broad grassy shoulder wings extend
50 m to each side, taper along their length, and bury their toes 22 m below the
road pivot. This fills the side gaps between the raised tunnel and the lower
surrounding hills. Only the terrain surround is wider; the concrete tunnel,
entrance positions, road surface, and transition trigger keep their existing dimensions.

Edit materials, lights, module transforms and colliders directly in the prefab.
Region Road Tunnels width/height/depth scale the saved assembly at placement.
For a longer tunnel with unchanged panel spacing, duplicate the saved lining bays
in a prefab variant, move its end collar, and update its TunnelGeometry authored
dimensions in the Inspector. Runtime placement never builds a mesh or hierarchy.
The summit uses the same prefab with its saved transition trigger activated;
existing server-authoritative transition logic is unchanged.

Explicit authoring only: `Tools/Blender/build_couch_guys_tunnel.py` exports the
source modules. `Tools > Couch Guys > Tunnel > Create Saved Modular Tunnel`
creates a missing prefab and refuses to overwrite an existing one.
Pass `--embankment-only` to the Blender script to export just the terrain surround
without rewriting the portal or lining FBXs. Existing prefab material/transform
overrides and model GUIDs remain intact when the mesh is reimported.
`Validate In Current Scene` checks three generated seeds and writes the Unity
preview and validation report here. If needed it temporarily opens SampleScene
additively, then closes it without saving. Validation does not run a multiplayer
host/client session or verify vehicle driving behaviour.
