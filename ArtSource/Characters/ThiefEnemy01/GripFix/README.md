# Weapon grip correction

Corrected the mounted weapons in Thief_Pistol, Thief_AssaultRifle, Thief_Shotgun and GrapplerThief. The earlier migration retained the old world-space weapon orientation while parenting it to the new hand. The three baked firearm muzzle transforms also pointed downward instead of along their barrels.

Each affected prefab now has an editable `WeaponGrip` transform below `Hand.R`. The actual vertical handle center is placed in the palm, and the mesh barrel follows the hand's finger axis. A `HandleCenter` marker records the measured handle position. The muzzle stays at its existing barrel-tip position relative to the weapon and now faces down the barrel. Original gun, muzzle and renderer objects/references are retained.

GrapplerThief's `m_gun` reference intentionally targets WeaponGrip, because its aiming code rotates that transform. This makes runtime aiming rotate the gun around the palm rather than around the mesh origin. Its existing muzzle, rope, prop LODs, materials and gameplay settings are preserved. No runtime code, animation clips, character geometry or LOD budgets were changed.

`GripInventory.txt` records the original hand axes, down-facing firearm muzzles and measured handle-component bounds. `GripValidation.txt` records checks on the saved four prefabs: T-pose plus 13 samples of Idle, Walk, Shoot, Frustration and Death, verifying handle anchoring and barrel/muzzle alignment. Three additional grappler aim directions verify no grip drift during runtime-style aiming. `GripComparison.png` shows actual saved prefab mesh captures in T-pose, idle and shooting, rendered in Blender with simplified lighting.

`Before/`, BeforeHashes.json, InstalledGripHashes.json and RootSettingsPreservation.json record original assets, installed outputs and retained root identities. Root gameplay serialization is preserved, with only the grappler aim-transform reference intentionally remapped. The four original prefab GUIDs remain unchanged. Shared pickup/weapon prefab assets and other enemies were not edited.

Explicit offline tools: Tools/Unity/FixThiefWeaponGrips.cs and Tools/install_thief_weapon_grips.py. The installer refuses to replace assets changed during authoring. There are no import/reload/Play-mode builders.
