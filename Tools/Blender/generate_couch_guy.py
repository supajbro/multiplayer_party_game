"""Procedurally build the Couch Guys floating robot character in Blender.

Run from Blender's Scripting workspace, or from the command line with:
    blender --background --python generate_couch_guy.py

The character faces Blender -Y. With Blender's usual FBX export settings
(-Z Forward, Y Up), it imports facing Unity +Z. All glow meshes share the
PlayerGlow material so player colour can be replaced as one Unity material.

The generated armature also contains Unity-ready actions. Export as FBX with
"Bake Animation" and "All Actions" enabled to include every clip.
"""

import math

import bpy
from mathutils import Vector


# -----------------------------------------------------------------------------
# Scene and material helpers
# -----------------------------------------------------------------------------


def clear_scene():
    """Remove every object and orphaned data block from the current file."""
    if bpy.context.object and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)

    # Actions may have fake users for FBX export, so remove them explicitly.
    for action in list(bpy.data.actions):
        bpy.data.actions.remove(action)

    for data_group in (bpy.data.meshes, bpy.data.curves, bpy.data.armatures,
                       bpy.data.materials):
        for data_block in list(data_group):
            if data_block.users == 0:
                data_group.remove(data_block)


def make_material(name, base_color, metallic=0.0, roughness=0.35,
                  emission_color=None, emission_strength=0.0):
    """Create a simple Principled material that is easy to reproduce in Unity."""
    material = bpy.data.materials.new(name)
    material.use_nodes = True
    material.diffuse_color = base_color

    principled = material.node_tree.nodes.get("Principled BSDF")
    principled.inputs["Base Color"].default_value = base_color
    principled.inputs["Metallic"].default_value = metallic
    principled.inputs["Roughness"].default_value = roughness

    # Blender 4 renamed the Principled emission socket; support Blender 3 too.
    emission_input = principled.inputs.get("Emission Color")
    if emission_input is None:
        emission_input = principled.inputs.get("Emission")
    if emission_input is not None and emission_color is not None:
        emission_input.default_value = emission_color

    strength_input = principled.inputs.get("Emission Strength")
    if strength_input is not None:
        strength_input.default_value = emission_strength

    return material


def create_materials():
    """Create the four intentionally simple character materials."""
    return {
        "white": make_material(
            "WhiteBody", (0.92, 0.95, 1.0, 1.0), metallic=0.05, roughness=0.2
        ),
        "visor": make_material(
            "DarkVisor", (0.008, 0.012, 0.022, 1.0), metallic=0.35,
            roughness=0.12
        ),
        "glow": make_material(
            "PlayerGlow", (0.015, 0.28, 1.0, 1.0), metallic=0.0,
            roughness=0.2, emission_color=(0.01, 0.35, 1.0, 1.0),
            emission_strength=7.0
        ),
        "joints": make_material(
            "DarkJoints", (0.035, 0.045, 0.065, 1.0), metallic=0.3,
            roughness=0.28
        ),
        "gunmetal": make_material(
            "WeaponGunmetal", (0.055, 0.07, 0.095, 1.0), metallic=0.55,
            roughness=0.24
        ),
        "weapon_accent": make_material(
            "WeaponAccent", (1.0, 0.22, 0.06, 1.0), metallic=0.1,
            roughness=0.3, emission_color=(1.0, 0.06, 0.01, 1.0),
            emission_strength=2.0
        ),
    }


def assign_material(obj, material):
    obj.data.materials.append(material)


def smooth_mesh(obj):
    for polygon in obj.data.polygons:
        polygon.use_smooth = True


def apply_transforms(obj):
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    obj.select_set(False)


def add_bevel(obj, width=0.08, segments=3):
    modifier = obj.modifiers.new("SoftEdges", "BEVEL")
    modifier.width = width
    modifier.segments = segments
    modifier.limit_method = "ANGLE"

    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    obj.select_set(False)
    smooth_mesh(obj)


def create_rounded_box(name, location, dimensions, material, bevel=0.1,
                       rotation=(0.0, 0.0, 0.0)):
    bpy.ops.mesh.primitive_cube_add(location=location, rotation=rotation)
    obj = bpy.context.object
    obj.name = name
    obj.dimensions = dimensions
    apply_transforms(obj)
    add_bevel(obj, min(bevel, min(dimensions) * 0.45), 4)
    assign_material(obj, material)
    return obj


