
import bpy
import math
from mathutils import Vector

# ============================================================
# COUCH GUYS - CARTOON FURNITURE SLED GENERATOR
# ============================================================
# Blender 3.6 / 4.x
#
# X = Width
# Y = Length (front is +Y)
# Z = Height
#
# The model origin is at ground level.
# All geometry is grouped under CG_Sled_ROOT.
#
# Set EXPORT_FBX to True to automatically export the sled.
# ============================================================

COLLECTION_NAME = "CouchGuys_Sled"
EXPORT_FBX = False
EXPORT_PATH = bpy.path.abspath("//CouchGuys_Sled.fbx")

# ------------------------------------------------------------
# CONFIGURATION
# ------------------------------------------------------------

SLED_WIDTH = 2.6
SLED_LENGTH = 1.8

DECK_HEIGHT = 0.58
DECK_THICKNESS = 0.14

RUNNER_SPACING = 1.82
RUNNER_WIDTH = 0.19

BEVEL_SEGMENTS = 3

# Cartoon colour palette
WOOD_MAIN = "#E5A45B"
WOOD_LIGHT = "#F5C77C"
WOOD_DARK = "#BD753E"

RUNNER_COLOUR = "#34495E"
RUNNER_HIGHLIGHT = "#506B82"

ACCENT = "#41C8BD"
ACCENT_DARK = "#218F8B"

YELLOW = "#FFD76A"
ROPE_COLOUR = "#F0DFAC"
RUBBER = "#263B4C"
BOLT_COLOUR = "#E5ECED"


# ------------------------------------------------------------
# CLEAN UP PREVIOUS GENERATED SLED
# ------------------------------------------------------------

old_collection = bpy.data.collections.get(COLLECTION_NAME)

if old_collection:
    for obj in list(old_collection.objects):
        bpy.data.objects.remove(obj, do_unlink=True)

    bpy.data.collections.remove(old_collection)

collection = bpy.data.collections.new(COLLECTION_NAME)
bpy.context.scene.collection.children.link(collection)

root = bpy.data.objects.new("CG_Sled_ROOT", None)
root.empty_display_type = 'PLAIN_AXES'
root.empty_display_size = 0.35
collection.objects.link(root)


# ------------------------------------------------------------
# MATERIAL HELPERS
# ------------------------------------------------------------

def hex_to_rgba(value):
    value = value.lstrip("#")

    return tuple(
        int(value[i:i + 2], 16) / 255.0
        for i in (0, 2, 4)
    ) + (1.0,)


def create_material(name, colour, metallic=0.0):
    mat = bpy.data.materials.get(name)

    if mat is None:
        mat = bpy.data.materials.new(name)

    rgba = hex_to_rgba(colour)

    mat.diffuse_color = rgba
    mat.use_nodes = True

    shader = mat.node_tree.nodes.get("Principled BSDF")

    if shader:
        shader.inputs["Base Color"].default_value = rgba
        shader.inputs["Roughness"].default_value = 0.78
        shader.inputs["Metallic"].default_value = metallic

    return mat


wood = create_material("CGS_Wood", WOOD_MAIN)
wood_light = create_material("CGS_Wood_Light", WOOD_LIGHT)
wood_dark = create_material("CGS_Wood_Dark", WOOD_DARK)

runner_mat = create_material(
    "CGS_Runners", RUNNER_COLOUR, 0.25
)

runner_highlight = create_material(
    "CGS_Runner_Highlight", RUNNER_HIGHLIGHT, 0.2
)

accent = create_material("CGS_Accent", ACCENT)
accent_dark = create_material("CGS_Accent_Dark", ACCENT_DARK)

yellow = create_material("CGS_Yellow", YELLOW)
rope_mat = create_material("CGS_Rope", ROPE_COLOUR)
rubber = create_material("CGS_Rubber", RUBBER)
bolt_mat = create_material(
    "CGS_Bolts", BOLT_COLOUR, 0.45
)


# ------------------------------------------------------------
# OBJECT HELPERS
# ------------------------------------------------------------

def register(obj, material=None):
    for col in list(obj.users_collection):
        col.objects.unlink(obj)

    collection.objects.link(obj)
    obj.parent = root

    if material:
        obj.data.materials.append(material)

    return obj


