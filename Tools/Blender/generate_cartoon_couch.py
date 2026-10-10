"""
COUCH GUYS - PROCEDURAL CARTOON COUCH GENERATOR
================================================

Builds ONE random, stylised, game-ready couch in Blender per run.
Designed to visually match the warm, low-poly, bevelled aesthetic of
generate_cartoon_house.py (pastel materials, chunky readable silhouettes,
small deliberate decorative details).

Features:
  * Loveseats, three-seat sofas, wide sofas and chaise sofas.
  * Random upholstery, silhouette, frame, arms, legs and cushions.
  * Optional tufting, piping, accent stripes and throw pillows.
  * Organised collection-independent object parenting.
  * Unity-friendly dimensions (1 Blender unit = 1 metre).
  * Named grip-point helpers and collider-size metadata.
  * Repeatable seeds and optional FBX export.

Coordinate convention:
  X = couch length, +Y = back, -Y = front, +Z = up.
  World origin = floor centre under main couch body.

USE: Blender > Scripting > Open > Run Script.
Running again removes ONLY the previously GeneratedCouch hierarchy.

For Unity:
  Export the GeneratedCouch hierarchy as FBX or set EXPORT_FBX = True.
  In Unity, add your actual networked couch pickup/carrying script and
  ONE appropriately-sized BoxCollider/Rigidbody to your prefab. The
  "ColliderBounds" empty provides the intended collider centre and size.
  "Grip_Left" / "Grip_Right" are suggested handle transforms.
"""

import bpy
import math
import random
import time

# ------------------------------------------------------------
# CONFIGURATION (EDIT THESE)
# ------------------------------------------------------------

SEED = None                 # None: different random couch per run.
COUCH_TYPE = "RANDOM"        # RANDOM, LOVESEAT, SOFA, WIDE_SOFA, CHAISE.
QUALITY = "MEDIUM"          # LOW / MEDIUM / HIGH (visual detail).
EXPORT_FBX = False          # Set True to export after generating.
FBX_PATH = "//CouchGuys_Couch.fbx"

# Visual settings.
BEVEL_WIDTH = 0.055
ADD_PIPING = True
ADD_THROW_PILLOWS = True
ADD_TUFTING = True
ADD_ACCENT_STRIPES = True

# Palettes match the house generator's muted, warm, painted-cartoon look.
# Each tuple: upholstery, secondary material, trim, wood, accent.
PALETTES = (
    ((0.62, 0.75, 0.72, 1), (0.53, 0.67, 0.64, 1), (0.90, 0.88, 0.79, 1),
     (0.45, 0.29, 0.20, 1), (0.92, 0.72, 0.46, 1)),   # sage
    ((0.83, 0.58, 0.44, 1), (0.71, 0.44, 0.33, 1), (0.95, 0.86, 0.74, 1),
     (0.42, 0.24, 0.17, 1), (0.74, 0.78, 0.64, 1)),   # terracotta
    ((0.76, 0.78, 0.82, 1), (0.61, 0.66, 0.72, 1), (0.94, 0.92, 0.88, 1),
     (0.45, 0.34, 0.27, 1), (0.88, 0.70, 0.48, 1)),   # blue grey
    ((0.88, 0.77, 0.52, 1), (0.76, 0.63, 0.39, 1), (0.97, 0.90, 0.76, 1),
     (0.38, 0.27, 0.18, 1), (0.53, 0.67, 0.62, 1)),   # mustard
    ((0.77, 0.70, 0.77, 1), (0.66, 0.57, 0.68, 1), (0.91, 0.86, 0.84, 1),
     (0.46, 0.27, 0.24, 1), (0.90, 0.69, 0.62, 1)),   # dusty lilac
    ((0.84, 0.81, 0.73, 1), (0.71, 0.66, 0.55, 1), (0.97, 0.93, 0.85, 1),
     (0.37, 0.30, 0.23, 1), (0.43, 0.59, 0.55, 1)),   # linen
    ((0.45, 0.63, 0.72, 1), (0.35, 0.53, 0.63, 1), (0.86, 0.91, 0.91, 1),
     (0.37, 0.25, 0.19, 1), (0.94, 0.66, 0.46, 1)),   # teal
    ((0.65, 0.49, 0.41, 1), (0.54, 0.37, 0.31, 1), (0.90, 0.83, 0.74, 1),
     (0.34, 0.24, 0.18, 1), (0.80, 0.75, 0.54, 1)),   # cocoa
    ((0.82, 0.66, 0.66, 1), (0.71, 0.53, 0.54, 1), (0.96, 0.89, 0.85, 1),
     (0.42, 0.28, 0.22, 1), (0.82, 0.77, 0.61, 1)),   # pink
    ((0.53, 0.62, 0.47, 1), (0.42, 0.51, 0.38, 1), (0.88, 0.87, 0.75, 1),
     (0.44, 0.30, 0.19, 1), (0.92, 0.78, 0.53, 1)),   # olive
)


