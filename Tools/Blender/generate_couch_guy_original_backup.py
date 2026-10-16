
"""Couch Guys floating robot character and expressive animation generator.

Preserves the original character geometry, both arms, three weapons, and rig.
Creates 36 Blender animation Actions for Unity export.

Blender: Scripting > Run Script
FBX: -Z Forward, Y Up, Bake Animation, All Actions enabled.
Turn OFF the "Visible Objects" export filter to include hidden weapons.

WARNING: Replaces all objects and Actions in the current Blender scene.
"""

import math
import bpy
from mathutils import Vector

# Which weapon to display while inspecting the model in Blender.
# Other weapons still exist and can be exported.
# Options: None, "Pistol", "AssaultRifle", "Shotgun"
PREVIEW_WEAPON = "Pistol"


# ============================================================
# SCENE AND MATERIALS
# ============================================================

def clear_scene():
    if bpy.context.object and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")

    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)

    for action in list(bpy.data.actions):
        bpy.data.actions.remove(action)

    for group in (
        bpy.data.meshes,
        bpy.data.curves,
        bpy.data.armatures,
        bpy.data.materials
    ):
        for item in list(group):
            if item.users == 0:
                group.remove(item)


def make_material(
    name, colour, metallic=0.0, roughness=0.35,
    emission_color=None, emission_strength=0.0
):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    mat.diffuse_color = colour

    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = colour
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Roughness"].default_value = roughness

    emission = (
        bsdf.inputs.get("Emission Color")
        or bsdf.inputs.get("Emission")
    )
    if emission is not None and emission_color is not None:
        emission.default_value = emission_color

    strength = bsdf.inputs.get("Emission Strength")
    if strength is not None:
        strength.default_value = emission_strength

    return mat


def create_materials():
    return {
        "white": make_material(
            "WhiteBody", (0.92, 0.95, 1.0, 1.0),
            metallic=0.05, roughness=0.2
        ),
        "visor": make_material(
            "DarkVisor", (0.008, 0.012, 0.022, 1.0),
            metallic=0.35, roughness=0.12
        ),
        "glow": make_material(
            "PlayerGlow", (0.015, 0.28, 1.0, 1.0),
            emission_color=(0.01, 0.35, 1.0, 1.0),
            emission_strength=7.0
        ),
        "joints": make_material(
            "DarkJoints", (0.035, 0.045, 0.065, 1.0),
            metallic=0.3, roughness=0.28
        ),
        "gunmetal": make_material(
            "WeaponGunmetal", (0.055, 0.07, 0.095, 1.0),
            metallic=0.55, roughness=0.24
        ),
        "weapon_accent": make_material(
            "WeaponAccent", (1.0, 0.22, 0.06, 1.0),
            emission_color=(1.0, 0.06, 0.01, 1.0),
            emission_strength=2.0
        ),
    }


def assign_material(obj, mat):
    obj.data.materials.append(mat)


def smooth_mesh(obj):
    for polygon in obj.data.polygons:
        polygon.use_smooth = True


def apply_transforms(obj):
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.transform_apply(
        location=False, rotation=True, scale=True
    )
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


def rounded_box(
    name, location, dimensions, material,
    bevel=0.1, rotation=(0, 0, 0)
):
    bpy.ops.mesh.primitive_cube_add(
        location=location, rotation=rotation
    )
    obj = bpy.context.object
    obj.name = name
    obj.dimensions = dimensions

    apply_transforms(obj)
    add_bevel(
        obj, min(bevel, min(dimensions) * 0.45), 4
    )
    assign_material(obj, material)
    return obj


def ellipsoid(
    name, location, scale, material,
    segments=24, rings=16, rotation=(0, 0, 0)
):
    bpy.ops.mesh.primitive_uv_sphere_add(
        segments=segments,
        ring_count=rings,
        location=location,
        rotation=rotation
    )
    obj = bpy.context.object
    obj.name = name
    obj.scale = scale

    apply_transforms(obj)
    smooth_mesh(obj)
    assign_material(obj, material)
    return obj


