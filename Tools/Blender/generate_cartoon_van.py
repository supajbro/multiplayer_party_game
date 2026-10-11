"""
Couch Guys - Procedural Cartoon Van Generator
===========================================
Creates a brand-new stylised delivery van on each run, inspired by the
soft-edged, colourful, low-poly proportions of the house generator.

BLENDER: Scripting > Open this .py > Run Script.
Blender CLI: blender --background --python generate_cartoon_van.py

CONFIGURATION: Change VAN_QUALITY to "LIGHT", "GOOD" or "BEST" below.
Set SEED to an integer to reproduce a specific variation; None is random.

Coordinates: 1 Blender unit = approximately 1 metre; van faces -Y,
Z is up, and root origin is at ground level near the vehicle centre.
Only an existing GeneratedVan hierarchy is replaced; houses remain intact.

Cargo: Functional rear double-door pivots + an ACTUAL open cargo entrance,
interior floor and Unity cargo/interaction markers. Door animation previews
in Blender at frames 1(closed), 24(open), 48(closed). Unity gameplay must
still implement holding E, loading the couch and vehicle storage behaviour.

NOTE: Speed and fuel figures are sample GAMEPLAY METADATA stored on the
root object. They do not implement driving physics in Unity by themselves.
"""

import bpy
import math
import random
import time


# ============================================================
# GENERATOR CONFIGURATION -- CHANGE THESE SETTINGS
# ============================================================
VAN_QUALITY = "GOOD"             # "LIGHT", "GOOD", or "BEST"
SEED = None                      # None = new random van on EVERY run

GENERATE_BRANDING = True         # Painted Couch Guys lettering on van side
GENERATE_INTERIOR = True         # Simple steering wheel, seats and dashboard
GENERATE_CARGO_INTERIOR = True   # Loading floor, inner walls and cargo rails
REAR_DOOR_OPEN_ANGLE = 110.0     # Swing angle in degrees (60-125 recommended)
REAR_DOOR_PREVIEW_OPEN = False   # True = show doors open after generation
ANIMATE_REAR_DOORS = True        # Keyframe closed -> open -> closed in Blender
GENERATE_WHEEL_DETAILS = True
GENERATE_ACCESSORIES = True
GENERATE_WEAR_ON_LIGHT_VAN = True

BEVEL_SEGMENTS = 2
EXPORT_FBX = False               # Enable to automatically export the generated van
EXPORT_FBX_PATH = "//couch_guys_van.fbx"  # Relative to saved .blend file

# Intentional arcade tuning suggestions. Unity scripts set actual handling.
VAN_TYPES = {
    "LIGHT": {
        "length": (3.85, 4.30), "width": (1.76, 1.90),
        "height": (1.90, 2.08), "wheel_radius": (0.36, 0.40),
        "max_speed_mps": 10.0, "fuel_capacity_l": 30.0,
        "fuel_use_l_per_km": 0.26, "recommended_delivery": "MEDIUM",
        "trim_level": 0,
    },
    "GOOD": {
        "length": (4.55, 5.00), "width": (1.94, 2.08),
        "height": (2.22, 2.43), "wheel_radius": (0.40, 0.44),
        "max_speed_mps": 13.5, "fuel_capacity_l": 45.0,
        "fuel_use_l_per_km": 0.20, "recommended_delivery": "MEDIUM_HARD",
        "trim_level": 1,
    },
    "BEST": {
        "length": (5.20, 5.70), "width": (2.08, 2.23),
        "height": (2.48, 2.70), "wheel_radius": (0.44, 0.48),
        "max_speed_mps": 17.0, "fuel_capacity_l": 60.0,
        "fuel_use_l_per_km": 0.14, "recommended_delivery": "HARD",
        "trim_level": 2,
    },
}

# Each entry changes both body colours and the paint treatment.
PALETTES = {
    "LIGHT": [
        ((0.76, 0.70, 0.59, 1), (0.46, 0.38, 0.30, 1)),
        ((0.69, 0.79, 0.68, 1), (0.35, 0.45, 0.34, 1)),
        ((0.79, 0.75, 0.62, 1), (0.48, 0.36, 0.28, 1)),
        ((0.75, 0.77, 0.81, 1), (0.42, 0.46, 0.52, 1)),
    ],
    "GOOD": [
        ((0.91, 0.81, 0.58, 1), (0.24, 0.48, 0.50, 1)),
        ((0.87, 0.91, 0.83, 1), (0.27, 0.56, 0.53, 1)),
        ((0.92, 0.85, 0.73, 1), (0.68, 0.36, 0.26, 1)),
        ((0.80, 0.87, 0.91, 1), (0.31, 0.47, 0.68, 1)),
    ],
    "BEST": [
        ((0.21, 0.43, 0.48, 1), (0.95, 0.75, 0.34, 1)),
        ((0.23, 0.30, 0.48, 1), (0.80, 0.83, 0.91, 1)),
        ((0.26, 0.49, 0.40, 1), (0.95, 0.79, 0.50, 1)),
        ((0.48, 0.30, 0.48, 1), (0.94, 0.72, 0.40, 1)),
    ],
}


# ============================================================
# BASIC BLENDER HELPERS
# ============================================================
def clear_previous_van():
    root = bpy.data.objects.get("GeneratedVan")
    if root is None:
        return

    def remove_recursive(obj):
        for child in list(obj.children):
            remove_recursive(child)
        bpy.data.objects.remove(obj, do_unlink=True)

    remove_recursive(root)


def empty(name, parent=None, location=(0, 0, 0)):
    obj = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(obj)
    obj.empty_display_type = 'PLAIN_AXES'
    obj.empty_display_size = 0.16
    obj.parent = parent
    obj.location = location
    return obj