def create_uv_ellipsoid(name, location, scale, material, segments=24, rings=16,
                        rotation=(0.0, 0.0, 0.0)):
    bpy.ops.mesh.primitive_uv_sphere_add(
        segments=segments, ring_count=rings, location=location, rotation=rotation
    )
    obj = bpy.context.object
    obj.name = name
    obj.scale = scale
    apply_transforms(obj)
    smooth_mesh(obj)
    assign_material(obj, material)
    return obj


def create_capsule(name, start, end, radius, material, bevel=None):
    """Create a low-poly rounded cuboid aligned between two points."""
    start = Vector(start)
    end = Vector(end)
    midpoint = (start + end) * 0.5
    direction = end - start
    length = direction.length

    obj = create_rounded_box(
        name,
        midpoint,
        (radius * 2.0, radius * 2.0, length),
        material,
        bevel=bevel if bevel is not None else radius * 0.9,
    )
    obj.rotation_mode = "QUATERNION"
    obj.rotation_quaternion = direction.to_track_quat("Z", "Y")
    apply_transforms(obj)
    return obj


def parent_to(obj, parent):
    obj.parent = parent


def create_empty(name, parent=None):
    obj = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(obj)
    obj.empty_display_type = "PLAIN_AXES"
    obj.empty_display_size = 0.18
    if parent:
        obj.parent = parent
    return obj


# -----------------------------------------------------------------------------
# Character geometry
# -----------------------------------------------------------------------------


def create_head(materials, root):
    head = create_rounded_box(
        "Head", (0.0, 0.0, 1.82), (1.42, 0.94, 1.02),
        materials["white"], bevel=0.25
    )
    parent_to(head, root)

    # The visor sits slightly proud of the face to prevent z-fighting.
    visor = create_rounded_box(
        "Visor", (0.0, -0.486, 1.84), (1.10, 0.09, 0.52),
        materials["visor"], bevel=0.15
    )
    parent_to(visor, root)

    eye_l = create_rounded_box(
        "Eye_L", (-0.27, -0.542, 1.86), (0.31, 0.045, 0.18),
        materials["glow"], bevel=0.09
    )
    eye_r = create_rounded_box(
        "Eye_R", (0.27, -0.542, 1.86), (0.31, 0.045, 0.18),
        materials["glow"], bevel=0.09
    )
    parent_to(eye_l, root)
    parent_to(eye_r, root)

    for side, x in (("L", -0.77), ("R", 0.77)):
        ear = create_uv_ellipsoid(
            f"Ear_{side}", (x, 0.0, 1.84), (0.18, 0.12, 0.39),
            materials["joints"], segments=20, rings=12,
            rotation=(0.0, 0.0, math.radians(8.0 if side == "L" else -8.0))
        )
        light_x = x + (-0.105 if side == "L" else 0.105)
        ear_light = create_uv_ellipsoid(
            f"EarLight_{side}", (light_x, -0.018, 1.84),
            (0.035, 0.105, 0.29), materials["glow"], segments=16, rings=10
        )
        parent_to(ear, root)
        parent_to(ear_light, root)

    return head


def create_torso(materials, root):
    torso = create_uv_ellipsoid(
        "Torso", (0.0, 0.04, 0.94), (0.58, 0.42, 0.58),
        materials["white"], segments=24, rings=16
    )
    parent_to(torso, root)

    neck = create_uv_ellipsoid(
        "NeckJoint", (0.0, 0.02, 1.37), (0.28, 0.24, 0.13),
        materials["joints"], segments=20, rings=12
    )
    parent_to(neck, root)

    chest = create_rounded_box(
        "ChestPanel", (0.0, -0.405, 1.01), (0.48, 0.035, 0.24),
        materials["white"], bevel=0.08
    )
    parent_to(chest, root)
    return torso


def join_objects(objects, name):
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.object.join()
    result = bpy.context.object
    result.name = name
    result.select_set(False)
    return result


