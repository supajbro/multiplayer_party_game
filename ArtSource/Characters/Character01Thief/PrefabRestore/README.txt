CHARACTER01 THIEF PREFAB RESTORATION

The seven gameplay prefabs (Thief, Pistol, AssaultRifle, Shotgun,
RocketLauncher, GrapplerThief, SpeakerThief) restore their archived
Character01_Thief model, controller, skeleton, fitted ragdoll and weapon/prop
configuration. The shared thief_enemy_01.prefab asset now provides the base
Character01_Thief visual too, retaining its existing asset GUID for references.

Current common component blocks are retained. Only visual/attachment references
(animator, ragdoll, muzzle, gun and held renderers) return to their original
Character01 equivalents. Model-dependent transforms return to their original
fit. Prefab GUIDs, root gameplay identities, settings and specialist props are
preserved. RogueCar is not changed. Alternate model and material files remain
available; no source model, texture or animation file is deleted or modified.

Before/ and BeforeHashes.json snapshot the previous production prefabs.
GameplayPreservation.json describes the serialized component merge.
UnityRestoreValidation.txt records saved-asset checks for all eight prefabs:
Character01 meshes, valid skin bones/materials/avatar/controller, five animation
states at five times, gameplay components, ragdoll and attachment references.
InstallationValidation.json records installed hashes and preservation checks.
These are isolated asset checks, not a full gameplay/multiplayer playtest.

Explicit offline tools only; none run on import, reload or Play mode:
Tools/stage_restore_character01.py
Tools/Unity/RestoreCharacter01Thieves.cs
Tools/install_restored_character01.py