def material(name, colour, roughness=0.76, metallic=0.0, emission=0.0):
    name = "CGVan_" + name
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.diffuse_color = colour
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get('Principled BSDF')
    if bsdf:
        bsdf.inputs['Base Color'].default_value = colour
        bsdf.inputs['Roughness'].default_value = roughness
        bsdf.inputs['Metallic'].default_value = metallic
        ec = bsdf.inputs.get('Emission Color')
        if ec is None:
            ec = bsdf.inputs.get('Emission')
        if ec is not None:
            ec.default_value = colour
        strength = bsdf.inputs.get('Emission Strength')
        if strength is not None:
            strength.default_value = emission
    return m


def tint(colour, rng, amount=0.027):
    return tuple(max(0, min(1, c + rng.uniform(-amount, amount)))
                 for c in colour[:3]) + (1.0,)


def shade(colour, n):
    return tuple(max(0, min(1, c * n)) for c in colour[:3]) + (1.0,)


def build_materials(quality, rng):
    body, accent = rng.choice(PALETTES[quality])
    body = tint(body, rng)
    accent = tint(accent, rng, .016)
    metallic = 0.19 if quality == 'BEST' else 0.015
    return {
        'body': material('Body', body, .53 if quality == 'BEST' else .76, metallic),
        'body_light': material('BodyHighlight', shade(body, 1.12)),
        'body_dark': material('BodyShadow', shade(body, .74)),
        'accent': material('Accent', accent, .43 if quality == 'BEST' else .7),
        'accent_dark': material('AccentDark', shade(accent, .62)),
        'glass': material('BlueTintGlass', (0.27, .47, .56, 1), .19, .05),
        'glass_deep': material('DarkTintGlass', (.12, .22, .31, 1), .16, .07),
        'glass_shine': material('GlassReflection', (.55, .72, .75, 1), .22),
        'rubber': material('Rubber', (.09, .10, .12, 1), .96),
        'tyre_tread': material('TyreTread', (.15, .16, .17, 1), .98),
        'black': material('CharcoalPlastic', (.14, .17, .19, 1)),
        'metal': material('WarmMetal', (.62, .66, .67, 1), .30, .75),
        'silver': material('BrightMetal', (.86, .87, .82, 1), .23, .8),
        'interior': material('Interior', (.28, .30, .29, 1)),
        'seat': material('Seats', (.43, .43, .39, 1)),
        'headlight': material('Headlights', (1.0, .89, .64, 1), .23, 0, 1.8),
        'taillight': material('BrakeLights', (.85, .17, .13, 1), .26, 0, .65),
        'amber': material('Indicators', (1.0, .59, .16, 1), .35, 0, .7),
        'white': material('WhitePaint', (.96, .93, .86, 1)),
        'rust': material('WornRust', (.52, .29, .17, 1), .96),
        'patch': material('ReplacementPanel', shade(body, .84)),
    }


def mesh_object(name, vertices, faces, mat, parent):
    mesh = bpy.data.meshes.new('CGVan_' + name)
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    mesh.materials.append(mat)
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    obj.parent = parent
    return obj


def box(name, pos, dims, mat, parent, bevel=.035, rotation=(0, 0, 0)):
    bpy.ops.mesh.primitive_cube_add(size=1, location=pos)
    obj = bpy.context.object
    obj.name = name
    obj.data.name = 'CGVan_' + name
    obj.dimensions = dims
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    obj.rotation_euler = rotation
    obj.data.materials.append(mat)
    obj.parent = parent
    if bevel > 0:
        mod = obj.modifiers.new('SoftCartoonEdges', 'BEVEL')
        mod.width = min(bevel, min(dims) * .22)
        mod.segments = BEVEL_SEGMENTS
        mod.limit_method = 'ANGLE'
        weighted = obj.modifiers.new('WeightedNormals', 'WEIGHTED_NORMAL')
        weighted.keep_sharp = True
    return obj


def plate(name, corners, outward, thickness, mat, parent):
    """Closed polygon plate, rendered on both sides and supported in Unity."""
    count = len(corners)
    dx, dy, dz = (outward[0] * thickness, outward[1] * thickness,
                  outward[2] * thickness)
    verts = [tuple(p) for p in corners] + [
        (p[0] + dx, p[1] + dy, p[2] + dz) for p in corners
    ]
    faces = [tuple(reversed(range(count))), tuple(range(count, count * 2))]
    faces += [(i, (i + 1) % count, (i + 1) % count + count, i + count)
              for i in range(count)]
    return mesh_object(name, verts, faces, mat, parent)


def cylinder(name, pos, radius, depth, mat, parent, vertices=16, axis_x=False):
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius,
                                        depth=depth, location=pos)
    obj = bpy.context.object
    obj.name = name
    obj.data.name = 'CGVan_' + name
    obj.parent = parent
    obj.data.materials.append(mat)
    if axis_x:
        obj.rotation_euler[1] = math.pi / 2
    return obj


def brand_text(name, txt, pos, mat, parent, size=.26, on_side=True):
    curve = bpy.data.curves.new('CGVan_TextCurve', 'FONT')
    curve.body = txt
    curve.size = size
    curve.extrude = .002
    curve.bevel_depth = .001
    obj = bpy.data.objects.new(name, curve)
    bpy.context.collection.objects.link(obj)
    obj.location = pos
    obj.rotation_euler = ((math.pi / 2, 0, math.pi / 2)
                          if on_side else (math.pi / 2, 0, 0))
    obj.data.materials.append(mat)
    # Convert lettering into real geometry for FBX/Unity compatibility.
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.convert(target='MESH')
    obj = bpy.context.view_layer.objects.active
    obj.parent = parent
    return obj


# ============================================================
# RANDOMIZED BODY LAYOUT
# ============================================================
def create_layout(quality, rng):
    cfg = VAN_TYPES[quality]
    length = rng.uniform(*cfg['length'])
    width = rng.uniform(*cfg['width'])
    height = rng.uniform(*cfg['height'])
    radius = rng.uniform(*cfg['wheel_radius'])
    return {
        'quality': quality,
        'length': length, 'width': width, 'height': height,
        'radius': radius,
        'front': -length / 2, 'rear': length / 2,
        'front_axle': -length / 2 + rng.uniform(.84, .94),
        'rear_axle': length / 2 - rng.uniform(.70, .82),
        'driver_side': rng.choice((-1, 1)),
        'stripe_style': rng.choice(('SLANT', 'DOUBLE', 'BAND')),
        'accessory_style': rng.choice(('RAILS', 'RACK', 'PLAIN')),
        'windows_at_rear': rng.random() < (.35 if quality == 'LIGHT' else .20),
    }