# ------------------------------------------------------------
# BLENDER OBJECT / MATERIAL UTILITIES
# ------------------------------------------------------------

def clamp(v, lo=0.0, hi=1.0):
    return max(lo, min(hi, v))


def tint(colour, factor):
    return tuple(clamp(c * factor) for c in colour[:3]) + (1.0,)


def jitter(colour, rng, amount=0.018):
    return tuple(clamp(c + rng.uniform(-amount, amount)) for c in colour[:3]) + (1.0,)


def make_material(name, colour, roughness=0.84, metallic=0.0):
    mat = bpy.data.materials.get(name)
    if mat is None:
        mat = bpy.data.materials.new(name)
    mat.diffuse_color = colour
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    if bsdf is not None:
        for socket_name, value in (
            ("Base Color", colour),
            ("Roughness", roughness),
            ("Metallic", metallic),
        ):
            socket = bsdf.inputs.get(socket_name)
            if socket is not None:
                socket.default_value = value
    return mat


def make_materials(rng):
    main, secondary, trim, wood, accent = rng.choice(PALETTES)
    main = jitter(main, rng)
    secondary = jitter(secondary, rng)

    return {
        "fabric": make_material("Couch_Fabric", main),
        "fabric_dark": make_material("Couch_FabricShadow", secondary),
        "fabric_light": make_material("Couch_FabricLight", tint(main, 1.085)),
        "piping": make_material("Couch_Piping", tint(secondary, 0.88)),
        "trim": make_material("Couch_Trim", trim),
        "wood": make_material("Couch_Wood", wood),
        "wood_light": make_material("Couch_WoodLight", tint(wood, 1.23)),
        "metal": make_material("Couch_Metal", (0.24, 0.26, 0.27, 1),
                               roughness=0.46, metallic=0.48),
        "accent": make_material("Couch_Accent", accent),
        "accent_dark": make_material("Couch_AccentShadow", tint(accent, 0.86)),
    }


def remove_previous_couch():
    root = bpy.data.objects.get("GeneratedCouch")
    if root is None:
        return

    def remove_tree(obj):
        for child in list(obj.children):
            remove_tree(child)
        bpy.data.objects.remove(obj, do_unlink=True)

    remove_tree(root)


def make_empty(name, parent=None, location=(0, 0, 0), display="PLAIN_AXES"):
    obj = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(obj)
    obj.empty_display_type = display
    obj.empty_display_size = 0.13
    obj.location = location
    if parent is not None:
        obj.parent = parent
    return obj


def mesh_object(name, verts, faces, material, parent, bevel=0.0):
    """Build geometry without using Blender edit mode or selecting anything."""
    mesh = bpy.data.meshes.new("Couch_" + name)
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    mesh.materials.append(material)

    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    if parent is not None:
        obj.parent = parent

    if bevel > 0.0:
        mod = obj.modifiers.new("RoundedCartoonEdges", "BEVEL")
        mod.width = bevel
        mod.segments = 2 if QUALITY == "LOW" else 3
        mod.limit_method = "ANGLE"
        # Weighted normals keep broad faces pleasantly flat in Blender.
        try:
            weighted = obj.modifiers.new("WeightedNormals", "WEIGHTED_NORMAL")
            weighted.keep_sharp = True
        except (AttributeError, RuntimeError):
            pass
    return obj