def create_hand(side, wrist, materials):
    """Create a single-mesh cartoon hand with palm, thumb, and four fingers."""
    sign = -1.0 if side == "L" else 1.0
    wrist = Vector(wrist)
    palm_center = wrist + Vector((sign * 0.03, -0.13, -0.02))

    pieces = [create_rounded_box(
        f"Hand_{side}_Palm", palm_center, (0.39, 0.32, 0.31),
        materials["joints"], bevel=0.13
    )]

    # Four readable fingers fan across the palm and reach toward Blender -Y.
    finger_x_offsets = (-0.135, -0.045, 0.045, 0.135)
    for index, offset in enumerate(finger_x_offsets, start=1):
        x = palm_center.x + offset
        start = (x, palm_center.y - 0.10, palm_center.z - 0.025)
        end = (x, palm_center.y - 0.30 + abs(offset) * 0.18,
               palm_center.z - 0.08)
        pieces.append(create_capsule(
            f"Finger_{index}_{side}", start, end, 0.058,
            materials["joints"], bevel=0.052
        ))

    # Thumb projects inward and forward so the hand reads as a grabbing hand.
    thumb_start = palm_center + Vector((-sign * 0.16, -0.015, -0.015))
    thumb_end = palm_center + Vector((-sign * 0.24, -0.17, -0.08))
    pieces.append(create_capsule(
        f"Thumb_{side}", thumb_start, thumb_end, 0.072,
        materials["joints"], bevel=0.065
    ))

    return join_objects(pieces, f"Hand_{side}")


def create_arm(side, materials, root):
    """Build one chunky three-part arm in a couch-carrying neutral pose."""
    sign = -1.0 if side == "L" else 1.0
    arm_root = create_empty(f"Arm_{side}", root)

    shoulder = Vector((sign * 0.58, -0.015, 1.19))
    elbow = Vector((sign * 0.86, -0.16, 1.00))
    wrist = Vector((sign * 0.98, -0.42, 0.88))

    shoulder_joint = create_uv_ellipsoid(
        f"ShoulderJoint_{side}", shoulder, (0.18, 0.18, 0.18),
        materials["joints"], segments=18, rings=12
    )
    upper = create_capsule(
        f"UpperArm_{side}", shoulder, elbow, 0.19, materials["white"],
        bevel=0.14
    )
    elbow_joint = create_uv_ellipsoid(
        f"ElbowJoint_{side}", elbow, (0.15, 0.15, 0.15),
        materials["joints"], segments=18, rings=12
    )
    forearm = create_capsule(
        f"Forearm_{side}", elbow, wrist, 0.175, materials["white"],
        bevel=0.13
    )
    hand = create_hand(side, wrist, materials)

    for obj in (shoulder_joint, upper, elbow_joint, forearm, hand):
        parent_to(obj, arm_root)

    return {
        "root": arm_root,
        "shoulder_joint": shoulder_joint,
        "upper": upper,
        "elbow_joint": elbow_joint,
        "forearm": forearm,
        "hand": hand,
        "points": (shoulder, elbow, wrist),
    }


def create_hover_ring(materials, root):
    bpy.ops.mesh.primitive_torus_add(
        major_radius=0.48,
        minor_radius=0.065,
        major_segments=32,
        minor_segments=10,
        location=(0.0, 0.02, 0.16),
    )
    ring = bpy.context.object
    ring.name = "HoverRing"
    smooth_mesh(ring)
    assign_material(ring, materials["glow"])
    parent_to(ring, root)
    apply_transforms(ring)
    return ring


def create_weapon_models(materials, root):
    """Create three readable low-poly weapons, all attached to the right hand."""
    weapons = []
    specs = (
        ("Weapon_Pistol", 0.62, 0.18, 0.22, 0.16),
        ("Weapon_AssaultRifle", 1.18, 0.17, 0.24, 0.34),
        ("Weapon_Shotgun", 1.34, 0.20, 0.22, 0.42),
    )
    for name, length, width, height, stock_length in specs:
        weapon = create_empty(name, root)
        body = create_rounded_box(
            name + "_Body", (0.98, -0.72 - length * 0.38, 0.91),
            (width, length, height), materials["gunmetal"], bevel=0.055)
        grip = create_rounded_box(
            name + "_Grip", (0.98, -0.58, 0.75),
            (0.15, 0.24, 0.32), materials["joints"], bevel=0.05,
            rotation=(math.radians(-18.0), 0.0, 0.0))
        barrel = create_capsule(
            name + "_Barrel", (0.98, -0.78, 0.94),
            (0.98, -0.78 - length * 0.58, 0.94),
            0.075 if name == "Weapon_Shotgun" else 0.055,
            materials["gunmetal"], bevel=0.035)
        accent = create_rounded_box(
            name + "_Accent", (0.98, -0.73, 1.025),
            (width * 0.72, max(0.16, length * 0.28), 0.035),
            materials["weapon_accent"], bevel=0.015)
        pieces = [body, grip, barrel, accent]
        if name != "Weapon_Pistol":
            stock = create_rounded_box(
                name + "_Stock", (0.98, -0.47 + stock_length * 0.35, 0.88),
                (width * 1.25, stock_length, height * 1.25),
                materials["joints"], bevel=0.07)
            pieces.append(stock)
        if name == "Weapon_AssaultRifle":
            magazine = create_rounded_box(
                name + "_Magazine", (0.98, -0.76, 0.72),
                (0.16, 0.24, 0.34), materials["weapon_accent"], bevel=0.045,
                rotation=(math.radians(-10.0), 0.0, 0.0))
            pieces.append(magazine)
        elif name == "Weapon_Shotgun":
            pump = create_rounded_box(
                name + "_Pump", (0.98, -1.15, 0.91),
                (0.24, 0.34, 0.20), materials["weapon_accent"], bevel=0.06)
            pieces.append(pump)

        for piece in pieces:
            parent_to(piece, weapon)
        muzzle = create_empty("Muzzle", weapon)
        muzzle.location = (0.98, -0.82 - length * 0.58, 0.94)
        weapon["unity_default_active"] = False
        weapon.hide_set(True)
        weapons.append(weapon)
    return weapons


