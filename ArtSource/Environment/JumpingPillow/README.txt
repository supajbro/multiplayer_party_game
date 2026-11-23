AUSTRALIAN JUMPING PILLOW - Couch Guys

AustralianJumpingPillow.blend: editable original with preview lighting/camera.
AustralianJumpingPillow.fbx: visual asset only; no preview ground or lights.
AustralianJumpingPillow_Collision.fbx: optional 864-triangle collision surface.
AustralianJumpingPillow_Preview.png: rendered three-quarter preview.
validation.json: source topology and dimension checks.

Scale: metres. Jumping surface 12 x 9 m, crown 1.34 m above ground.
Anchor footprint 12.312 x 9.234 m. Root at ground-centre (0,0,0).
FBX: Y-up, -Z forward; unit scale 1 metre. Object transforms applied.
Place root at terrain height: the small underside/skirt should be buried.
Hierarchy: AustralianJumpingPillow / JumpingSurface + AnchoredPerimeter.

Upper surface: 72 x 54 shared quads, approximately 16.7 cm spacing.
Three persistent material regions: red foreground, blue middle, yellow rear.
No separated strips or seams across the upper surface. UV channel SurfaceUV.
DeformationMask vertex RGB: 0 = fixed border, 1 = free interior, with a
four-row falloff. Blender also has DeformableSurface/PinnedPerimeter groups.
Bottom vertices are fixed. Surface is a closed manifold; anchor is separate.
The source FBX has no baked animation or physics; the saved Unity prefab supplies runtime behavior.

UNITY INTEGRATION
Saved prefab: Assets/_Project/Prefabs/Environment/JumpingPillow/JumpingPillow.prefab
Both suburban region libraries use this asset in all three former park slots.
JumpingPillow component: configure Bounce Height (3.5 m default), Minimum
Landing Speed (3 m/s), contact normal threshold, cooldown and ground offset.
The player motor bounces downward landings on the saved surface collider.
Local Visual Deformation settings control player depression, landing impact strength,
spread radius/speed, maximum depression/rebound, spring frequency/damping and recovery.
Each client independently detects all active avatars, including disabled remote motors.
No deformation mesh data, impulses, RPCs or SyncVars are sent over the network.
The pinned mask keeps the border/underside fixed, and the separate anchor is unchanged.
Only the instance surface mesh deforms; physics collision stays at its terrain-fitted rest shape.

Vehicle Bounce settings independently control enable, height, cooldown, allowed upward
speed, contact reach/penetration tolerance and normal threshold. Vehicle deformation radius and depression
are also exposed. Trucks (including occupied/getaway trucks), traffic/rogue enemy cars,
hoverboards and sleds are recognized; other props are ignored. Dynamic vehicle impulses
run only on the current vehicle physics authority. Client replicas contribute visual loads
but never receive local launches. Existing vehicle transform replication is unchanged.
Truck/car/hoverboard launches reuse the existing airborne launch state to suspend their
motors until landing. Bounce impulses preserve horizontal momentum and are mass independent.

At generation the existing surface, anchored edge and 864-triangle collider
receive matching per-vertex terrain height offsets. Source meshes, material
sections, UVs and vertex mask remain unchanged. Instances own fitted copies
which are released when destroyed. Geometry/hierarchies are not rebuilt.
Both FBX imports have Read/Write enabled for this one-time placement fit.
The static MeshCollider is non-convex, with no Rigidbody or trigger.

Four Inspector-editable Couch Guys/Toon Lit materials use subtle highlights.
The old park centre paths/features are skipped to keep the pillow clear.
The saved prefab/materials are never automatically rebuilt on import or Play.
Explicit creation menu: Tools > Couch Guys > Jumping Pillow > Create Saved
Prefab. This preserves an existing prefab and existing materials.

Explicit regeneration only:
blender --background --python Tools/Blender/build_jumping_pillow.py
This overwrites these authored files; normal opening/import does not rebuild.
