"""Procedurally generate a game-ready cartoon human thief for Couch Guys.

Run in Blender's Scripting workspace or from a command line:
    blender --background --python generate_cartoon_thief.py

The thief faces Blender -Y. With the project's usual FBX axes (-Z Forward,
Y Up), it faces Unity +Z. One Blender unit is one metre. Re-running removes
only the previous ``Thief_Root`` hierarchy and this generator's four Actions.

Export with Bake Animation and All Actions enabled. The body uses deliberately
rigid, single-bone weighting: it is cheap, predictable over every generated
proportion, and the overlapping rounded pieces hide joint gaps in motion.
"""

import math
import random
import time

import bpy
from mathutils import Vector


# =============================================================================
# GENERATION SETTINGS -- edit these two values
# =============================================================================

SEED = 7284             # None = choose and print a fresh seed
WEAPON_TYPE = "PISTOL"  # PISTOL, SHOTGUN, or ASSAULT_RIFLE

WEAPON_TYPES = ("PISTOL", "SHOTGUN", "ASSAULT_RIFLE")

SKIN_TONES = (
    (0.96, 0.67, 0.49, 1), (0.86, 0.51, 0.34, 1),
    (0.72, 0.39, 0.24, 1), (0.52, 0.27, 0.17, 1),
    (0.34, 0.17, 0.11, 1),
)
SHIRT_COLOURS = (
    (0.12, 0.18, 0.26, 1), (0.12, 0.27, 0.23, 1),
    (0.34, 0.12, 0.16, 1), (0.22, 0.20, 0.18, 1),
    (0.20, 0.16, 0.28, 1),
)
PANTS_COLOURS = (
    (0.08, 0.10, 0.13, 1), (0.12, 0.16, 0.22, 1),
    (0.16, 0.17, 0.16, 1), (0.20, 0.16, 0.13, 1),
)
BEANIE_COLOURS = (
    (0.12, 0.13, 0.15, 1), (0.14, 0.25, 0.35, 1),
    (0.42, 0.12, 0.14, 1), (0.18, 0.32, 0.22, 1),
    (0.38, 0.24, 0.10, 1), (0.28, 0.18, 0.38, 1),
)
SHOE_COLOURS = ((0.055, 0.06, 0.07, 1), (0.16, 0.11, 0.08, 1))


# =============================================================================
# Scene, material, and primitive helpers
# =============================================================================

def delete_generated_thief():
    root = bpy.data.objects.get("Thief_Root")
    if root:
        descendants = []

        def gather(obj):
            for child in list(obj.children):
                gather(child)
            descendants.append(obj)

        gather(root)
        for obj in descendants:
            bpy.data.objects.remove(obj, do_unlink=True)

    for action_name in ("Thief_Idle", "Thief_Walk", "Thief_Shoot", "Thief_Death"):
        action = bpy.data.actions.get(action_name)
        if action:
            bpy.data.actions.remove(action)

    for data_group, prefix in ((bpy.data.meshes, "Thief_"),
                               (bpy.data.armatures, "Thief_"),
                               (bpy.data.materials, "Thief_")):
        for block in list(data_group):
            if block.users == 0 and block.name.startswith(prefix):
                data_group.remove(block)


def make_empty(name, parent=None, size=0.18):
    obj = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(obj)
    obj.empty_display_type = "PLAIN_AXES"
    obj.empty_display_size = size
    if parent:
        obj.parent = parent
    return obj


def make_material(name, colour, roughness=0.72, metallic=0.0):
    mat = bpy.data.materials.new("Thief_" + name)
    mat.diffuse_color = colour
    mat.use_nodes = True
    shader = mat.node_tree.nodes.get("Principled BSDF")
    shader.inputs["Base Color"].default_value = colour
    shader.inputs["Roughness"].default_value = roughness
    shader.inputs["Metallic"].default_value = metallic
    return mat