# -----------------------------------------------------------------------------
# Rigging
# -----------------------------------------------------------------------------


def create_armature(left_arm, right_arm, root):
    armature_data = bpy.data.armatures.new("CouchGuy_Armature")
    rig = bpy.data.objects.new("CouchGuy_Rig", armature_data)
    bpy.context.collection.objects.link(rig)
    rig.parent = root
    rig.show_in_front = True
    armature_data.display_type = "OCTAHEDRAL"

    bpy.context.view_layer.objects.active = rig
    rig.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")

    bones = {}

    def add_bone(name, head, tail, parent=None, connected=False):
        bone = armature_data.edit_bones.new(name)
        bone.head = head
        bone.tail = tail
        if parent:
            bone.parent = bones[parent]
            bone.use_connect = connected
        bones[name] = bone
        return bone

    add_bone("Root", (0.0, 0.0, 0.0), (0.0, 0.0, 0.28))
    add_bone("Torso", (0.0, 0.0, 0.28), (0.0, 0.0, 1.38), "Root", True)
    add_bone("Head", (0.0, 0.0, 1.38), (0.0, 0.0, 2.22), "Torso", True)

    for side, arm in (("L", left_arm), ("R", right_arm)):
        shoulder, elbow, wrist = arm["points"]
        hand_tail = wrist + Vector((0.0, -0.34, -0.07))
        add_bone(f"UpperArm_{side}", shoulder, elbow, "Torso", False)
        add_bone(f"LowerArm_{side}", elbow, wrist, f"UpperArm_{side}", True)
        add_bone(f"Hand_{side}", wrist, hand_tail, f"LowerArm_{side}", True)

    bpy.ops.object.mode_set(mode="OBJECT")
    rig.select_set(False)
    return rig


def bind_rigid_object(obj, rig, bone_name):
    """Assign a whole mesh to one bone while preserving the object hierarchy."""
    vertex_group = obj.vertex_groups.new(name=bone_name)
    vertex_group.add(range(len(obj.data.vertices)), 1.0, "REPLACE")
    modifier = obj.modifiers.new("Armature", "ARMATURE")
    modifier.object = rig
    modifier.use_vertex_groups = True


def rig_character(rig, geometry):
    # Head accessories intentionally follow the head as rigid components.
    for name in ("Head", "Visor", "Eye_L", "Eye_R", "Ear_L", "Ear_R",
                 "EarLight_L", "EarLight_R"):
        bind_rigid_object(geometry[name], rig, "Head")

    for name in ("Torso", "NeckJoint", "ChestPanel"):
        bind_rigid_object(geometry[name], rig, "Torso")

    bind_rigid_object(geometry["HoverRing"], rig, "Root")

    for side in ("L", "R"):
        bind_rigid_object(geometry[f"ShoulderJoint_{side}"], rig,
                          f"UpperArm_{side}")
        bind_rigid_object(geometry[f"UpperArm_{side}"], rig,
                          f"UpperArm_{side}")
        bind_rigid_object(geometry[f"ElbowJoint_{side}"], rig,
                          f"LowerArm_{side}")
        bind_rigid_object(geometry[f"Forearm_{side}"], rig,
                          f"LowerArm_{side}")
        bind_rigid_object(geometry[f"Hand_{side}"], rig, f"Hand_{side}")