def build_body(layout, mat, groups):
    w = layout['width']
    h = layout['height']
    yf = layout['front']
    yr = layout['rear']
    # One continuous, closed, broad silhouette: front hood -> angled
    # windshield -> raised cabin/cargo roof -> squared rear doors.
    outline = [
        (yf + .07, .52),
        (yf + .07, 1.15),
        (yf + .72, 1.30),
        (yf + 1.36, h - .16),
        (yf + 1.52, h),
        (yr - .20, h),
        (yr, h - .18),
        (yr, .52),
    ]
    n = len(outline)
    vertices = [(-w / 2, y, z) for y, z in outline]
    vertices += [(w / 2, y, z) for y, z in outline]
    # Outline is clockwise in the Y/Z plane, so -X uses that
    # winding and +X is reversed. This makes surfaces face OUTWARD
    # when Unity/FBX performs backface culling.
    faces = [tuple(range(n)), tuple(reversed(range(n, n * 2)))]
    # Rear-facing last upright outline segment is the CARGO ENTRANCE.
    # Deliberately DO NOT generate the cross-body face here. Without
    # this exclusion the rear doors would open onto a solid van wall!
    faces += [(i + n, (i + 1) % n + n, (i + 1) % n, i)
              for i in range(n) if i != n - 2]
    body = mesh_object('MainVanBody', vertices, faces, mat['body'], groups['Body'])
    bevel = body.modifiers.new('GentlyRoundedVanShell', 'BEVEL')
    bevel.width = .09 if layout['quality'] != 'LIGHT' else .055
    bevel.segments = BEVEL_SEGMENTS
    bevel.limit_method = 'ANGLE'
    weighted = body.modifiers.new('BodyWeightedNormals', 'WEIGHTED_NORMAL')
    weighted.keep_sharp = True
    box('UnderbodyAndChassis', (0, .02, .55), (w * .84, layout['length'] * .91, .23),
        mat['black'], groups['Body'], .065)
    # Curved-looking edges are achieved with chunky rounded trim and no
    # textures, preserving the stylised Couch Guys aesthetic.
    box('RoofInset', (0, .29, h - .014),
        (w * .88, layout['length'] * .55, .075),
        mat['body_light'], groups['Body'], .038)
    for side in (-1, 1):
        box('LowerSill_' + str(side),
            (side * (w / 2 + .025), .25, .69),
            (.095, layout['length'] * .69, .13),
            mat['body_dark'], groups['Trim'], .022)
    # The hood is a raised region and trim, not a separate disconnected cube.
    box('HoodRaisedCentre', (0, yf + .35, 1.265),
        (w * .70, .56, .038), mat['body_light'], groups['Body'], .015)


def build_glass(layout, mat, groups, rng):
    yf = layout['front']
    w = layout['width']
    h = layout['height']
    # Put windshield directly over the roof/hood sloping surface.
    def slope_y(z):
        return yf + .72 + (z - 1.30) / (h - .16 - 1.30) * .64 - .018

    lower = 1.44
    upper = h - .30
    bx = w * .405
    plate('FrontWindshield', [
        (-bx, slope_y(lower), lower), (bx, slope_y(lower), lower),
        (bx, slope_y(upper), upper), (-bx, slope_y(upper), upper),
    ], (0, -1, 0), .025,
        mat['glass_deep'] if layout['quality'] == 'BEST' else mat['glass'],
        groups['Windows'])
    # Single diagonal sheen is simple geometry, no expensive transparent shader.
    plate('WindshieldReflection', [
        (-bx * .74, slope_y(upper - .02) - .05, upper - .02),
        (-bx * .43, slope_y(upper - .02) - .05, upper - .02),
        (-bx * .61, slope_y(lower + .11) - .05, lower + .11),
        (-bx * .83, slope_y(lower + .11) - .05, lower + .11),
    ], (0, -1, 0), .008, mat['glass_shine'], groups['Windows'])

    for side in (-1, 1):
        sx = side * (w / 2 + .015)
        # Front side glass: polygon follows the windscreen slope.
        plate('CabSideWindow_' + str(side), [
            (sx, yf + .91, 1.48),
            (sx, yf + 1.28, h - .30),
            (sx, yf + 1.95, h - .30),
            (sx, yf + 1.95, 1.48),
        ], (side, 0, 0), .020, mat['glass_deep'], groups['Windows'])
        box('WindowLowerChrome_' + str(side),
            (side * (w / 2 + .047), yf + 1.43, 1.45),
            (.032, 1.07, .055), mat['metal'] if layout['quality'] == 'BEST'
            else mat['black'], groups['Windows'], .008)
        box('CabDoorPillar_' + str(side),
            (side * (w / 2 + .036), yf + 1.975, (h + 1.48) / 2),
            (.055, .058, h - 1.47), mat['body_dark'], groups['Doors'], .011)
        # Rear cargo side is intentionally solid, not an all-window minibus.
        if layout['windows_at_rear'] and layout['quality'] == 'LIGHT':
            side_y = yf + 2.52
            plate('SmallCargoWindow_' + str(side), [
                (sx, side_y, h - .80), (sx, side_y + .62, h - .80),
                (sx, side_y + .62, h - .31), (sx, side_y, h - .31),
            ], (side, 0, 0), .02, mat['glass_deep'], groups['Windows'])
    # Pair of windscreen wipers (geometry, not rigged).
    for side in (-1, 1):
        box('WindscreenWiper_' + str(side),
            (side * bx * .44, slope_y(lower) - .07, lower + .042),
            (bx * .70, .038, .028), mat['black'], groups['Windows'], .008,
            rotation=(.0, .0, side * .12))