def create_materials(rng):
    skin = rng.choice(SKIN_TONES)
    shirt = rng.choice(SHIRT_COLOURS)
    stripe = tuple(min(1.0, c * 1.75 + 0.08) for c in shirt[:3]) + (1,)
    return {
        "skin": make_material("Skin", skin, 0.78),
        "shirt": make_material("Shirt", shirt),
        "stripe": make_material("ShirtStripe", stripe),
        "pants": make_material("Pants", rng.choice(PANTS_COLOURS)),
        "shoe": make_material("Shoes", rng.choice(SHOE_COLOURS), 0.58),
        "beanie": make_material("Beanie", rng.choice(BEANIE_COLOURS), 0.84),
        "mask": make_material("Mask", rng.choice(((0.025, 0.03, 0.04, 1),
                                                   (0.055, 0.06, 0.08, 1)))),
        "eye": make_material("EyeWhite", (0.91, 0.91, 0.82, 1)),
        "pupil": make_material("Pupils", (0.035, 0.025, 0.02, 1)),
        "mouth": make_material("Mouth", (0.20, 0.055, 0.045, 1)),
        "glove": make_material("Gloves", (0.045, 0.05, 0.065, 1), 0.64),
        "metal": make_material("WeaponMetal", (0.105, 0.12, 0.14, 1), 0.34, 0.30),
        "weapon_dark": make_material("WeaponDark", (0.035, 0.04, 0.05, 1), 0.46, 0.12),
        "wood": make_material("WeaponAccent", (0.34, 0.17, 0.075, 1), 0.72),
        "buckle": make_material("Buckle", (0.62, 0.48, 0.18, 1), 0.32, 0.45),
    }


def assign_material(obj, material):
    obj.data.materials.append(material)


def apply_transforms(obj):
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    obj.select_set(False)


def soften(obj, width, segments=2):
    modifier = obj.modifiers.new("CartoonSoftEdges", "BEVEL")
    modifier.width = width
    modifier.segments = segments
    modifier.limit_method = "ANGLE"
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    obj.select_set(False)
    for polygon in obj.data.polygons:
        polygon.use_smooth = True


def box(name, location, dimensions, material, parent, bevel=0.05,
        rotation=(0, 0, 0)):
    bpy.ops.mesh.primitive_cube_add(location=location, rotation=rotation)
    obj = bpy.context.object
    obj.name = name
    obj.data.name = "Thief_" + name
    obj.dimensions = dimensions
    apply_transforms(obj)
    if bevel:
        soften(obj, min(bevel, min(dimensions) * 0.32))
    assign_material(obj, material)
    obj.parent = parent
    return obj


def ellipsoid(name, location, scale, material, parent, segments=16, rings=10,
              rotation=(0, 0, 0)):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segments, ring_count=rings,
                                        location=location, rotation=rotation)
    obj = bpy.context.object
    obj.name = name
    obj.data.name = "Thief_" + name
    obj.scale = scale
    apply_transforms(obj)
    for polygon in obj.data.polygons:
        polygon.use_smooth = True
    assign_material(obj, material)
    obj.parent = parent
    return obj


def cylinder(name, location, radius, depth, material, parent, vertices=12,
             rotation=(0, 0, 0), bevel=0.025):
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius, depth=depth,
                                       location=location, rotation=rotation)
    obj = bpy.context.object
    obj.name = name
    obj.data.name = "Thief_" + name
    if bevel:
        soften(obj, min(bevel, radius * 0.25))
    assign_material(obj, material)
    obj.parent = parent
    return obj


def capsule(name, start, end, radius, material, parent):
    start, end = Vector(start), Vector(end)
    direction = end - start
    obj = cylinder(name, (start + end) * 0.5, radius, direction.length,
                   material, parent, vertices=12, bevel=radius * 0.22)
    obj.rotation_mode = "QUATERNION"
    obj.rotation_quaternion = direction.to_track_quat("Z", "Y")
    apply_transforms(obj)
    return obj


def profile(rng):
    """Choose bounded, correlated dimensions for readable silhouette variety."""
    build = rng.choice(("SLIM", "AVERAGE", "HEAVY", "BROAD"))
    ranges = {
        "SLIM": (0.82, 0.94), "AVERAGE": (0.94, 1.06),
        "HEAVY": (1.10, 1.25), "BROAD": (1.04, 1.18),
    }
    width = rng.uniform(*ranges[build])
    height = rng.uniform(0.91, 1.10)
    if build == "HEAVY":
        height *= rng.uniform(0.93, 1.01)
    return {
        "build": build, "height": height, "width": width,
        "shoulders": width * rng.uniform(1.00, 1.14 if build == "BROAD" else 1.07),
        "head": rng.uniform(0.91, 1.14), "head_w": rng.uniform(0.90, 1.12),
        "arm": rng.uniform(0.90, 1.10), "arm_thick": rng.uniform(0.88, 1.14),
        "leg_thick": rng.uniform(0.88, 1.16), "hand": rng.uniform(0.92, 1.17),
        "shoe": rng.uniform(0.92, 1.20), "nose": rng.uniform(0.86, 1.30),
        "ears": rng.uniform(0.82, 1.24), "beanie": rng.uniform(0.91, 1.16),
        "gloves": rng.random() < 0.82, "backpack": rng.random() < 0.34,
        "neck_scarf": rng.random() < 0.25,
    }