def attach_weapons_to_hand(rig, weapons):
    """Bone-parent each weapon root while preserving its authored world placement."""
    for weapon in weapons:
        world_matrix = weapon.matrix_world.copy()
        weapon.parent = rig
        weapon.parent_type = "BONE"
        weapon.parent_bone = "Hand_R"
        weapon.matrix_world = world_matrix


# -----------------------------------------------------------------------------
# Animation library
# -----------------------------------------------------------------------------


ANIMATED_BONES = (
    "Torso", "Head",
    "UpperArm_L", "LowerArm_L", "Hand_L",
    "UpperArm_R", "LowerArm_R", "Hand_R",
)


def key_pose(rig, frame, transforms):
    """Key one complete pose so clips remain independent and predictable."""
    for bone_name in ANIMATED_BONES:
        pose_bone = rig.pose.bones[bone_name]
        pose_bone.rotation_mode = "XYZ"
        pose_bone.location = (0.0, 0.0, 0.0)
        pose_bone.rotation_euler = (0.0, 0.0, 0.0)

        transform = transforms.get(bone_name, {})
        if "location" in transform:
            pose_bone.location = transform["location"]
        if "rotation" in transform:
            pose_bone.rotation_euler = tuple(
                math.radians(angle) for angle in transform["rotation"]
            )

        pose_bone.keyframe_insert("location", frame=frame, group=bone_name)
        pose_bone.keyframe_insert("rotation_euler", frame=frame, group=bone_name)


def create_action(rig, name, frame_end, poses, loop=False):
    """Create one named Action from a list of (frame, pose dictionary) pairs."""
    action = bpy.data.actions.new(name)
    action.use_fake_user = True
    action.use_frame_range = True
    action.frame_start = 1
    action.frame_end = frame_end
    action.use_cyclic = loop
    action["unity_loop"] = loop
    action["unity_clip_name"] = name
    rig.animation_data.action = action

    for frame, pose in poses:
        key_pose(rig, frame, pose)

    return action


