
import bpy
import random
import math
from mathutils import Vector

# ============================================================
# COUCH GUYS - PROCEDURAL CARTOON STREETLIGHT GENERATOR
# Blender 3.6+ / 4.x
#
# Generates a new variation each run.
# Ground pivot: (0, 0, 0)
# Arm extends towards local +X.
# Designed for Unity export.
# ============================================================

# ---------------- CONFIGURATION ----------------

SEED = None  # None = random every run
EXPORT_FBX = False
EXPORT_PATH = "//CouchGuys_Streetlight.fbx"

MIN_HEIGHT = 4.7
MAX_HEIGHT = 5.6

MIN_ARM_LENGTH = 1.2
MAX_ARM_LENGTH = 1.7

ROOT_NAME = "CouchGuys_Streetlight"
PREFIX = "CG_Lamp_"

# Main pole, secondary trim, dark metal
PALETTES = [
    ((0.10, 0.54, 0.65, 1), (1.00, 0.73, 0.23, 1),
     (0.06, 0.19, 0.28, 1)),
    ((0.20, 0.43, 0.79, 1), (1.00, 0.66, 0.24, 1),
     (0.08, 0.16, 0.31, 1)),
    ((0.17, 0.58, 0.45, 1), (1.00, 0.83, 0.36, 1),
     (0.08, 0.25, 0.23, 1)),
    ((0.86, 0.34, 0.29, 1), (1.00, 0.81, 0.38, 1),
     (0.28, 0.13, 0.22, 1)),
]

rng = random.Random(SEED)

HEIGHT = rng.uniform(MIN_HEIGHT, MAX_HEIGHT)
ARM_LENGTH = rng.uniform(MIN_ARM_LENGTH, MAX_ARM_LENGTH)
ARM_RISE = rng.uniform(0.28, 0.43)

MAIN_COLOR, ACCENT_COLOR, DARK_COLOR = rng.choice(PALETTES)

# ---------------- CLEAN UP PREVIOUS MODEL ----------------

for obj in list(bpy.data.objects):
    if obj.name == ROOT_NAME or obj.name.startswith(PREFIX):
        bpy.data.objects.remove(obj, do_unlink=True)

# ---------------- ROOT ----------------

root = bpy.data.objects.new(ROOT_NAME, None)
bpy.context.collection.objects.link(root)
root.empty_display_type = 'PLAIN_AXES'
root.empty_display_size = 0.4

# ---------------- MATERIALS ----------------

def make_material(name, color, metallic=0.0,
                  roughness=0.65, emission=0.0):
    mat = bpy.data.materials.get(name)

    if mat is None:
        mat = bpy.data.materials.new(name)

    mat.use_nodes = True
    mat.diffuse_color = color

    nodes = mat.node_tree.nodes
    bsdf = nodes.get("Principled BSDF")

    if bsdf:
        bsdf.inputs["Base Color"].default_value = color
        bsdf.inputs["Metallic"].default_value = metallic
        bsdf.inputs["Roughness"].default_value = roughness

        for socket_name in ("Emission Color", "Emission"):
            socket = bsdf.inputs.get(socket_name)
            if socket:
                socket.default_value = color
                break

        strength = bsdf.inputs.get("Emission Strength")
        if strength:
            strength.default_value = emission

    return mat


main_mat = make_material(
    "CG_Streetlight_Main", MAIN_COLOR, 0.25, 0.48
)

accent_mat = make_material(
    "CG_Streetlight_Accent", ACCENT_COLOR, 0.2, 0.5
)

dark_mat = make_material(
    "CG_Streetlight_Dark", DARK_COLOR, 0.35, 0.45
)

glass_mat = make_material(
    "CG_Streetlight_Glow",
    (1.0, 0.83, 0.36, 1),
    0.0, 0.3, 3.0
)

highlight_mat = make_material(
    "CG_Streetlight_Highlight",
    (1.0, 0.96, 0.75, 1),
    0.0, 0.25, 1.8
)

# ---------------- GEOMETRY HELPERS ----------------