def add_bevel(obj, amount=0.04):
    bevel = obj.modifiers.new(
        "Cartoon Rounded Edges", 'BEVEL'
    )

    bevel.width = amount
    bevel.segments = BEVEL_SEGMENTS
    bevel.limit_method = 'ANGLE'

    normal = obj.modifiers.new(
        "Weighted Normals", 'WEIGHTED_NORMAL'
    )

    normal.keep_sharp = True


def create_box(name, position, dimensions,
               material, bevel=0.03):

    hx, hy, hz = (
        dimensions[0] / 2,
        dimensions[1] / 2,
        dimensions[2] / 2
    )

    vertices = [
        (-hx, -hy, -hz),
        ( hx, -hy, -hz),
        ( hx,  hy, -hz),
        (-hx,  hy, -hz),
        (-hx, -hy,  hz),
        ( hx, -hy,  hz),
        ( hx,  hy,  hz),
        (-hx,  hy,  hz)
    ]

    faces = [
        (3, 2, 1, 0),
        (4, 5, 6, 7),
        (0, 1, 5, 4),
        (1, 2, 6, 5),
        (2, 3, 7, 6),
        (3, 0, 4, 7)
    ]

    mesh = bpy.data.meshes.new(name + "_Mesh")
    mesh.from_pydata(vertices, [], faces)
    mesh.update()

    obj = bpy.data.objects.new(name, mesh)
    collection.objects.link(obj)

    obj.parent = root
    obj.location = position

    obj.data.materials.append(material)

    if bevel > 0:
        add_bevel(obj, bevel)

    return obj


def create_cylinder(name, start, end,
                    radius, material, vertices=12):

    start = Vector(start)
    end = Vector(end)

    direction = end - start

    bpy.ops.mesh.primitive_cylinder_add(
        vertices=vertices,
        radius=radius,
        depth=direction.length
    )

    obj = bpy.context.object
    obj.name = name

    obj.location = (start + end) / 2

    obj.rotation_euler = direction.to_track_quat(
        'Z', 'Y'
    ).to_euler()

    register(obj, material)

    add_bevel(obj, min(radius * 0.25, 0.015))

    return obj


def create_ring(name, position, material,
                radius=0.085):

    bpy.ops.mesh.primitive_torus_add(
        major_segments=16,
        minor_segments=8,
        major_radius=radius,
        minor_radius=0.025,
        location=position,
        rotation=(math.pi / 2, 0, 0)
    )

    obj = bpy.context.object
    obj.name = name

    register(obj, material)

    return obj


def create_rope(name, positions,
                material, thickness=0.032):

    curve = bpy.data.curves.new(
        name + "_Curve", type='CURVE'
    )

    curve.dimensions = '3D'
    curve.resolution_u = 12
    curve.bevel_depth = thickness
    curve.bevel_resolution = 3
    curve.fill_mode = 'FULL'

    spline = curve.splines.new('BEZIER')
    spline.bezier_points.add(len(positions) - 1)

    for point, coord in zip(
        spline.bezier_points, positions
    ):
        point.co = coord
        point.handle_left_type = 'AUTO'
        point.handle_right_type = 'AUTO'

    obj = bpy.data.objects.new(name, curve)
    collection.objects.link(obj)

    obj.parent = root
    obj.data.materials.append(material)

    # Convert rope to mesh for reliable Unity export.
    bpy.ops.object.select_all(action='DESELECT')

    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj

    bpy.ops.object.convert(target='MESH')

    return bpy.context.object


def create_marker(name, position):
    obj = bpy.data.objects.new(name, None)

    obj.empty_display_type = 'SPHERE'
    obj.empty_display_size = 0.12

    collection.objects.link(obj)

    obj.parent = root
    obj.location = position

    return obj


# ============================================================
# 1. CURVED SLED RUNNERS
# ============================================================

# Centreline profile: Y, Z.
# The front curls upwards for the cartoon sled silhouette.

runner_profile = [
    (-1.14, 0.28),
    (-1.04, 0.20),
    (-0.88, 0.17),
    (-0.45, 0.16),
    ( 0.05, 0.16),
    ( 0.50, 0.16),
    ( 0.80, 0.18),
    ( 0.98, 0.24),
    ( 1.10, 0.38),
    ( 1.17, 0.57)
]