def build_wheel(name, side, y, layout, mat, groups, rng):
    w = layout['width']
    radius = layout['radius']
    mount = empty(name, groups['Wheels'],
                  (side * (w / 2 + .05), y, radius))
    cylinder('Tyre_' + name, (0, 0, 0), radius, .21,
             mat['rubber'], mount, 20, axis_x=True)
    # Raised outside tyre wall and recessed wheel centre.
    cylinder('OuterTyreBead_' + name, (side * .105, 0, 0), radius * .86,
             .025, mat['tyre_tread'], mount, 20, axis_x=True)
    rim_colour = (mat['black'] if layout['quality'] == 'LIGHT'
                  else mat['silver'] if layout['quality'] == 'BEST' else mat['metal'])
    cylinder('Rim_' + name, (side * .126, 0, 0), radius * .66,
             .032, rim_colour, mount, 16, axis_x=True)
    cylinder('Hub_' + name, (side * .148, 0, 0), radius * .21,
             .045, mat['black'] if layout['quality'] == 'LIGHT'
             else mat['accent'], mount, 12, axis_x=True)
    if GENERATE_WHEEL_DETAILS:
        count = 5 if layout['quality'] == 'LIGHT' else 6 if layout['quality'] == 'GOOD' else 8
        for i in range(count):
            a = math.tau * i / count
            rr = radius * .40
            box('WheelSpoke_' + name + '_' + str(i),
                (side * .148, -math.sin(a) * rr, math.cos(a) * rr),
                (.03, radius * .12, radius * .28),
                mat['black'] if layout['quality'] == 'LIGHT' else mat['accent_dark'],
                mount, .006, rotation=(a, 0, 0))
    return mount


def build_wheels(layout, mat, groups, rng):
    for side, suffix in ((-1, 'L'), (1, 'R')):
        for y, suffix2 in ((layout['front_axle'], 'F'),
                           (layout['rear_axle'], 'R')):
            build_wheel('Wheel_' + suffix2 + suffix, side, y,
                        layout, mat, groups, rng)
    # wheel wells are visible dark semicircle-shaped stylisations from the side
    for side in (-1, 1):
        for y, lab in ((layout['front_axle'], 'Front'),
                       (layout['rear_axle'], 'Rear')):
            cylinder('WheelArch_' + lab + '_' + str(side),
                     (side * (layout['width'] / 2 + .012), y, layout['radius']),
                     layout['radius'] * 1.095, .027, mat['black'],
                     groups['Trim'], 20, axis_x=True)
    # Wheel arches are behind tyres in X so rims remain clearly visible.


def build_front_rear(layout, mat, groups, rng):
    w = layout['width']
    yf = layout['front']
    yr = layout['rear']
    h = layout['height']
    quality = layout['quality']

    # Low, broad bumpers provide a visual read of the direction of travel.
    for label, y in (('Front', yf + .005), ('Rear', yr + .075)):
        box(label + 'Bumper', (0, y, .64), (w * .98, .18, .23),
            mat['black'] if quality == 'LIGHT' else mat['accent_dark'],
            groups['Trim'], .046)
        if quality == 'BEST':
            box(label + 'PremiumBumperLip', (0, y - .009, .54),
                (w * .77, .22, .055), mat['metal'], groups['Trim'], .018)

    # Front fascia, grille slats and stylised large headlights.
    box('FrontGrille', (0, yf + .025, 1.025),
        (w * (.46 if quality == 'LIGHT' else .55), .095, .29),
        mat['black'], groups['Trim'], .044)
    for i in range(3 if quality == 'LIGHT' else 4 if quality == 'GOOD' else 5):
        z = .92 + i * .058
        box('FrontGrilleSlat_' + str(i),
            (0, yf - .034, z), (w * .39, .035, .020),
            mat['metal'] if quality == 'BEST' else mat['body_dark'],
            groups['Trim'], .006)
    for side in (-1, 1):
        x = side * w * .36
        box('HeadlampHousing_' + str(side),
            (x, yf + .015, 1.17), (w * .19, .10, .18),
            mat['black'], groups['Lights'], .044)
        box('HeadlampGlow_' + str(side),
            (x, yf - .042, 1.18), (w * .145, .025, .123),
            mat['headlight'], groups['Lights'], .031)
        if quality != 'LIGHT':
            box('FrontTurnSignal_' + str(side),
                (side * w * .454, yf - .016, 1.13),
                (.105, .047, .11), mat['amber'], groups['Lights'], .025)
        # Tall wraparound rear taillights, more elaborate by quality.
        box('RearLightBacking_' + str(side),
            (side * w * .445, yr + .042, 1.12),
            (.17, .09, .59 if quality == 'BEST' else .39),
            mat['black'], groups['Lights'], .036)
        box('RearBrakeLight_' + str(side),
            (side * w * .445, yr + .095, 1.15),
            (.117, .027, .50 if quality == 'BEST' else .31),
            mat['taillight'], groups['Lights'], .024)
        if quality == 'BEST':
            box('RearLEDWhiteStrip_' + str(side),
                (side * w * .445, yr + .118, 1.13),
                (.125, .018, .055), mat['headlight'], groups['Lights'], .009)

    # Rear leaf geometry is created later under hinged transform pivots
    # (build_rear_cargo). Keeping it out of the fixed rear fascia means
    # the door panels AND their handles move together when opened.
    # High-mounted third stop lamp on upper tiers.
    if quality != 'LIGHT':
        box('UpperBrakeLight', (0, yr + .07, h - .23),
            (.45, .055, .068), mat['taillight'], groups['Lights'], .012)