def box(name, xyz, size, material, parent, bevel=BEVEL_WIDTH):
    """Box with world-space centre, using a local mesh and located object."""
    x, y, z = xyz
    sx, sy, sz = size
    if min(sx, sy, sz) <= 0:
        raise ValueError("Invalid box size for " + name + ": " + str(size))
    a, b, c = sx / 2, sy / 2, sz / 2
    verts = [
        (-a, -b, -c), (a, -b, -c), (a, b, -c), (-a, b, -c),
        (-a, -b, c), (a, -b, c), (a, b, c), (-a, b, c),
    ]
    faces = [
        (0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4),
        (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7),
    ]
    obj = mesh_object(name, verts, faces, material, parent,
                      max(0.0, min(bevel, min(size) * 0.29)))
    obj.location = (x, y, z)
    return obj


def tapered_block(name, xyz, bottom_size, top_size, height, material, parent,
                  top_shift=(0.0, 0.0), bevel=0.025):
    """Tapered legs, flare arms and lean-back sofas: 8-vertex custom mesh."""
    bx, by = bottom_size
    tx, ty = top_size
    shift_x, shift_y = top_shift
    z0 = -height / 2
    z1 = height / 2
    verts = [
        (-bx / 2, -by / 2, z0), (bx / 2, -by / 2, z0),
        (bx / 2, by / 2, z0), (-bx / 2, by / 2, z0),
        (-tx / 2 + shift_x, -ty / 2 + shift_y, z1),
        (tx / 2 + shift_x, -ty / 2 + shift_y, z1),
        (tx / 2 + shift_x, ty / 2 + shift_y, z1),
        (-tx / 2 + shift_x, ty / 2 + shift_y, z1),
    ]
    faces = [
        (0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4),
        (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7),
    ]
    result = mesh_object(name, verts, faces, material, parent, bevel)
    result.location = xyz
    return result


def cylinder(name, xyz, radius, length, material, parent, axis="Z", segments=12):
    """Cylinder centred at xyz; long axis can be X, Y or Z."""
    segments = max(8, segments)
    vertices = []
    faces = []
    for z in (-length / 2, length / 2):
        for i in range(segments):
            angle = math.tau * i / segments
            vertices.append((radius * math.cos(angle),
                             radius * math.sin(angle), z))
    faces.append(tuple(reversed(range(segments))))
    faces.append(tuple(range(segments, segments * 2)))
    for i in range(segments):
        j = (i + 1) % segments
        faces.append((i, j, segments + j, segments + i))
    obj = mesh_object(name, vertices, faces, material, parent)
    obj.location = xyz
    if axis == "X":
        obj.rotation_euler[1] = math.pi / 2
    elif axis == "Y":
        obj.rotation_euler[0] = math.pi / 2
    return obj


def button(name, xyz, radius, material, parent):
    bpy.ops.mesh.primitive_uv_sphere_add(
        segments=8 if QUALITY == "LOW" else 12,
        ring_count=4 if QUALITY == "LOW" else 8,
        radius=radius,
        location=xyz,
    )
    obj = bpy.context.object
    obj.name = name
    obj.data.name = "Couch_" + name
    obj.data.materials.append(material)
    obj.parent = parent
    return obj


def material_detail(name, xyz, size, material, parent):
    """Short form for minimal raised accent details."""
    return box(name, xyz, size, material, parent, bevel=0.005)


# ------------------------------------------------------------
# RANDOMISED SOFA LAYOUT
# ------------------------------------------------------------