def finish(obj, name, material, bevel=0.0):
    obj.name = PREFIX + name

    if material:
        obj.data.materials.append(material)

    if bevel > 0:
        mod = obj.modifiers.new("Soft Cartoon Edges", 'BEVEL')
        mod.width = bevel
        mod.segments = 2
        mod.limit_method = 'ANGLE'

        normal = obj.modifiers.new("Weighted Normals", 'WEIGHTED_NORMAL')
        normal.keep_sharp = True

    obj.parent = root
    return obj


def cube(name, loc, dimensions, material, bevel=0.04):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc)
    obj = bpy.context.object

    obj.dimensions = dimensions
    bpy.ops.object.transform_apply(
        location=False, rotation=False, scale=True
    )

    return finish(obj, name, material, bevel)


def cylinder(name, loc, radius, depth, material,
             vertices=12, bevel=0.025):
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=vertices,
        radius=radius,
        depth=depth,
        location=loc
    )
    return finish(obj=bpy.context.object, name=name,
                  material=material, bevel=bevel)


def cone(name, loc, bottom_radius, top_radius, depth,
         material, vertices=12, bevel=0.02):
    bpy.ops.mesh.primitive_cone_add(
        vertices=vertices,
        radius1=bottom_radius,
        radius2=top_radius,
        depth=depth,
        location=loc
    )
    return finish(bpy.context.object, name, material, bevel)


def sphere(name, loc, scale, material):
    bpy.ops.mesh.primitive_uv_sphere_add(
        segments=16,
        ring_count=8,
        radius=1,
        location=loc
    )

    obj = bpy.context.object
    obj.scale = scale
    bpy.ops.object.transform_apply(
        location=False, rotation=False, scale=True
    )

    for poly in obj.data.polygons:
        poly.use_smooth = True

    return finish(obj, name, material)


def curved_tube(name, points, radius, material):
    curve = bpy.data.curves.new(PREFIX + name, 'CURVE')
    curve.dimensions = '3D'
    curve.resolution_u = 16
    curve.bevel_depth = radius
    curve.bevel_resolution = 3
    curve.use_fill_caps = True

    spline = curve.splines.new('BEZIER')
    spline.bezier_points.add(len(points) - 1)

    for p, pos in zip(spline.bezier_points, points):
        p.co = pos
        p.handle_left_type = 'AUTO'
        p.handle_right_type = 'AUTO'

    obj = bpy.data.objects.new(PREFIX + name, curve)
    bpy.context.collection.objects.link(obj)
    curve.materials.append(material)
    obj.parent = root
    return obj


# ============================================================
# 1. BASE
# ============================================================

# Wide, stable, stylised base
cylinder(
    "Base_Foot", (0, 0, 0.09),
    0.33, 0.18, dark_mat,
    bevel=0.045
)

cylinder(
    "Base_Accent", (0, 0, 0.20),
    0.29, 0.075, accent_mat
)

cone(
    "Base_Taper", (0, 0, 0.44),
    0.25, 0.15, 0.43, main_mat
)

cylinder(
    "Base_Top_Ring", (0, 0, 0.69),
    0.175, 0.07, dark_mat
)

# Small bolts around base
for i in range(4):
    angle = i * math.pi / 2

    x = math.cos(angle) * 0.245
    y = math.sin(angle) * 0.245

    cylinder(
        f"Base_Bolt_{i}",
        (x, y, 0.19),
        0.035, 0.03,
        accent_mat,
        vertices=8,
        bevel=0.008
    )

# ============================================================
# 2. MAIN TAPERED POLE
# ============================================================

pole_bottom = 0.7
pole_top = HEIGHT - 0.15
pole_length = pole_top - pole_bottom

cone(
    "Main_Tapered_Pole",
    (0, 0, pole_bottom + pole_length / 2),
    0.15, 0.095, pole_length,
    main_mat,
    vertices=12,
    bevel=0.02
)

# Decorative bands
for z, radius in [
    (0.88, 0.16),
    (1.08, 0.155),
    (HEIGHT - 0.47, 0.115)
]:
    cylinder(
        f"Pole_Trim_{z:.2f}",
        (0, 0, z),
        radius, 0.055,
        accent_mat,
        bevel=0.012
    )