def make_runner(name, x_position):

    half_height = 0.07
    half_width = RUNNER_WIDTH / 2

    top = []
    bottom = []

    for i, (y, z) in enumerate(runner_profile):

        before = Vector(
            runner_profile[max(i - 1, 0)]
        )

        after = Vector(
            runner_profile[min(
                i + 1, len(runner_profile) - 1
            )]
        )

        tangent = (after - before).normalized()

        normal = Vector((
            -tangent.y,
            tangent.x
        ))

        centre = Vector((y, z))

        top.append(
            centre + normal * half_height
        )

        bottom.append(
            centre - normal * half_height
        )

    outline = top + list(reversed(bottom))

    count = len(outline)

    vertices = []

    for x_offset in (-half_width, half_width):
        for point in outline:
            vertices.append((
                x_offset,
                point.x,
                point.y
            ))

    faces = []

    faces.append(tuple(reversed(range(count))))

    faces.append(
        tuple(range(count, count * 2))
    )

    for i in range(count):

        next_i = (i + 1) % count

        faces.append((
            i,
            next_i,
            next_i + count,
            i + count
        ))

    mesh = bpy.data.meshes.new(name + "_Mesh")
    mesh.from_pydata(vertices, [], faces)
    mesh.update()

    obj = bpy.data.objects.new(name, mesh)

    collection.objects.link(obj)

    obj.parent = root
    obj.location.x = x_position

    obj.data.materials.append(runner_mat)

    add_bevel(obj, 0.025)

    return obj


runner_x = RUNNER_SPACING / 2

make_runner("Runner_Left", -runner_x)
make_runner("Runner_Right", runner_x)


# Runner side stripes
for side, x in [
    ("Left", -runner_x),
    ("Right", runner_x)
]:

    outer_direction = -1 if x < 0 else 1

    stripe_x = x + outer_direction * (
        RUNNER_WIDTH / 2 + 0.009
    )

    create_box(
        "Runner_Stripe_" + side,
        (stripe_x, -0.05, 0.19),
        (0.018, 1.48, 0.038),
        runner_highlight,
        0.012
    )


# ============================================================
# 2. UNDER-DECK SUPPORTS
# ============================================================

for x in (-runner_x, runner_x):

    for y in (-0.56, 0.56):

        create_box(
            "Vertical_Deck_Support",
            (x, y, 0.38),
            (0.20, 0.22, 0.29),
            runner_mat,
            0.035
        )


for y in (-0.56, 0.56):

    create_box(
        "Cross_Support",
        (0, y, 0.475),
        (2.35, 0.17, 0.14),
        accent_dark,
        0.035
    )


# Longitudinal reinforcing beams
for x in (-0.83, 0.83):

    create_box(
        "Deck_Reinforcement",
        (x, 0, 0.495),
        (0.12, 1.63, 0.11),
        wood_dark,
        0.025
    )


# ============================================================
# 3. MAIN WOODEN PLATFORM
# ============================================================

plank_positions = [
    -0.66,
    -0.33,
     0.00,
     0.33,
     0.66
]

plank_materials = [
    wood,
    wood_light,
    wood,
    wood_light,
    wood
]

for i, y in enumerate(plank_positions):

    create_box(
        "Deck_Plank_%02d" % (i + 1),
        (0, y, DECK_HEIGHT),
        (SLED_WIDTH, 0.295, DECK_THICKNESS),
        plank_materials[i],
        0.045
    )


# ============================================================
# 4. COLOURFUL SIDE BUMPERS
# ============================================================

# These sit only slightly above the platform.
# They are visual bumpers rather than tall walls.

for side, x in [
    ("Left", -1.32),
    ("Right", 1.32)
]:

    create_box(
        "Side_Bumper_" + side,
        (x, 0, 0.62),
        (0.115, SLED_LENGTH, 0.13),
        accent,
        0.045
    )

    create_box(
        "Bumper_Trim_" + side,
        (x, 0, 0.692),
        (0.075, 1.72, 0.026),
        yellow,
        0.012
    )