def choose_layout(rng):
    available = ("LOVESEAT", "SOFA", "SOFA", "WIDE_SOFA", "CHAISE")
    sofa_type = COUCH_TYPE.upper()
    if sofa_type == "RANDOM":
        sofa_type = rng.choice(available)
    if sofa_type not in ("LOVESEAT", "SOFA", "WIDE_SOFA", "CHAISE"):
        raise ValueError("Unknown COUCH_TYPE: " + str(COUCH_TYPE))

    seats = 2 if sofa_type in ("LOVESEAT", "CHAISE") else 3

    width_ranges = {
        "LOVESEAT": (1.55, 1.94),
        "SOFA": (2.04, 2.42),
        "WIDE_SOFA": (2.45, 2.84),
        "CHAISE": (2.06, 2.44),
    }
    width = rng.uniform(*width_ranges[sofa_type])
    depth = rng.uniform(0.79, 0.96)
    leg_height = rng.uniform(0.12, 0.20)
    base_height = rng.uniform(0.20, 0.25)
    cushion_height = rng.uniform(0.18, 0.235)
    back_height = rng.uniform(0.85, 1.10)
    arm_width = rng.uniform(0.16, 0.24)

    arm_style = rng.choice(("BLOCK", "BLOCK", "ROLLED", "FLARED", "SLIM"))
    leg_style = rng.choice(("TAPERED", "TAPERED", "BLOCK", "METAL", "SKIRT"))
    back_style = rng.choice(("PLAIN", "PLAIN", "CUSHIONED", "CUSHIONED",
                             "BUTTON_TUFTED", "CHANNELLED"))
    colour_style = rng.choice(("PLAIN", "PLAIN", "TWO_TONE", "STRIPED"))
    chaise_extra = rng.uniform(0.45, 0.75) if sofa_type == "CHAISE" else 0
    chaise_side = rng.choice((-1, 1)) if sofa_type == "CHAISE" else 0

    return {
        "type": sofa_type, "seats": seats, "width": width,
        "depth": depth, "leg_height": leg_height,
        "base_height": base_height, "cushion_height": cushion_height,
        "back_height": back_height, "back_depth": rng.uniform(0.15, 0.22),
        "arm_width": arm_width,
        "arm_style": arm_style, "leg_style": leg_style,
        "back_style": back_style, "colour_style": colour_style,
        "chaise_extra": chaise_extra, "chaise_side": chaise_side,
        "cushion_gap": rng.uniform(0.018, 0.033),
        "throw_pillow_count": rng.choice((0, 0, 1, 1, 2)),
        "tuft_buttons": (rng.random() < 0.72),
    }


def dimensions(layout):
    l = layout
    base_top = l["leg_height"] + l["base_height"]
    seat_top = base_top + l["cushion_height"]
    arm_top = seat_top + (0.10 if l["arm_style"] == "SLIM" else 0.15)
    return {
        "base_top": base_top,
        "seat_top": seat_top,
        "arm_top": min(arm_top, l["back_height"] - 0.035),
        "seat_front": -l["depth"] / 2 + 0.052,
        "back_face": l["depth"] / 2 - l["back_depth"],
        "back_center_y": l["depth"] / 2 - l["back_depth"] / 2,
    }


# ------------------------------------------------------------
# SOFA ASSEMBLY
# ------------------------------------------------------------

def build_frame(layout, mats, groups, d):
    l = layout
    w, dep = l["width"], l["depth"]
    leg_h = l["leg_height"]
    base_h = l["base_height"]
    upholstery = groups["Frame"]

    box("MainBase",
        (0, 0, leg_h + base_h / 2),
        (w - 0.035, dep - 0.035, base_h),
        mats["fabric_dark"], upholstery, 0.06)

    # Readable dark waist line, a visual motif shared with the houses.
    box("LowerFrameTrim",
        (0, -dep / 2 + 0.016, leg_h + 0.065),
        (w - 0.055, 0.035, 0.055),
        mats["wood"] if l["leg_style"] != "SKIRT" else mats["piping"],
        upholstery, 0.013)

    if l["type"] == "CHAISE":
        ext = l["chaise_extra"]
        side = l["chaise_side"]
        slot_w = (w - 2 * l["arm_width"] - 0.055) / l["seats"]
        centre = side * (w / 2 - l["arm_width"] - slot_w / 2 - 0.018)
        box("ChaiseExtendedBase",
            (centre, -dep / 2 - ext / 2 + 0.025, leg_h + base_h / 2),
            (slot_w + 0.075, ext + 0.10, base_h),
            mats["fabric_dark"], upholstery, 0.049)
        # Additional front support so the overhanging chaise feels grounded.
        if l["leg_style"] != "SKIRT":
            box("ChaiseUnderSupport",
                (centre, -dep / 2 - ext + 0.09, leg_h * 0.65),
                (slot_w * 0.72, 0.13, leg_h * 0.75),
                mats["wood"], groups["Legs"], 0.015)