# Decorative mounting badge
badge_height = rng.uniform(1.4, 1.8)

cube(
    "Pole_Decorative_Badge",
    (0, -0.145, badge_height),
    (0.16, 0.045, 0.23),
    accent_mat,
    bevel=0.035
)

# ============================================================
# 3. CURVED STREETLIGHT ARM
# ============================================================

arm_start_z = HEIGHT - 0.25
arm_end_z = HEIGHT + 0.04
end_x = ARM_LENGTH

curved_tube(
    "Curved_Lamp_Arm",
    [
        (0.0, 0.0, arm_start_z),
        (0.3, 0.0, HEIGHT + ARM_RISE * 0.45),
        (end_x * 0.65, 0.0, HEIGHT + ARM_RISE),
        (end_x, 0.0, arm_end_z)
    ],
    radius=0.075,
    material=main_mat
)

# Support fitting where arm meets the pole
sphere(
    "Arm_Connection",
    (0, 0, arm_start_z),
    (0.17, 0.16, 0.17),
    dark_mat
)

# Decorative joint at end of arm
sphere(
    "Lamp_Arm_End",
    (end_x, 0, arm_end_z),
    (0.12, 0.12, 0.12),
    accent_mat
)

# ============================================================
# 4. CARTOON LAMP HOUSING
# ============================================================

# Short vertical neck
cylinder(
    "Lamp_Neck",
    (end_x, 0, HEIGHT - 0.08),
    0.115, 0.25,
    dark_mat
)

# Top fitting
cylinder(
    "Lamp_Top_Cap",
    (end_x, 0, HEIGHT - 0.17),
    0.20, 0.09,
    accent_mat
)

# Wide, downward-flaring shade
cone(
    "Lamp_Shade",
    (end_x, 0, HEIGHT - 0.31),
    0.37, 0.18, 0.25,
    main_mat,
    vertices=12,
    bevel=0.035
)

# Rim around lower edge of shade
cylinder(
    "Lamp_Shade_Rim",
    (end_x, 0, HEIGHT - 0.445),
    0.38, 0.065,
    dark_mat,
    bevel=0.015
)

# Emissive glass
cylinder(
    "Glowing_Glass",
    (end_x, 0, HEIGHT - 0.505),
    0.25, 0.115,
    glass_mat,
    vertices=16,
    bevel=0.035
)

# Bright underside
cylinder(
    "Glowing_Underside",
    (end_x, 0, HEIGHT - 0.567),
    0.215, 0.025,
    highlight_mat,
    vertices=16,
    bevel=0.008
)

# Decorative rim surrounding the glass
cylinder(
    "Glass_Lower_Trim",
    (end_x, 0, HEIGHT - 0.59),
    0.275, 0.04,
    accent_mat,
    vertices=16,
    bevel=0.012
)

# ============================================================
# 5. OPTIONAL CARTOON DETAIL
# ============================================================

# Simple extra trim; no detailed geometry needed
cylinder(
    "Pole_Top_Cap",
    (0, 0, HEIGHT - 0.09),
    0.13, 0.12,
    dark_mat,
    bevel=0.025
)

# ============================================================
# 6. FINISH
# ============================================================

bpy.context.view_layer.objects.active = root

bpy.ops.object.select_all(action='DESELECT')
root.select_set(True)

for child in root.children_recursive:
    child.select_set(True)

# Export as FBX if enabled.
if EXPORT_FBX:
    bpy.ops.export_scene.fbx(
        filepath=bpy.path.abspath(EXPORT_PATH),
        use_selection=True,
        object_types={'MESH', 'EMPTY'},
        apply_unit_scale=True,
        axis_forward='-Z',
        axis_up='Y',
        use_mesh_modifiers=True,
        bake_anim=False,
        add_leaf_bones=False
    )

print("================================")
print("COUCH GUYS STREETLIGHT GENERATED")
print(f"Height: {HEIGHT:.2f} m")
print(f"Arm Length: {ARM_LENGTH:.2f} m")
print(f"Random Seed: {SEED}")
print("Ground pivot: (0, 0, 0)")
print("================================")
