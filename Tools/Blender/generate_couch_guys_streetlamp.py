"""COUCH GUYS — STREETLAMP (fixed, repeatable Blender generator)

Blender 3.6 / 4.x / 5.x. No add-ons or other files required.

INSTRUCTIONS
    Blender > Scripting > Open > this file > Run Script.
    Go to Layout if necessary: the new object is selected and framed in 3D views.
    Re-running the script replaces ONLY its own generated collection.

One Unity-friendly mesh (with named material slots), ground-level origin and
exactly the same geometry every time. All dimensions are in metres.
"""

import math
import bpy
import bmesh
from mathutils import Vector, Matrix

# ------------------------------ SETTINGS ----------------------------------
COLLECTION_NAME = "CG_StreetLamp_Generated"
OBJECT_NAME = "CouchGuys_StreetLamp_01"
SHOW_PREVIEW_LIGHT = False  # Blender-only Area Light; not needed for FBX
FRAME_IN_VIEWPORT = True

# ----------------------------- PREPARE -------------------------------------
# Many Blender errors come from running a script while editing a mesh.
if bpy.context.object is not None and bpy.context.object.mode != 'OBJECT':
    bpy.ops.object.mode_set(mode='OBJECT')

old_collection = bpy.data.collections.get(COLLECTION_NAME)
if old_collection:
    for old_obj in list(old_collection.objects):
        bpy.data.objects.remove(old_obj, do_unlink=True)
    bpy.data.collections.remove(old_collection)

collection = bpy.data.collections.new(COLLECTION_NAME)
bpy.context.scene.collection.children.link(collection)


def rgba(hexcode):
    s = hexcode.lstrip('#')
    vals = [int(s[n:n+2], 16) / 255.0 for n in (0, 2, 4)]
    return tuple(v / 12.92 if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4
                 for v in vals) + (1.0,)


def get_material(name, hexcode, metallic=0.0, roughness=0.5,
                 emission_hex=None, strength=0.0):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.diffuse_color = rgba(hexcode)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get('Principled BSDF')
    if bsdf is not None:
        for socket_name, val in [('Base Color', rgba(hexcode)),
                                 ('Metallic', metallic), ('Roughness', roughness)]:
            socket = bsdf.inputs.get(socket_name)
            if socket is not None:
                socket.default_value = val
        light_socket = bsdf.inputs.get('Emission Color')
        if light_socket is None:
            light_socket = bsdf.inputs.get('Emission')
        if light_socket is not None:
            light_socket.default_value = rgba(emission_hex or hexcode)
        power = bsdf.inputs.get('Emission Strength')
        if power is not None:
            power.default_value = strength
    return mat


materials = [
    get_material('CG_Lamp | Blue slate powdercoat', '314B60', .2, .47),
    get_material('CG_Lamp | Deep navy', '172C3C', .2, .57),
    get_material('CG_Lamp | Skyblue metal highlights', '53798A', .2, .45),
    get_material('CG_Lamp | Dark rubber seams', '142732', .0, .78),
    get_material('CG_Lamp | Satin silver bolts', '92ADBB', .64, .35),
    get_material('CG_Lamp | Teal accent', '7BD3CA', .0, .48),
    get_material('CG_Lamp | Warm glowing diffuser', 'FFE1A3', 0, .5, 'FFD77E', 2.0),
    get_material('CG_Lamp | Bright inner diffuser', 'FFF1C7', 0, .35, 'FFE7AA', 3.0),
]
METAL, DARK, EDGE, RUBBER, SILVER, TEAL, GLOW, BRIGHT = range(8)

# Make geometry in memory, then create ONE mesh: avoids object-mode/modifier
# operators failing depending on the editor Blender is using.
all_vertices = []
all_faces = []
all_materials = []


def append_bmesh(bm, mat_index):
    bm.normal_update()
    bm.verts.ensure_lookup_table()
    bm.verts.index_update()
    base = len(all_vertices)
    all_vertices.extend(tuple(v.co) for v in bm.verts)
    for f in bm.faces:
        if len(f.verts) >= 3:
            all_faces.append(tuple(base + v.index for v in f.verts))
            all_materials.append(mat_index)
    bm.free()


def bevel(bm, radius, segments=2):
    """Optional bevel; core shape is preserved even on older Blender APIs."""
    if radius <= 0:
        return
    edges = list(bm.edges)
    if not edges:
        return
    try:
        bmesh.ops.bevel(bm, geom=edges, offset=radius, segments=segments,
                         profile=0.5, affect='EDGES')
    except Exception:
        # Blender 3.x builds use the older bevel keyword.
        try:
            bmesh.ops.bevel(bm, geom=edges, offset=radius, segments=segments,
                             profile=0.5, vertex_only=False)
        except Exception:
            pass