def build_back(layout, mats, groups, d, rng):
    l = layout
    top = l["back_height"]
    low = d["base_top"] - 0.033
    height = top - low
    body_y = d["back_center_y"]
    w = l["width"] - l["arm_width"] * 1.6

    tapered_block("BackrestBody", (0, body_y, low + height / 2),
                  (w, l["back_depth"]),
                  (w + 0.018, l["back_depth"] * 0.83),
                  height, mats["fabric_dark"], groups["Backrest"],
                  top_shift=(0, 0.027), bevel=0.052)

    front_y = d["back_face"] - 0.012
    back_style = l["back_style"]

    if back_style in ("PLAIN", "BUTTON_TUFTED"):
        # Slightly raised upholstered face gives the big back a soft silhouette.
        body_h = max(0.14, top - d["seat_top"] + 0.12)
        face_bottom = d["seat_top"] - 0.105
        box("BackFaceUpholstery",
            (0, front_y - 0.026, face_bottom + body_h / 2),
            (w - 0.035, 0.090, body_h),
            mats["fabric"], groups["Backrest"], 0.048)

        if back_style == "BUTTON_TUFTED" and ADD_TUFTING:
            columns = 4 if l["seats"] == 2 else 6
            rows = 2
            for row in range(rows):
                for col in range(columns):
                    x = (col - (columns - 1) / 2) * (w * 0.81 / columns)
                    z = face_bottom + body_h * (0.32 + row * 0.40)
                    button("BackTuft_%d_%d" % (row, col),
                           (x, front_y - 0.081, z), 0.021,
                           mats["piping"], groups["Backrest"])

    elif back_style == "CHANNELLED":
        count = l["seats"] * 2 + 1
        segment_w = (w - 0.045) / count
        body_h = max(0.14, top - d["seat_top"] + 0.10)
        z = d["seat_top"] - 0.078 + body_h / 2
        for index in range(count):
            x = (index - (count - 1) / 2) * segment_w
            box("VerticalChannel_%d" % index,
                (x, front_y - 0.038, z),
                (segment_w * 0.94, 0.10, body_h),
                mats["fabric"], groups["Backrest"], 0.047)

    else:  # CUSHIONED: individual plush cushions leaning against back.
        gap = 0.022
        inner_w = w - 0.065
        cushion_w = inner_w / l["seats"] - gap
        height = max(0.32, top - d["seat_top"] + 0.14)
        for i in range(l["seats"]):
            x = (i - (l["seats"] - 1) / 2) * (inner_w / l["seats"])
            obj = box("BackCushion_%d" % i,
                      (x, front_y - 0.078, d["seat_top"] - 0.065 + height / 2),
                      (cushion_w, 0.18, height), mats["fabric"],
                      groups["Backrest"], 0.065)
            obj.rotation_euler[0] = math.radians(rng.uniform(-2.4, -0.5))
            if ADD_PIPING and QUALITY != "LOW":
                material_detail("BackCushionSeam_%d" % i,
                                (x, front_y - 0.176,
                                 d["seat_top"] + height * 0.38),
                                (cushion_w * 0.86, 0.015, 0.011),
                                mats["piping"], groups["Backrest"])


def build_arms(layout, mats, groups, d, rng):
    l = layout
    w, dep = l["width"], l["depth"]
    armw = l["arm_width"]
    armz = d["arm_top"]
    base_z = d["base_top"] - 0.015
    h = armz - base_z
    style = l["arm_style"]

    for side, label in ((-1, "Left"), (1, "Right")):
        x = side * (w / 2 - armw / 2)
        mat = mats["fabric_dark"] if l["colour_style"] == "TWO_TONE" else mats["fabric"]

        if style == "ROLLED":
            box(label + "ArmSupport", (x, 0.01, base_z + h * 0.40),
                (armw * 0.78, dep * 0.87, h * 0.80),
                mat, groups["Arms"], 0.040)
            cylinder(label + "RolledArm",
                     (x, 0, armz - armw * 0.27), armw * 0.68,
                     dep * 0.92, mat, groups["Arms"], axis="Y",
                     segments=12 if QUALITY == "LOW" else 16)

        elif style == "FLARED":
            # Flares slightly outward to make a bigger cartoon silhouette.
            tapered_block(label + "FlaredArm",
                          (x, 0, base_z + h / 2),
                          (armw * 0.87, dep * 0.84),
                          (armw * 1.36, dep * 0.93),
                          h, mat, groups["Arms"],
                          top_shift=(side * 0.052, 0.0), bevel=0.035)
            box(label + "ArmTop",
                (x + side * 0.043, 0, armz),
                (armw * 1.28, dep * 0.93, 0.065),
                mats["fabric_light"], groups["Arms"], 0.024)

        elif style == "SLIM":
            box(label + "SlimArm", (x, 0, base_z + h / 2),
                (armw * 0.66, dep * 0.92, h),
                mat, groups["Arms"], 0.038)
            box(label + "WoodArmCap", (x, 0, armz + 0.012),
                (armw * 0.84, dep * 0.93, 0.039),
                mats["wood"], groups["Arms"], 0.014)

        else:  # BLOCK
            box(label + "ChunkyArm", (x, 0, base_z + h / 2),
                (armw * 1.03, dep * 0.93, h), mat,
                groups["Arms"], 0.073)
            if QUALITY != "LOW":
                material_detail(label + "ArmFrontSeam",
                                (x, -dep * 0.468 - 0.004, base_z + h * 0.51),
                                (armw * 0.62, 0.014, h * 0.64),
                                mats["piping"], groups["Arms"])


