# Easy delivery couches 01–10

The numbered Blender contact sheet is `easy_couches_contact_sheet.png`.

## Assets and editing

- Reusable generator: `Tools/Blender/generate_easy_couches.py`.
- Blender source: `Tools/Blender/Sources/easy_couches.blend` (textures packed).
- FBXs: `Assets/_Project/Models/Couch/Easy/easy_couch_01.fbx` through `easy_couch_10.fbx`.
- Each FBX contains LOD0, LOD1 and LOD2 with a shared floor-centre origin, metre scale, and identical orientation.
- Saved visual prefabs: `Assets/_Project/Prefabs/Couches/Easy/EasyCouch_01.prefab` through `EasyCouch_10.prefab`.
- Unity Toon materials: `Assets/_Project/Materials/Couch/Easy/`; baked UV pattern PNGs are beside the models in `Textures/`.
- Edit each prefab's LODGroup and CouchVisualBounds in the Inspector. Collision envelopes follow the visual and are used by existing vehicle clearance queries.
- Edit the ten-entry visual list on `Assets/_Project/Settings/Delivery/EasyDelivery.asset`. Keep the same list/order in host and client builds, as with other networked content.

## Runtime integration

The existing `Couch.prefab` remains the only networked gameplay couch. Its original three-model list, carry points, settings, Rigidbody, NetworkTransform, stamina and interaction components are preserved. The ten prefabs are visual assets instantiated by the existing visual-selection path; they are not standalone pickups or additional network spawnables.

For each Easy delivery, the server independently chooses a uniformly random entry in the Easy configuration. Its index is synchronised by the existing FishNet SyncVar, including late joining clients. Legacy indices remain reserved for the original models. Medium and Hard continue using that original pool. Easy collision profiles are applied as gameplay state; legacy selection restores the original collider and cargo bounds.

No prefab hierarchy is constructed at runtime. No authoring tool regenerates assets on import, script reload or Play. The explicit authoring menu creates missing assets and preserves existing prefab/material Inspector customisations.

## Reproduction and validation

Run Blender in background with `--python Tools/Blender/generate_easy_couches.py` to explicitly regenerate model exports and the sheet. This overwrites generated model/source files, so retain any manual Blender edits first.

Unity menus under **Couch Guys > Couches** provide asset validation, networking validation and focused interaction regression tests. Temporary request files in `Temp/` are optional automation triggers for those explicit commands.

- `mesh_statistics.json`: Blender triangle counts (LOD0 approximately 4.7–9.4k; LOD1 approximately 2.3–4.5k; LOD2 approximately 0.9–1.8k).
- `unity_validation.txt`: imported scale, all ten unique prefabs, materials, three LODs, collision profiles, original models and difficulty configuration.
- `network_validation.txt`: actual FishNet host and independent loopback client checks, ten simultaneous variants, late join, live changes, all four grips on every variant, and vehicle cargo cycles.
- `interaction_tests.xml` and `.txt`: focused existing couch, delivery, cargo, sled and grapple regression results. The first run recorded 84 passes and five failures in existing tests: GrappleMigrationPreservesExistingWeaponTuning, CoverAndNonPlayersBlockGrapple, ThreeWeaponRacksRemainDistinctAndPreservePickupAmmo, and both SecuredCargoFollowsTruckWithoutPhysicsOrNetworkPoseWriters cases. These areas were not changed by the couch work. The truck failures access FishNet server state without an initialised NetworkObject; live vehicle checks run with fully spawned network objects.

The integration probes use a temporary Play session and do not save or replace gameplay scenes. Loopback validation does not replace a separate-machine Steam transport session or a visual art review in the target level.