def landmarks(p):
    h = p["height"]
    ankle = 0.25
    knee = 0.79 * h
    hip = 1.40 * h
    chest = 2.00 * h
    neck = 2.39 * h
    head_c = 2.73 * h
    head_top = 3.10 * h
    shoulder_x = 0.54 * p["shoulders"]
    arm_len = 0.60 * p["arm"] * h
    return dict(ankle=ankle, knee=knee, hip=hip, chest=chest, neck=neck,
                head_c=head_c, head_top=head_top, shoulder_x=shoulder_x,
                elbow_z=chest - arm_len * 0.52, wrist_z=chest - arm_len,
                foot_x=0.25 * p["width"])


# =============================================================================
# Character geometry
# =============================================================================

def generate_body(p, lm, m, groups, rng):
    pieces = []

    def add(obj, bone):
        pieces.append((obj, bone))
        return obj

    # Pelvis, torso, sweater stripes, belt, and optional backpack.
    add(ellipsoid("Thief_Body", (0, 0.02, (lm["hip"] + lm["neck"]) / 2),
                  (0.58 * p["width"], 0.39 * p["width"],
                   (lm["neck"] - lm["hip"]) * 0.54), m["shirt"], groups["Body"]), "Chest")
    add(ellipsoid("Thief_Pelvis", (0, 0.03, lm["hip"]),
                  (0.46 * p["width"], 0.34 * p["width"], 0.30 * p["height"]),
                  m["pants"], groups["Body"]), "Pelvis")
    for i, z in enumerate((lm["hip"] + 0.38 * p["height"],
                           lm["hip"] + 0.68 * p["height"])):
        add(box(f"Thief_ShirtStripe_{i+1}", (0, -0.365 * p["width"], z),
                (0.98 * p["width"], 0.055, 0.13 * p["height"]), m["stripe"],
                groups["Clothes"], 0.035), "Chest")
    add(box("Thief_Belt", (0, -0.01, lm["hip"] + 0.03),
            (1.0 * p["width"], 0.73 * p["width"], 0.13), m["weapon_dark"],
            groups["Clothes"], 0.035), "Pelvis")
    add(box("Thief_BeltBuckle", (0, -0.385 * p["width"], lm["hip"] + 0.03),
            (0.20, 0.06, 0.17), m["buckle"], groups["Clothes"], 0.025), "Pelvis")
    if p["backpack"]:
        add(box("Thief_Backpack", (0, 0.38 * p["width"], lm["chest"]),
                (0.65 * p["width"], 0.25, 0.70 * p["height"]), m["pants"],
                groups["Accessories"], 0.12), "Chest")

    # Legs overlap at the joints. Shoes project toward -Y, giving a clear front.
    for side, sign in (("L", -1), ("R", 1)):
        x = sign * lm["foot_x"]
        upper_r = 0.19 * p["leg_thick"]
        lower_r = upper_r * 0.88
        add(capsule(f"Thief_UpperLeg.{side}", (x, 0, lm["hip"]),
                    (x, 0.015, lm["knee"]), upper_r, m["pants"], groups["Body"]),
            f"UpperLeg.{side}")
        add(ellipsoid(f"Thief_Knee.{side}", (x, 0.005, lm["knee"]),
                      (upper_r, upper_r, upper_r), m["pants"], groups["Body"]),
            f"LowerLeg.{side}")
        add(capsule(f"Thief_LowerLeg.{side}", (x, 0.01, lm["knee"]),
                    (x, -0.01, lm["ankle"]), lower_r, m["pants"], groups["Body"]),
            f"LowerLeg.{side}")
        add(ellipsoid(f"Thief_Shoe.{side}", (x, -0.16 * p["shoe"], 0.16),
                      (0.25 * p["shoe"], 0.42 * p["shoe"], 0.17), m["shoe"],
                      groups["Clothes"]), f"Foot.{side}")

    # Arms are a relaxed weapon-holding rest pose with chunky hidden joints.
    for side, sign in (("L", -1), ("R", 1)):
        shoulder = Vector((sign * lm["shoulder_x"], 0, lm["chest"] + 0.18))
        elbow = Vector((sign * (lm["shoulder_x"] + 0.12), -0.10, lm["elbow_z"]))
        wrist = Vector((sign * (0.32 if side == "R" else 0.20), -0.34,
                        lm["wrist_z"] + (0.08 if side == "L" else 0)))
        arm_r = 0.17 * p["arm_thick"]
        hand_mat = m["glove"] if p["gloves"] else m["skin"]
        add(ellipsoid(f"Thief_Shoulder.{side}", shoulder,
                      (arm_r * 1.2,) * 3, m["shirt"], groups["Body"]),
            f"UpperArm.{side}")
        add(capsule(f"Thief_UpperArm.{side}", shoulder, elbow, arm_r,
                    m["shirt"], groups["Body"]), f"UpperArm.{side}")
        # Sleeve stripe reinforces the burglar read without textures.
        stripe_t = 0.055
        stripe_pos = shoulder.lerp(elbow, 0.72)
        add(ellipsoid(f"Thief_SleeveStripe.{side}", stripe_pos,
                      (arm_r * 1.06, arm_r * 1.06, stripe_t), m["stripe"],
                      groups["Clothes"]), f"UpperArm.{side}")
        add(ellipsoid(f"Thief_Elbow.{side}", elbow, (arm_r * 0.95,) * 3,
                      m["shirt"], groups["Body"]), f"LowerArm.{side}")
        add(capsule(f"Thief_LowerArm.{side}", elbow, wrist, arm_r * 0.91,
                    m["shirt"], groups["Body"]), f"LowerArm.{side}")
        add(ellipsoid(f"Thief_Hand.{side}", wrist + Vector((0, -0.045, 0)),
                      (0.20 * p["hand"], 0.17 * p["hand"], 0.20 * p["hand"]),
                      hand_mat, groups["Body"]), f"Hand.{side}")

    return pieces