# Backstop prevents the couch from
# visually sliding off the rear end.

create_box(
    "Rear_Stop",
    (0, -0.845, 0.685),
    (2.45, 0.115, 0.21),
    accent_dark,
    0.035
)


# Front decorative bumper
create_box(
    "Front_Bumper",
    (0, 0.87, 0.585),
    (2.25, 0.12, 0.16),
    accent,
    0.035
)


# Front yellow accent
create_box(
    "Front_Bumper_Highlight",
    (0, 0.94, 0.60),
    (1.65, 0.025, 0.055),
    yellow,
    0.012
)


# ============================================================
# 5. CARTOON BOLTS AND REINFORCEMENTS
# ============================================================

for x in (-1.14, 1.14):

    for y in plank_positions:

        create_cylinder(
            "Deck_Bolt",
            (x, y, 0.652),
            (x, y, 0.672),
            0.027,
            bolt_mat,
            vertices=8
        )


# Metal corner plates
for x in (-1.18, 1.18):

    for y in (-0.76, 0.76):

        create_box(
            "Corner_Plate",
            (x, y, 0.655),
            (0.16, 0.13, 0.028),
            runner_mat,
            0.018
        )


# ============================================================
# 6. TOWING ROPE AND FRONT HANDLE
# ============================================================

for side, x in [
    ("Left", -0.62),
    ("Right", 0.62)
]:

    create_ring(
        "Tow_Ring_" + side,
        (x, 0.96, 0.59),
        bolt_mat
    )


create_rope(
    "Tow_Rope_Left",
    [
        (-0.62, 0.96, 0.59),
        (-0.61, 1.12, 0.58),
        (-0.38, 1.40, 0.49),
        (-0.22, 1.63, 0.49)
    ],
    rope_mat
)

create_rope(
    "Tow_Rope_Right",
    [
        (0.62, 0.96, 0.59),
        (0.61, 1.12, 0.58),
        (0.38, 1.40, 0.49),
        (0.22, 1.63, 0.49)
    ],
    rope_mat
)


# Wooden handle for pulling the sled
create_cylinder(
    "Tow_Handle",
    (-0.34, 1.64, 0.49),
    (0.34, 1.64, 0.49),
    0.07,
    wood_dark,
    vertices=12
)


# Rubber grips
for x1, x2 in [
    (-0.33, -0.25),
    (0.25, 0.33)
]:

    create_cylinder(
        "Tow_Handle_Grip",
        (x1, 1.64, 0.49),
        (x2, 1.64, 0.49),
        0.078,
        rubber,
        vertices=12
    )


# ============================================================
# 7. TIE-DOWN RINGS
# ============================================================

# Useful visual attachment points for future upgrades.

for x in (-1.30, 1.30):

    for y in (-0.52, 0.48):

        create_ring(
            "Cargo_Tie_Down",
            (x, y, 0.69),
            yellow,
            0.058
        )


# ============================================================
# 8. UNITY ATTACHMENT MARKERS
# ============================================================

# These are transforms intended to help Codex
# integrate the sled with the couch carrying system.

create_marker(
    "CouchAnchor",
    (0, 0, 0.66)
)

create_marker(
    "TowPoint",
    (0, 1.64, 0.49)
)

create_marker(
    "SledCentre",
    (0, 0, 0.32)
)


# ============================================================
# 9. FINALISE
# ============================================================

# Select the generated sled
bpy.ops.object.select_all(action='DESELECT')

for obj in collection.objects:
    obj.select_set(True)

bpy.context.view_layer.objects.active = root

# Optional automatic FBX export
if EXPORT_FBX:

    bpy.ops.export_scene.fbx(
        filepath=EXPORT_PATH,
        use_selection=True,
        axis_forward='-Z',
        axis_up='Y',
        apply_unit_scale=True,
        add_leaf_bones=False
    )

    print("Exported sled to:", EXPORT_PATH)


print("===================================")
print("COUCH GUYS SLED GENERATED")
print("Collection:", COLLECTION_NAME)
print("Width:", SLED_WIDTH)
print("Length:", SLED_LENGTH)
print("Objects:", len(collection.objects))
print("===================================")