# ============================================================
# OPENABLE REAR DOORS + REAL COUCH CARGO BAY
# ============================================================
def build_rear_cargo(layout, mat, groups, rng):
    """Build an unsealed load bay with genuinely pivoting rear double doors.

    For Unity, rotate RearDoorHinge_L and RearDoorHinge_R around their LOCAL
    Z axis (with Blender -Y as the forward direction). Both doors' meshes,
    windows, handles and details are children of the correct hinge empty.

    Positive Z opens left door, negative Z opens right door. The geometry is
    CLOSED by default but can be previewed at the animated open frame.
    """
    w = layout['width']
    yr = layout['rear']
    yf = layout['front']
    h = layout['height']
    q = layout['quality']
    doors = groups['Doors']
    cargo = groups['Cargo']

    # Rear body was left open by build_body(). Define fixed threshold, upper
    # header and side pillars. They stay attached to the vehicle, not doors.
    sill_z = .68
    roof_z = h - .22
    entrance_h = roof_z - sill_z
    jamb_x = w / 2 - .07
    box('CargoRearLowerThreshold', (0, yr - .04, .57),
        (w * .88, .26, .13), mat['metal'] if q != 'LIGHT' else mat['black'],
        cargo, .018)
    box('CargoRearUpperHeader', (0, yr - .08, h - .16),
        (w * .92, .20, .12), mat['body_dark'], cargo, .023)
    for side, name in ((-1, 'L'), (1, 'R')):
        box('CargoDoorJamb_' + name, (side * jamb_x, yr - .07, (roof_z + sill_z) / 2),
            (.12, .18, entrance_h + .09), mat['body_dark'], cargo, .018)
        box('CargoDoorRubberSeal_' + name,
            (side * (jamb_x - .06), yr + .024, (roof_z + sill_z) / 2),
            (.028, .035, entrance_h), mat['rubber'], cargo, .008)

    # Actual cargo interior, leaving space behind front seats. The inner
    # lining is separate from the exterior shell. No false solid rear panel.
    cargo_front_y = yf + 2.05
    cargo_rear_y = yr - .11
    cargo_length = max(.65, cargo_rear_y - cargo_front_y)
    cargo_center_y = (cargo_front_y + cargo_rear_y) / 2
    cargo_floor_z = .69
    cargo_top_z = h - .18
    if GENERATE_CARGO_INTERIOR:
        box('CargoFloor', (0, cargo_center_y, cargo_floor_z - .035),
            (w - .22, cargo_length, .07), mat['interior'], cargo, .014)
        box('CargoFrontBulkhead', (0, cargo_front_y, (cargo_top_z + cargo_floor_z) / 2),
            (w - .17, .065, cargo_top_z - cargo_floor_z),
            mat['seat'], cargo, .018)
        for side, name in ((-1, 'L'), (1, 'R')):
            x = side * (w / 2 - .067)
            box('CargoInnerWall_' + name,
                (x, cargo_center_y, (cargo_top_z + cargo_floor_z) / 2),
                (.042, cargo_length, cargo_top_z - cargo_floor_z),
                mat['interior'], cargo, .010)
            box('CargoLowerTieDownRail_' + name,
                (x - side * .055, cargo_center_y, cargo_floor_z + .24),
                (.052, cargo_length * .74, .075),
                mat['metal'] if q == 'BEST' else mat['black'], cargo, .010)
        if q != 'LIGHT':
            # Two chunky, stylised securing rails which don't block the couch.
            for i in range(2):
                y = cargo_front_y + cargo_length * (.28 + .40 * i)
                box('CargoFloorTiedown_' + str(i), (0, y, cargo_floor_z + .008),
                    (w * .63, .055, .018), mat['metal'], cargo, .006)

    # Anchors are ordinary empties, not visible geometry or colliders.
    markers = groups['Anchors']
    empty('CargoLoadingAnchor', markers, (0, yr + .62, cargo_floor_z + .34))
    empty('CargoStorageAnchor', markers,
          (0, cargo_center_y, cargo_floor_z + .45))
    empty('CargoBoundsMin', markers,
          (-w / 2 + .15, cargo_front_y + .09, cargo_floor_z))
    empty('CargoBoundsMax', markers,
          (w / 2 - .15, cargo_rear_y - .06, cargo_top_z - .10))
    empty('RearDoorInteractionAnchor', markers, (0, yr + .64, 1.13))

    # Door leaves pivot at the OUTER rear edges, swing outward to the sides.
    # Materials and all adornments live under the hinge, not under Doors.
    door_width = jamb_x - .055
    leaf_h = entrance_h - .020
    leaf_centre_z = (roof_z + sill_z) / 2
    hinge_y = yr + .075
    pivots = []
    for side, name in ((-1, 'L'), (1, 'R')):
        hinge_x = side * jamb_x
        pivot = empty('RearDoorHinge_' + name, doors,
                      (hinge_x, hinge_y, 0))
        pivot['door_side'] = name
        pivot['open_degrees_z'] = -side * max(0.0, min(135.0, REAR_DOOR_OPEN_ANGLE))
        pivot['closed_degrees_z'] = 0.0
        pivot['preview_open'] = bool(REAR_DOOR_PREVIEW_OPEN)
        pivots.append((pivot, side))

        # Geometry is authored in HINGE-LOCAL space, so changing pivot Z
        # rotation truly swings it open instead of rotating around van centre.
        cx = -side * door_width * .5
        box('RearDoorLeaf_' + name, (cx, .018, leaf_centre_z),
            (door_width, .085, leaf_h), mat['body'], pivot, .035)
        box('RearDoorInsetPanel_' + name, (cx, .071, leaf_centre_z),
            (door_width * .83, .023, leaf_h * .82),
            mat['body_light'] if q != 'LIGHT' else mat['patch'], pivot, .015)
        # Coloured lower accent differentiates qualities at the back too.
        if q != 'LIGHT':
            box('RearDoorAccent_' + name, (cx, .098, sill_z + .21),
                (door_width * .80, .023, .13 if q == 'GOOD' else .19),
                mat['accent'], pivot, .012)
        box('RearDoorInnerLip_' + name, (cx, -.039, leaf_centre_z),
            (door_width * .94, .035, leaf_h * .90),
            mat['body_dark'], pivot, .015)
        box('RearDoorOuterEdge_' + name,
            (-side * (door_width - .022), .078, leaf_centre_z),
            (.033, .046, leaf_h * .97), mat['body_dark'], pivot, .006)
        box('RearDoorHandle_' + name,
            (-side * (door_width - .16), .134, 1.18),
            (.13, .067, .074), mat['metal'] if q == 'BEST' else mat['black'],
            pivot, .017)
        box('RearDoorReflector_' + name,
            (cx, .104, sill_z + .14),
            (door_width * .27, .026, .06), mat['taillight'], pivot, .011)
        if layout['windows_at_rear']:
            box('RearDoorWindow_' + name,
                (cx, .097, roof_z - .37),
                (door_width * .69, .032, .42),
                mat['glass_deep'], pivot, .022)
            box('RearDoorWindowTrim_' + name,
                (cx, .118, roof_z - .59),
                (door_width * .73, .027, .03), mat['black'], pivot, .007)
        if q == 'BEST':
            box('RearDoorPremiumTrim_' + name,
                (cx, .104, roof_z - .12),
                (door_width * .78, .022, .039), mat['silver'], pivot, .008)
        elif q == 'LIGHT' and GENERATE_WEAR_ON_LIGHT_VAN and rng.random() < .6:
            box('RearDoorPaintChip_' + name,
                (cx - side * .10, .106, 1.30),
                (.19, .022, .052), mat['rust'], pivot, .007)

        # Stationary, visible hinge knuckles; their rotations are separate
        # from the moving door leaves, like on a real delivery van.
        for i, z in enumerate((sill_z + .16, roof_z - .20)):
            cylinder('RearHingePin_' + name + '_' + str(i),
                     (hinge_x, hinge_y - .008, z),
                     .059, .14, mat['metal'] if q != 'LIGHT'
                     else mat['black'], doors, vertices=10)

    # Number plate belongs to the LEFT leaf so it swings with the door.
    left_pivot = pivots[0][0]
    plate_x = door_width * .54
    box('LicensePlateRear', (plate_x, .129, .93),
        (.31, .026, .13), mat['white'], left_pivot, .015)

    angle_rad = math.radians(max(0.0, min(135.0, REAR_DOOR_OPEN_ANGLE)))
    if ANIMATE_REAR_DOORS:
        for pivot, side in pivots:
            for frame, angle in ((1, 0), (24, -side * angle_rad), (48, 0)):
                pivot.rotation_euler.z = angle
                pivot.keyframe_insert(data_path='rotation_euler', index=2,
                                      frame=frame, group='RearCargoDoors')
            # Linear, predictable open/close; Unity may choose to drive the
            # pivots directly rather than play the baked Blender animation.
            if pivot.animation_data and pivot.animation_data.action:
                action = pivot.animation_data.action
                if hasattr(action, 'fcurves'):
                    for fc in action.fcurves:
                        for kp in fc.keyframe_points:
                            kp.interpolation = 'LINEAR'
        bpy.context.scene.frame_set(24 if REAR_DOOR_PREVIEW_OPEN else 1)
    elif REAR_DOOR_PREVIEW_OPEN:
        for pivot, side in pivots:
            pivot.rotation_euler.z = -side * angle_rad

    layout['cargo_length'] = cargo_length
    layout['cargo_width'] = w - .30
    layout['cargo_height'] = cargo_top_z - cargo_floor_z
    layout['rear_door_open_angle'] = math.degrees(angle_rad)