def generate_head(p, lm, m, groups, rng):
    pieces = []

    def add(obj, bone="Head"):
        pieces.append((obj, bone))
        return obj

    hw = 0.43 * p["head"] * p["head_w"]
    hd = 0.36 * p["head"]
    hh = 0.48 * p["head"]
    centre = Vector((0, -0.015, lm["head_c"]))
    add(ellipsoid("Thief_Head", centre, (hw, hd, hh), m["skin"], groups["Head"],
                  segments=20, rings=12))

    ear_z = centre.z + rng.uniform(-0.03, 0.035)
    for side, sign in (("L", -1), ("R", 1)):
        add(ellipsoid(f"Thief_Ear.{side}",
                      (sign * (hw + 0.025), 0, ear_z),
                      (0.10 * p["ears"], 0.055, 0.14 * p["ears"]),
                      m["skin"], groups["Head"], segments=12, rings=8))

    face_y = centre.y - hd * 0.91
    eye_z = centre.z + 0.08 * p["head"]
    eye_sep = hw * rng.uniform(0.43, 0.52)
    # Wide black bridge plus rounded eye lobes forms an unmistakable thief mask.
    add(box("Thief_Mask", (0, face_y - 0.014, eye_z),
            (hw * 1.58, 0.075, 0.25 * p["head"]), m["mask"], groups["Head"], 0.09))
    for side, sign in (("L", -1), ("R", 1)):
        add(ellipsoid(f"Thief_MaskLobe.{side}",
                      (sign * eye_sep, face_y - 0.045, eye_z),
                      (0.19 * p["head"], 0.055, 0.15 * p["head"]),
                      m["mask"], groups["Head"], 14, 8))
        add(ellipsoid(f"Thief_Eye.{side}",
                      (sign * eye_sep, face_y - 0.098, eye_z),
                      (0.085 * p["head"], 0.025, 0.060 * p["head"]),
                      m["eye"], groups["Head"], 12, 8))
        add(ellipsoid(f"Thief_Pupil.{side}",
                      (sign * eye_sep, face_y - 0.124, eye_z),
                      (0.025, 0.012, 0.033), m["pupil"], groups["Head"], 10, 6))

    add(ellipsoid("Thief_Nose", (0, face_y - 0.13, centre.z - 0.055),
                  (0.12 * p["nose"], 0.17 * p["nose"], 0.15 * p["nose"]),
                  m["skin"], groups["Head"], 14, 8))
    mouth_w = 0.19 * rng.uniform(0.85, 1.18)
    add(box("Thief_Mouth", (0, face_y - 0.084, centre.z - 0.27 * p["head"]),
            (mouth_w, 0.026, 0.035), m["mouth"], groups["Head"], 0.016,
            rotation=(0, 0, math.radians(rng.uniform(-5, 5)))))

    # Low-poly squashy beanie with cuff and a small top nub.
    beanie_z = centre.z + hh * 0.78
    add(ellipsoid("Thief_Beanie", (0, 0.015, beanie_z + 0.15 * p["beanie"]),
                  (hw * 1.04, hd * 1.02, 0.34 * p["beanie"]), m["beanie"],
                  groups["Head"], 16, 9))
    add(cylinder("Thief_BeanieCuff", (0, -0.005, beanie_z), hw * 1.04,
                 0.17 * p["beanie"], m["beanie"], groups["Head"], vertices=16,
                 bevel=0.035))
    if rng.random() < 0.45:
        add(ellipsoid("Thief_BeaniePom", (0, 0.02, beanie_z + 0.43 * p["beanie"]),
                      (0.105, 0.105, 0.105), m["beanie"], groups["Head"], 12, 7))

    if p["neck_scarf"]:
        add(cylinder("Thief_NeckScarf", (0, 0, lm["neck"]), 0.29 * p["width"],
                     0.16, m["beanie"], groups["Accessories"], vertices=14,
                     bevel=0.03), "Neck")
    return pieces