def block(cx, cy, cz, sx, sy, sz, mat=METAL, edge=.02, segments=2):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=(sx, sy, sz), verts=list(bm.verts))
    # Bevel before translating for better numeric stability.
    bevel(bm, min(edge, sx * .23, sy * .23, sz * .23), segments)
    bmesh.ops.translate(bm, vec=(cx, cy, cz), verts=list(bm.verts))
    append_bmesh(bm, mat)


def profile_xz(points, width, mat, edge=0.0):
    """Extrude any simple XZ outline sideways to make curved arm/head shapes."""
    count = len(points)
    vertices = [(x, -width / 2, z) for x, z in points]
    vertices += [(x, width / 2, z) for x, z in points]
    faces = [tuple(range(count - 1, -1, -1)),
             tuple(range(count, count * 2))]
    for i in range(count):
        j = (i + 1) % count
        faces.append((i, j, count + j, count + i))
    tmp = bpy.data.meshes.new('_CG_temp_profile')
    tmp.from_pydata(vertices, [], faces)
    tmp.update()
    bm = bmesh.new()
    bm.from_mesh(tmp)
    bpy.data.meshes.remove(tmp)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bevel(bm, edge, 2)
    append_bmesh(bm, mat)


def tapered_pole(z_bottom, z_top, bottom_width, top_width, mat=METAL):
    """Sturdy octagonal square-ish taper, stronger at its bottom."""
    vertices = []
    corner = .15
    for z, w in [(z_bottom, bottom_width), (z_top, top_width)]:
        h = w / 2
        inset = w * corner
        loop = [(-h + inset, -h), (h - inset, -h),
                (h, -h + inset), (h, h - inset),
                (h - inset, h), (-h + inset, h),
                (-h, h - inset), (-h, -h + inset)]
        vertices.extend([(x, y, z) for x, y in loop])
    faces = [tuple(range(7, -1, -1)), tuple(range(8, 16))]
    faces += [(i, (i + 1) % 8, 8 + (i + 1) % 8, 8 + i) for i in range(8)]
    tmp = bpy.data.meshes.new('_CG_temp_post')
    tmp.from_pydata(vertices, [], faces)
    tmp.update()
    bm = bmesh.new()
    bm.from_mesh(tmp)
    bpy.data.meshes.remove(tmp)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    append_bmesh(bm, mat)


def ring_bolt(x, y, z, radius=.025):
    # Octagonal, low-poly bolt sitting on a small base.
    segments = 8
    vs = []
    for zz, rr in [(z, radius), (z + .018, radius * .85)]:
        vs.extend([(x + rr * math.cos(i * math.tau / segments),
                    y + rr * math.sin(i * math.tau / segments), zz)
                   for i in range(segments)])
    fs = [tuple(range(segments - 1, -1, -1)),
          tuple(range(segments, segments * 2))]
    fs.extend((i, (i + 1) % segments, segments + (i + 1) % segments,
               segments + i) for i in range(segments))
    bm = bmesh.new()
    tmp = bpy.data.meshes.new('_CG_temp_bolt')
    tmp.from_pydata(vs, [], fs)
    bm.from_mesh(tmp)
    bpy.data.meshes.remove(tmp)
    append_bmesh(bm, SILVER)

# ------------------------ SCULPTED STREET LAMP -----------------------------
# Base and mounting hardware (origin = pavement directly under the lamp).
block(0, 0, .065, .49, .47, .13, DARK, .045, 3)
block(0, 0, .19, .385, .38, .24, METAL, .075, 3)
block(0, 0, .32, .33, .335, .075, EDGE, .024)
for bx in (-.18, .18):
    for by in (-.17, .17):
        ring_bolt(bx, by, .134, .021)

# The pole is roughly the same dimensions as the in-game placeholder, but
# softly chamfered and tapered so it feels intentionally designed.
tapered_pole(.34, 5.24, .285, .205)
block(0, 0, 3.53, .262, .26, .052, EDGE, .015)
block(0, 0, 5.17, .30, .294, .225, DARK, .04)
block(0, 0, 5.265, .307, .304, .07, EDGE, .02)

# Street-facing pole service panel (-Y), with a two-tone inset.
block(0, -.163, 1.04, .21, .028, .50, DARK, .018)
block(0, -.181, 1.04, .156, .013, .435, EDGE, .012)
block(0, -.19, 1.20, .045, .015, .038, SILVER, .006)
block(0, -.19, .88, .045, .015, .038, SILVER, .006)