def capsule(name, start, end, radius, material, bevel=None):
    start = Vector(start)
    end = Vector(end)
    direction = end - start

    obj = rounded_box(
        name,
        (start + end) * 0.5,
        (radius * 2, radius * 2, direction.length),
        material,
        bevel=bevel if bevel is not None else radius * 0.9
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

    if parent is not None:
        obj.parent = parent

    return obj


# ============================================================
# ORIGINAL CHARACTER MODEL
# ============================================================

def create_head(materials, root):
    head = rounded_box(
        "Head", (0, 0, 1.82), (1.42, 0.94, 1.02),
        materials["white"], bevel=0.25
    )
    parent_to(head, root)

    visor = rounded_box(
        "Visor", (0, -0.486, 1.84), (1.10, 0.09, 0.52),
        materials["visor"], bevel=0.15
    )
    parent_to(visor, root)

    for side, x in (("L", -0.27), ("R", 0.27)):
        eye = rounded_box(
            "Eye_" + side,
            (x, -0.542, 1.86),
            (0.31, 0.045, 0.18),
            materials["glow"],
            bevel=0.09
        )
        parent_to(eye, root)

    for side, x in (("L", -0.77), ("R", 0.77)):
        ear = ellipsoid(
            "Ear_" + side,
            (x, 0, 1.84),
            (0.18, 0.12, 0.39),
            materials["joints"],
            segments=20,
            rings=12,
            rotation=(
                0, 0,
                math.radians(8 if side == "L" else -8)
            )
        )

        light_x = x + (-0.105 if side == "L" else 0.105)
        light = ellipsoid(
            "EarLight_" + side,
            (light_x, -0.018, 1.84),
            (0.035, 0.105, 0.29),
            materials["glow"],
            segments=16,
            rings=10
        )

        parent_to(ear, root)
        parent_to(light, root)

    return head


def create_torso(materials, root):
    torso = ellipsoid(
        "Torso", (0, 0.04, 0.94),
        (0.58, 0.42, 0.58),
        materials["white"]
    )
    parent_to(torso, root)

    neck = ellipsoid(
        "NeckJoint", (0, 0.02, 1.37),
        (0.28, 0.24, 0.13),
        materials["joints"],
        segments=20,
        rings=12
    )
    parent_to(neck, root)

    chest = rounded_box(
        "ChestPanel", (0, -0.405, 1.01),
        (0.48, 0.035, 0.24),
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
    sign = -1 if side == "L" else 1
    wrist = Vector(wrist)

    palm_center = wrist + Vector(
        (sign * 0.03, -0.13, -0.02)
    )

    pieces = [
        rounded_box(
            "Hand_" + side + "_Palm",
            palm_center,
            (0.39, 0.32, 0.31),
            materials["joints"],
            bevel=0.13
        )
    ]

    for index, offset in enumerate(
        (-0.135, -0.045, 0.045, 0.135), start=1
    ):
        x = palm_center.x + offset

        start = (
            x,
            palm_center.y - 0.10,
            palm_center.z - 0.025
        )
        end = (
            x,
            palm_center.y - 0.30 + abs(offset) * 0.18,
            palm_center.z - 0.08
        )

        pieces.append(
            capsule(
                "Finger_%d_%s" % (index, side),
                start, end, 0.058,
                materials["joints"],
                bevel=0.052
            )
        )

    thumb_start = palm_center + Vector(
        (-sign * 0.16, -0.015, -0.015)
    )
    thumb_end = palm_center + Vector(
        (-sign * 0.24, -0.17, -0.08)
    )

    pieces.append(
        capsule(
            "Thumb_" + side,
            thumb_start,
            thumb_end,
            0.072,
            materials["joints"],
            bevel=0.065
        )
    )

    return join_objects(pieces, "Hand_" + side)


def create_arm(side, materials, root):
    sign = -1 if side == "L" else 1

    arm_root = create_empty("Arm_" + side, root)

    shoulder = Vector((sign * 0.58, -0.015, 1.19))
    elbow = Vector((sign * 0.86, -0.16, 1.00))
    wrist = Vector((sign * 0.98, -0.42, 0.88))

    shoulder_joint = ellipsoid(
        "ShoulderJoint_" + side,
        shoulder, (0.18, 0.18, 0.18),
        materials["joints"],
        segments=18, rings=12
    )

    upper = capsule(
        "UpperArm_" + side,
        shoulder, elbow,
        0.19, materials["white"],
        bevel=0.14
    )

    elbow_joint = ellipsoid(
        "ElbowJoint_" + side,
        elbow, (0.15, 0.15, 0.15),
        materials["joints"],
        segments=18, rings=12
    )

    forearm = capsule(
        "Forearm_" + side,
        elbow, wrist,
        0.175, materials["white"],
        bevel=0.13
    )

    hand = create_hand(side, wrist, materials)

    for obj in (
        shoulder_joint, upper,
        elbow_joint, forearm, hand
    ):
        parent_to(obj, arm_root)

    return {
        "root": arm_root,
        "shoulder_joint": shoulder_joint,
        "upper": upper,
        "elbow_joint": elbow_joint,
        "forearm": forearm,
        "hand": hand,
        "points": (shoulder, elbow, wrist)
    }


def create_hover_ring(materials, root):
    bpy.ops.mesh.primitive_torus_add(
        major_radius=0.48,
        minor_radius=0.065,
        major_segments=32,
        minor_segments=10,
        location=(0, 0.02, 0.16)
    )

    ring = bpy.context.object
    ring.name = "HoverRing"
    smooth_mesh(ring)
    assign_material(ring, materials["glow"])
    parent_to(ring, root)
    apply_transforms(ring)

    return ring


# ============================================================
# COMPLETE WEAPON MODELS
# ============================================================

def create_weapon_models(materials, root):
    weapons = []

    specifications = (
        ("Weapon_Pistol", 0.62, 0.18, 0.22, 0.16),
        ("Weapon_AssaultRifle", 1.18, 0.17, 0.24, 0.34),
        ("Weapon_Shotgun", 1.34, 0.20, 0.22, 0.42)
    )

    for name, length, width, height, stock_length in specifications:
        weapon = create_empty(name, root)

        body = rounded_box(
            name + "_Body",
            (0.98, -0.72 - length * 0.38, 0.91),
            (width, length, height),
            materials["gunmetal"],
            bevel=0.055
        )

        grip = rounded_box(
            name + "_Grip",
            (0.98, -0.58, 0.75),
            (0.15, 0.24, 0.32),
            materials["joints"],
            bevel=0.05,
            rotation=(math.radians(-18), 0, 0)
        )

        barrel = capsule(
            name + "_Barrel",
            (0.98, -0.78, 0.94),
            (0.98, -0.78 - length * 0.58, 0.94),
            0.075 if name == "Weapon_Shotgun" else 0.055,
            materials["gunmetal"],
            bevel=0.035
        )

        accent = rounded_box(
            name + "_Accent",
            (0.98, -0.73, 1.025),
            (
                width * 0.72,
                max(0.16, length * 0.28),
                0.035
            ),
            materials["weapon_accent"],
            bevel=0.015
        )

        pieces = [body, grip, barrel, accent]

        if name != "Weapon_Pistol":
            stock = rounded_box(
                name + "_Stock",
                (0.98, -0.47 + stock_length * 0.35, 0.88),
                (
                    width * 1.25,
                    stock_length,
                    height * 1.25
                ),
                materials["joints"],
                bevel=0.07
            )
            pieces.append(stock)

        if name == "Weapon_AssaultRifle":
            magazine = rounded_box(
                name + "_Magazine",
                (0.98, -0.76, 0.72),
                (0.16, 0.24, 0.34),
                materials["weapon_accent"],
                bevel=0.045,
                rotation=(math.radians(-10), 0, 0)
            )
            pieces.append(magazine)

        elif name == "Weapon_Shotgun":
            pump = rounded_box(
                name + "_Pump",
                (0.98, -1.15, 0.91),
                (0.24, 0.34, 0.20),
                materials["weapon_accent"],
                bevel=0.06
            )
            pieces.append(pump)

        for piece in pieces:
            parent_to(piece, weapon)

        muzzle = create_empty("Muzzle", weapon)
        muzzle.location = (
            0.98,
            -0.82 - length * 0.58,
            0.94
        )

        weapon["unity_default_active"] = False
        weapons.append(weapon)

    return weapons


# ============================================================
# RIGGING
# ============================================================

def create_armature(left_arm, right_arm, root):
    data = bpy.data.armatures.new("CouchGuy_Armature")
    rig = bpy.data.objects.new("CouchGuy_Rig", data)
    bpy.context.collection.objects.link(rig)

    rig.parent = root
    rig.show_in_front = True
    data.display_type = "OCTAHEDRAL"

    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = rig
    rig.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")

    bones = {}

    def add_bone(name, head, tail, parent=None, connected=False):
        bone = data.edit_bones.new(name)
        bone.head = head
        bone.tail = tail

        if parent:
            bone.parent = bones[parent]
            bone.use_connect = connected

        bones[name] = bone
        return bone

    add_bone("Root", (0, 0, 0), (0, 0, 0.28))
    add_bone(
        "Torso",
        (0, 0, 0.28), (0, 0, 1.38),
        "Root", True
    )
    add_bone(
        "Head",
        (0, 0, 1.38), (0, 0, 2.22),
        "Torso", True
    )

    # Eye bones enable blinking without changing original geometry.
    for side, x in (("L", -0.27), ("R", 0.27)):
        add_bone(
            "Eye_" + side,
            (x, -0.542, 1.86),
            (x, -0.542, 2.02),
            "Head"
        )

    for side, arm in (
        ("L", left_arm),
        ("R", right_arm)
    ):
        shoulder, elbow, wrist = arm["points"]
        hand_tail = wrist + Vector((0, -0.34, -0.07))

        add_bone(
            "UpperArm_" + side,
            shoulder, elbow,
            "Torso"
        )
        add_bone(
            "LowerArm_" + side,
            elbow, wrist,
            "UpperArm_" + side,
            True
        )
        add_bone(
            "Hand_" + side,
            wrist, hand_tail,
            "LowerArm_" + side,
            True
        )

    bpy.ops.object.mode_set(mode="OBJECT")
    rig.select_set(False)
    return rig


def bind_rigid_object(obj, rig, bone_name):
    group = obj.vertex_groups.new(name=bone_name)
    group.add(
        list(range(len(obj.data.vertices))),
        1.0,
        "REPLACE"
    )

    modifier = obj.modifiers.new("Armature", "ARMATURE")
    modifier.object = rig
    modifier.use_vertex_groups = True


def rig_character(rig, geometry):
    for name in (
        "Head", "Visor",
        "Ear_L", "Ear_R",
        "EarLight_L", "EarLight_R"
    ):
        bind_rigid_object(geometry[name], rig, "Head")

    for side in ("L", "R"):
        bind_rigid_object(
            geometry["Eye_" + side],
            rig, "Eye_" + side
        )

    for name in ("Torso", "NeckJoint", "ChestPanel"):
        bind_rigid_object(geometry[name], rig, "Torso")

    bind_rigid_object(geometry["HoverRing"], rig, "Root")

    for side in ("L", "R"):
        bind_rigid_object(
            geometry["ShoulderJoint_" + side],
            rig, "UpperArm_" + side
        )
        bind_rigid_object(
            geometry["UpperArm_" + side],
            rig, "UpperArm_" + side
        )
        bind_rigid_object(
            geometry["ElbowJoint_" + side],
            rig, "LowerArm_" + side
        )
        bind_rigid_object(
            geometry["Forearm_" + side],
            rig, "LowerArm_" + side
        )
        bind_rigid_object(
            geometry["Hand_" + side],
            rig, "Hand_" + side
        )


def attach_weapons_to_hand(rig, weapons):
    for weapon in weapons:
        world_matrix = weapon.matrix_world.copy()

        weapon.parent = rig
        weapon.parent_type = "BONE"
        weapon.parent_bone = "Hand_R"
        weapon.matrix_world = world_matrix


def set_weapon_preview(weapons, selected=PREVIEW_WEAPON):
    allowed = {"Pistol", "AssaultRifle", "Shotgun", None}

    if selected not in allowed:
        raise ValueError("Invalid preview weapon: " + str(selected))

    for weapon in weapons:
        visible = weapon.name == "Weapon_" + str(selected)

        stack = [weapon]
        while stack:
            obj = stack.pop()
            obj.hide_set(not visible)
            stack.extend(obj.children)


# ============================================================
# ANIMATION SYSTEM
# ============================================================

FPS = 30
TAU = math.tau

ANIMATED_BONES = (
    "Torso", "Head",
    "Eye_L", "Eye_R",
    "UpperArm_L", "LowerArm_L", "Hand_L",
    "UpperArm_R", "LowerArm_R", "Hand_R"
)


def copy_pose(base=None):
    return {
        bone: dict(values)
        for bone, values in (base or {}).items()
    }


def add_motion(
    pose, bone,
    rotation=(0, 0, 0),
    location=(0, 0, 0)
):
    data = pose.setdefault(bone, {})

    old_rotation = data.get("rotation", (0, 0, 0))
    old_location = data.get("location", (0, 0, 0))

    data["rotation"] = tuple(
        a + b for a, b in zip(old_rotation, rotation)
    )
    data["location"] = tuple(
        a + b for a, b in zip(old_location, location)
    )
    return pose


def eyes(pose, squint=0.0, widen=0.0):
    sx = 1.0 + 0.12 * widen - 0.16 * squint
    sz = max(
        0.065,
        1.0 + 0.22 * widen - 0.91 * squint
    )

    pose["Eye_L"] = {"scale": (sx, 1.0, sz)}
    pose["Eye_R"] = {"scale": (sx, 1.0, sz)}
    return pose


def gaussian(t, centre, width):
    return math.exp(-0.5 * ((t - centre) / width) ** 2)


def ease(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3 - 2 * t)


def blink(t, centres=(0.27, 0.76)):
    return min(
        1.0,
        sum(gaussian(t, c, 0.024) for c in centres)
    )


def blend_poses(a, b, amount):
    result = {}

    for bone in set(a) | set(b):
        av = a.get(bone, {})
        bv = b.get(bone, {})

        result[bone] = {}

        for key in ("rotation", "location", "scale"):
            if key not in av and key not in bv:
                continue

            neutral = (
                (1, 1, 1) if key == "scale"
                else (0, 0, 0)
            )

            x = av.get(key, neutral)
            y = bv.get(key, neutral)

            result[bone][key] = tuple(
                v + (w - v) * amount
                for v, w in zip(x, y)
            )

    return result


# ============================================================
# BASE POSES
# ============================================================

CARRY = {
    "Torso": {"rotation": (-6, 0, 0)},
    "Head": {"rotation": (5, 0, 0)},

    "UpperArm_L": {"rotation": (-26, -9, 13)},
    "LowerArm_L": {"rotation": (-25, 0, -4)},
    "Hand_L": {"rotation": (10, 0, -5)},

    "UpperArm_R": {"rotation": (-26, 9, -13)},
    "LowerArm_R": {"rotation": (-25, 0, 4)},
    "Hand_R": {"rotation": (10, 0, 5)}
}

WEAPON_HOLDS = {
    "Pistol": {
        "Torso": {"rotation": (-4, 0, -7)},
        "Head": {"rotation": (4, 1, 7)},
        "UpperArm_R": {"rotation": (-48, 10, -29)},
        "LowerArm_R": {"rotation": (-22, 0, 0)},
        "Hand_R": {"rotation": (-10, 0, 0)},
        "UpperArm_L": {"rotation": (-10, -8, 16)},
        "LowerArm_L": {"rotation": (-9, 0, 0)}
    },
    "AssaultRifle": {
        "Torso": {"rotation": (-8, 0, 0)},
        "Head": {"rotation": (7, 0, 0)},
        "UpperArm_R": {"rotation": (-43, 9, -25)},
        "LowerArm_R": {"rotation": (-29, 0, 0)},
        "Hand_R": {"rotation": (-9, 0, 0)},
        "UpperArm_L": {"rotation": (-40, -16, 29)},
        "LowerArm_L": {"rotation": (-38, 0, 0)},
        "Hand_L": {"rotation": (8, 0, -8)}
    },
    "Shotgun": {
        "Torso": {"rotation": (-11, 0, -2)},
        "Head": {"rotation": (9, 0, 2)},
        "UpperArm_R": {"rotation": (-47, 10, -24)},
        "LowerArm_R": {"rotation": (-32, 0, 0)},
        "Hand_R": {"rotation": (-9, 0, 0)},
        "UpperArm_L": {"rotation": (-45, -17, 31)},
        "LowerArm_L": {"rotation": (-42, 0, 0)},
        "Hand_L": {"rotation": (7, 0, -8)}
    }
}


# ============================================================
# IDLE ANIMATIONS
# ============================================================

def idle_pose(t):
    p = {}

    s = math.sin(TAU * t)
    q = math.sin(TAU * t + 0.72)

    add_motion(
        p, "Torso",
        (-1.8 + 2.5 * s, 2.2 * q, 3.0 * s),
        (
            0.014 * math.sin(2 * TAU * t),
            0,
            0.055 * q
        )
    )

    add_motion(
        p, "Head",
        (
            3.5 * math.sin(TAU * t + 1.0),
            7 * math.sin(TAU * t + 0.24),
            -4 * s
        )
    )

    for side, sign, phase in (
        ("L", -1, 0),
        ("R", 1, math.pi)
    ):
        swing = math.sin(TAU * t + phase)
        lag = math.sin(TAU * t + phase - 0.7)

        add_motion(
            p, "UpperArm_" + side,
            (
                11 * swing,
                sign * 5 + 4 * lag,
                -sign * 6 + 7 * swing
            )
        )
        add_motion(
            p, "LowerArm_" + side,
            (-5 + 9 * lag, 0, -5 * swing)
        )
        add_motion(
            p, "Hand_" + side,
            (6 * lag, 0, sign * 5 * swing)
        )

    return eyes(p, blink(t))


def idle_scan(t):
    p = idle_pose(t)

    glance = (
        math.sin(math.pi * t)
        * math.sin(2 * math.pi * t)
    )

    add_motion(
        p, "Head",
        (
            -4 * math.sin(2 * math.pi * t),
            30 * glance,
            -9 * glance
        )
    )
    add_motion(
        p, "Torso",
        (0, -8 * glance, 7 * glance)
    )
    add_motion(
        p, "UpperArm_R",
        (-14 * math.sin(math.pi * t), 0, -7 * glance)
    )

    return eyes(
        p,
        blink(t, (0.12, 0.88)),
        widen=0.35 * math.sin(math.pi * t) ** 2
    )


def idle_playful(t):
    p = idle_pose(t)
    amount = math.sin(math.pi * t) ** 2

    add_motion(
        p, "Torso",
        (-7 * amount, 12 * amount, 12 * amount),
        (0, -0.05 * amount, 0.09 * amount)
    )
    add_motion(
        p, "Head",
        (9 * amount, -17 * amount, -16 * amount)
    )
    add_motion(
        p, "UpperArm_L",
        (-26 * amount, -7 * amount, 16 * amount)
    )
    add_motion(
        p, "Hand_L",
        (-15 * amount, 0, -18 * amount)
    )
    add_motion(
        p, "UpperArm_R",
        (13 * amount, 4 * amount, -12 * amount)
    )

    return eyes(
        p,
        blink(t, (0.07, 0.87)),
        widen=0.3 * amount
    )


def idle_wave(t):
    p = idle_pose(t)
    amount = math.sin(math.pi * t) ** 2

    add_motion(
        p, "Torso",
        (0, -4 * amount, -7 * amount),
        (0, 0, 0.035 * amount)
    )
    add_motion(
        p, "Head",
        (-3 * amount, 11 * amount, 11 * amount)
    )
    add_motion(
        p, "UpperArm_R",
        (-74 * amount, 0, -48 * amount)
    )
    add_motion(
        p, "LowerArm_R",
        (34 * amount, 0, -16 * amount)
    )
    add_motion(
        p, "Hand_R",
        (
            7 * amount,
            0,
            34 * amount * math.sin(8 * math.pi * t)
        )
    )

    return eyes(
        p,
        blink(t, (0.10, 0.90)),
        widen=0.32 * amount
    )


# ============================================================
# LOCOMOTION
# ============================================================

def movement_pose(t, mode="forward"):
    s = math.sin(TAU * t)
    c = math.cos(TAU * t)
    bounce = math.cos(2 * TAU * t)

    if mode == "forward":
        pitch, roll, lateral = -11, 5 * s, 0

    elif mode == "backward":
        pitch, roll, lateral = 9, -5 * s, 0

    elif mode == "sprint":
        pitch, roll, lateral = -21, 9 * s, 0

    else:
        direction = -1 if mode == "left" else 1
        pitch = -3
        roll = -14 * direction + 3 * s
        lateral = direction * 0.035

    speed = 1.5 if mode == "sprint" else 1.0
    p = {}

    add_motion(
        p, "Torso",
        (pitch + 2 * bounce, 2 * s, roll),
        (
            lateral + 0.035 * s,
            0.02 * c,
            0.035 * speed * bounce
        )
    )

    add_motion(
        p, "Head",
        (
            -pitch * 0.65 - 3 * bounce,
            -2 * s,
            -roll * 0.65
        )
    )

    for side, sign, phase in (
        ("L", -1, 0),
        ("R", 1, math.pi)
    ):
        v = math.sin(TAU * t + phase)
        lag = math.sin(TAU * t + phase - 0.7)

        add_motion(
            p, "UpperArm_" + side,
            (
                speed * 15 * v
                + (17 if mode == "sprint" else 0),
                sign * (
                    -4 if mode == "sprint" else 3
                ),
                -sign * (
                    7 if mode == "sprint" else 4
                ) + speed * 7 * v
            )
        )

        add_motion(
            p, "LowerArm_" + side,
            (-10 + 10 * lag, 0, -6 * lag)
        )

        add_motion(
            p, "Hand_" + side,
            (7 * lag, 0, 9 * v)
        )

    return eyes(p, blink(t, (0.48,)))


# ============================================================
# COUCH CARRYING
# ============================================================

def carry_pose(t, mode="idle"):
    p = copy_pose(CARRY)

    s = math.sin(TAU * t)
    bounce = math.sin(2 * TAU * t)

    effort = (
        1.0 if mode == "idle"
        else 2.2 if mode == "sprint"
        else 1.5
    )

    if mode == "struggle":
        effort = 3.0

    add_motion(
        p, "Torso",
        (
            -1.5 * effort + 2 * bounce,
            0,
            2.5 * effort * s
        ),
        (0, 0.015 * s, 0.027 * effort * bounce)
    )

    add_motion(
        p, "Head",
        (
            2 * effort - 2 * bounce,
            2 * s,
            -3 * effort * s
        )
    )

    for side, sign in (("L", -1), ("R", 1)):
        add_motion(
            p, "UpperArm_" + side,
            (-3 * bounce, 0, sign * 2.2 * s)
        )
        add_motion(
            p, "LowerArm_" + side,
            (-2.6 * s, 0, sign * bounce)
        )
        add_motion(
            p, "Hand_" + side,
            (2.5 * s, 0, sign * 2 * bounce)
        )

    if mode == "struggle":
        add_motion(
            p, "Torso",
            (
                0,
                6 * math.sin(6 * TAU * t),
                5 * math.sin(5 * TAU * t)
            )
        )

    return eyes(
        p,
        squint=min(
            1.0,
            blink(t, (0.31,))
            + (0.21 if mode == "struggle" else 0)
        )
    )


# ============================================================
# WEAPON ANIMATIONS
# ============================================================

def weapon_idle(t, weapon):
    p = copy_pose(WEAPON_HOLDS[weapon])

    breathe = math.sin(TAU * t)

    add_motion(
        p, "Torso",
        (
            1.5 * breathe,
            2.8 * math.sin(TAU * t + 0.4),
            2 * breathe
        ),
        (0, 0, 0.03 * breathe)
    )

    add_motion(
        p, "Head",
        (
            -2 * breathe,
            4 * math.sin(TAU * t + 0.7),
            -2.4 * breathe
        )
    )

    add_motion(
        p, "UpperArm_R",
        (2.5 * breathe, 0, 1.5 * breathe)
    )
    add_motion(
        p, "Hand_R",
        (-1.5 * breathe, 0, 0)
    )
    add_motion(
        p, "UpperArm_L",
        (1.5 * breathe, 0, -1.8 * breathe)
    )

    return eyes(p, blink(t, (0.25, 0.85)))


def weapon_walk(t, weapon):
    p = copy_pose(WEAPON_HOLDS[weapon])

    s = math.sin(TAU * t)
    bounce = math.cos(2 * TAU * t)

    add_motion(
        p, "Torso",
        (-4 + 2 * bounce, 2.5 * s, 4 * s),
        (0.022 * s, 0, 0.038 * bounce)
    )

    add_motion(
        p, "Head",
        (3 - 3 * bounce, -3 * s, -3.6 * s)
    )

    add_motion(
        p, "UpperArm_R",
        (3.5 * s, 0, 1.6 * s)
    )
    add_motion(
        p, "LowerArm_R",
        (-2.5 * s, 0, 0)
    )
    add_motion(
        p, "UpperArm_L",
        (-5 * s, 0, -3.2 * s)
    )
    add_motion(
        p, "LowerArm_L",
        (3 * s, 0, 0)
    )

    return eyes(p, blink(t, (0.46,)))


def recoil_envelope(t):
    if t < 0.12:
        return ease(t / 0.12)

    if t < 0.32:
        return 1 - 0.23 * ease((t - 0.12) / 0.20)

    if t < 0.68:
        return 0.77 * (
            1 - ease((t - 0.32) / 0.36)
        )

    return 0.0


def weapon_shoot(t, weapon):
    p = copy_pose(WEAPON_HOLDS[weapon])

    kick = recoil_envelope(t)

    settle = (
        -math.sin((t - 0.48) * math.pi / 0.37) * 0.20
        if 0.48 < t < 0.85
        else 0.0
    )

    strength = {
        "Pistol": 1.0,
        "AssaultRifle": 0.63,
        "Shotgun": 1.85
    }[weapon]

    recoil = strength * (kick + settle)

    # Whole-body reaction, not just the arm.
    add_motion(
        p, "Torso",
        (
            10 * recoil,
            -2.5 * recoil,
            -4.2 * recoil
        ),
        (
            0.022 * recoil,
            0.07 * recoil,
            -0.042 * recoil
        )
    )

    # Head lags behind the body.
    add_motion(
        p, "Head",
        (
            -12 * recoil,
            3 * recoil,
            5 * recoil
        )
    )

    add_motion(
        p, "UpperArm_R",
        (17 * recoil, -2.5 * recoil, 3.5 * recoil)
    )
    add_motion(
        p, "LowerArm_R",
        (9 * recoil, 0, 0)
    )
    add_motion(
        p, "Hand_R",
        (6 * recoil, 0, 2 * recoil)
    )

    add_motion(
        p, "UpperArm_L",
        (10 * recoil, 0, -4.5 * recoil)
    )
    add_motion(
        p, "LowerArm_L",
        (6 * recoil, 0, 0)
    )

    if weapon == "Shotgun":
        wobble = gaussian(t, 0.57, 0.13)

        add_motion(
            p, "Torso",
            (-4 * wobble, 0, 7 * wobble)
        )
        add_motion(
            p, "Head",
            (7 * wobble, 0, -8 * wobble)
        )

    return eyes(
        p,
        squint=min(0.7, 0.52 * max(0, kick))
    )


def weapon_reload(t, weapon):
    p = copy_pose(WEAPON_HOLDS[weapon])

    amount = math.sin(math.pi * t) ** 2

    add_motion(
        p, "Torso",
        (2 * amount, 0, 4 * amount)
    )
    add_motion(
        p, "Head",
        (12 * amount, -6 * amount, -3 * amount)
    )
    add_motion(
        p, "UpperArm_R",
        (7 * amount, 0, 8 * amount)
    )
    add_motion(
        p, "Hand_R",
        (-14 * amount, 0, -6 * amount)
    )

    add_motion(
        p, "UpperArm_L",
        (-24 * amount, 6 * amount, -14 * amount)
    )
    add_motion(
        p, "LowerArm_L",
        (-27 * amount, 0, 8 * amount)
    )
    add_motion(
        p, "Hand_L",
        (25 * amount, 0, 13 * amount)
    )

    if weapon == "Shotgun":
        pump = math.sin(6 * math.pi * t) ** 4 * amount
        add_motion(
            p, "LowerArm_L",
            (-14 * pump, 0, 0)
        )
        add_motion(
            p, "Hand_L",
            (19 * pump, 0, -7 * pump)
        )

    elif weapon == "AssaultRifle":
        rack = gaussian(t, 0.73, 0.06)
        add_motion(
            p, "UpperArm_R",
            (-9 * rack, 0, 0)
        )
        add_motion(
            p, "Hand_L",
            (-20 * rack, 0, 0)
        )

    else:
        tap = gaussian(t, 0.72, 0.065)
        add_motion(
            p, "Hand_R",
            (16 * tap, 0, 0)
        )

    return eyes(
        p,
        squint=min(
            1.0,
            blink(t, (0.60,)) + 0.11 * amount
        )
    )


# ============================================================
# GAMEPLAY ACTIONS
# ============================================================

def gameplay_pose(t, name):
    neutral = {}

    if name == "Pickup":
        anticipation = max(
            0.0, 1 - abs((t - 0.20) / 0.20)
        )
        reach = ease((t - 0.18) / 0.44)

        p = blend_poses(neutral, CARRY, reach)

        add_motion(
            p, "Torso",
            (
                18 * anticipation - 8 * reach,
                0, 0
            ),
            (0, 0, -0.08 * anticipation)
        )
        add_motion(
            p, "Head",
            (-10 * anticipation, 0, 0)
        )
        add_motion(
            p, "UpperArm_L",
            (-18 * anticipation, 0, 0)
        )
        add_motion(
            p, "UpperArm_R",
            (-18 * anticipation, 0, 0)
        )

        return eyes(p, blink(t, (0.20,)))

    if name == "Drop":
        release = ease(t / 0.62)
        p = blend_poses(CARRY, neutral, release)

        flash = gaussian(t, 0.36, 0.11)

        add_motion(
            p, "UpperArm_L",
            (18 * flash, -4 * flash, -15 * flash)
        )
        add_motion(
            p, "UpperArm_R",
            (18 * flash, 4 * flash, 15 * flash)
        )
        add_motion(
            p, "Torso",
            (-10 * flash, 0, 0),
            (0, 0, 0.04 * flash)
        )

        return p

    if name == "Point":
        a = math.sin(math.pi * t) ** 2
        p = {}

        add_motion(p, "Torso", (-3 * a, -10 * a, -4 * a))
        add_motion(p, "Head", (0, 21 * a, 7 * a))
        add_motion(
            p, "UpperArm_R",
            (-70 * a, 8 * a, -30 * a)
        )
        add_motion(
            p, "LowerArm_R",
            (8 * a, 0, 0)
        )

        return eyes(p, widen=0.3 * a)

    if name.startswith("Hit_Reaction"):
        sign = -1 if name.endswith("Left") else 1

        hit = gaussian(t, 0.18, 0.13)
        aftershock = (
            math.sin(4 * math.pi * t)
            * (1 - ease(t))
        )

        p = {}

        add_motion(
            p, "Torso",
            (
                15 * hit - 5 * aftershock,
                7 * sign * hit,
                19 * sign * hit
            ),
            (
                0.07 * sign * hit,
                0.08 * hit,
                -0.04 * hit
            )
        )

        add_motion(
            p, "Head",
            (
                -12 * hit,
                -11 * sign * hit,
                -22 * sign * hit
            )
        )

        add_motion(
            p, "UpperArm_L",
            (19 * hit, -6 * hit, -18 * hit)
        )
        add_motion(
            p, "UpperArm_R",
            (17 * hit, 6 * hit, 18 * hit)
        )

        return eyes(p, squint=0.85 * hit)

    if name == "Jump":
        takeoff = gaussian(t, 0.20, 0.12)
        airborne = math.sin(math.pi * t) ** 2

        p = {}

        add_motion(
            p, "Torso",
            (
                -10 * airborne + 7 * takeoff,
                0,
                4 * airborne
            ),
            (
                0, 0,
                0.15 * airborne - 0.04 * takeoff
            )
        )

        add_motion(
            p, "Head",
            (9 * airborne, 0, -3 * airborne)
        )

        for side, sign in (("L", -1), ("R", 1)):
            add_motion(
                p, "UpperArm_" + side,
                (18 * airborne, 0, -sign * 17 * airborne)
            )

        return p

    if name == "Landing":
        impact = gaussian(t, 0.19, 0.13)
        p = {}

        add_motion(
            p, "Torso",
            (14 * impact, 0, 0),
            (0, 0, -0.13 * impact)
        )
        add_motion(
            p, "Head",
            (-14 * impact, 0, 0)
        )

        for side, sign in (("L", -1), ("R", 1)):
            add_motion(
                p, "UpperArm_" + side,
                (-19 * impact, 0, -sign * 11 * impact)
            )

        return eyes(p, squint=0.3 * impact)

    if name == "Death":
        fall = ease(t / 0.70)
        p = {}

        add_motion(
            p, "Torso",
            (50 * fall, 0, 25 * fall),
            (0, 0.04 * fall, -0.42 * fall)
        )
        add_motion(
            p, "Head",
            (30 * fall, -9 * fall, -17 * fall)
        )

        for side, sign in (("L", -1), ("R", 1)):
            add_motion(
                p, "UpperArm_" + side,
                (48 * fall, 0, -sign * 24 * fall)
            )
            add_motion(
                p, "LowerArm_" + side,
                (36 * fall, 0, 0)
            )

        return eyes(p, squint=0.99 * fall)

    if name == "Celebrate":
        amount = math.sin(math.pi * t) ** 2
        bounce = (
            math.sin(6 * math.pi * t) * amount
        )
        p = {}

        add_motion(
            p, "Torso",
            (-10 * amount, 0, 12 * bounce),
            (
                0, 0,
                0.15 * amount + 0.06 * bounce
            )
        )
        add_motion(
            p, "Head",
            (12 * amount, 0, -11 * bounce)
        )

        for side, sign in (("L", -1), ("R", 1)):
            add_motion(
                p, "UpperArm_" + side,
                (
                    -78 * amount,
                    0,
                    -sign * 29 * amount
                )
            )
            add_motion(
                p, "LowerArm_" + side,
                (-12 * amount, 0, -sign * 14 * bounce)
            )
            add_motion(
                p, "Hand_" + side,
                (0, 0, sign * 26 * bounce)
            )

        return eyes(p, widen=0.45 * amount)

    return neutral


def exhausted_pose(t):
    p = idle_pose(t)

    s = math.sin(TAU * t)

    add_motion(
        p, "Torso",
        (13, 0, 3 * s),
        (0, 0, -0.09)
    )
    add_motion(
        p, "Head",
        (16, 0, -4 * s)
    )
    add_motion(
        p, "UpperArm_L",
        (18, 0, 8)
    )
    add_motion(
        p, "UpperArm_R",
        (18, 0, -8)
    )

    return eyes(
        p,
        squint=0.3 + 0.13 * s ** 2
    )


# ============================================================
# ACTION CREATION
# ============================================================

def key_pose(rig, frame, transforms):
    for name in ANIMATED_BONES:
        bone = rig.pose.bones[name]
        values = transforms.get(name, {})

        bone.rotation_mode = "XYZ"
        bone.location = values.get(
            "location", (0, 0, 0)
        )
        bone.rotation_euler = tuple(
            math.radians(v)
            for v in values.get(
                "rotation", (0, 0, 0)
            )
        )
        bone.scale = values.get(
            "scale", (1, 1, 1)
        )

        bone.keyframe_insert(
            data_path="location",
            frame=frame,
            group=name
        )
        bone.keyframe_insert(
            data_path="rotation_euler",
            frame=frame,
            group=name
        )

        if name.startswith("Eye_"):
            bone.keyframe_insert(
                data_path="scale",
                frame=frame,
                group=name
            )


def create_action(
    rig, name, frame_end,
    pose_generator,
    loop=False, interval=2
):
    action = bpy.data.actions.new(name)
    action.use_fake_user = True

    # Blender versions differ in Action properties.
    if hasattr(action, "use_frame_range"):
        action.use_frame_range = True
        action.frame_start = 1
        action.frame_end = frame_end

    if hasattr(action, "use_cyclic"):
        action.use_cyclic = loop

    action["unity_loop"] = loop
    action["unity_clip_name"] = name

    rig.animation_data.action = action

    frames = list(range(1, frame_end + 1, interval))

    if frames[-1] != frame_end:
        frames.append(frame_end)

    for frame in frames:
        t = (frame - 1) / (frame_end - 1)
        key_pose(rig, frame, pose_generator(t))

    return action


def create_animation_library(rig):
    rig.animation_data_create()
    bpy.context.scene.render.fps = FPS

    clips = [
        ("Idle", 96, idle_pose, True, 2),
        ("Idle_LookAround", 90, idle_scan, False, 2),
        ("Idle_Playful", 90, idle_playful, False, 2),
        ("Idle_Wave", 72, idle_wave, False, 2),

        (
            "HoverMove", 30,
            lambda t: movement_pose(t, "forward"),
            True, 2
        ),
        (
            "HoverMove_Backward", 30,
            lambda t: movement_pose(t, "backward"),
            True, 2
        ),
        (
            "Strafe_L", 30,
            lambda t: movement_pose(t, "left"),
            True, 2
        ),
        (
            "Strafe_R", 30,
            lambda t: movement_pose(t, "right"),
            True, 2
        ),
        (
            "Sprint", 24,
            lambda t: movement_pose(t, "sprint"),
            True, 2
        ),

        (
            "Pickup", 36,
            lambda t: gameplay_pose(t, "Pickup"),
            False, 2
        ),
        (
            "Carry_Idle", 60,
            lambda t: carry_pose(t, "idle"),
            True, 2
        ),
        (
            "Carry_Move", 30,
            lambda t: carry_pose(t, "move"),
            True, 2
        ),
        (
            "Carry_Sprint", 24,
            lambda t: carry_pose(t, "sprint"),
            True, 2
        ),
        (
            "Carry_Struggle", 38,
            lambda t: carry_pose(t, "struggle"),
            True, 2
        ),
        (
            "Drop", 24,
            lambda t: gameplay_pose(t, "Drop"),
            False, 2
        ),
        (
            "Hold_Item", 48,
            lambda t: carry_pose(t, "idle"),
            True, 2
        ),

        (
            "Point", 42,
            lambda t: gameplay_pose(t, "Point"),
            False, 2
        ),
        (
            "Hit_Reaction", 26,
            lambda t: gameplay_pose(t, "Hit_Reaction"),
            False, 1
        ),
        (
            "Hit_Reaction_Left", 26,
            lambda t: gameplay_pose(t, "Hit_Reaction_Left"),
            False, 1
        ),
        (
            "Jump", 30,
            lambda t: gameplay_pose(t, "Jump"),
            False, 2
        ),
        (
            "Landing", 22,
            lambda t: gameplay_pose(t, "Landing"),
            False, 1
        ),
        (
            "Death", 50,
            lambda t: gameplay_pose(t, "Death"),
            False, 2
        ),
        (
            "Celebrate", 64,
            lambda t: gameplay_pose(t, "Celebrate"),
            False, 2
        ),
        (
            "Exhausted", 70,
            exhausted_pose,
            True, 2
        )
    ]

    for weapon in (
        "Pistol",
        "AssaultRifle",
        "Shotgun"
    ):
        clips.extend([
            (
                weapon + "_Idle",
                60,
                lambda t, w=weapon: weapon_idle(t, w),
                True, 2
            ),
            (
                weapon + "_Walk",
                30,
                lambda t, w=weapon: weapon_walk(t, w),
                True, 2
            ),
            (
                weapon + "_Shoot",
                {
                    "Pistol": 16,
                    "AssaultRifle": 10,
                    "Shotgun": 25
                }[weapon],
                lambda t, w=weapon: weapon_shoot(t, w),
                False, 1
            ),
            (
                weapon + "_Reload",
                {
                    "Pistol": 48,
                    "AssaultRifle": 57,
                    "Shotgun": 78
                }[weapon],
                lambda t, w=weapon: weapon_reload(t, w),
                False, 2
            )
        ])

    for name, duration, sampler, loop, interval in clips:
        create_action(
            rig,
            name,
            duration,
            sampler,
            loop,
            interval
        )

    rig.animation_data.action = bpy.data.actions["Idle"]

    bpy.context.scene.frame_start = 1
    bpy.context.scene.frame_end = 96
    bpy.context.scene.frame_set(1)

    print(
        "Generated %d animation Actions at %d FPS."
        % (len(clips), FPS)
    )


# ============================================================
# GENERATION VALIDATION
# ============================================================

def validate_character(rig, geometry, weapons):
    required_meshes = (
        "Head", "Visor", "Torso", "HoverRing",
        "UpperArm_L", "Forearm_L", "Hand_L",
        "UpperArm_R", "Forearm_R", "Hand_R"
    )

    missing_meshes = [
        name for name in required_meshes
        if name not in geometry
    ]

    required_bones = (
        "Root", "Torso", "Head",
        "Eye_L", "Eye_R",
        "UpperArm_L", "LowerArm_L", "Hand_L",
        "UpperArm_R", "LowerArm_R", "Hand_R"
    )

    missing_bones = [
        name for name in required_bones
        if name not in rig.pose.bones
    ]

    expected_weapons = {
        "Weapon_Pistol",
        "Weapon_AssaultRifle",
        "Weapon_Shotgun"
    }

    actual_weapons = {
        weapon.name for weapon in weapons
    }

    missing_weapons = expected_weapons - actual_weapons

    required_actions = {
        "Idle", "HoverMove", "Pickup",
        "Carry_Move", "Drop",
        "Pistol_Idle", "Pistol_Walk", "Pistol_Shoot",
        "AssaultRifle_Idle",
        "AssaultRifle_Walk",
        "AssaultRifle_Shoot",
        "Shotgun_Idle", "Shotgun_Walk", "Shotgun_Shoot"
    }

    missing_actions = (
        required_actions - set(bpy.data.actions.keys())
    )

    errors = []

    if missing_meshes:
        errors.append("Meshes: " + ", ".join(missing_meshes))

    if missing_bones:
        errors.append("Bones: " + ", ".join(missing_bones))

    if missing_weapons:
        errors.append(
            "Weapons: " + ", ".join(sorted(missing_weapons))
        )

    if missing_actions:
        errors.append(
            "Actions: " + ", ".join(sorted(missing_actions))
        )

    if errors:
        raise RuntimeError(
            "CouchGuy validation failed: "
            + " | ".join(errors)
        )

    print(
        "Validation passed: both arms, both hands, "
        "three weapons and all required animation Actions."
    )


# ============================================================
# MAIN
# ============================================================

def setup_character():
    clear_scene()

    materials = create_materials()

    root = create_empty("CouchGuy_Root")
    root.empty_display_type = "CIRCLE"
    root.empty_display_size = 0.3

    # Restore the complete original geometry.
    create_head(materials, root)
    create_torso(materials, root)

    left_arm = create_arm("L", materials, root)
    right_arm = create_arm("R", materials, root)

    create_hover_ring(materials, root)
    weapons = create_weapon_models(materials, root)

    # Construct and connect the full rig.
    rig = create_armature(left_arm, right_arm, root)

    geometry = {
        obj.name: obj
        for obj in bpy.context.scene.objects
    }

    rig_character(rig, geometry)
    attach_weapons_to_hand(rig, weapons)

    # Generate all animation clips.
    create_animation_library(rig)

    # Check that nothing essential went missing.
    validate_character(rig, geometry, weapons)

    # Show only one gun for Blender preview.
    set_weapon_preview(weapons)

    bpy.ops.object.select_all(action="DESELECT")
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig

    root["character_name"] = "Couch Guy Robot"
    root["animation_set"] = "Expressive v2"
    root["forward_axis"] = "-Y"
    root["unity_scale_meters"] = 1.0
    root["player_glow_material"] = "PlayerGlow"

    print("Couch Guy generated successfully.")
    print(
        "Select CouchGuy_Rig, then open "
        "Dope Sheet > Action Editor to preview animations."
    )

    return root, rig


if __name__ == "__main__":
    setup_character()