# =============================================================================
# Humanoid rig and skinning
# =============================================================================

def create_armature(p, lm, root):
    data = bpy.data.armatures.new("Thief_ArmatureData")
    rig = bpy.data.objects.new("Thief_Armature", data)
    bpy.context.collection.objects.link(rig)
    rig.parent = root
    rig.show_in_front = True
    data.display_type = "OCTAHEDRAL"
    bpy.context.view_layer.objects.active = rig
    rig.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    bones = {}

    def bone(name, head, tail, parent=None, connected=False):
        b = data.edit_bones.new(name)
        b.head, b.tail = head, tail
        if parent:
            b.parent = bones[parent]
            b.use_connect = connected
        bones[name] = b

    bone("Root", (0, 0, 0), (0, 0, 0.22))
    bone("Pelvis", (0, 0, 0.22), (0, 0, lm["hip"]), "Root", True)
    bone("Spine", (0, 0, lm["hip"]), (0, 0, lm["chest"]), "Pelvis", True)
    bone("Chest", (0, 0, lm["chest"]), (0, 0, lm["neck"]), "Spine", True)
    bone("Neck", (0, 0, lm["neck"]), (0, 0, lm["head_c"] - 0.20), "Chest", True)
    bone("Head", (0, 0, lm["head_c"] - 0.20), (0, 0, lm["head_top"] + 0.28),
         "Neck", True)
    for side, sign in (("L", -1), ("R", 1)):
        shoulder = (sign * lm["shoulder_x"], 0, lm["chest"] + 0.18)
        elbow = (sign * (lm["shoulder_x"] + 0.12), -0.10, lm["elbow_z"])
        wrist = (sign * (0.32 if side == "R" else 0.20), -0.34,
                 lm["wrist_z"] + (0.08 if side == "L" else 0))
        bone(f"UpperArm.{side}", shoulder, elbow, "Chest")
        bone(f"LowerArm.{side}", elbow, wrist, f"UpperArm.{side}", True)
        bone(f"Hand.{side}", wrist, (wrist[0], wrist[1] - 0.27, wrist[2]),
             f"LowerArm.{side}", True)
        x = sign * lm["foot_x"]
        bone(f"UpperLeg.{side}", (x, 0, lm["hip"]), (x, 0.01, lm["knee"]), "Pelvis")
        bone(f"LowerLeg.{side}", (x, 0.01, lm["knee"]), (x, -0.01, lm["ankle"]),
             f"UpperLeg.{side}", True)
        bone(f"Foot.{side}", (x, -0.01, lm["ankle"]), (x, -0.45, 0.13),
             f"LowerLeg.{side}", True)
    wrist_r = bones["Hand.R"].head.copy()
    bone("weapon_socket", wrist_r, wrist_r + Vector((0, -0.34, 0)), "Hand.R")
    bpy.ops.object.mode_set(mode="OBJECT")
    rig.select_set(False)
    return rig


def bind_piece(obj, rig, bone_name):
    group = obj.vertex_groups.new(name=bone_name)
    group.add(range(len(obj.data.vertices)), 1.0, "REPLACE")
    modifier = obj.modifiers.new("Thief_Armature", "ARMATURE")
    modifier.object = rig
    modifier.use_vertex_groups = True


# =============================================================================
# Replaceable cartoon weapons
# =============================================================================

def parent_to_bone_keep_transform(obj, rig, bone_name):
    world = obj.matrix_world.copy()
    obj.parent = rig
    obj.parent_type = "BONE"
    obj.parent_bone = bone_name
    obj.matrix_world = world