def build_side_doors(layout, mat, groups, rng):
    w = layout['width']
    yf = layout['front']
    yr = layout['rear']
    h = layout['height']
    quality = layout['quality']
    for side in (-1, 1):
        x = side * (w / 2 + .034)
        # Front cab handle; proper front door seam for later gameplay rigging.
        box('CabDoorHandle_' + str(side), (x + side * .016, yf + 1.67, 1.34),
            (.048, .19, .055), mat['black'] if quality == 'LIGHT' else mat['metal'],
            groups['Doors'], .015)
        box('CabDoorLowerSeam_' + str(side),
            (x, yf + 1.99, 1.02), (.035, .028, .68),
            mat['body_dark'], groups['Doors'], .007)
        # Sliding cargo door looks like a separate piece, with a lower rail.
        cy = min(yf + 3.26, yr - .31)
        for dy, name in ((yf + 2.13, 'Start'), (cy, 'End')):
            box('SliderDoorEdge_' + name + '_' + str(side),
                (x, dy, (h + .84) / 2),
                (.032, .023, h - .95), mat['body_dark'], groups['Doors'], .006)
        box('CargoDoorRail_' + str(side),
            (x, (yf + 2.14 + cy) / 2, 1.02),
            (.038, max(.30, cy - (yf + 2.14)), .024),
            mat['body_dark'], groups['Doors'], .006)
        box('CargoDoorHandle_' + str(side),
            (x + side * .013, yf + 2.34, 1.44),
            (.052, .24, .049), mat['black'] if quality == 'LIGHT' else mat['metal'],
            groups['Doors'], .011)
        # Oversize mirrors and stalks: intentionally cute chunky silhouettes.
        mirror_y = yf + 1.07
        box('MirrorArm_' + str(side),
            (side * (w / 2 + .17), mirror_y, 1.65),
            (.28, .075, .075), mat['black'], groups['Accessories'], .015)
        box('SideMirror_' + str(side),
            (side * (w / 2 + .31), mirror_y, 1.70),
            (.17, .25, .25), mat['body_dark'] if quality == 'LIGHT'
            else mat['black'], groups['Accessories'], .045)
        box('SideMirrorGlass_' + str(side),
            (side * (w / 2 + .403), mirror_y, 1.70),
            (.018, .17, .16), mat['glass_shine'], groups['Accessories'], .018)


