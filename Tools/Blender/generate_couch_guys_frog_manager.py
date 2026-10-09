"""
COUCH GUYS - CARTOON FROG DELIVERY MANAGER
==========================================
Run in Blender 3.6+ (recommended 4.2+):
    Blender > Scripting > Open > Run Script
or:
    blender --background --python generate_couch_guys_frog_manager.py

Creates a game-oriented, rounded, muscular frog NPC with:
  * One skinned mesh with multiple cartoon materials
  * An armature with articulated arms, legs, head, jaw and eyelids
  * Three separately named Actions: FrogManager_Idle, FrogManager_Talk,
    FrogManager_Backflip
  * Optional Unity-friendly FBX export (see EXPORT_FBX below)
  * Optional .blend save and a studio preview scene

Blender uses Z-up, character faces -Y. All geometry is generated from code:
no textures, external models or add-ons are required.

Animation timing: 30 FPS; Idle 1-90, Talk 1-80, Backflip 1-72.
For Unity: FBX import > Animation > add/use the three clips; loop Idle/Talk,
not Backflip. The Backflip rises and rotates about the character's centre.
"""

import bpy
import math
import random
from mathutils import Vector

# -----------------------------------------------------------------------------
# USER SETTINGS
# -----------------------------------------------------------------------------
RANDOM_SEED = 2184
EXPORT_FBX = False                   # True -> exports an FBX beside this .py/.blend
SAVE_BLEND = False                   # True -> also saves a .blend file
CREATE_PREVIEW_SCENE = True          # Camera, lights and ground (excluded from FBX)
GENERATE_MANAGER_CAP = True
GENERATE_DELIVERY_VEST = True
GENERATE_WARTS = True
WART_COUNT = 33
FPS = 30

FBX_FILENAME = "CouchGuys_FrogManager.fbx"
BLEND_FILENAME = "CouchGuys_FrogManager.blend"

# Colour palette deliberately chosen to complement the bright, readable,
# rounded Couch Guys neighborhood and orange delivery/inventory UI.
COLORS = {
    "skin":     "79C65D",  # lively frog green
    "skin_light": "9BDC76",
    "skin_shadow": "4F9564",
    "skin_dark": "376E55",
    "belly": "E5E7AC",
    "belly_shadow": "BCCC85",
    "wart": "629F50",
    "eye_white": "F8F5DA",
    "iris": "EEAB45",
    "iris_edge": "98552E",
    "pupil": "1A2F37",
    "shine": "FFFFFF",
    "mouth": "322B39",
    "tongue": "EB8090",
    "tooth": "FFF5D6",
    "vest": "FF9A35",
    "vest_shadow": "E4702E",
    "vest_trim": "FFE1A0",
    "badge": "254E55",
    "badge_text": "FFF8D8",
    "cap": "F89939",
    "cap_trim": "F9C46F",
    "ground": "D9E9EA",
}

rng = random.Random(RANDOM_SEED)
PI = math.pi
DEG = math.radians

# -----------------------------------------------------------------------------
# START FRESH
# -----------------------------------------------------------------------------
if bpy.context.object and bpy.context.object.mode != 'OBJECT':
    bpy.ops.object.mode_set(mode='OBJECT')
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)

for data_collection in (bpy.data.armatures, bpy.data.meshes, bpy.data.curves,
                        bpy.data.cameras, bpy.data.lights):
    # Blender automatically deletes orphan datablocks later; this makes reruns
    # predictable without touching anything outside this generated scene.
    pass

bpy.context.scene.render.fps = FPS
bpy.context.scene.render.fps_base = 1