def generate_weapon(weapon_type, p, lm, m, groups, rig):
    weapon = make_empty("Thief_Weapon", groups["Weapon"], 0.12)
    # World-space construction near right hand; the full hierarchy is then
    # parented to weapon_socket while preserving its transforms.
    x = 0.32
    z = lm["wrist_z"] - 0.01
    if weapon_type == "PISTOL":
        box("Thief_Weapon_Receiver", (x, -0.58, z + 0.03), (0.25, 0.48, 0.22),
            m["metal"], weapon, 0.045)
        box("Thief_Weapon_Grip", (x, -0.42, z - 0.20), (0.19, 0.20, 0.38),
            m["weapon_dark"], weapon, 0.045, rotation=(math.radians(-12), 0, 0))
        cylinder("Thief_Weapon_Barrel", (x, -0.90, z + 0.05), 0.10, 0.54,
                 m["metal"], weapon, vertices=12, rotation=(math.radians(90), 0, 0))
        box("Thief_Weapon_Sight", (x, -0.74, z + 0.17), (0.06, 0.12, 0.06),
            m["buckle"], weapon, 0.015)
        muzzle_pos = (x, -1.18, z + 0.05)
    elif weapon_type == "SHOTGUN":
        cylinder("Thief_Weapon_Barrel", (x, -1.03, z + 0.06), 0.105, 1.30,
                 m["metal"], weapon, vertices=12, rotation=(math.radians(90), 0, 0))
        box("Thief_Weapon_Receiver", (x, -0.55, z + 0.02), (0.28, 0.48, 0.28),
            m["weapon_dark"], weapon, 0.055)
        box("Thief_Weapon_Pump", (x, -1.08, z + 0.01), (0.34, 0.42, 0.26),
            m["wood"], weapon, 0.065)
        box("Thief_Weapon_Stock", (x, -0.19, z - 0.04), (0.32, 0.50, 0.32),
            m["wood"], weapon, 0.08, rotation=(math.radians(-8), 0, 0))
        box("Thief_Weapon_Grip", (x, -0.47, z - 0.23), (0.19, 0.20, 0.37),
            m["wood"], weapon, 0.045, rotation=(math.radians(-12), 0, 0))
        muzzle_pos = (x, -1.69, z + 0.06)
    else:
        box("Thief_Weapon_Receiver", (x, -0.66, z + 0.02), (0.34, 0.68, 0.32),
            m["metal"], weapon, 0.06)
        cylinder("Thief_Weapon_Barrel", (x, -1.19, z + 0.07), 0.075, 0.80,
                 m["weapon_dark"], weapon, vertices=10, rotation=(math.radians(90), 0, 0))
        box("Thief_Weapon_Stock", (x, -0.18, z + 0.02), (0.30, 0.50, 0.29),
            m["weapon_dark"], weapon, 0.065)
        box("Thief_Weapon_Grip", (x, -0.49, z - 0.24), (0.18, 0.20, 0.37),
            m["weapon_dark"], weapon, 0.04, rotation=(math.radians(-13), 0, 0))
        box("Thief_Weapon_Magazine", (x, -0.75, z - 0.24), (0.22, 0.27, 0.40),
            m["weapon_dark"], weapon, 0.045, rotation=(math.radians(-8), 0, 0))
        box("Thief_Weapon_TopSight", (x, -0.70, z + 0.23), (0.10, 0.23, 0.10),
            m["buckle"], weapon, 0.02)
        muzzle_pos = (x, -1.60, z + 0.07)

    muzzle = make_empty("Muzzle", weapon, 0.11)
    muzzle.location = muzzle_pos
    muzzle.rotation_euler = (math.radians(90), 0, 0)
    weapon["weapon_type"] = weapon_type
    weapon["unity_replaceable"] = True
    parent_to_bone_keep_transform(weapon, rig, "weapon_socket")
    return weapon, muzzle


# =============================================================================
# Animation library
# =============================================================================

ANIMATED_BONES = (
    "Root", "Pelvis", "Spine", "Chest", "Neck", "Head",
    "UpperArm.L", "LowerArm.L", "Hand.L", "UpperArm.R", "LowerArm.R", "Hand.R",
    "UpperLeg.L", "LowerLeg.L", "Foot.L", "UpperLeg.R", "LowerLeg.R", "Foot.R",
    "weapon_socket",
)