def create_animation_library(rig):
    """Build a compact set of in-place gameplay animations at 30 FPS."""
    rig.animation_data_create()
    bpy.context.scene.render.fps = 30

    neutral = {}
    idle_high = {
        "Torso": {"location": (0.0, 0.0, 0.035), "rotation": (1.5, 0.0, 0.0)},
        "Head": {"rotation": (-1.5, 0.0, 2.0)},
        "UpperArm_L": {"rotation": (0.0, -2.5, 1.5)},
        "UpperArm_R": {"rotation": (0.0, 2.5, -1.5)},
        "Hand_L": {"rotation": (0.0, 0.0, -3.0)},
        "Hand_R": {"rotation": (0.0, 0.0, 3.0)},
    }
    idle_low = {
        "Torso": {"location": (0.0, 0.0, -0.02), "rotation": (-1.0, 0.0, 0.0)},
        "Head": {"rotation": (1.0, 0.0, -2.0)},
        "UpperArm_L": {"rotation": (0.0, 2.0, -1.0)},
        "UpperArm_R": {"rotation": (0.0, -2.0, 1.0)},
    }
    create_action(rig, "Idle", 48, [
        (1, neutral), (13, idle_high), (25, neutral), (37, idle_low),
        (48, neutral),
    ], loop=True)

    move_a = {
        "Torso": {"location": (0.0, -0.015, 0.025), "rotation": (-7.0, 0.0, 2.5)},
        "Head": {"rotation": (4.0, 0.0, -2.5)},
        "UpperArm_L": {"rotation": (8.0, -3.0, 5.0)},
        "LowerArm_L": {"rotation": (-5.0, 0.0, 0.0)},
        "UpperArm_R": {"rotation": (-6.0, 3.0, -5.0)},
    }
    move_b = {
        "Torso": {"location": (0.0, 0.015, -0.015), "rotation": (-5.0, 0.0, -2.5)},
        "Head": {"rotation": (3.0, 0.0, 2.5)},
        "UpperArm_L": {"rotation": (-6.0, -3.0, 5.0)},
        "UpperArm_R": {"rotation": (8.0, 3.0, -5.0)},
        "LowerArm_R": {"rotation": (-5.0, 0.0, 0.0)},
    }
    create_action(rig, "HoverMove", 24, [
        (1, move_a), (7, neutral), (13, move_b), (19, neutral), (24, move_a),
    ], loop=True)

    backward_a = {
        "Torso": {"location": (0.0, 0.0, 0.02), "rotation": (7.0, 0.0, 2.0)},
        "Head": {"rotation": (-4.0, 0.0, -2.0)},
        "UpperArm_L": {"rotation": (-5.0, 0.0, -4.0)},
        "UpperArm_R": {"rotation": (5.0, 0.0, 4.0)},
    }
    backward_b = {
        "Torso": {"location": (0.0, 0.0, -0.015), "rotation": (6.0, 0.0, -2.0)},
        "Head": {"rotation": (-3.0, 0.0, 2.0)},
        "UpperArm_L": {"rotation": (5.0, 0.0, -4.0)},
        "UpperArm_R": {"rotation": (-5.0, 0.0, 4.0)},
    }
    create_action(rig, "HoverMove_Backward", 24, [
        (1, backward_a), (7, neutral), (13, backward_b), (19, neutral),
        (24, backward_a),
    ], loop=True)

    strafe_l = {
        "Torso": {"location": (0.0, 0.0, 0.015), "rotation": (0.0, -9.0, 4.0)},
        "Head": {"rotation": (0.0, 5.0, -3.0)},
        "UpperArm_L": {"rotation": (3.0, -5.0, 7.0)},
        "UpperArm_R": {"rotation": (-3.0, 5.0, -2.0)},
    }
    strafe_r = {
        "Torso": {"location": (0.0, 0.0, 0.015), "rotation": (0.0, 9.0, -4.0)},
        "Head": {"rotation": (0.0, -5.0, 3.0)},
        "UpperArm_L": {"rotation": (-3.0, -5.0, 2.0)},
        "UpperArm_R": {"rotation": (3.0, 5.0, -7.0)},
    }
    create_action(rig, "Strafe_L", 24, [
        (1, strafe_l), (7, neutral), (13, strafe_l), (19, neutral),
        (24, strafe_l),
    ], loop=True)
    create_action(rig, "Strafe_R", 24, [
        (1, strafe_r), (7, neutral), (13, strafe_r), (19, neutral),
        (24, strafe_r),
    ], loop=True)

    reach = {
        "Torso": {"rotation": (-7.0, 0.0, 0.0)},
        "Head": {"rotation": (8.0, 0.0, 0.0)},
        "UpperArm_L": {"rotation": (-24.0, -8.0, 9.0)},
        "LowerArm_L": {"rotation": (-25.0, 0.0, 0.0)},
        "Hand_L": {"rotation": (10.0, 0.0, -5.0)},
        "UpperArm_R": {"rotation": (-24.0, 8.0, -9.0)},
        "LowerArm_R": {"rotation": (-25.0, 0.0, 0.0)},
        "Hand_R": {"rotation": (10.0, 0.0, 5.0)},
    }
    carry = {
        "Torso": {"rotation": (-3.0, 0.0, 0.0)},
        "Head": {"rotation": (3.0, 0.0, 0.0)},
        "UpperArm_L": {"rotation": (-18.0, -6.0, 8.0)},
        "LowerArm_L": {"rotation": (-18.0, 0.0, 0.0)},
        "Hand_L": {"rotation": (6.0, 0.0, -3.0)},
        "UpperArm_R": {"rotation": (-18.0, 6.0, -8.0)},
        "LowerArm_R": {"rotation": (-18.0, 0.0, 0.0)},
        "Hand_R": {"rotation": (6.0, 0.0, 3.0)},
    }
    create_action(rig, "Pickup", 36, [
        (1, neutral), (10, {"Torso": {"rotation": (5.0, 0.0, 0.0)}}),
        (24, reach), (36, carry),
    ])

    carry_high = dict(carry)
    carry_high["Torso"] = {"location": (0.0, 0.0, 0.025), "rotation": (-3.0, 0.0, 1.0)}
    carry_low = dict(carry)
    carry_low["Torso"] = {"location": (0.0, 0.0, -0.015), "rotation": (-3.0, 0.0, -1.0)}
    create_action(rig, "Carry_Idle", 48, [
        (1, carry), (13, carry_high), (25, carry), (37, carry_low),
        (48, carry),
    ], loop=True)

    carry_move_a = dict(carry)
    carry_move_a["Torso"] = {"location": (0.0, 0.0, 0.02), "rotation": (-7.0, 0.0, 2.0)}
    carry_move_b = dict(carry)
    carry_move_b["Torso"] = {"location": (0.0, 0.0, -0.015), "rotation": (-6.0, 0.0, -2.0)}
    create_action(rig, "Carry_Move", 24, [
        (1, carry_move_a), (7, carry), (13, carry_move_b), (19, carry),
        (24, carry_move_a),
    ], loop=True)

    create_action(rig, "Drop", 24, [
        (1, carry), (10, reach), (18, {"Torso": {"rotation": (3.0, 0.0, 0.0)}}),
        (24, neutral),
    ])
    create_action(rig, "Hold_Item", 36, [
        (1, carry), (18, carry_high), (36, carry),
    ], loop=True)

    point = {
        "Torso": {"rotation": (0.0, -5.0, -4.0)},
        "Head": {"rotation": (0.0, 10.0, 5.0)},
        "UpperArm_R": {"rotation": (-38.0, 2.0, -28.0)},
        "LowerArm_R": {"rotation": (8.0, 0.0, 0.0)},
        "Hand_R": {"rotation": (-6.0, 0.0, 0.0)},
    }
    create_action(rig, "Point", 36, [
        (1, neutral), (10, point), (27, point), (36, neutral),
    ])

    hit = {
        "Torso": {"location": (0.0, 0.04, -0.02), "rotation": (15.0, 0.0, 12.0)},
        "Head": {"rotation": (-12.0, 0.0, -18.0)},
        "UpperArm_L": {"rotation": (18.0, -8.0, -15.0)},
        "UpperArm_R": {"rotation": (-12.0, 8.0, 18.0)},
    }
    create_action(rig, "Hit_Reaction", 24, [
        (1, neutral), (5, hit), (13, {"Torso": {"rotation": (-5.0, 0.0, -4.0)}}),
        (24, neutral),
    ])

    jump_peak = {
        "Torso": {"location": (0.0, 0.0, 0.06), "rotation": (-5.0, 0.0, 0.0)},
        "Head": {"rotation": (5.0, 0.0, 0.0)},
        "UpperArm_L": {"rotation": (8.0, -5.0, 10.0)},
        "UpperArm_R": {"rotation": (8.0, 5.0, -10.0)},
    }
    create_action(rig, "Jump", 30, [
        (1, neutral), (8, {"Torso": {"location": (0.0, 0.0, -0.04)}}),
        (16, jump_peak), (30, neutral),
    ])

    pistol_hold = {
        "Torso": {"rotation": (-2.0, 0.0, -4.0)},
        "Head": {"rotation": (2.0, 0.0, 4.0)},
        "UpperArm_R": {"rotation": (-38.0, 9.0, -28.0)},
        "LowerArm_R": {"rotation": (-20.0, 0.0, 0.0)},
        "Hand_R": {"rotation": (-8.0, 0.0, 0.0)},
        "UpperArm_L": {"rotation": (-14.0, -5.0, 12.0)},
    }
    rifle_hold = {
        "Torso": {"rotation": (-5.0, 0.0, 0.0)},
        "Head": {"rotation": (4.0, 0.0, 0.0)},
        "UpperArm_R": {"rotation": (-34.0, 8.0, -24.0)},
        "LowerArm_R": {"rotation": (-24.0, 0.0, 0.0)},
        "Hand_R": {"rotation": (-6.0, 0.0, 0.0)},
        "UpperArm_L": {"rotation": (-31.0, -12.0, 24.0)},
        "LowerArm_L": {"rotation": (-31.0, 0.0, 0.0)},
        "Hand_L": {"rotation": (4.0, 0.0, -6.0)},
    }
    shotgun_hold = dict(rifle_hold)
    shotgun_hold["Torso"] = {"rotation": (-7.0, 0.0, 0.0)}
    shotgun_hold["UpperArm_L"] = {"rotation": (-36.0, -13.0, 27.0)}

    def breathing_pose(base, height, roll):
        pose = dict(base)
        torso_rotation = base.get("Torso", {}).get("rotation", (0.0, 0.0, 0.0))
        pose["Torso"] = {
            "location": (0.0, 0.0, height),
            "rotation": (torso_rotation[0], torso_rotation[1], roll),
        }
        return pose

    def weapon_walk_pose(base, side):
        pose = dict(base)
        torso_rotation = base["Torso"]["rotation"]
        pose["Torso"] = {
            "location": (0.0, -0.015, 0.025 if side > 0 else -0.015),
            "rotation": (torso_rotation[0] - 3.0, 0.0, side * 2.5),
        }
        pose["Head"] = {"rotation": (4.0, 0.0, -side * 2.0)}
        return pose

    for prefix, hold in (("Pistol", pistol_hold),
                         ("AssaultRifle", rifle_hold),
                         ("Shotgun", shotgun_hold)):
        create_action(rig, prefix + "_Idle", 42, [
            (1, hold), (11, breathing_pose(hold, 0.025, 1.0)),
            (22, hold), (32, breathing_pose(hold, -0.015, -1.0)),
            (42, hold),
        ], loop=True)
        create_action(rig, prefix + "_Walk", 24, [
            (1, weapon_walk_pose(hold, 1.0)), (7, hold),
            (13, weapon_walk_pose(hold, -1.0)), (19, hold),
            (24, weapon_walk_pose(hold, 1.0)),
        ], loop=True)

    pistol_recoil = dict(pistol_hold)
    pistol_recoil["Torso"] = {"location": (0.0, 0.06, 0.0), "rotation": (9.0, 0.0, -6.0)}
    pistol_recoil["UpperArm_R"] = {"rotation": (-24.0, 9.0, -28.0)}
    create_action(rig, "Pistol_Shoot", 14, [
        (1, pistol_hold), (3, pistol_recoil), (8, pistol_hold), (14, pistol_hold),
    ])

    rifle_recoil = dict(rifle_hold)
    rifle_recoil["Torso"] = {"location": (0.0, 0.025, 0.0), "rotation": (0.0, 0.0, 0.0)}
    rifle_recoil["UpperArm_R"] = {"rotation": (-29.0, 8.0, -24.0)}
    create_action(rig, "AssaultRifle_Shoot", 8, [
        (1, rifle_hold), (2, rifle_recoil), (4, rifle_hold),
        (5, rifle_recoil), (8, rifle_hold),
    ])

    shotgun_recoil = dict(shotgun_hold)
    shotgun_recoil["Torso"] = {"location": (0.0, 0.12, -0.025), "rotation": (18.0, 0.0, 0.0)}
    shotgun_recoil["Head"] = {"rotation": (-10.0, 0.0, 0.0)}
    shotgun_recoil["UpperArm_R"] = {"rotation": (-15.0, 8.0, -22.0)}
    shotgun_recoil["UpperArm_L"] = {"rotation": (-18.0, -11.0, 20.0)}
    create_action(rig, "Shotgun_Shoot", 20, [
        (1, shotgun_hold), (4, shotgun_recoil), (11, shotgun_hold),
        (20, shotgun_hold),
    ])

    # Leave Idle active so pressing Play in Blender immediately previews safely.
    rig.animation_data.action = bpy.data.actions["Idle"]
    bpy.context.scene.frame_start = 1
    bpy.context.scene.frame_end = 48
    bpy.context.scene.frame_set(1)


# -----------------------------------------------------------------------------
# Assembly
# -----------------------------------------------------------------------------


def setup_character():
    clear_scene()
    materials = create_materials()

    root = create_empty("CouchGuy_Root")
    root.empty_display_type = "CIRCLE"
    root.empty_display_size = 0.3

    create_head(materials, root)
    create_torso(materials, root)
    left_arm = create_arm("L", materials, root)
    right_arm = create_arm("R", materials, root)
    create_hover_ring(materials, root)
    weapons = create_weapon_models(materials, root)

    rig = create_armature(left_arm, right_arm, root)
    geometry = {obj.name: obj for obj in bpy.context.scene.objects}
    rig_character(rig, geometry)
    attach_weapons_to_hand(rig, weapons)
    create_animation_library(rig)

    # Clean selection and a useful default viewport presentation.
    bpy.ops.object.select_all(action="DESELECT")
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig

    # Metadata documents assumptions for future exporters/tools.
    root["character_name"] = "Couch Guy Robot"
    root["forward_axis"] = "-Y"
    root["unity_scale_meters"] = 1.0
    root["player_glow_material"] = "PlayerGlow"

    print("Couch Guy robot generated successfully.")
    return root, rig


if __name__ == "__main__":
    setup_character()