def build_seat_cushions(layout, mats, groups, d, rng):
    l = layout
    w = l["width"]
    cushion_gap = l["cushion_gap"]
    seats = l["seats"]
    armw = l["arm_width"]
    usable_w = w - armw * 2 - 0.070
    slot_w = usable_w / seats

    seat_front = d["seat_front"]
    seat_rear = d["back_face"] + 0.029
    normal_length = seat_rear - seat_front
    centre_z = d["base_top"] + l["cushion_height"] / 2 + 0.01

    for i in range(seats):
        x = (i - (seats - 1) / 2) * slot_w
        ext = (
            l["chaise_extra"]
            if l["type"] == "CHAISE"
            and ((i == 0 and l["chaise_side"] == -1)
                 or (i == seats - 1 and l["chaise_side"] == 1))
            else 0.0
        )
        cushion_len = normal_length + ext
        centre_y = (seat_front + seat_rear - ext) / 2

        seat_mat = mats["fabric"]
        if l["colour_style"] == "TWO_TONE" and i == seats - 1:
            seat_mat = mats["fabric_light"]

        box("SeatCushion_%d" % i,
            (x, centre_y, centre_z),
            (slot_w - cushion_gap, cushion_len, l["cushion_height"]),
            seat_mat, groups["SeatCushions"], 0.061)

        if ADD_PIPING and QUALITY != "LOW":
            front = seat_front - ext
            z = d["base_top"] + l["cushion_height"] * 0.57
            material_detail("SeatFrontPiping_%d" % i,
                            (x, front - 0.006, z),
                            (slot_w - cushion_gap - 0.055, 0.014, 0.014),
                            mats["piping"], groups["SeatCushions"])
            # Thin, raised stitch line at the top/front edge.
            material_detail("TopSeam_%d" % i,
                            (x, front + 0.067,
                             d["seat_top"] + 0.017),
                            (slot_w - cushion_gap - 0.11, 0.014, 0.009),
                            mats["fabric_light"], groups["SeatCushions"])

        if l["colour_style"] == "STRIPED" and ADD_ACCENT_STRIPES:
            # One thick, offset stripe is readable even at game-camera distance.
            stripe_w = min(0.095, slot_w * 0.15)
            stripe_x = x + (slot_w * 0.21 if i % 2 == 0 else -slot_w * 0.21)
            material_detail("SeatAccentStripe_%d" % i,
                            (stripe_x, centre_y, d["seat_top"] + 0.018),
                            (stripe_w, cushion_len * 0.79, 0.011),
                            mats["accent"], groups["SeatCushions"])


def build_legs(layout, mats, groups, d):
    l = layout
    w, dep = l["width"], l["depth"]
    leg_h = l["leg_height"]
    style = l["leg_style"]
    leg_group = groups["Legs"]

    if style == "SKIRT":
        box("FloorLengthUpholsterySkirt",
            (0, 0, leg_h * 0.5),
            (w - 0.10, dep - 0.10, leg_h + 0.022),
            mats["fabric_dark"], leg_group, 0.035)
        box("SkirtBottomEdge",
            (0, -dep / 2 + 0.046, 0.042),
            (w - 0.11, 0.022, 0.035),
            mats["piping"], leg_group, 0.005)
        return

    x_positions = (-w * 0.5 + 0.20, w * 0.5 - 0.20)
    y_positions = (-dep * 0.5 + 0.15, dep * 0.5 - 0.15)

    for xi, x in enumerate(x_positions):
        for yi, y in enumerate(y_positions):
            name = "Leg_%d_%d" % (xi, yi)
            z = leg_h / 2
            if style == "TAPERED":
                tapered_block(name, (x, y, z),
                              (0.079, 0.078), (0.125, 0.125),
                              leg_h, mats["wood"], leg_group,
                              top_shift=(-0.015 if x < 0 else 0.015, 0),
                              bevel=0.009)
                box(name + "TopPlate", (x, y, leg_h - 0.021),
                    (0.15, 0.14, 0.040), mats["wood_light"],
                    leg_group, 0.012)
            elif style == "METAL":
                cylinder(name, (x, y, z), 0.036, leg_h,
                         mats["metal"], leg_group, segments=10)
                cylinder(name + "Foot", (x, y, 0.018), 0.060, 0.036,
                         mats["metal"], leg_group, segments=10)
            else:  # BLOCK
                box(name, (x, y, z), (0.138, 0.137, leg_h),
                    mats["wood"], leg_group, 0.021)

    if w > 2.48:
        # Wide sofas get a subtle centre support, avoiding a floating look.
        box("CentreSupportLeg", (0, dep * 0.25, leg_h / 2),
            (0.115, 0.12, leg_h), mats["wood"],
            leg_group, 0.018)