def key_pose(rig, frame, transforms):
    for name in ANIMATED_BONES:
        b = rig.pose.bones[name]
        b.rotation_mode = "XYZ"
        b.location = (0, 0, 0)
        b.rotation_euler = (0, 0, 0)
        values = transforms.get(name, {})
        if "location" in values:
            b.location = values["location"]
        if "rotation" in values:
            b.rotation_euler = tuple(math.radians(v) for v in values["rotation"])
        b.keyframe_insert("location", frame=frame, group=name)
        b.keyframe_insert("rotation_euler", frame=frame, group=name)


def create_action(rig, name, end, poses, loop=False):
    action = bpy.data.actions.new(name)
    action.use_fake_user = True
    action.use_frame_range = True
    action.frame_start, action.frame_end = 1, end
    action["unity_loop"] = loop
    action["unity_clip_name"] = name
    rig.animation_data.action = action
    for frame, pose in poses:
        key_pose(rig, frame, pose)
    # Blender 4.4 removed Action.use_cyclic; add cycles directly where possible.
    if loop:
        if hasattr(action, "use_cyclic"):
            action.use_cyclic = True
        for curve in getattr(action, "fcurves", ()): 
            curve.modifiers.new("CYCLES")
    return action


def create_animations(rig, weapon_type):
    rig.animation_data_create()
    bpy.context.scene.render.fps = 30
    neutral = {}
    breathe = {
        "Pelvis": {"location": (0, 0, 0.025)},
        "Chest": {"rotation": (-2.5, 0, 1.5)}, "Head": {"rotation": (2, 0, -2)},
        "UpperArm.L": {"rotation": (-2, 1, 2)}, "UpperArm.R": {"rotation": (-2, -1, -2)},
        "weapon_socket": {"rotation": (-1.5, 0, 0)},
    }
    settle = {
        "Pelvis": {"location": (0, 0, -0.018)},
        "Chest": {"rotation": (1.5, 0, -1)}, "Head": {"rotation": (-1, 0, 1.5)},
    }
    create_action(rig, "Thief_Idle", 48,
                  [(1, neutral), (13, breathe), (25, neutral), (37, settle), (48, neutral)], True)

    step_l = {
        "Pelvis": {"location": (0, 0, 0.055), "rotation": (0, 0, -4)},
        "Spine": {"rotation": (-4, 0, 5)}, "Head": {"rotation": (2, 0, -3)},
        "UpperLeg.L": {"rotation": (-28, 0, 0)}, "LowerLeg.L": {"rotation": (38, 0, 0)},
        "UpperLeg.R": {"rotation": (24, 0, 0)}, "LowerLeg.R": {"rotation": (5, 0, 0)},
        "UpperArm.L": {"rotation": (10, 0, 5)}, "UpperArm.R": {"rotation": (-5, 0, -3)},
    }
    step_r = {
        "Pelvis": {"location": (0, 0, 0.055), "rotation": (0, 0, 4)},
        "Spine": {"rotation": (-4, 0, -5)}, "Head": {"rotation": (2, 0, 3)},
        "UpperLeg.L": {"rotation": (24, 0, 0)}, "LowerLeg.L": {"rotation": (5, 0, 0)},
        "UpperLeg.R": {"rotation": (-28, 0, 0)}, "LowerLeg.R": {"rotation": (38, 0, 0)},
        "UpperArm.L": {"rotation": (-10, 0, 5)}, "UpperArm.R": {"rotation": (5, 0, -3)},
    }
    down = {"Pelvis": {"location": (0, 0, -0.035)}, "Spine": {"rotation": (-2, 0, 0)}}
    create_action(rig, "Thief_Walk", 24,
                  [(1, step_l), (7, down), (13, step_r), (19, down), (24, step_l)], True)

    aim = {
        "Chest": {"rotation": (-7, 0, 0)}, "Head": {"rotation": (5, 0, 0)},
        "UpperArm.R": {"rotation": (-15, -3, -8)}, "LowerArm.R": {"rotation": (-12, 0, 0)},
        "UpperArm.L": {"rotation": (-12, 4, 9)}, "LowerArm.L": {"rotation": (-20, 0, 0)},
    }
    recoil_amount = 20 if weapon_type == "SHOTGUN" else 9
    recoil = dict(aim)
    recoil["Chest"] = {"location": (0, 0.06 if weapon_type == "SHOTGUN" else 0.025, 0),
                       "rotation": (recoil_amount, 0, 0)}
    recoil["weapon_socket"] = {"rotation": (recoil_amount * 0.75, 0, 0)}
    if weapon_type == "ASSAULT_RIFLE":
        recoil_small = dict(aim)
        recoil_small["weapon_socket"] = {"rotation": (5, 0, 0)}
        shoot_poses = [(1, neutral), (6, aim), (9, recoil), (12, aim),
                       (15, recoil_small), (18, aim), (24, neutral)]
    else:
        shoot_poses = [(1, neutral), (7, aim), (10, recoil), (15, aim), (24, neutral)]
    create_action(rig, "Thief_Shoot", 24, shoot_poses)

    hit = {
        "Root": {"rotation": (0, 0, -12)}, "Chest": {"rotation": (18, 0, 15)},
        "Head": {"rotation": (-16, 0, -18)},
        "UpperArm.L": {"rotation": (30, -15, 25)}, "UpperArm.R": {"rotation": (-25, 14, -22)},
    }
    falling = {
        "Root": {"location": (0, 0.24, -0.30), "rotation": (42, 5, -25)},
        "Pelvis": {"rotation": (18, 0, 0)}, "Chest": {"rotation": (25, 0, 18)},
        "Head": {"rotation": (-18, 0, -12)},
        "UpperLeg.L": {"rotation": (-30, 8, 0)}, "UpperLeg.R": {"rotation": (24, -8, 0)},
        "UpperArm.L": {"rotation": (48, -20, 32)}, "UpperArm.R": {"rotation": (-45, 18, -35)},
        "weapon_socket": {"rotation": (55, 0, 25)},
    }
    ground = {
        "Root": {"location": (0, 0.58, -1.05), "rotation": (82, 8, -32)},
        "Pelvis": {"rotation": (22, 0, 0)}, "Chest": {"rotation": (18, 0, 14)},
        "Head": {"rotation": (-22, 8, -15)},
        "UpperLeg.L": {"rotation": (-38, 12, -5)}, "LowerLeg.L": {"rotation": (52, 0, 0)},
        "UpperLeg.R": {"rotation": (18, -10, 6)}, "LowerLeg.R": {"rotation": (22, 0, 0)},
        "UpperArm.L": {"rotation": (58, -22, 38)}, "UpperArm.R": {"rotation": (-55, 20, -40)},
        "weapon_socket": {"rotation": (70, 0, 28)},
    }
    create_action(rig, "Thief_Death", 52,
                  [(1, neutral), (7, hit), (18, falling), (38, ground), (52, ground)])

    rig.animation_data.action = bpy.data.actions["Thief_Idle"]
    bpy.context.scene.frame_start, bpy.context.scene.frame_end = 1, 48
    bpy.context.scene.frame_set(1)