def build_decoration(layout, mat, groups, rng):
    q = layout['quality']
    w = layout['width']
    h = layout['height']
    yf = layout['front']
    yr = layout['rear']
    length = layout['length']
    for side in (-1, 1):
        x = side * (w / 2 + .047)
        rearward_start = yf + 2.20
        usable = max(.20, yr - .27 - rearward_start)
        if q == 'LIGHT':
            # Old fleet van: off-centre faded stripe and visibly patchy paint.
            z = 1.06 + rng.uniform(-.09, .10)
            box('FadedLowerStripe_' + str(side),
                (x, rearward_start + usable * .52, z),
                (.025, usable * .87, .092),
                mat['accent_dark'], groups['Trim'], .009)
            box('MismatchedRepairPanel_' + str(side),
                (x + side * .015, rearward_start + usable * .64, 1.51),
                (.027, usable * .32, .32), mat['patch'], groups['Damage'], .012)
            if GENERATE_WEAR_ON_LIGHT_VAN:
                for i in range(rng.randint(3, 6)):
                    yy = rng.uniform(rearward_start + .10, yr - .25)
                    zz = rng.uniform(.87, min(h - .30, 1.73))
                    box('PaintScratch_' + str(side) + '_' + str(i),
                        (x + side * .035, yy, zz),
                        (.018, rng.uniform(.05, .19), .022),
                        mat['rust'] if i % 3 == 0 else mat['body_dark'],
                        groups['Damage'], .003,
                        rotation=(side * rng.uniform(-.16, .16), 0, 0))
        elif q == 'GOOD':
            box('TwoToneLowerStripe_' + str(side),
                (x, rearward_start + usable * .50, .93),
                (.038, usable * .94, .29), mat['accent'], groups['Trim'], .014)
            box('Pinstripe_' + str(side),
                (x + side * .018, rearward_start + usable * .50, 1.12),
                (.019, usable * .84, .043), mat['white'], groups['Trim'], .008)
        else:
            box('PremiumMidStripe_' + str(side),
                (x, rearward_start + usable * .50, 1.13),
                (.038, usable * .95, .19), mat['accent'], groups['Trim'], .011)
            box('PremiumAccentPinstripe_' + str(side),
                (x + side * .020, rearward_start + usable * .50, 1.30),
                (.019, usable * .90, .044), mat['silver'], groups['Trim'], .009)
            box('PremiumBottomSkirt_' + str(side),
                (side * (w / 2 + .064), rearward_start + usable * .44, .78),
                (.056, usable * .88, .095), mat['black'], groups['Trim'], .014)

    # Branding is deliberately prominent enough to read during gameplay.
    if GENERATE_BRANDING:
        label = 'COUCH GUYS' if q != 'LIGHT' else 'CG MOVERS'
        side_location = (w / 2 + .079, yf + length * .49,
                         min(h - .58, 1.69))
        brand_text('FleetLogo_' + q, label, side_location,
                   mat['white'] if q == 'BEST' else mat['accent_dark'],
                   groups['Branding'], size=.245 if q == 'LIGHT' else .28)
        if q == 'BEST':
            brand_text('FleetPremiumSubline', 'EXPRESS',
                       (w / 2 + .081, yf + length * .54, 1.42),
                       mat['accent'], groups['Branding'], size=.15)


def build_accessories(layout, mat, groups, rng):
    if not GENERATE_ACCESSORIES:
        return
    q = layout['quality']
    w = layout['width']
    h = layout['height']
    yr = layout['rear']
    yf = layout['front']
    # Light: a cheap, asymmetrical roof rack; good: roof crossbars;
    # best: rounded roof pod, rails and aerodynamic spoiler.
    if q == 'LIGHT':
        if rng.random() < .70:
            for i, yy in enumerate((yf + 2.10, yr - .70)):
                box('OldRackCrossbar_' + str(i),
                    (0, yy, h + .15),
                    (w * .82, .075, .09), mat['black'], groups['Accessories'], .018)
            for side in (-1, 1):
                box('OldRoofRackRail_' + str(side),
                    (side * w * .36, .32, h + .19),
                    (.060, layout['length'] * .43, .08),
                    mat['metal'], groups['Accessories'], .014)
        # Imperfect cheap external cargo sign, like a used local delivery van.
        if rng.random() < .50:
            box('OldRoofSign', (0, .38, h + .25),
                (w * .48, .30, .20), mat['patch'], groups['Accessories'], .043)
    elif q == 'GOOD':
        for side in (-1, 1):
            box('RoofRail_' + str(side),
                (side * w * .37, .37, h + .15),
                (.072, layout['length'] * .45, .13),
                mat['black'], groups['Accessories'], .033)
        if rng.random() < .60:
            for i in range(2):
                box('RoofCrossbar_' + str(i),
                    (0, -.02 + i * .88, h + .23),
                    (w * .72, .071, .075),
                    mat['metal'], groups['Accessories'], .018)
    else:
        # Top-of-range tall roof + spoiler + wider, sleek accessories.
        box('AerodynamicRoofPod',
            (0, yf + layout['length'] * .58, h + .12),
            (w * .68, layout['length'] * .33, .19),
            mat['body_light'], groups['Accessories'], .095)
        box('RearRoofSpoiler', (0, yr - .19, h + .12),
            (w * .91, .35, .14), mat['accent'], groups['Accessories'], .065)
        for side in (-1, 1):
            box('PremiumRoofRail_' + str(side),
                (side * w * .39, .24, h + .18),
                (.065, layout['length'] * .52, .11),
                mat['silver'], groups['Accessories'], .028)
            box('SideRunningBoard_' + str(side),
                (side * (w / 2 + .13), .22, .51),
                (.21, layout['length'] * .58, .055),
                mat['metal'], groups['Accessories'], .017)