def srgb_hex(s):
    vals = [int(s[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    # Blender material RGB sockets expect scene-linear values.
    return tuple((v / 12.92 if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4)
                 for v in vals)


def make_mat(name, hex_color, roughness=0.8, metallic=0.0):
    m = bpy.data.materials.new(name)
    rgb = srgb_hex(hex_color)
    m.diffuse_color = (*rgb, 1)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get('Principled BSDF')
    if bsdf:
        bsdf.inputs['Base Color'].default_value = (*rgb, 1)
        bsdf.inputs['Roughness'].default_value = roughness
        bsdf.inputs['Metallic'].default_value = metallic
    return m


mats = {key: make_mat('Frog_' + key, hx, 0.47 if key in ('eye_white', 'iris', 'shine')
                     else (0.56 if key in ('vest', 'cap', 'badge') else 0.78))
        for key, hx in COLORS.items()}

mesh_parts = []


def add_weighted(obj, mat_name, bone):
    """Give each shape rigid skin weights; merge shapes into ONE skinned mesh."""
    obj.name = 'FM_' + obj.name
    obj.data.materials.clear()
    obj.data.materials.append(mats[mat_name])
    if bone:
        group = obj.vertex_groups.new(name=bone)
        group.add(list(range(len(obj.data.vertices))), 1.0, 'REPLACE')
    for polygon in obj.data.polygons:
        polygon.use_smooth = True
    mesh_parts.append(obj)
    return obj


def freeze_transform(obj):
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)


def orb(name, xyz, radii, material, bone, rot=(0, 0, 0), seg=24, rings=16):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=seg, ring_count=rings, location=xyz)
    o = bpy.context.object
    o.name = name
    o.rotation_euler = rot
    o.scale = radii
    freeze_transform(o)
    return add_weighted(o, material, bone)


def capsule(name, a, b, radii, material, bone):
    """Stretch a smooth orb along a line, with exact object transforms applied."""
    av, bv = Vector(a), Vector(b)
    v = bv - av
    middle = (av + bv) * 0.5
    rot = v.to_track_quat('Z', 'Y').to_euler()
    return orb(name, middle, (radii[0], radii[1], v.length * 0.5 + radii[2]),
               material, bone, rot=rot)


def rounded_box(name, xyz, dimensions, radius, material, bone, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_cube_add(size=1, location=xyz)
    o = bpy.context.object
    o.name = name
    o.dimensions = dimensions
    o.rotation_euler = rot
    freeze_transform(o)
    bevel = o.modifiers.new('soft_rounded_edges', 'BEVEL')
    bevel.width = radius
    bevel.segments = 3
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.modifier_apply(modifier=bevel.name)
    o.modifiers.new('weighted_corner_normals', 'WEIGHTED_NORMAL')
    # Applying prevents extra modifiers being lost on final joined mesh.
    bpy.ops.object.modifier_apply(modifier='weighted_corner_normals')
    return add_weighted(o, material, bone)


def stroke(name, coords, radius, material, bone, resolution=3):
    """Beveled 3D line for smiles, seams, eyebrows and little decorative marks."""
    data = bpy.data.curves.new(name + '_curve', 'CURVE')
    data.dimensions = '3D'
    data.resolution_u = 16
    data.bevel_depth = radius
    data.resolution_u = 12
    data.bevel_resolution = resolution
    spline = data.splines.new('POLY')
    spline.points.add(len(coords) - 1)
    for p, coord in zip(spline.points, coords):
        p.co = (coord[0], coord[1], coord[2], 1.0)
    o = bpy.data.objects.new(name, data)
    bpy.context.collection.objects.link(o)
    bpy.ops.object.select_all(action='DESELECT')
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.convert(target='MESH')
    o = bpy.context.object
    freeze_transform(o)
    return add_weighted(o, material, bone)


def text_mesh(name, word, xyz, size, material, bone):
    """Text is converted to mesh so it is included in the FBX body mesh."""
    d = bpy.data.curves.new(name, type='FONT')
    d.body = word
    d.size = size
    d.align_x = 'CENTER'
    d.align_y = 'CENTER'
    d.extrude = 0.002
    d.bevel_depth = 0.0007
    o = bpy.data.objects.new(name, d)
    bpy.context.collection.objects.link(o)
    o.location = xyz
    o.rotation_euler = (PI / 2, 0, 0)  # face -Y
    bpy.ops.object.select_all(action='DESELECT')
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.convert(target='MESH')
    o = bpy.context.object
    freeze_transform(o)
    return add_weighted(o, material, bone)

# -----------------------------------------------------------------------------
# ARMATURE
# Separate pivot bones, most pointing upward to ensure intuitive pose channels.
# Meshes are weighted 100% to a pivot, creating a puppet-like cartoon look.
# -----------------------------------------------------------------------------
arm_data = bpy.data.armatures.new('FrogManager_Skeleton')
rig = bpy.data.objects.new('CouchGuys_FrogManager_Rig', arm_data)
bpy.context.collection.objects.link(rig)
bpy.context.view_layer.objects.active = rig
rig.select_set(True)
bpy.ops.object.mode_set(mode='EDIT')


def make_bone(name, at, parent=None, length=0.22):
    eb = arm_data.edit_bones.new(name)
    eb.head = at
    eb.tail = (at[0], at[1], at[2] + length)
    eb.roll = 0.0
    if parent:
        eb.parent = arm_data.edit_bones[parent]
        eb.use_connect = False
    return eb


make_bone('WorldRoot', (0, 0, 0), length=0.25)
make_bone('Flip', (0, 0, 1.86), 'WorldRoot')
make_bone('Pelvis', (0, 0, 1.40), 'Flip')
make_bone('Chest', (0, 0, 2.10), 'Pelvis')
make_bone('Head', (0, -0.12, 2.77), 'Chest')
make_bone('Jaw', (0, -0.54, 2.62), 'Head')
for side, sign in (('L', 1), ('R', -1)):
    make_bone('Eyelid.' + side, (sign * 0.50, -0.72, 3.21), 'Head')
    make_bone('Arm.' + side, (sign * 1.00, 0.01, 2.34), 'Chest')
    make_bone('Forearm.' + side, (sign * 1.30, -0.06, 1.82), 'Arm.' + side)
    make_bone('Hand.' + side, (sign * 1.29, -0.24, 1.29), 'Forearm.' + side)
    make_bone('Thigh.' + side, (sign * 0.52, 0.02, 1.28), 'Pelvis')
    make_bone('Shin.' + side, (sign * 0.67, -0.03, 0.76), 'Thigh.' + side)
    make_bone('Foot.' + side, (sign * 0.67, -0.17, 0.36), 'Shin.' + side)

bpy.ops.object.mode_set(mode='OBJECT')
rig.show_in_front = True
for b in rig.pose.bones:
    b.rotation_mode = 'XYZ'

# -----------------------------------------------------------------------------
# SILHOUETTE: stocky giant frog with very broad shoulders and friendly belly.
# -----------------------------------------------------------------------------
orb('Lower_torso', (0, 0.09, 1.61), (0.72, 0.51, 0.62), 'skin', 'Pelvis')
orb('Upper_torso', (0, 0.04, 2.19), (0.82, 0.54, 0.58), 'skin', 'Chest')
orb('Shoulder_back_mass', (0, 0.23, 2.22), (1.08, 0.46, 0.43), 'skin_shadow', 'Chest')
orb('Throat', (0, -0.18, 2.64), (0.52, 0.38, 0.25), 'skin_light', 'Chest')

# Plump golden-green belly, broad pale pectorals (friendly instead of scary).
orb('Belly_patch', (0, -0.428, 1.69), (0.63, 0.21, 0.53), 'belly', 'Pelvis')
orb('Lower_belly_glow', (0, -0.538, 1.48), (0.41, 0.115, 0.27),
    'belly_shadow', 'Pelvis')
orb('Belly_highlight', (0, -0.567, 1.79), (0.49, 0.085, 0.36), 'belly', 'Pelvis')
for sign in (-1, 1):
    orb('Pectoral', (sign * 0.355, -0.405, 2.245), (0.38, 0.235, 0.31),
        'skin_light', 'Chest', rot=(0, sign * DEG(9), 0))
    orb('Pec_lighter_sheen', (sign * 0.38, -0.589, 2.27), (0.26, 0.075, 0.21),
        'skin_light', 'Chest')
    orb('Waist_fold', (sign * 0.53, -0.357, 1.46), (0.18, 0.13, 0.09),
        'skin_shadow', 'Pelvis')

# A few subtle belly spots, placed on surface for extra amphibian character.
for n in range(13):
    x = rng.uniform(-0.42, 0.42)
    z = rng.uniform(1.45, 1.98)
    dx = x / 0.63
    dz = (z - 1.70) / 0.56
    q = max(0.0, 1 - dx * dx - dz * dz)
    y = -0.428 - 0.21 * math.sqrt(q) - 0.045
    r = rng.uniform(0.012, 0.031)
    orb('Belly_freckle', (x, y, z), (r, 0.008, r * 0.72),
        'belly_shadow', 'Pelvis', seg=12, rings=8)

# -----------------------------------------------------------------------------
# HEAD: wide forehead, huge sleepy/curious orange eyes, toad cheeks and a grin.
# Unlike the monster reference, the silhouette stays soft and inviting.
# -----------------------------------------------------------------------------
orb('Head_cranium', (0, -0.16, 2.95), (0.83, 0.55, 0.47), 'skin', 'Head')
orb('Wide_squishy_muzzle', (0, -0.50, 2.73), (0.76, 0.46, 0.27),
    'skin_light', 'Head')
orb('Lower_cheek_L', (0.60, -0.49, 2.64), (0.25, 0.29, 0.24), 'skin', 'Head')
orb('Lower_cheek_R', (-0.60, -0.49, 2.64), (0.25, 0.29, 0.24), 'skin', 'Head')

# Dark recessed smiling mouth; moving jaw lip makes talking readable.
orb('Open_mouth_dark', (0, -0.932, 2.582), (0.555, 0.105, 0.105),
    'mouth', 'Head')
orb('Tongue', (0, -1.038, 2.544), (0.225, 0.024, 0.042), 'tongue', 'Jaw')
orb('Chunky_lower_jaw', (0, -0.665, 2.447), (0.62, 0.28, 0.118),
    'skin', 'Jaw')
stroke('Smile_lower_line', [(x, -1.036 - 0.009 * (1 - abs(x) / 0.54),
                             2.552 + 0.058 * (x / 0.54) ** 2)
                            for x in [i * 0.054 for i in range(-10, 11)]],
       0.026, 'skin_dark', 'Jaw')
# Upturned smile ends.
for sign in (-1, 1):
    stroke('Smile_corner', [(sign * 0.49, -1.001, 2.61),
                            (sign * 0.56, -0.940, 2.64),
                            (sign * 0.59, -0.875, 2.68)],
           0.024, 'skin_dark', 'Head')
    # One tiny rounded fang per side nods to the reference without a horror look.
    orb('Little_tooth', (sign * 0.415, -1.050, 2.594),
        (0.038, 0.022, 0.051), 'tooth', 'Head', seg=12, rings=8)

# Two nostrils positioned on raised snout, towards the camera (-Y).
for sign in (-1, 1):
    orb('Nostril_pad', (sign * 0.245, -0.899, 2.763),
        (0.105, 0.036, 0.072), 'skin', 'Head')
    orb('Nostril', (sign * 0.245, -0.929, 2.765),
        (0.043, 0.017, 0.029), 'skin_dark', 'Head', rot=(0, 0, sign * DEG(17)),
        seg=16, rings=10)

for side, sign in (('L', 1), ('R', -1)):
    x = sign * 0.50
    orb('Eye_mound_' + side, (x, -0.426, 3.216),
        (0.340, 0.305, 0.332), 'skin', 'Head')
    orb('Eye_socket_' + side, (x, -0.652, 3.192),
        (0.266, 0.098, 0.244), 'skin_dark', 'Head')
    orb('Eye_white_' + side, (x, -0.712, 3.189),
        (0.221, 0.102, 0.207), 'eye_white', 'Head')
    # Looking slightly towards the person buying couches.
    orb('Iris_edge_' + side, (x - sign * 0.012, -0.805, 3.165),
        (0.154, 0.044, 0.155), 'iris_edge', 'Head')
    orb('Iris_' + side, (x - sign * 0.012, -0.842, 3.166),
        (0.125, 0.035, 0.128), 'iris', 'Head')
    orb('Pupil_' + side, (x - sign * 0.015, -0.872, 3.161),
        (0.069, 0.025, 0.097), 'pupil', 'Head')
    orb('Eye_glint_' + side, (x - sign * 0.057, -0.898, 3.215),
        (0.034, 0.016, 0.036), 'shine', 'Head', seg=12, rings=8)
    # Distinct heavy, smiling brow that can blink/squint by translating its bone.
    orb('Soft_upper_eyelid_' + side, (x, -0.715, 3.339),
        (0.254, 0.108, 0.083), 'skin', 'Eyelid.' + side)
    orb('Brow_crest_' + side, (x, -0.605, 3.429),
        (0.292, 0.151, 0.107), 'skin_light', 'Eyelid.' + side,
        rot=(0, sign * DEG(8), sign * DEG(8)))

# Cartoon wart freckles along cheeks/back, not high-frequency realistic noise.
if GENERATE_WARTS:
    for side, sign in (('L', 1), ('R', -1)):
        for i in range(4):
            x = sign * (0.64 + rng.uniform(-0.07, 0.09))
            y = -0.57 + rng.uniform(-0.13, 0.08)
            z = 2.76 + i * 0.055
            r = 0.034 + 0.008 * (i % 2)
            orb('Cheek_bump', (x, y, z), (r, r, r * 0.7),
                'skin_shadow' if i % 3 == 0 else 'skin', 'Head',
                seg=12, rings=8)

# -----------------------------------------------------------------------------
# MUSCULAR ARMS, FROG MITTENS AND FEET
# Each joint is articulated using an Armature bone, not Object transforms.
# -----------------------------------------------------------------------------
for side, sign in (('L', 1), ('R', -1)):
    upper = 'Arm.' + side
    lower = 'Forearm.' + side
    hand = 'Hand.' + side
    thigh = 'Thigh.' + side
    shin = 'Shin.' + side
    foot = 'Foot.' + side

    orb('Shoulder_' + side, (sign * 1.02, 0.02, 2.30),
        (0.455, 0.46, 0.46), 'skin', upper)
    orb('Shoulder_top_' + side, (sign * 1.02, -0.025, 2.52),
        (0.30, 0.31, 0.16), 'skin_light', upper)
    capsule('Bicep_' + side,
            (sign * 1.10, -0.005, 2.29), (sign * 1.32, -0.045, 1.82),
            (0.337, 0.326, 0.17), 'skin', upper)
    orb('Bicep_bulge_' + side, (sign * 1.255, -0.20, 2.08),
        (0.255, 0.24, 0.27), 'skin_light', upper)
    orb('Elbow_' + side, (sign * 1.30, -0.06, 1.805),
        (0.27, 0.285, 0.27), 'skin_shadow', lower)
    capsule('Forearm_muscle_' + side,
            (sign * 1.31, -0.07, 1.79), (sign * 1.30, -0.245, 1.31),
            (0.294, 0.315, 0.15), 'skin', lower)
    orb('Wrist_cuff_' + side, (sign * 1.29, -0.24, 1.33),
        (0.26, 0.29, 0.13), 'skin_shadow', lower)
    orb('Palm_' + side, (sign * 1.30, -0.29, 1.155),
        (0.247, 0.19, 0.205), 'skin_light', hand)
    for finger in range(3):
        xoff = (finger - 1) * 0.167
        orb('Finger_' + side, (sign * 1.30 + xoff, -0.36, 0.99),
            (0.098, 0.125, 0.154), 'skin', hand, seg=16, rings=12)
        orb('Finger_pad_' + side, (sign * 1.30 + xoff, -0.39, 0.895),
            (0.095, 0.093, 0.065), 'skin_light', hand, seg=14, rings=10)
    orb('Thumb_' + side, (sign * 1.53, -0.427, 1.16),
        (0.14, 0.12, 0.12), 'skin_light', hand)

    # Thick bent amphibian legs. Feet have three big floppy web-toes.
    orb('Hip_' + side, (sign * 0.50, 0.105, 1.29),
        (0.35, 0.42, 0.38), 'skin', thigh)
    capsule('Thigh_' + side, (sign * 0.55, 0.10, 1.21),
            (sign * 0.675, -0.02, 0.77), (0.30, 0.34, 0.18),
            'skin', thigh)
    orb('Knee_' + side, (sign * 0.67, -0.085, 0.78),
        (0.28, 0.31, 0.28), 'skin_light', shin)
    capsule('Calf_' + side, (sign * 0.68, -0.015, 0.75),
            (sign * 0.665, -0.16, 0.36), (0.244, 0.252, 0.13),
            'skin', shin)
    orb('Foot_base_' + side, (sign * 0.665, -0.33, 0.185),
        (0.355, 0.37, 0.17), 'skin_shadow', foot)
    for toe in (-1, 0, 1):
        tx = sign * 0.665 + toe * 0.235
        ty = -0.58 + abs(toe) * 0.055
        orb('Web_toe_' + side, (tx, ty, 0.13),
            (0.17, 0.29, 0.12), 'skin', foot,
            rot=(0, 0, -toe * DEG(13)), seg=18, rings=12)
        orb('Toe_tip_' + side, (tx, ty - 0.19, 0.123),
            (0.158, 0.115, 0.09), 'skin_light', foot, seg=16, rings=10)

    # A few raised bumps on the outside of each enormous shoulder.
    if GENERATE_WARTS:
        for i in range(WART_COUNT // 2):
            theta = rng.uniform(0.15, PI * 0.95)
            phi = rng.uniform(-PI, PI)
            x = sign * 1.025 + 0.437 * math.sin(theta) * math.cos(phi)
            y = 0.02 + 0.438 * math.sin(theta) * math.sin(phi)
            z = 2.30 + 0.445 * math.cos(theta)
            # Don't clutter the inside of the arm or front of face.
            if sign * (x - sign * 1.025) < 0.03 or z < 2.15:
                continue
            r = rng.uniform(0.033, 0.072)
            orb('Shoulder_wart_' + side, (x, y, z), (r, r * 0.8, r),
                'wart' if rng.random() > .65 else 'skin_shadow',
                upper, seg=12, rings=8)

# -----------------------------------------------------------------------------
# DELIVERY MANAGER UNIFORM
# Orange vest/cap are ties to the Couch Guys delivery UI (not a scary enemy).
# -----------------------------------------------------------------------------
if GENERATE_DELIVERY_VEST:
    for side, sign in (('L', 1), ('R', -1)):
        orb('HiVis_vest_side_' + side, (sign * 0.675, -0.315, 2.02),
            (0.176, 0.145, 0.47), 'vest', 'Chest',
            rot=(0, sign * DEG(5), sign * DEG(-12)))
        orb('Vest_collar_' + side, (sign * 0.47, -0.39, 2.54),
            (0.165, 0.095, 0.18), 'vest_shadow', 'Chest',
            rot=(0, 0, sign * DEG(28)))
        stroke('Vest_trim_' + side,
               [(sign * 0.58, -0.430, 2.47),
                (sign * 0.57, -0.458, 2.24),
                (sign * 0.64, -0.428, 1.94),
                (sign * 0.63, -0.385, 1.71)],
               0.025, 'vest_trim', 'Chest')
    # Decorative chest badge and miniature icon of a couch.
    rounded_box('Manager_badge', (-0.28, -0.644, 2.385),
                (0.325, 0.045, 0.146), 0.025, 'badge', 'Chest')
    text_mesh('Badge_word', 'BOSS', (-0.28, -0.681, 2.387),
              0.093, 'badge_text', 'Chest')
    # Functional-looking tool pouch, clipped at the side, not covering the belly.
    rounded_box('Belt_pouch', (0.61, -0.49, 1.425),
                (0.325, 0.145, 0.26), 0.065, 'vest', 'Pelvis',
                rot=(0, -DEG(9), DEG(8)))
    rounded_box('Pouch_flap', (0.61, -0.586, 1.484),
                (0.30, 0.06, 0.105), 0.024, 'vest_shadow', 'Pelvis')
    orb('Pouch_button', (0.61, -0.623, 1.47),
        (0.023, 0.014, 0.023), 'vest_trim', 'Pelvis', seg=12, rings=8)

if GENERATE_MANAGER_CAP:
    # Narrow cap sits ABOVE the eye crests instead of hiding facial expression.
    orb('Cap_crown', (0, -0.055, 3.575),
        (0.515, 0.385, 0.153), 'cap', 'Head')
    orb('Cap_top', (0, -0.05, 3.624),
        (0.38, 0.28, 0.11), 'cap_trim', 'Head')
    orb('Cap_brim', (0, -0.45, 3.535),
        (0.47, 0.275, 0.056), 'cap', 'Head')
    stroke('Cap_rim', [(-0.435, -0.54, 3.533), (-0.23, -0.69, 3.531),
                       (0, -0.73, 3.529), (0.23, -0.69, 3.531),
                       (0.435, -0.54, 3.533)], 0.018, 'cap_trim', 'Head')
    # One small couch pictogram on the crown.
    rounded_box('Cap_logo_back', (0, -0.418, 3.652),
                (0.22, 0.038, 0.078), 0.015, 'badge', 'Head')
    rounded_box('Cap_logo_seat', (0, -0.444, 3.626),
                (0.168, 0.040, 0.060), 0.012, 'vest_trim', 'Head')

# -----------------------------------------------------------------------------
# JOIN INTO ONE OPTIMISED SKINNED MESH
# Preserves vertex groups, materials and disconnected cartoon shape islands.
# -----------------------------------------------------------------------------
bpy.ops.object.select_all(action='DESELECT')
for obj in mesh_parts:
    obj.select_set(True)
bpy.context.view_layer.objects.active = mesh_parts[0]
bpy.ops.object.join()
character = bpy.context.object
character.name = 'CouchGuys_FrogManager_Mesh'
character.data.name = 'FrogManager_CombinedGeometry'

arm_mod = character.modifiers.new('Animated_Frog_Armature', 'ARMATURE')
arm_mod.object = rig
character.parent = rig
# Parent has identity transform; mesh vertex positions already in armature space.
character.matrix_parent_inverse.identity()

# No nonuniform scaling remains on any skinned mesh.
rig.display_type = 'WIRE'
# The armature object itself has no visible geometry; leave it render-enabled
# so its deformation is evaluated by render engines and FBX exporters.

# -----------------------------------------------------------------------------
# ACTIONS / KEYFRAMES
# Armatures in Blender and Unity can play these independently.
# Some motions are exaggerated intentionally to read at typical gameplay distance.
# -----------------------------------------------------------------------------
ANIMATION_NAMES = ('FrogManager_Idle', 'FrogManager_Talk', 'FrogManager_Backflip')
all_actions = {}


def reset_pose():
    for pb in rig.pose.bones:
        pb.rotation_mode = 'XYZ'
        pb.rotation_euler = (0, 0, 0)
        pb.location = (0, 0, 0)
        pb.scale = (1, 1, 1)


def start_action(name):
    reset_pose()
    if not rig.animation_data:
        rig.animation_data_create()
    rig.animation_data.action = None
    action = bpy.data.actions.new(name)
    rig.animation_data.action = action
    all_actions[name] = action
    return action


def k_rot(bone, frame, xyz_degrees):
    p = rig.pose.bones[bone]
    p.rotation_euler = tuple(DEG(v) for v in xyz_degrees)
    p.keyframe_insert(data_path='rotation_euler', frame=frame, group=bone)


def k_loc(bone, frame, xyz):
    p = rig.pose.bones[bone]
    p.location = xyz
    p.keyframe_insert(data_path='location', frame=frame, group=bone)


def pose_key(frame, rotations=None, locations=None):
    for bone, angles in (rotations or {}).items():
        k_rot(bone, frame, angles)
    for bone, xyz in (locations or {}).items():
        k_loc(bone, frame, xyz)


def start_rest_key(frame):
    for b in rig.pose.bones:
        k_rot(b.name, frame, (0, 0, 0))
        k_loc(b.name, frame, (0, 0, 0))


def set_linear(action, bone_names, data_path='rotation_euler'):
    """Maintain a full uninterrupted 360-degree turn during the backflip."""
    # Blender pre-4.4 exposes Action.fcurves; Blender 4.4+ may use layered
    # Actions, whose fcurves need to be found in channelbags.
    try:
        curves = list(action.fcurves)
    except Exception:
        curves = []
    if not curves:
        try:
            for layer in action.layers:
                for strip in layer.strips:
                    for channelbag in strip.channelbags:
                        curves.extend(channelbag.fcurves)
        except (AttributeError, TypeError):
            pass
    for curve in curves:
        if data_path in curve.data_path and any('pose.bones["%s"]' % n in curve.data_path
                                                 for n in bone_names):
            for kp in curve.keyframe_points:
                kp.interpolation = 'LINEAR'


# --- IDLE: body breathes, head looks around, hands relax, a couple of blinks.
start_action('FrogManager_Idle')
start_rest_key(1)
pose_key(12, {
    'Chest': (-2.5, 0, -2.5), 'Head': (2.0, 0, 4.0),
    'Arm.L': (-3, 0, -3), 'Arm.R': (2, 0, 2),
    'Forearm.L': (4, 0, 0), 'Forearm.R': (2, 0, 0),
}, {'WorldRoot': (0, 0, 0.024)})
pose_key(26, {
    'Chest': (2.0, 0, 1.5), 'Head': (-3, 0, -3),
    'Arm.L': (3, 0, 1), 'Arm.R': (-2, 0, -3),
}, {'WorldRoot': (0, 0, 0.052)})
pose_key(45, {'Chest': (-1, 0, 3.0), 'Head': (1.0, 0, 4.8)},
         {'WorldRoot': (0, 0, 0.014)})
pose_key(66, {
    'Chest': (2, 0, -2), 'Head': (0, 0, -5),
    'Forearm.L': (2, 0, 0), 'Forearm.R': (-4, 0, 0)
}, {'WorldRoot': (0, 0, 0.040)})
start_rest_key(90)
for side in ('L', 'R'):
    name = 'Eyelid.' + side
    for frame, value in ((1, 0), (30, 0), (33, -0.12), (36, 0),
                         (70, 0), (73, -0.13), (77, 0), (90, 0)):
        # Bone local Y points UP in the rig, so negative Y lowers the lid.
        k_loc(name, frame, (0, value, 0))

# --- TALK: arms alternately present and explain, mouth opens with syllables.
start_action('FrogManager_Talk')
start_rest_key(1)
# Gestures are asymmetrical, as if the frog is confidently explaining prices.
talk_poses = [
    (8,   4,  21, -12, -10,  18, -10,  13,  0.01),
    (17, -3,  38, -22, -22,  30, -22, -12,  0.04),
    (27,  3,  18,  -9, -40,  42,  -7,  12,  0.035),
    (37, -2,  -8,  15, -18,  28,  22,  -5,  0.02),
    (47,  3,  32, -15, -14,  28, -17,  13,  0.045),
    (57, -4,  12,   3, -35,  40,  10,  -8,  0.02),
    (67,  2,  27, -16, -18,  25, -15,  10,  0.035),
]
for frame, chest_x, left_arm, right_arm, left_forearm, right_forearm, left_hand, head_z, bob in talk_poses:
    pose_key(frame, {
        'Chest': (chest_x, 0, head_z * 0.22),
        'Head': (chest_x * -0.6, 0, head_z),
        'Arm.L': (-18, 0, -left_arm),
        'Arm.R': (-16, 0, -right_arm),
        'Forearm.L': (-22 + left_forearm, 0, 7),
        'Forearm.R': (-22 + right_forearm, 0, -7),
        'Hand.L': (left_hand, 0, 12),
        'Hand.R': (-left_hand * 0.6, 0, -14),
    }, {'WorldRoot': (0, 0, bob)})

# Mouth chattering in a visible rhythm. Jaw hinges in local X, opening by
# a modest amount; avoid opening into the neck in a game-sized NPC.
for frame, mouth_open in ((1, 0), (5, 14), (10, 5), (15, 21),
                          (20, 1), (25, 16), (30, 0), (36, 23),
                          (41, 6), (46, 18), (50, 0), (55, 21),
                          (60, 2), (65, 15), (70, 0), (75, 8), (80, 0)):
    k_rot('Jaw', frame, (mouth_open, 0, 0))
for side in ('L', 'R'):
    name = 'Eyelid.' + side
    for frame, value in ((1, 0), (46, 0), (49, -0.10),
                         (52, 0), (80, 0)):
        k_loc(name, frame, (0, value, 0))
start_rest_key(80)

# --- BACKFLIP: crouch, launch, full 360, land, celebrate and settle.
# WorldRoot lifts vertically; Flip is centred around chest so the spin stays
# clear of the ground. A FULL Euler 360 is keyed, NOT quaternion shortest arc.
backflip = [
    # frame,  height, flip degrees, knee bend, arms up, curl forearms
    (1,   0.00,    0,    0,    0,     0),
    (5,  -0.13,   -7,   18,  -24,   -25),
    (9,  -0.19,   -13,  27,  -35,   -30),
    (13,  0.24,   12,    4,   38,    20),
    (20,  1.10,   94,   48,   72,   -38),
    (28,  1.51,  195,   66,   89,   -57),
    (35,  1.38,  286,   48,   63,   -42),
    (44,  0.40,  348,   10,   10,    -5),
    (50, -0.09,  360,   22,  -25,   -20),
    (56,  0.08,  360,    0,  -78,   -30),
    (64,  0.00,  360,    0,  -62,   -15),
    (72,  0.00,  360,    0,    0,     0),
]
start_action('FrogManager_Backflip')
start_rest_key(1)
for frame, height, flip, knee, arm_raise, elbow in backflip:
    pose_key(frame, {
        'Flip': (flip, 0, 0),
        'Pelvis': (-min(knee * 0.10, 7), 0, 0),
        'Chest': (-9 if 20 <= frame <= 35 else 2 if frame == 56 else 0, 0, 0),
        'Head': (8 if 20 <= frame <= 35 else -5 if frame == 56 else 0, 0, 0),
        'Thigh.L': (-knee, 0, 6), 'Thigh.R': (-knee, 0, -6),
        'Shin.L': (knee * 0.60, 0, 0), 'Shin.R': (knee * 0.60, 0, 0),
        'Arm.L': (-30 if 20 <= frame <= 35 else -2, 0, -arm_raise),
        'Arm.R': (-30 if 20 <= frame <= 35 else -2, 0, arm_raise),
        'Forearm.L': (elbow, 0, 5), 'Forearm.R': (elbow, 0, -5),
        'Hand.L': (12 if frame == 56 else 0, 0, 0),
        'Hand.R': (12 if frame == 56 else 0, 0, 0),
    }, {'WorldRoot': (0, 0, height)})
set_linear(all_actions['FrogManager_Backflip'], ['Flip'])

# Give users a clean first frame and a default preview action.
rig.animation_data.action = all_actions['FrogManager_Idle']
bpy.context.scene.frame_set(1)

# -----------------------------------------------------------------------------
# ART-DIRECTION PREVIEW (EXCLUDED from FBX)
# -----------------------------------------------------------------------------
if CREATE_PREVIEW_SCENE:
    ground = mats['ground']
    bpy.ops.mesh.primitive_plane_add(size=200, location=(0, 0, -0.02))
    plane = bpy.context.object
    plane.name = 'PREVIEW_ONLY_Floor'
    plane.data.materials.append(ground)

    world = bpy.context.scene.world
    if world is None:
        world = bpy.data.worlds.new('Friendly_Studio_World')
        bpy.context.scene.world = world
    world.use_nodes = True
    background = world.node_tree.nodes.get('Background')
    if background:
        background.inputs['Color'].default_value = (*srgb_hex('BEDCE6'), 1)
        background.inputs['Strength'].default_value = 0.8

    def look_at(obj, target):
        direction = Vector(target) - obj.location
        obj.rotation_euler = direction.to_track_quat('-Z', 'Y').to_euler()

    def light(name, at, power, size, color):
        ld = bpy.data.lights.new(name, 'AREA')
        ld.energy = power
        ld.shape = 'DISK'
        ld.size = size
        ld.color = color
        ob = bpy.data.objects.new(name, ld)
        bpy.context.collection.objects.link(ob)
        ob.location = at
        look_at(ob, (0, 0, 1.85))

    light('PREVIEW_ONLY_Key', (4.0, -5.0, 7.0), 1300, 5.0, (1.0, 0.91, 0.78))
    light('PREVIEW_ONLY_Fill', (-4.0, -4.0, 4.0), 850, 4.3, (0.75, 0.89, 1.0))
    light('PREVIEW_ONLY_Rim', (0.5, 3.0, 5.0), 1050, 3.0, (1.0, 0.95, 0.82))

    camera_data = bpy.data.cameras.new('FrogManager_Studio_Camera')
    camera = bpy.data.objects.new('PREVIEW_ONLY_Camera', camera_data)
    bpy.context.collection.objects.link(camera)
    camera.location = (4.3, -8.8, 3.3)
    look_at(camera, (0, -0.2, 1.90))
    camera_data.type = 'ORTHO'
    camera_data.ortho_scale = 5.1
    bpy.context.scene.camera = camera
    bpy.context.scene.render.resolution_x = 1024
    bpy.context.scene.render.resolution_y = 1024
    bpy.context.scene.render.resolution_percentage = 100
    bpy.context.scene.render.image_settings.file_format = 'PNG'

    try:
        bpy.context.scene.render.engine = 'BLENDER_EEVEE_NEXT'
    except (TypeError, ValueError):
        try:
            bpy.context.scene.render.engine = 'BLENDER_EEVEE'
        except (TypeError, ValueError):
            pass

# Armature remains easy to select and animate; hide bone sticks outside Pose Mode.
bpy.ops.object.select_all(action='DESELECT')
rig.select_set(True)
bpy.context.view_layer.objects.active = rig

# -----------------------------------------------------------------------------
# OPTIONAL SAVE / FBX EXPORT
# Export only armature and final combined skinned mesh. The preview objects,
# camera, lights and ground cannot accidentally make it into Unity.
# -----------------------------------------------------------------------------
base_dir = bpy.path.abspath('//')
if not base_dir:
    base_dir = '.'

if SAVE_BLEND:
    import os
    output_blend = os.path.join(base_dir, BLEND_FILENAME)
    bpy.ops.wm.save_as_mainfile(filepath=output_blend)
    print('[FrogManager] Saved Blender file:', output_blend)

if EXPORT_FBX:
    import os
    export_path = os.path.join(base_dir, FBX_FILENAME)
    bpy.ops.object.select_all(action='DESELECT')
    rig.select_set(True)
    character.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.export_scene.fbx(
        filepath=export_path,
        use_selection=True,
        object_types={'ARMATURE', 'MESH'},
        axis_forward='-Z', axis_up='Y',
        apply_unit_scale=True,
        use_mesh_modifiers=True,
        add_leaf_bones=False,
        use_armature_deform_only=False,
        bake_anim=True,
        bake_anim_use_all_actions=True,
        bake_anim_use_nla_strips=False,
        bake_anim_step=1,
        bake_anim_simplify_factor=0.0,
        path_mode='AUTO',
    )
    bpy.ops.object.select_all(action='DESELECT')
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    print('[FrogManager] Exported Unity FBX:', export_path)

print('[FrogManager] DONE. Meshes: 1 | Armature bones:', len(rig.data.bones))
print('[FrogManager] Actions:', ', '.join(ANIMATION_NAMES))
print('[FrogManager] Select the rig > Dope Sheet > Action Editor to inspect clips.')