# =============================================================================
# Assembly
# =============================================================================

def generate_thief(seed=SEED, weapon_type=WEAPON_TYPE):
    weapon_type = str(weapon_type).upper()
    if weapon_type not in WEAPON_TYPES:
        raise ValueError(f"WEAPON_TYPE must be one of {WEAPON_TYPES}; got {weapon_type!r}")
    delete_generated_thief()
    actual_seed = seed if seed is not None else random.SystemRandom().randrange(1, 2 ** 31)
    rng = random.Random(f"CouchGuysThief:{actual_seed}:{weapon_type}")
    p = profile(rng)
    lm = landmarks(p)
    materials = create_materials(rng)

    root = make_empty("Thief_Root", size=0.32)
    root.empty_display_type = "CIRCLE"
    groups = {name: make_empty("Thief_" + name, root, 0.16) for name in
              ("Body", "Head", "Clothes", "Accessories", "Weapon")}
    pieces = generate_body(p, lm, materials, groups, rng)
    pieces.extend(generate_head(p, lm, materials, groups, rng))
    rig = create_armature(p, lm, root)
    for obj, bone_name in pieces:
        bind_piece(obj, rig, bone_name)
    generate_weapon(weapon_type, p, lm, materials, groups, rig)
    create_animations(rig, weapon_type)

    root["generator"] = "Couch Guys Cartoon Thief v1"
    root["seed"] = actual_seed
    root["weapon_type"] = weapon_type
    root["body_type"] = p["build"]
    root["height_variation"] = round(p["height"], 4)
    root["forward_axis"] = "-Y"
    root["unity_scale_meters"] = 1.0
    root["generated_utc"] = time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())

    bpy.ops.object.select_all(action="DESELECT")
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    print("\nGenerated Thief")
    print(f"Seed: {actual_seed}")
    print(f"Weapon: {weapon_type}")
    print(f"Height Variation: {p['height']:.2f}")
    print(f"Body Type: {p['build'].title()}")
    print("Animations:\n- Thief_Idle\n- Thief_Walk\n- Thief_Shoot\n- Thief_Death")
    return root, rig


if __name__ == "__main__":
    generate_thief()