def build_throw_pillows(layout, mats, groups, d, rng):
    if not ADD_THROW_PILLOWS:
        return
    l = layout
    seats = l["seats"]
    slot_w = (l["width"] - 2 * l["arm_width"] - 0.07) / seats
    pillow_count = l["throw_pillow_count"]
    for p in range(pillow_count):
        side = -1 if p == 0 else 1
        x = side * (l["width"] / 2 - l["arm_width"] - slot_w * 0.32)
        y = d["back_face"] - 0.22
        z = d["seat_top"] + 0.14
        pillow_size = rng.uniform(0.23, 0.31)
        obj = box("ThrowPillow_%d" % p, (x, y, z),
                  (pillow_size, 0.105, pillow_size * rng.uniform(0.85, 1.1)),
                  mats["accent"] if p == 0 else mats["trim"],
                  groups["Accessories"], 0.048)
        obj.rotation_euler[1] = math.radians(side * rng.uniform(11, 23))
        obj.rotation_euler[0] = math.radians(rng.uniform(-6, 6))

        if QUALITY != "LOW":
            # Readable central fabric stitch.
            material_detail("ThrowPillowStitch_%d" % p,
                            (x, y - 0.057, z),
                            (pillow_size * 0.58, 0.012, 0.013),
                            mats["accent_dark"] if p == 0 else mats["fabric_dark"],
                            groups["Accessories"])


def build_optional_details(layout, mats, groups, d, rng):
    l = layout
    # Narrow edge welt across the top backrest in some variations.
    if ADD_PIPING and QUALITY == "HIGH" and rng.random() < 0.65:
        box("TopBackPiping",
            (0, d["back_center_y"] - l["back_depth"] * 0.25,
             l["back_height"] + 0.009),
            (l["width"] - 2 * l["arm_width"], 0.022, 0.018),
            mats["piping"], groups["Details"], 0.007)

    # Tiny designer badge on the rear edge. Not central to gameplay.
    if QUALITY == "HIGH" and rng.random() < 0.22:
        box("FurnitureMakerBadge",
            (0, l["depth"] / 2 + 0.008, d["base_top"] - 0.045),
            (0.085, 0.012, 0.034),
            mats["metal"], groups["Details"], 0.005)


def make_gameplay_helpers(layout, root, group, d):
    l = layout
    w = l["width"]
    extra = l["chaise_extra"]
    overall_depth = l["depth"] + extra
    collider_y = -extra / 2
    overall_height = max(l["back_height"], d["arm_top"] + 0.055)
    # This empty is not a Unity Collider itself. See the file header.
    bounds = make_empty("ColliderBounds", group,
                        location=(0, collider_y, overall_height / 2),
                        display="CUBE")
    bounds.empty_display_size = 0.25
    bounds["recommended_box_size"] = [
        round(w, 4), round(overall_depth, 4), round(overall_height, 4)
    ]
    bounds["recommended_box_center"] = [
        0.0, round(collider_y, 4), round(overall_height / 2, 4)
    ]

    left = make_empty("Grip_Left", group,
                      (-w / 2 - 0.055, 0, d["seat_top"] - 0.02),
                      display="SPHERE")
    right = make_empty("Grip_Right", group,
                       (w / 2 + 0.055, 0, d["seat_top"] - 0.02),
                       display="SPHERE")
    make_empty("Grip_Front", group,
               (0, -l["depth"] / 2 - extra - 0.06, d["seat_top"] - 0.045),
               display="SPHERE")
    make_empty("Grip_Back", group,
               (0, l["depth"] / 2 + 0.055, d["seat_top"] - 0.045),
               display="SPHERE")

    root["estimated_width_m"] = round(w, 3)
    root["estimated_depth_m"] = round(overall_depth, 3)
    root["estimated_height_m"] = round(overall_height, 3)
    root["collider_centre_y"] = round(collider_y, 3)
    root["collider_centre_z"] = round(overall_height / 2, 3)
    root["suggested_collider_dimensions"] = [
        round(w, 3), round(overall_depth, 3), round(overall_height, 3)
    ]
    left["grip_side"] = "LEFT"
    right["grip_side"] = "RIGHT"


