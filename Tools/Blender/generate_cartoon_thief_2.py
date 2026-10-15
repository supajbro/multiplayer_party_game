"""Procedurally generate a game-ready Fortnite-inspired cartoon human thief for Couch Guys.

Run in Blender's Scripting workspace or from a command line:
    blender --background --python generate_cartoon_thief.py

The thief faces Blender -Y. With the project's usual FBX axes (-Z Forward,
Y Up), it faces Unity +Z. One Blender unit is one metre. Re-running removes
only the previous ``Thief_Root`` hierarchy and this generator's four Actions.

Export with Bake Animation and All Actions enabled. The revised v3 body uses a
more cartoony hero silhouette, layered clothing, and a stronger stylised face.
Details remain rigidly assigned to existing bones to retain the Unity rig,
weapon sockets, and four original animation clips.
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
# Hat and gear vary with seed; leave the weapon options above unchanged.

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
    shirt = rng.choice(((0.48, 0.39, 0.29, 1), (0.31, 0.34, 0.30, 1),
                        (0.30, 0.32, 0.38, 1), (0.44, 0.29, 0.25, 1)))
    vest = rng.choice(((0.075, 0.090, 0.104, 1), (0.105, 0.124, 0.135, 1),
                       (0.10, 0.13, 0.11, 1), (0.16, 0.14, 0.12, 1)))
    return {
        "skin": make_material("Skin", skin, 0.84),
        "shirt": make_material("Shirt", shirt),
        "stripe": make_material("SleeveHem", tuple(min(1, c * 0.68) for c in shirt[:3]) + (1,)),
        "pants": make_material("Pants", rng.choice(PANTS_COLOURS)),
        "pants_seam": make_material("PantsStitch", (0.25, 0.26, 0.26, 1)),
        "shoe": make_material("Boots", rng.choice(SHOE_COLOURS), 0.65),
        "sole": make_material("BootSole", (0.025, 0.028, 0.031, 1)),
        "beanie": make_material("Headwear", rng.choice(BEANIE_COLOURS), 0.83),
        "mask": make_material("Mask", (0.055, 0.06, 0.065, 1)),
        "eye": make_material("Sclera", (0.72, 0.70, 0.60, 1)),
        "pupil": make_material("Iris", (0.035, 0.025, 0.02, 1)),
        "mouth": make_material("FaceDetail", (0.19, 0.105, 0.075, 1)),
        "stubble": make_material("BeardShadow", tuple(c * 0.58 for c in skin[:3]) + (1,)),
        "tattoo": make_material("Tattoo", (0.13, 0.16, 0.16, 1)),
        "vest": make_material("UtilityVest", vest),
        "vest_edge": make_material("VestTrim", tuple(min(1, c * 1.55) for c in vest[:3]) + (1,)),
        "vest_dark": make_material("VestShadow", (0.038, 0.046, 0.050, 1)),
        "patch": make_material("ClothPatch", (0.70, 0.18, 0.16, 1)),
        "patch_light": make_material("PatchStripe", (0.89, 0.84, 0.73, 1)),
        "glove": make_material("Gloves", (0.052, 0.057, 0.067, 1), 0.73),
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


def loft(name, sections, material, parent, sides=10, phase=0.0):
    """Low-poly organic volume; (x,y,z,half_width,half_depth) per section.

    This replaces the old piles of spheres with a single tapered silhouette.
    """
    verts = []
    for x, y, z, rx, ry in sections:
        for j in range(sides):
            a = math.tau * j / sides + phase
            verts.append((x + math.cos(a) * rx, y + math.sin(a) * ry, z))
    faces = [tuple(reversed(tuple(range(sides))))]
    for row in range(len(sections) - 1):
        for j in range(sides):
            nxt = (j + 1) % sides
            a, b = row * sides + j, (row + 1) * sides + j
            faces.append((a, row * sides + nxt, (row + 1) * sides + nxt, b))
    faces.append(tuple(range((len(sections) - 1) * sides, len(sections) * sides)))
    mesh = bpy.data.meshes.new('Thief_' + name)
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    assign_material(obj, material)
    obj.parent = parent
    return obj


def tapered_limb(name, start, end, widths, material, parent, sides=10):
    """Contoured tapered tube along an arbitrary bone; widths = 3 (x,y) pairs."""
    start, end = Vector(start), Vector(end)
    axis = (end - start).normalized()
    side = axis.cross(Vector((0, 1, 0)))
    if side.length < 0.001:
        side = axis.cross(Vector((0, 0, 1)))
    side.normalize()
    other = axis.cross(side).normalized()
    rings = []
    for t, (w, d) in zip((0, 0.46, 1), widths):
        c = start.lerp(end, t)
        rings.append([tuple(c + side * (math.cos(math.tau * i / sides) * w)
                           + other * (math.sin(math.tau * i / sides) * d))
                      for i in range(sides)])
    verts = [v for ring in rings for v in ring]
    faces = [tuple(reversed(range(sides)))]
    for r in range(len(rings) - 1):
        for j in range(sides):
            n = (j + 1) % sides
            faces.append((r * sides + j, r * sides + n,
                          (r + 1) * sides + n, (r + 1) * sides + j))
    faces.append(tuple(range((len(rings) - 1) * sides, len(rings) * sides)))
    mesh = bpy.data.meshes.new('Thief_' + name)
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    assign_material(obj, material)
    obj.parent = parent
    return obj


def plate(name, outline, depth, material, parent):
    """Extrude an X/Y/Z polygon backwards, for cloth panels/face markings."""
    n = len(outline)
    vertices = [tuple(p) for p in outline]
    vertices += [(x, y + depth, z) for x, y, z in outline]
    # Face Blender -Y regardless of whether the outline is left/right mirrored.
    area = sum(outline[i][0] * outline[(i+1) % n][2]
               - outline[(i+1) % n][0] * outline[i][2] for i in range(n))
    front = tuple(range(n)) if area > 0 else tuple(reversed(range(n)))
    faces = [front, tuple(i + n for i in reversed(front))]
    for i in range(n):
        j = (i + 1) % n
        if area > 0:
            faces.append((i, j, n+j, n+i))
        else:
            faces.append((j, i, n+i, n+j))
    mesh = bpy.data.meshes.new('Thief_' + name)
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    assign_material(obj, material)
    obj.parent = parent
    return obj


def profile(rng):
    """Fortnite-like stylised hero proportions, still grounded enough for gameplay."""
    build = rng.choice(("SLIM", "AVERAGE", "HEAVY", "BROAD"))
    widths = {"SLIM": (0.88, 0.98), "AVERAGE": (0.98, 1.11),
              "HEAVY": (1.09, 1.24), "BROAD": (1.07, 1.22)}
    width = rng.uniform(*widths[build])
    height = rng.uniform(0.95, 1.08)
    hero_scale = rng.uniform(1.02, 1.12)
    return {
        "build": build, "height": height, "width": width,
        "hero_scale": hero_scale,
        "shoulders": width * rng.uniform(1.14, 1.28) * hero_scale,
        "head": rng.uniform(1.04, 1.17) * hero_scale,
        "head_w": rng.uniform(0.98, 1.12),
        "arm": rng.uniform(0.98, 1.08),
        "arm_thick": rng.uniform(1.04, 1.22) * hero_scale,
        "leg_thick": rng.uniform(0.97, 1.15) * hero_scale,
        "hand": rng.uniform(1.08, 1.24) * hero_scale,
        "shoe": rng.uniform(1.08, 1.26) * hero_scale,
        "nose": rng.uniform(0.92, 1.20),
        "ears": rng.uniform(0.92, 1.12),
        "beanie": rng.uniform(0.98, 1.12),
        "gloves": rng.random() < 0.86,
        "backpack": rng.random() < 0.24,
        "neck_scarf": rng.random() < 0.12,
        "headwear": rng.choices(("BACKWARDS_CAP", "BEANIE", "BARE"), (6, 2, 2))[0],
        "facial_hair": rng.random() < 0.82,
        "tattoo": rng.random() < 0.70,
    }


def landmarks(p):
    h = p["height"]
    ankle = 0.25
    knee = 0.76 * h
    hip = 1.37 * h
    chest = 2.04 * h
    neck = 2.44 * h
    head_c = 2.82 * h
    head_top = 3.24 * h
    shoulder_x = 0.57 * p["shoulders"]
    arm_len = 0.61 * p["arm"] * h
    return dict(ankle=ankle, knee=knee, hip=hip, chest=chest, neck=neck,
                head_c=head_c, head_top=head_top, shoulder_x=shoulder_x,
                elbow_z=chest - arm_len * 0.49, wrist_z=chest - arm_len,
                foot_x=0.27 * p["width"])


# =============================================================================
# Character geometry
# =============================================================================

def generate_body(p, lm, m, groups, rng):
    pieces = []

    def add(obj, bone):
        pieces.append((obj, bone))
        return obj

    w, h = p["width"], p["height"]
    hero = p["hero_scale"]
    hip, chest, neck = lm["hip"], lm["chest"], lm["neck"]

    # Broader comic-book chest, narrower waist and stronger V silhouette.
    add(loft("Thief_Body", [
        (0, 0.0, hip + .03*h, .38*w, .28*w),
        (0, 0.01, hip + .18*h, .40*w, .30*w),
        (0, .020, chest - .24*h, .54*w*hero, .36*w),
        (0, .025, chest + .03*h, .60*w*hero, .37*w),
        (0, .02, chest + .18*h, .57*w*hero, .35*w),
        (0, .02, neck - .14*h, .35*w, .26*w),
        (0, .02, neck - .05*h, .22*w, .20*w),
    ], m["shirt"], groups["Body"], sides=12), "Chest")
    add(loft("Thief_Pelvis", [
        (0, 0, hip - .23*h, .36*w, .28*w),
        (0, .01, hip - .08*h, .46*w, .31*w),
        (0, .005, hip + .12*h, .45*w, .31*w),
    ], m["pants"], groups["Body"], sides=12), "Pelvis")
    add(loft("Thief_Neck", [
        (0, .005, neck - .17*h, .20*w, .18*w),
        (0, .005, neck + .07*h, .20*w, .19*w),
    ], m["skin"], groups["Body"], sides=10), "Neck")
    add(cylinder("Thief_ShirtCollar", (0, -.018, neck - .115*h), .26*w,
                 .09*h, m["stripe"], groups["Clothes"], vertices=12), "Chest")

    bottom = hip + .08*h
    top = neck - .11*h
    for side, sign in (("L", -1), ("R", 1)):
        def q(x, y, z):
            return (sign * x * w, y * w, z)

        vest_outline = [q(.08, -.35, bottom), q(.52, -.27, bottom),
                        q(.57, -.26, chest + .07*h),
                        q(.50, -.26, chest + .26*h),
                        q(.26, -.30, top), q(.16, -.33, chest + .19*h),
                        q(.09, -.36, chest - .12*h)]
        add(plate(f"Thief_VestPanel.{side}", vest_outline, .06,
                  m["vest"], groups["Clothes"]), "Chest")
        add(box(f"Thief_VestZipper.{side}", q(.115, -.38, hip + .50*h),
                (.038*w, .027*w, .66*h), m["vest_edge"], groups["Clothes"], .006), "Chest")
        add(box(f"Thief_ChestPocket.{side}", q(.35, -.37, chest + .03*h),
                (.30*w, .10, .21*h), m["vest_edge"], groups["Clothes"], .028), "Chest")
        add(box(f"Thief_ChestFlap.{side}", q(.35, -.425, chest + .12*h),
                (.31*w, .03, .055*h), m["vest"], groups["Clothes"], .010), "Chest")
        add(box(f"Thief_ChestSnap.{side}", q(.35, -.446, chest + .09*h),
                (.04, .012, .04), m["buckle"], groups["Clothes"], .006), "Chest")
        add(box(f"Thief_VestCargoPocket.{side}", q(.33, -.36, hip + .31*h),
                (.31*w, .13, .30*h), m["vest_edge"], groups["Clothes"], .032), "Chest")
        add(box(f"Thief_VestCargoFlap.{side}", q(.33, -.43, hip + .45*h),
                (.34*w, .036, .07*h), m["vest"], groups["Clothes"], .014), "Chest")
        if side == "R":
            add(box("Thief_ChestBadge", q(.34, -.44, chest + .205*h),
                    (.27*w, .019, .10*h), m["patch"], groups["Accessories"], .009), "Chest")
            add(box("Thief_BadgeStripe", q(.34, -.456, chest + .20*h),
                    (.26*w, .013, .027*h), m["patch_light"], groups["Accessories"], .004), "Chest")
        else:
            add(box("Thief_RadioClip", q(.35, -.456, chest + .205*h),
                    (.12, .048, .14*h), m["vest_dark"], groups["Accessories"], .012), "Chest")
    add(plate("Thief_VestBack", [(-.52*w, .31*w, bottom), (.52*w, .31*w, bottom),
                                 (.54*w, .31*w, chest + .20*h),
                                 (.25*w, .23*w, top), (-.25*w, .23*w, top),
                                 (-.54*w, .31*w, chest + .20*h)], -.04,
              m["vest"], groups["Clothes"]), "Chest")
    for side, sign in (("L", -1), ("R", 1)):
        add(box(f"Thief_ShoulderWebbing.{side}",
                (sign * .43*w, 0.01, chest + .23*h), (.19*w, .54*w, .07),
                m["vest_dark"], groups["Clothes"], .022), "Chest")
    add(box("Thief_Belt", (0, 0, hip + .005*h), (1.06*w, .71*w, .12*h),
            m["vest_dark"], groups["Clothes"], .030), "Pelvis")
    add(box("Thief_BeltBuckle", (0, -.365*w, hip + .005*h),
            (.18*w, .042, .14*h), m["buckle"], groups["Clothes"], .017), "Pelvis")
    add(box("Thief_BeltBuckleCentre", (0, -.39*w, hip + .005*h),
            (.10*w, .012, .07*h), m["vest_dark"], groups["Clothes"], .008), "Pelvis")
    if p["backpack"]:
        add(box("Thief_Backpack", (0, .37*w, chest - .10*h),
                (.64*w, .28*w, .72*h), m["vest"], groups["Accessories"], .082), "Chest")
        add(box("Thief_BackpackPocket", (0, .53*w, chest - .22*h),
                (.45*w, .08, .24*h), m["vest_edge"], groups["Accessories"], .027), "Chest")

    for side, sign in (("L", -1), ("R", 1)):
        x = sign * lm["foot_x"]
        knee, ankle = lm["knee"], lm["ankle"]
        thickness = p["leg_thick"]
        add(loft(f"Thief_UpperLeg.{side}", [
            (x, 0, knee - .04*h, .18*thickness, .17*thickness),
            (x, .01, knee + .13*h, .20*thickness, .19*thickness),
            (x, .014, hip - .10*h, .25*thickness, .22*thickness),
            (x, .006, hip + .015*h, .24*thickness, .21*thickness),
        ], m["pants"], groups["Body"], sides=10), f"UpperLeg.{side}")
        add(loft(f"Thief_LowerLeg.{side}", [
            (x, -.02, ankle - .01*h, .13*thickness, .13*thickness),
            (x, -.008, ankle + .17*h, .18*thickness, .18*thickness),
            (x, .005, knee - .10*h, .19*thickness, .19*thickness),
            (x, .008, knee + .03*h, .18*thickness, .18*thickness),
        ], m["pants"], groups["Body"], sides=10), f"LowerLeg.{side}")
        add(box(f"Thief_Kneepad.{side}", (x, -.18*thickness, knee),
                (.31*thickness, .065, .22*h), m["vest_edge"],
                groups["Clothes"], .028), f"LowerLeg.{side}")
        add(box(f"Thief_CargoPocket.{side}", (x + sign*.18*thickness,
                -.115, knee + .25*h), (.18, .13, .28*h), m["pants_seam"],
                groups["Clothes"], .020), f"UpperLeg.{side}")
        add(box(f"Thief_CargoFlap.{side}", (x + sign*.18*thickness,
                -.18, knee + .38*h), (.19, .03, .06*h), m["pants"],
                groups["Clothes"], .010), f"UpperLeg.{side}")
        add(box(f"Thief_AnkleCuff.{side}", (x, -.01, ankle + .02*h),
                (.31*thickness, .34*thickness, .13*h), m["pants_seam"],
                groups["Clothes"], .020), f"LowerLeg.{side}")
        shoe = p["shoe"]
        add(box(f"Thief_Boot.{side}", (x, -.16*shoe, .16*h),
                (.40*shoe, .73*shoe, .31*h), m["shoe"], groups["Clothes"], .08), f"Foot.{side}")
        add(box(f"Thief_BootSole.{side}", (x, -.18*shoe, .035*h),
                (.43*shoe, .78*shoe, .08*h), m["sole"], groups["Clothes"], .028), f"Foot.{side}")
        add(box(f"Thief_BootToe.{side}", (x, -.42*shoe, .13*h),
                (.37*shoe, .25*shoe, .14*h), m["vest_edge"], groups["Clothes"], .038), f"Foot.{side}")
        for j in range(3):
            add(box(f"Thief_BootLace.{side}.{j}",
                    (x, -.17*shoe - j*.08*shoe, .32*h),
                    (.20*shoe, .023, .016), m["pants_seam"],
                    groups["Clothes"], .004), f"Foot.{side}")

    for side, sign in (("L", -1), ("R", 1)):
        shoulder = Vector((sign * lm["shoulder_x"], 0, chest + .18))
        elbow = Vector((sign * (lm["shoulder_x"] + .12), -.10, lm["elbow_z"]))
        wrist = Vector((sign * (.34 if side == "R" else .22), -.35,
                        lm["wrist_z"] + (.09 if side == "L" else 0)))
        arm_r = .18 * p["arm_thick"]
        sleeve_end = shoulder.lerp(elbow, .32)
        add(tapered_limb(f"Thief_Sleeve.{side}", shoulder, sleeve_end,
                         [(arm_r*1.28, arm_r*1.10), (arm_r*1.29, arm_r*1.12),
                          (arm_r*1.07, arm_r*1.00)],
                         m["shirt"], groups["Clothes"]), f"UpperArm.{side}")
        add(tapered_limb(f"Thief_UpperArm.{side}", sleeve_end, elbow,
                         [(arm_r*.99, arm_r*.99), (arm_r*1.18, arm_r*1.10),
                          (arm_r*.86, arm_r*.90)],
                         m["skin"], groups["Body"]), f"UpperArm.{side}")
        cuff_start = shoulder.lerp(elbow, .27)
        cuff_end = shoulder.lerp(elbow, .33)
        add(tapered_limb(f"Thief_SleeveCuff.{side}", cuff_start, cuff_end,
                         [(arm_r*1.11, arm_r*1.07),
                          (arm_r*1.11, arm_r*1.07),
                          (arm_r*1.04, arm_r*1.00)],
                         m["stripe"], groups["Clothes"]), f"UpperArm.{side}")
        add(tapered_limb(f"Thief_Forearm.{side}", elbow, wrist,
                         [(arm_r*.94, arm_r*.91), (arm_r*1.09, arm_r*.99),
                          (arm_r*.73, arm_r*.74)],
                         m["skin"], groups["Body"]), f"LowerArm.{side}")
        if p["tattoo"]:
            for n, t in enumerate((.48, .62, .75)):
                c = shoulder.lerp(elbow, t)
                add(box(f"Thief_Tattoo.{side}.{n}",
                        (c.x, c.y - arm_r*.98, c.z),
                        (.06 + n*.016, .009, .022), m["tattoo"],
                        groups["Accessories"], .005,
                        rotation=(0, 0, math.radians(sign*(25 - 11*n)))), f"UpperArm.{side}")
        add(box(f"Thief_GloveCuff.{side}", wrist + Vector((0, .035, .05)),
                (.30*p["hand"], .27*p["hand"], .15*h), m["vest_dark"],
                groups["Clothes"], .022), f"Hand.{side}")
        glove_mat = m["glove"] if p["gloves"] else m["skin"]
        add(box(f"Thief_Hand.{side}", wrist + Vector((0, -.095, -.02)),
                (.31*p["hand"], .26*p["hand"], .21*h), glove_mat,
                groups["Body"], .045), f"Hand.{side}")
        for i, dx in enumerate((-.10, -.035, .035, .10)):
            add(box(f"Thief_Finger.{side}.{i}",
                    wrist + Vector((dx*p["hand"], -.245, -.025)),
                    (.07*p["hand"], .18, .09*h), glove_mat,
                    groups["Body"], .022), f"Hand.{side}")
        add(box(f"Thief_Thumb.{side}", wrist + Vector((sign*.19, -.13, -.05)),
                (.11, .13, .11*h), glove_mat, groups["Body"], .030,
                rotation=(0, 0, math.radians(sign*22))), f"Hand.{side}")

    return pieces


def generate_head(p, lm, m, groups, rng):
    pieces = []

    def add(obj, bone="Head"):
        pieces.append((obj, bone))
        return obj

    hw = .46 * p["head"] * p["head_w"]
    hd = .38 * p["head"]
    hh = .49 * p["head"]
    centre = Vector((0, -.015, lm["head_c"]))
    add(loft("Thief_Head", [
        (0, -.08, centre.z - hh*.98, hw*.42, hd*.54),
        (0, -.05, centre.z - hh*.74, hw*.76, hd*.84),
        (0, -.02, centre.z - hh*.22, hw*.92, hd*.98),
        (0, 0, centre.z + hh*.22, hw*1.02, hd),
        (0, .015, centre.z + hh*.73, hw*.93, hd*.92),
        (0, .038, centre.z + hh*1.03, hw*.68, hd*.74),
    ], m["skin"], groups["Head"], sides=10, phase=math.pi/10))
    # Strong jaw plate adds the heroic Fortnite-like silhouette.
    add(plate("Thief_Jaw", [(-hw*.70, -.01, centre.z-hh*.28),
                              (hw*.70, -.01, centre.z-hh*.28),
                              (hw*.54, -.01, centre.z-hh*.62),
                              (0, -.01, centre.z-hh*.82),
                              (-hw*.54, -.01, centre.z-hh*.62)],
              .10, m["skin"], groups["Head"]))
    face_y = centre.y - hd*.98
    eye_z = centre.z + .11*p["head"]
    eye_sep = hw*.49
    for side, sign in (("L", -1), ("R", 1)):
        add(box(f"Thief_Ear.{side}",
                (sign*(hw+.008), -.005, centre.z-.03),
                (.13*p["ears"], .15, .25*p["ears"]),
                m["skin"], groups["Head"], .038))
        add(box(f"Thief_EarInner.{side}",
                (sign*(hw+.076), -.08, centre.z-.03),
                (.012, .023, .118), m["stubble"], groups["Head"], .004))
        add(box(f"Thief_Eye.{side}",
                (sign*eye_sep, face_y-.02, eye_z),
                (.22*p["head"], .034, .064*p["head"]),
                m["eye"], groups["Head"], .016,
                rotation=(0, 0, math.radians(-sign*8))))
        add(box(f"Thief_Pupil.{side}",
                (sign*eye_sep, face_y-.044, eye_z-.006),
                (.036, .012, .063), m["pupil"], groups["Head"], .007))
        add(box(f"Thief_Eyebrow.{side}",
                (sign*eye_sep, face_y-.024, eye_z+.11*p["head"]),
                (.29*p["head"], .068, .074*p["head"]),
                m["stubble"], groups["Head"], .016,
                rotation=(0, 0, math.radians(-sign*16))))
    add(plate("Thief_Nose", [(-.115*p["nose"], face_y-.02, centre.z+.05),
                              (.115*p["nose"], face_y-.02, centre.z+.05),
                              (.122*p["nose"], face_y-.175, centre.z-.16),
                              (0, face_y-.218, centre.z-.22),
                              (-.122*p["nose"], face_y-.175, centre.z-.16)],
              .09, m["skin"], groups["Head"]))
    if p["facial_hair"]:
        add(plate("Thief_ChinStubble", [(-hw*.55, face_y+.03, centre.z-.27),
                                        (hw*.55, face_y+.03, centre.z-.27),
                                        (hw*.40, face_y+.03, centre.z-.41),
                                        (0, face_y+.03, centre.z-.48),
                                        (-hw*.40, face_y+.03, centre.z-.41)],
                  .007, m["stubble"], groups["Head"]))
        add(box("Thief_Moustache", (0, face_y-.025, centre.z-.24),
                (.25, .017, .024), m["stubble"], groups["Head"], .006))
    add(box("Thief_Mouth", (.03, face_y-.035, centre.z-.33*p["head"]),
            (.29, .023, .028), m["mouth"], groups["Head"], .008,
            rotation=(0, 0, math.radians(rng.uniform(-5, 10)))))

    if p["headwear"] == "BACKWARDS_CAP":
        hat_z = centre.z + hh*.66
        add(loft("Thief_BaseballCap", [
            (0, .03, hat_z-.07, hw*1.08, hd*1.07),
            (0, .03, hat_z+.15, hw*1.06, hd*1.03),
            (0, .055, hat_z+.28, hw*.77, hd*.75),
            (0, .06, hat_z+.32, hw*.46, hd*.43),
        ], m["beanie"], groups["Head"], sides=12))
        add(box("Thief_CapFrontStrap", (0, -hd*.99, hat_z-.045),
                (.36, .062, .074), m["vest_dark"], groups["Head"], .017))
        add(box("Thief_BackwardsCapBill", (0, hd+.16, hat_z-.09),
                (.72*hw*2, .40, .088), m["beanie"], groups["Head"], .042,
                rotation=(math.radians(8), 0, 0)))
        add(box("Thief_CapStitch", (0, hd+.35, hat_z-.072),
                (.41, .015, .021), m["vest_edge"], groups["Head"], .007))
    elif p["headwear"] == "BEANIE":
        hat_z = centre.z + hh*.58
        add(loft("Thief_Beanie", [
            (0, .03, hat_z-.08, hw*1.07, hd*1.06),
            (0, .03, hat_z+.14, hw*1.07, hd*1.06),
            (0, .03, hat_z+.32, hw*.84, hd*.83),
            (0, .03, hat_z+.40, hw*.32, hd*.34),
        ], m["beanie"], groups["Head"], sides=12))
        add(box("Thief_BeanieCuff", (0, -.04, hat_z+.015),
                (hw*2.14, hd*1.98, .145), m["vest_edge"], groups["Head"], .034))
    else:
        add(plate("Thief_Hairline", [(-hw*.74, face_y+.06, centre.z+hh*.67),
                                     (hw*.74, face_y+.06, centre.z+hh*.67),
                                     (hw*.68, face_y+.045, centre.z+hh*.86),
                                     (0, face_y+.065, centre.z+hh*.81),
                                     (-hw*.68, face_y+.045, centre.z+hh*.86)],
                  .07, m["stubble"], groups["Head"]))
    if p["neck_scarf"]:
        add(box("Thief_NeckScarf", (0, -.22, lm["neck"]-.105),
                (.52, .14, .19), m["beanie"], groups["Accessories"], .025), "Neck")
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
    idle_left = {
        "Root": {"rotation": (0, 0, -2)},
        "Pelvis": {"location": (0, -0.015, 0.028), "rotation": (0, 0, -8)},
        "Spine": {"rotation": (-3, 0, 7)},
        "Chest": {"rotation": (-6, 0, 10)},
        "Neck": {"rotation": (2, 0, -5)},
        "Head": {"rotation": (4, 0, -9)},
        "UpperArm.L": {"rotation": (8, 2, 11)},
        "LowerArm.L": {"rotation": (-8, 0, 3)},
        "UpperArm.R": {"rotation": (-6, -2, -12)},
        "LowerArm.R": {"rotation": (-4, 0, -4)},
        "UpperLeg.L": {"rotation": (5, 0, 0)},
        "LowerLeg.L": {"rotation": (3, 0, 0)},
        "UpperLeg.R": {"rotation": (-4, 0, 0)},
        "weapon_socket": {"rotation": (-4, 0, -4)},
    }
    idle_right = {
        "Root": {"rotation": (0, 0, 2)},
        "Pelvis": {"location": (0, 0.01, -0.016), "rotation": (0, 0, 8)},
        "Spine": {"rotation": (-2, 0, -6)},
        "Chest": {"rotation": (-4, 0, -8)},
        "Neck": {"rotation": (1, 0, 4)},
        "Head": {"rotation": (3, 0, 8)},
        "UpperArm.L": {"rotation": (-3, 1, 6)},
        "LowerArm.L": {"rotation": (-3, 0, 2)},
        "UpperArm.R": {"rotation": (5, -1, -5)},
        "LowerArm.R": {"rotation": (-8, 0, -2)},
        "UpperLeg.L": {"rotation": (-4, 0, 0)},
        "UpperLeg.R": {"rotation": (4, 0, 0)},
        "LowerLeg.R": {"rotation": (2, 0, 0)},
        "weapon_socket": {"rotation": (-1, 0, 2)},
    }
    breathe = {
        "Pelvis": {"location": (0, 0, 0.018)},
        "Spine": {"rotation": (-2, 0, 0)},
        "Chest": {"rotation": (-7, 0, 0)},
        "Head": {"rotation": (5, 0, 0)},
        "UpperArm.L": {"rotation": (3, 0, 6)},
        "UpperArm.R": {"rotation": (0, 0, -8)},
        "weapon_socket": {"rotation": (-3, 0, 0)},
    }
    create_action(rig, "Thief_Idle", 56,
                  [(1, idle_left), (15, breathe), (29, idle_right), (43, breathe), (56, idle_left)], True)

    walk_contact_l = {
        "Pelvis": {"location": (0, 0, 0.03), "rotation": (2, 0, -10)},
        "Spine": {"rotation": (-7, 0, 8)},
        "Chest": {"rotation": (-10, 0, 10)},
        "Head": {"rotation": (6, 0, -5)},
        "UpperLeg.L": {"rotation": (-34, 0, 0)},
        "LowerLeg.L": {"rotation": (18, 0, 0)},
        "Foot.L": {"rotation": (6, 0, 0)},
        "UpperLeg.R": {"rotation": (28, 0, 0)},
        "LowerLeg.R": {"rotation": (16, 0, 0)},
        "Foot.R": {"rotation": (-8, 0, 0)},
        "UpperArm.L": {"rotation": (20, 0, 13)},
        "LowerArm.L": {"rotation": (-10, 0, 3)},
        "UpperArm.R": {"rotation": (-18, 0, -14)},
        "LowerArm.R": {"rotation": (-5, 0, -3)},
        "weapon_socket": {"rotation": (-8, 0, -6)},
    }
    walk_pass_l = {
        "Pelvis": {"location": (0, 0, -0.03), "rotation": (0, 0, -1)},
        "Spine": {"rotation": (-3, 0, 2)},
        "Chest": {"rotation": (-5, 0, 2)},
        "Head": {"rotation": (2, 0, -1)},
        "UpperLeg.L": {"rotation": (12, 0, 0)},
        "LowerLeg.L": {"rotation": (2, 0, 0)},
        "UpperLeg.R": {"rotation": (-6, 0, 0)},
        "LowerLeg.R": {"rotation": (38, 0, 0)},
        "Foot.R": {"rotation": (8, 0, 0)},
        "UpperArm.L": {"rotation": (6, 0, 4)},
        "UpperArm.R": {"rotation": (-8, 0, -5)},
    }
    walk_contact_r = {
        "Pelvis": {"location": (0, 0, 0.03), "rotation": (2, 0, 10)},
        "Spine": {"rotation": (-7, 0, -8)},
        "Chest": {"rotation": (-10, 0, -10)},
        "Head": {"rotation": (6, 0, 5)},
        "UpperLeg.L": {"rotation": (28, 0, 0)},
        "LowerLeg.L": {"rotation": (16, 0, 0)},
        "Foot.L": {"rotation": (-8, 0, 0)},
        "UpperLeg.R": {"rotation": (-34, 0, 0)},
        "LowerLeg.R": {"rotation": (18, 0, 0)},
        "Foot.R": {"rotation": (6, 0, 0)},
        "UpperArm.L": {"rotation": (-18, 0, 14)},
        "LowerArm.L": {"rotation": (-5, 0, 3)},
        "UpperArm.R": {"rotation": (20, 0, -13)},
        "LowerArm.R": {"rotation": (-10, 0, -3)},
        "weapon_socket": {"rotation": (-8, 0, 6)},
    }
    walk_pass_r = {
        "Pelvis": {"location": (0, 0, -0.03), "rotation": (0, 0, 1)},
        "Spine": {"rotation": (-3, 0, -2)},
        "Chest": {"rotation": (-5, 0, -2)},
        "Head": {"rotation": (2, 0, 1)},
        "UpperLeg.L": {"rotation": (-6, 0, 0)},
        "LowerLeg.L": {"rotation": (38, 0, 0)},
        "Foot.L": {"rotation": (8, 0, 0)},
        "UpperLeg.R": {"rotation": (12, 0, 0)},
        "LowerLeg.R": {"rotation": (2, 0, 0)},
        "UpperArm.L": {"rotation": (-8, 0, 5)},
        "UpperArm.R": {"rotation": (6, 0, -4)},
    }
    create_action(rig, "Thief_Walk", 28,
                  [(1, walk_contact_l), (7, walk_pass_l), (14, walk_contact_r), (21, walk_pass_r), (28, walk_contact_l)], True)

    aim_ready = {
        "Pelvis": {"location": (0, 0, -0.01), "rotation": (3, 0, 0)},
        "Spine": {"rotation": (-4, 0, 0)},
        "Chest": {"rotation": (-10, 0, 0)},
        "Head": {"rotation": (7, 0, 0)},
        "UpperArm.R": {"rotation": (-18, -5, -11)},
        "LowerArm.R": {"rotation": (-18, 0, 0)},
        "Hand.R": {"rotation": (2, 0, 0)},
        "UpperArm.L": {"rotation": (-16, 7, 14)},
        "LowerArm.L": {"rotation": (-26, 0, 0)},
        "Hand.L": {"rotation": (0, 0, 4)},
    }
    anticipation = {
        "Pelvis": {"location": (0, 0.015, -0.03), "rotation": (-2, 0, -1)},
        "Spine": {"rotation": (3, 0, 0)},
        "Chest": {"rotation": (8, 0, 0)},
        "Head": {"rotation": (-4, 0, 0)},
        "UpperArm.R": {"rotation": (-10, -4, -7)},
        "UpperArm.L": {"rotation": (-9, 5, 9)},
        "weapon_socket": {"rotation": (-6, 0, 0)},
    }
    recoil_amount = 28 if weapon_type == "SHOTGUN" else (13 if weapon_type == "PISTOL" else 8)
    recoil = {
        "Root": {"rotation": (0, 0, -2 if weapon_type == "SHOTGUN" else 0)},
        "Pelvis": {"location": (0, 0.035 if weapon_type == "SHOTGUN" else 0.018, 0.01),
                   "rotation": (6, 0, 0)},
        "Spine": {"rotation": (-8, 0, 0)},
        "Chest": {"location": (0, 0.07 if weapon_type == "SHOTGUN" else 0.03, 0),
                  "rotation": (recoil_amount, 0, 0)},
        "Head": {"rotation": (8, 0, 0)},
        "UpperArm.R": {"rotation": (-22, -4, -9)},
        "LowerArm.R": {"rotation": (-12, 0, 0)},
        "UpperArm.L": {"rotation": (-20, 7, 13)},
        "LowerArm.L": {"rotation": (-17, 0, 0)},
        "weapon_socket": {"rotation": (recoil_amount * 0.95, 0, 0)},
    }
    recover = {
        "Pelvis": {"location": (0, 0.01, -0.01)},
        "Chest": {"rotation": (-6, 0, 0)},
        "Head": {"rotation": (5, 0, 0)},
        "weapon_socket": {"rotation": (-2, 0, 0)},
    }
    if weapon_type == "ASSAULT_RIFLE":
        recoil_2 = dict(recoil)
        recoil_2["Chest"] = {"location": (0, 0.02, 0), "rotation": (6, 0, 0)}
        recoil_2["weapon_socket"] = {"rotation": (6, 0, 0)}
        shoot_poses = [(1, anticipation), (5, aim_ready), (8, recoil), (11, recover),
                       (14, recoil_2), (17, recover), (21, aim_ready), (28, neutral)]
        shoot_end = 28
    else:
        shoot_poses = [(1, anticipation), (6, aim_ready), (9, recoil), (15, recover), (24, neutral)]
        shoot_end = 24
    create_action(rig, "Thief_Shoot", shoot_end, shoot_poses)

    hit = {
        "Root": {"rotation": (0, 0, -18)},
        "Pelvis": {"location": (0, 0.03, 0.02), "rotation": (-6, 0, -6)},
        "Chest": {"rotation": (24, 0, 22)},
        "Head": {"rotation": (-22, 0, -24)},
        "UpperArm.L": {"rotation": (36, -16, 30)},
        "UpperArm.R": {"rotation": (-28, 16, -28)},
        "weapon_socket": {"rotation": (18, 0, 14)},
    }
    falling = {
        "Root": {"location": (0, 0.30, -0.28), "rotation": (52, 8, -30)},
        "Pelvis": {"rotation": (24, 0, 0)},
        "Chest": {"rotation": (32, 0, 24)},
        "Head": {"rotation": (-24, 0, -16)},
        "UpperLeg.L": {"rotation": (-36, 10, 0)},
        "UpperLeg.R": {"rotation": (28, -10, 0)},
        "LowerLeg.L": {"rotation": (22, 0, 0)},
        "LowerLeg.R": {"rotation": (10, 0, 0)},
        "UpperArm.L": {"rotation": (58, -24, 36)},
        "UpperArm.R": {"rotation": (-52, 20, -38)},
        "weapon_socket": {"rotation": (70, 0, 30)},
    }
    ground = {
        "Root": {"location": (0, 0.66, -1.10), "rotation": (88, 10, -34)},
        "Pelvis": {"rotation": (25, 0, 0)},
        "Chest": {"rotation": (20, 0, 16)},
        "Head": {"rotation": (-24, 10, -16)},
        "UpperLeg.L": {"rotation": (-42, 14, -6)},
        "LowerLeg.L": {"rotation": (56, 0, 0)},
        "UpperLeg.R": {"rotation": (20, -10, 8)},
        "LowerLeg.R": {"rotation": (24, 0, 0)},
        "UpperArm.L": {"rotation": (64, -24, 40)},
        "UpperArm.R": {"rotation": (-60, 22, -42)},
        "weapon_socket": {"rotation": (76, 0, 32)},
    }
    create_action(rig, "Thief_Death", 56,
                  [(1, neutral), (7, hit), (19, falling), (40, ground), (56, ground)])

    rig.animation_data.action = bpy.data.actions["Thief_Idle"]
    bpy.context.scene.frame_start, bpy.context.scene.frame_end = 1, 56
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

    root["generator"] = "Couch Guys Cartoon Thief v3"
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
