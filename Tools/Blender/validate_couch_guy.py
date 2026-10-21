"""Read-only validation for the generated Couch Guy Blender asset."""

import bpy


rig = bpy.data.objects["CouchGuy_Rig"]
required_bones = (
    "Root", "Torso", "Head",
    "UpperArm_L", "LowerArm_L", "Hand_L",
    "UpperArm_R", "LowerArm_R", "Hand_R",
)
weapon_names = (
    "Weapon_Pistol", "Weapon_AssaultRifle", "Weapon_Shotgun"
)
weapons = [bpy.data.objects[name] for name in weapon_names]
face = bpy.data.objects["FaceDisplay"]

checks = {
    "required_bones": all(name in rig.pose.bones for name in required_bones),
    "both_arms_and_hands": all(
        name in bpy.data.objects
        for name in (
            "UpperArm_L", "Forearm_L", "Hand_L",
            "UpperArm_R", "Forearm_R", "Hand_R",
        )
    ),
    "three_weapons": len(weapons) == 3,
    "weapon_muzzles": all(
        any(child.name.startswith("Muzzle") for child in weapon.children)
        for weapon in weapons
    ),
    "face_uv": len(face.data.uv_layers) > 0,
    "face_material": any(
        material.name == "DarkVisor" for material in face.data.materials
    ),
    "hover_pivots": all(
        name in bpy.data.objects
        for name in ("HoverRotor_Outer", "HoverRotor_Inner")
    ),
    "animation_library": len(bpy.data.actions) == 54,
    "hand_attached_weapons": all(
        weapon.parent == rig and weapon.parent_type == 'BONE' and weapon.parent_bone == 'Hand_L'
        for weapon in weapons
    ),
    "weapon_carry_actions": all(
        name + '_' + state in bpy.data.actions
        for name in ('Pistol', 'AssaultRifle', 'Shotgun')
        for state in ('Idle', 'Walk', 'Backward', 'Strafe_L', 'Strafe_R', 'Run', 'Raise', 'Sprint', 'Shoot')
    ),
}

for name, passed in checks.items():
    print("AUDIT %s=%s" % (name, passed))

print(
    "AUDIT objects=%d meshes=%d actions=%d"
    % (len(bpy.data.objects), len(bpy.data.meshes), len(bpy.data.actions))
)

if not all(checks.values()):
    raise RuntimeError("Couch Guy validation failed")

