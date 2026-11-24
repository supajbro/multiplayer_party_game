# Character 01 - thief visual trial

Derived from the installed Hodaart Character 01. Original ThirdParty assets are unchanged.

Design: burgundy jacket and low-poly beanie, deep plum trousers and backpack, charcoal cuffs, placket, mask, gloves, socks and straps, two golden buttons and gold sneaker/backpack accents with off-white sneaker trim. Clothing colours follow the approved rogue car / getaway truck concept in `Concepts/Character01_Thief_GetawayPalette.png`. The original eyes and eyebrows are carried over from the pack's separate Head Parts file; no replacement facial design was generated.

Assets/_Project/Models/Thieves/Character01Thief/Character01_Thief.fbx is the Unity model asset. It exports one skinned mesh and the existing skeleton, one URP material and a 256px colour palette. No preview lights/cameras or animation clips are exported. Existing body vertices and skin weights are preserved. Hat/mask follow the existing Head bone; the backpack follows Spine2.

The editable Blender master and four preview renders are in this ArtSource folder, outside Unity Assets. The preview uses a temporary relaxed arm pose; the saved master and FBX retain the original T-pose. No new rig or animation was authored. The original Unity Humanoid import configuration is retained so supplied humanoid animations can be reused; retargeted clips and extensive motion/deformation are not yet validated.

Use this first variant to judge the outfit/style before adapting the other nine. Some of the original garment features (coat length, pocket, buttons, rolled sleeves) remain intentionally. This is a jacket-based thief, not a new hoodie model. Accessory fit during deep torso bends is a later validation step.

No gameplay prefabs, existing character prefabs, scenes or controllers were modified. No prefab-building scripts were added.

The GetawayPalette update changes only palette pixels and UV assignments in the unarmed, pistol, assault rifle and shotgun models and their editable Blender masters. Existing saved prefabs reference these same model assets. FBX geometry, skin weights, skeletons, animation curves and all Unity metadata remain identical. Preview renders, original-file backups and the semantic FBX validation report are under `GetawayPalette/`. The explicit authoring script is `Tools/Blender/apply_character01_getaway_palette.py`; it never runs on import or Play mode.