def export_to_fbx(root):
    """Optional convenience export; does not overwrite the Blender scene."""
    bpy.ops.object.select_all(action="DESELECT")

    def select_tree(obj):
        obj.select_set(True)
        for child in obj.children:
            select_tree(child)

    select_tree(root)
    bpy.context.view_layer.objects.active = root
    output = bpy.path.abspath(FBX_PATH)
    try:
        bpy.ops.export_scene.fbx(
            filepath=output,
            use_selection=True,
            object_types={"MESH", "EMPTY"},
            apply_unit_scale=True,
            use_mesh_modifiers=True,
            axis_forward="-Z",
            axis_up="Y",
            add_leaf_bones=False,
        )
    except Exception as exc:
        print("FBX export failed: %s" % exc)
        print("You can still export the GeneratedCouch hierarchy manually.")
        return
    print("FBX exported to:", output)


# ------------------------------------------------------------
# GENERATION ENTRY POINT
# ------------------------------------------------------------

def generate_couch(seed=SEED):
    remove_previous_couch()
    if seed is None:
        seed = random.SystemRandom().randrange(1, 2 ** 31)

    rng = random.Random(seed)
    materials = make_materials(rng)
    layout = choose_layout(rng)
    dim = dimensions(layout)

    root = make_empty("GeneratedCouch")
    group_names = (
        "Frame", "Backrest", "Arms", "SeatCushions",
        "Legs", "Accessories", "Details", "GameplayHelpers"
    )
    groups = {name: make_empty(name, root) for name in group_names}

    build_frame(layout, materials, groups, dim)
    build_back(layout, materials, groups, dim, rng)
    build_arms(layout, materials, groups, dim, rng)
    build_seat_cushions(layout, materials, groups, dim, rng)
    build_legs(layout, materials, groups, dim)
    build_throw_pillows(layout, materials, groups, dim, rng)
    build_optional_details(layout, materials, groups, dim, rng)
    make_gameplay_helpers(layout, root, groups["GameplayHelpers"], dim)

    root["generator"] = "Couch Guys Procedural Couch v1"
    root["seed"] = seed
    root["couch_type"] = layout["type"]
    root["seat_count"] = layout["seats"]
    root["arm_style"] = layout["arm_style"]
    root["leg_style"] = layout["leg_style"]
    root["back_style"] = layout["back_style"]
    root["colour_style"] = layout["colour_style"]
    root["forward_axis"] = "-Y"
    root["unity_scale_meters"] = 1.0
    root["generated_utc"] = time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())

    bpy.ops.object.select_all(action="DESELECT")
    root.select_set(True)
    bpy.context.view_layer.objects.active = root

    print("")
    print("=" * 60)
    print("COUCH GUYS COUCH GENERATED")
    print("=" * 60)
    print("Seed:", seed)
    print("Type:", layout["type"])
    print("Seats:", layout["seats"])
    print("Arms:", layout["arm_style"])
    print("Legs:", layout["leg_style"])
    print("Back:", layout["back_style"])
    print("Fabric style:", layout["colour_style"])
    print("Width x depth x height (metres):",
          round(layout["width"], 2),
          round(layout["depth"] + layout["chaise_extra"], 2),
          round(max(layout["back_height"], dim["arm_top"]), 2))
    print("=" * 60)
    print("")

    if EXPORT_FBX:
        export_to_fbx(root)

    return root


if __name__ == "__main__":
    generate_couch()