# Slightly rising curved-arm silhouette; more friendly than a hard L-shape.
profile_xz([
    (-.11, 5.08), (.23, 5.15), (.47, 5.215),
    (1.34, 5.215), (1.44, 5.29), (1.44, 5.415),
    (1.31, 5.50), (.43, 5.50), (.18, 5.39), (-.11, 5.31),
], .255, METAL, .035)
block(.32, 0, 5.22, .40, .198, .085, DARK, .024)
block(1.33, 0, 5.34, .24, .325, .26, DARK, .035)

# Broad overhead housing, with a distinct wedged top and smooth decorative lid.
profile_xz([
    (1.08, 5.075), (1.07, 5.38), (1.25, 5.465),
    (2.15, 5.405), (2.30, 5.30), (2.275, 5.065),
    (2.15, 5.00), (1.16, 5.00),
], .77, DARK, .045)
block(1.685, 0, 5.442, 1.09, .525, .065, METAL, .026)
block(1.75, 0, 5.49, .65, .075, .033, EDGE, .009)

# Side trim, cheerful accent marks and a readable panel from a distance.
for ysign in (-1, 1):
    block(1.71, ysign * .39, 5.19, .84, .024, .047, EDGE, .008)
    block(1.20, ysign * .40, 5.225, .13, .026, .07, TEAL, .009)
    block(2.18, ysign * .40, 5.16, .043, .02, .043, SILVER, .006)

# Thick gasket, glowing diffuser and bright middle area, facing straight down.
block(1.70, 0, 4.998, 1.15, .70, .09, RUBBER, .026)
block(1.70, 0, 4.942, 1.065, .61, .071, GLOW, .025)
block(1.70, 0, 4.897, .915, .435, .023, BRIGHT, .009)
for xx in (1.44, 1.96):
    block(xx, 0, 4.885, .026, .50, .017, EDGE, .004)

# ---------------------- FINALISE SINGLE MESH -------------------------------
mesh = bpy.data.meshes.new(OBJECT_NAME + '_Mesh')
mesh.from_pydata(all_vertices, [], all_faces)
mesh.update(calc_edges=True)
for material in materials:
    mesh.materials.append(material)
for polygon, mat_index in zip(mesh.polygons, all_materials):
    polygon.material_index = mat_index

lamp = bpy.data.objects.new(OBJECT_NAME, mesh)
collection.objects.link(lamp)
lamp.location = (0, 0, 0)
lamp.hide_set(False)
lamp.hide_viewport = False
lamp['couch_guys'] = 'streetlamp'
lamp['fixed_design'] = True
lamp['unity_light_position_local'] = (1.70, 0.0, 4.85)
lamp['unity_light_direction'] = 'down -Z'

if SHOW_PREVIEW_LIGHT:
    data = bpy.data.lights.new('CG_StreetLamp_PreviewOnly', type='AREA')
    data.energy = 450
    data.shape = 'RECTANGLE'
    data.size = 1.0
    data.size_y = .5
    data.color = (1.0, .79, .52)
    preview_light = bpy.data.objects.new('CG_Lamp_Preview_Light', data)
    collection.objects.link(preview_light)
    preview_light.location = (1.70, 0, 4.84)
    # Area lights shine down along their local -Z axis, their default orientation.

# Select resulting lamp automatically for FBX export or manual inspection.
bpy.ops.object.select_all(action='DESELECT')
lamp.select_set(True)
bpy.context.view_layer.objects.active = lamp
bpy.context.view_layer.update()

# Crucial fix: centre all relevant Blender viewports on this model.
if FRAME_IN_VIEWPORT:
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type == 'VIEW_3D':
                for space in area.spaces:
                    if space.type == 'VIEW_3D' and space.region_3d is not None:
                        space.region_3d.view_location = Vector((1.05, 0.0, 2.7))
                        space.region_3d.view_distance = 9.2
                        space.clip_end = max(space.clip_end, 1000.0)
                        # This lets solid-view display the blue and yellow materials.
                        try:
                            space.shading.color_type = 'MATERIAL'
                        except Exception:
                            pass

print('\n[COUCH GUYS] Streetlamp generated successfully!')
print('[COUCH GUYS] Object:', lamp.name)
print('[COUCH GUYS] Collection:', collection.name)
print('[COUCH GUYS] Mesh: %d verts / %d faces' % (len(mesh.vertices), len(mesh.polygons)))
print('[COUCH GUYS] Select it in the Outliner and press Numpad . to frame, if needed.')
print('[COUCH GUYS] For Unity, add a downward Spot Light at local (1.70, 0, 4.85).')