def build_interior(layout, mat, groups):
    if not GENERATE_INTERIOR:
        return
    yf = layout['front']
    h = layout['height']
    w = layout['width']
    # These parts are only partially visible through the tinted cab glass.
    cabin_y = yf + 1.55
    box('Dashboard', (0, yf + .90, 1.22),
        (w * .83, .33, .20), mat['interior'], groups['Interior'], .055)
    for side in (-1, 1):
        box('FrontSeat_' + str(side),
            (side * w * .23, cabin_y + .29, 1.04),
            (.48, .52, .58), mat['seat'], groups['Interior'], .115)
        box('SeatHeadrest_' + str(side),
            (side * w * .23, cabin_y + .43, 1.51),
            (.29, .20, .25), mat['interior'], groups['Interior'], .065)
    driver_x = layout['driver_side'] * w * .23
    cylinder('SteeringWheel', (driver_x, yf + .93, 1.40),
             .19, .04, mat['black'], groups['Interior'], 12)


def build_unity_markers(layout, groups):
    # Gameplay anchors. Local Z is up; local -Y is driving forward.
    root = groups['Anchors']
    empty('DriverSeatAnchor', root,
          (layout['driver_side'] * layout['width'] * .23,
           layout['front'] + 1.60, 1.16))
    empty('PassengerSeatAnchor', root,
          (-layout['driver_side'] * layout['width'] * .23,
           layout['front'] + 1.60, 1.16))
    empty('CargoAnchor', root, (0, .66, 1.20))
    # Kept for compatibility with previous van prefabs. Use
    # RearDoorInteractionAnchor and CargoLoadingAnchor for new gameplay.
    empty('RearDoorAnchor', root, (0, layout['rear'] + .10, 1.0))
    empty('CameraFollowAnchor', root, (0, .0, layout['height'] + .95))


def export_fbx(root):
    # Export only meshes and empties under this van, not other scene objects.
    bpy.ops.object.select_all(action='DESELECT')
    def select_recursive(obj):
        obj.select_set(True)
        for child in obj.children:
            select_recursive(child)
    select_recursive(root)
    bpy.context.view_layer.objects.active = root
    path = bpy.path.abspath(EXPORT_FBX_PATH)
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True,
                             object_types={'MESH', 'EMPTY'},
                             use_mesh_modifiers=True,
                             axis_forward='-Z', axis_up='Y',
                             apply_unit_scale=True)
    print('Van FBX exported to:', path)


# ============================================================
# GENERATE
# ============================================================
def generate_van(quality=VAN_QUALITY, seed=SEED):
    quality = str(quality).upper().strip()
    if quality not in VAN_TYPES:
        raise ValueError('VAN_QUALITY must be LIGHT, GOOD or BEST; got %r' % quality)

    if seed is None:
        seed = random.SystemRandom().randrange(1, 2 ** 31)
    rng = random.Random(seed)

    clear_previous_van()
    cfg = VAN_TYPES[quality]
    layout = create_layout(quality, rng)
    mats = build_materials(quality, rng)
    root = empty('GeneratedVan')
    group_names = (
        'Body', 'Windows', 'Wheels', 'Trim', 'Doors', 'Lights',
        'Accessories', 'Interior', 'Cargo', 'Branding', 'Damage', 'Anchors',
    )
    groups = {name: empty(name, root) for name in group_names}

    build_body(layout, mats, groups)
    build_glass(layout, mats, groups, rng)
    build_front_rear(layout, mats, groups, rng)
    build_rear_cargo(layout, mats, groups, rng)
    build_side_doors(layout, mats, groups, rng)
    build_decoration(layout, mats, groups, rng)
    build_accessories(layout, mats, groups, rng)
    build_interior(layout, mats, groups)
    build_wheels(layout, mats, groups, rng)
    build_unity_markers(layout, groups)

    # Exportable authoring data for an eventual Unity van controller.
    root['generator'] = 'Couch Guys Procedural Van v2 (openable rear cargo)'
    root['quality'] = quality
    root['seed'] = seed
    root['forward_axis_blender'] = '-Y'
    root['metres_per_unit'] = 1.0
    root['width_m'] = round(layout['width'], 3)
    root['length_m'] = round(layout['length'], 3)
    root['height_m'] = round(layout['height'], 3)
    root['wheel_radius_m'] = round(layout['radius'], 3)
    root['max_speed_mps'] = cfg['max_speed_mps']
    root['fuel_capacity_l'] = cfg['fuel_capacity_l']
    root['fuel_use_l_per_km'] = cfg['fuel_use_l_per_km']
    root['recommended_delivery'] = cfg['recommended_delivery']
    root['trim_level'] = cfg['trim_level']
    root['cargo_length_m'] = round(layout['cargo_length'], 3)
    root['cargo_width_m'] = round(layout['cargo_width'], 3)
    root['cargo_height_m'] = round(layout['cargo_height'], 3)
    root['rear_door_open_angle_deg'] = round(layout['rear_door_open_angle'], 2)
    root['rear_doors_openable'] = True
    root['cargo_loading_supported_by_geometry'] = True
    root['couch_loading_requires_unity_script'] = True
    root['generated_utc'] = time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime())

    if EXPORT_FBX:
        export_fbx(root)
    bpy.ops.object.select_all(action='DESELECT')
    root.select_set(True)
    bpy.context.view_layer.objects.active = root

    print('\n' + '=' * 56)
    print('COUCH GUYS PROCEDURAL VAN GENERATED')
    print('Quality: %s | seed: %s' % (quality, seed))
    print('Size: %.2f x %.2f x %.2f m' %
          (layout['width'], layout['length'], layout['height']))
    print('Suggested speed: %.1f m/s | fuel capacity: %.1f L | '
          'fuel use: %.2f L/km' %
          (cfg['max_speed_mps'], cfg['fuel_capacity_l'],
           cfg['fuel_use_l_per_km']))
    print('Openable rear doors: RearDoorHinge_L and RearDoorHinge_R')
    print('Cargo bay: %.2f x %.2f x %.2f m' %
          (layout['cargo_width'], layout['cargo_length'], layout['cargo_height']))
    print('Unity anchors: CargoLoadingAnchor, CargoStorageAnchor, CargoBoundsMin/Max')
    print('Van stats and couch loading are metadata; implement gameplay in Unity.')
    print('=' * 56 + '\n')
    return root


if __name__ == '__main__':
    generate_van()
