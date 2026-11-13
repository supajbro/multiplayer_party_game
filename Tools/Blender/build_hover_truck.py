"""Explicit Blender refinement of the Pixal3D truck and its moving assemblies."""
import bpy
import bmesh
import json
import math
import sys
from pathlib import Path
from mathutils import Vector, Matrix

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'ArtSource/Vehicles/HoverTruck'
OUTPUT = ROOT / 'Assets/_Project/Models/Vehicles/HoverTruck'
OUTPUT.mkdir(parents=True, exist_ok=True)
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
glb = next((SOURCE / 'Generated').rglob('*.glb'))
bpy.ops.import_scene.gltf(filepath=str(glb))
generated = [o for o in bpy.context.scene.objects if o.type == 'MESH']
for obj in generated:
    obj.name = 'Pixal3D_Base'
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
points = [obj.matrix_world @ Vector(v) for obj in generated for v in obj.bound_box]
minimum = Vector(tuple(min(p[i] for p in points) for i in range(3)))
maximum = Vector(tuple(max(p[i] for p in points) for i in range(3)))
print('PIXAL BOUNDS', list(minimum), list(maximum), flush=True)


def camera_render(name, position, target, path, size=900):
    bpy.ops.object.camera_add(location=position)
    camera = bpy.context.object
    camera.name = name
    camera.rotation_euler = (Vector(target) - camera.location).to_track_quat('-Z', 'Y').to_euler()
    camera.data.type = 'ORTHO'
    camera.data.ortho_scale = 12.8 if 'Final' in name else 2.8
    scene = bpy.context.scene
    scene.camera = camera
    scene.render.resolution_x = size
    scene.render.resolution_y = size
    scene.render.resolution_percentage = 100
    scene.render.filepath = str(path)
    scene.render.image_settings.file_format = 'PNG'
    bpy.ops.render.render(write_still=True)


scene = bpy.context.scene
scene.render.engine = 'CYCLES'
scene.cycles.samples = 24
scene.world.color = (.3, .3, .3)
for location, energy, size in [((4, -5, 9), 1600, 7), ((-5, 1, 6), 1200, 6), ((0, 6, 9), 1700, 5)]:
    bpy.ops.object.light_add(type='AREA', location=location)
    light = bpy.context.object
    light.data.energy = energy
    light.data.shape = 'DISK'
    light.data.size = size
    light.rotation_euler = (-light.location).to_track_quat('-Z', 'Y').to_euler()
if '--inspect' in sys.argv:
    for name, pos in [('A', (2, -3, 2)), ('B', (-2, 3, 2))]:
        camera_render(name, pos, (0, 0, 0), SOURCE / ('BasePreview_' + name + '.png'), 800)
    bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE / 'Pixal3D_Base.blend'))
    sys.exit()

# Blender coordinates use X across, -Y forward, Z up. The reconstruction is
# aligned here after inspecting its two opposing views.
for obj in generated:
    obj.rotation_euler.z = math.radians(45)
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)


def material(name, color, metal=0, emission=0):
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = (*color, 1)
    mat.use_nodes = True
    bsdf = next(n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    bsdf.inputs['Base Color'].default_value = (*color, 1)
    bsdf.inputs['Metallic'].default_value = metal
    bsdf.inputs['Roughness'].default_value = .38
    bsdf.inputs['Emission Color'].default_value = (*color, 1)
    bsdf.inputs['Emission Strength'].default_value = emission
    return mat


white = material('Hover_Porcelain', (.83, .86, .91))
blue = material('Hover_Cobalt', (.025, .12, .65), .25)
dark = material('Hover_Graphite', (.035, .047, .065), .4)
steel = material('Hover_Steel', (.26, .32, .4), .6)
orange = material('Hover_Orange', (1, .245, .025))
yellow = material('Hover_Hazard', (1, .58, .015))
cyan = material('Hover_Cyan', (.01, .8, 1), .1, 4)
red = material('Hover_Taillight', (1, .06, .025), .1, 2)
glass = material('Hover_Windscreen', (.025, .38, .55), .45)
parts = []


def empty(name, location=(0, 0, 0), parent=None):
    obj = bpy.data.objects.new(name, None)
    scene.collection.objects.link(obj)
    obj.parent = parent
    obj.location = location
    parts.append(obj)
    return obj


def finish(obj, name, location, mat, parent, bevel=0):
    obj.name = name
    obj.location = location
    obj.parent = parent
    obj.data.materials.clear()
    obj.data.materials.append(mat)
    if bevel:
        modifier = obj.modifiers.new('Rounded panel edges', 'BEVEL')
        modifier.width = bevel
        modifier.segments = 3
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.modifier_apply(modifier=modifier.name)
    for polygon in obj.data.polygons:
        polygon.use_smooth = True
    weighted = obj.modifiers.new('Weighted panel normals', 'WEIGHTED_NORMAL')
    weighted.keep_sharp = True
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier=weighted.name)
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(island_margin=.025)
    bpy.ops.object.mode_set(mode='OBJECT')
    parts.append(obj)
    return obj


def box(name, location, size, mat, parent=None, bevel=.08):
    bpy.ops.mesh.primitive_cube_add()
    obj = bpy.context.object
    obj.scale = Vector(size) * .5
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return finish(obj, name, location, mat, parent, min(bevel, min(size) * .4))


def cylinder(name, location, radius, depth, mat, parent=None, rotation=None):
    bpy.ops.mesh.primitive_cylinder_add(vertices=24, radius=radius, depth=depth)
    obj = bpy.context.object
    finish(obj, name, location, mat, parent, .025)
    if rotation:
        obj.rotation_euler = rotation
    return obj


def ring(name, location, radius, thickness, mat, parent=None, rotation=None):
    bpy.ops.mesh.primitive_torus_add(major_radius=radius, minor_radius=thickness, major_segments=32, minor_segments=8)
    obj = bpy.context.object
    finish(obj, name, location, mat, parent)
    if rotation:
        obj.rotation_euler = rotation
    return obj


chassis = empty('Main_Chassis')
# Keep the generated lower hull's geometry, UVs and texture. Functional upper
# assemblies are rebuilt as separately editable mesh objects with clean pivots.
points = [obj.matrix_world @ Vector(v) for obj in generated for v in obj.bound_box]
minimum = Vector(tuple(min(p[i] for p in points) for i in range(3)))
maximum = Vector(tuple(max(p[i] for p in points) for i in range(3)))
extent = maximum - minimum
for obj in generated:
    for vertex in obj.data.vertices:
        p = obj.matrix_world @ vertex.co
        vertex.co = ((p.x - (maximum.x + minimum.x) / 2) * 3.6 / extent.x,
                     (p.y - (maximum.y + minimum.y) / 2) * 9.5 / extent.y,
                     (p.z - minimum.z) * 2.7 / extent.z)
    obj.matrix_world.identity()
    mesh = bmesh.new()
    mesh.from_mesh(obj.data)
    retained = lambda v: v.co.z > .55 and (v.co.z < 1.05 or
        (abs(v.co.x) > 1.5 and v.co.z < 1.55) or (v.co.y < -3.65 and v.co.z < 1.85))
    bmesh.ops.delete(mesh, geom=[v for v in mesh.verts if not retained(v)], context='VERTS')
    bmesh.ops.remove_doubles(mesh, verts=list(mesh.verts), dist=.0001)
    bmesh.ops.recalc_face_normals(mesh, faces=list(mesh.faces))
    mesh.to_mesh(obj.data)
    mesh.free()
    obj.name = 'Pixal3D_LowerHull'
    obj.parent = chassis
    parts.append(obj)
    for mat in obj.data.materials:
        mat.name = 'Hover_PixalHull'
        for node in mat.node_tree.nodes:
            if node.type == 'TEX_IMAGE' and node.image:
                image = node.image
                image.filepath_raw = str(OUTPUT / (image.name.replace('.', '_') + '.png'))
                image.file_format = 'PNG'
                image.save()

box('Deck', (0, 0, .9), (3.5, 9.3, .3), dark, chassis, .12)
box('Lower_Keel', (0, 0, .55), (3.15, 9.0, .45), blue, chassis, .2)
vertices = [(x, y, z) for x in (-1.3, 1.3) for y, z in
            [(-4.7, .8), (-3.45, .8), (-3.45, 1.65), (-4.7, 1.12)]]
mesh = bpy.data.meshes.new('Sloped nose panel')
mesh.from_pydata(vertices, [], [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4),
                               (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)])
mesh.update()
nose = bpy.data.objects.new('Front_Nose', mesh)
scene.collection.objects.link(nose)
finish(nose, 'Front_Nose', (0, 0, 0), white, chassis, .12)


def hood_decal(name, points, mat):
    area = sum(points[i][0] * points[(i + 1) % len(points)][1] -
               points[(i + 1) % len(points)][0] * points[i][1] for i in range(len(points)))
    if area < 0:
        points = list(reversed(points))
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata([(x, y, 1.12 + (y + 4.7) * .424 + (.018 if mat == white else .008)) for x, y in points], [],
                     [tuple(range(len(points)))])
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    scene.collection.objects.link(obj)
    finish(obj, name, (0, 0, 0), mat, chassis)


hood_decal('Bandit_Mask', [(-.72, -3.9), (-.18, -4.01), (0, -3.98), (.18, -4.01),
    (.72, -3.9), (.53, -4.33), (.23, -4.38), (0, -4.28), (-.23, -4.38), (-.53, -4.33)], dark)
hood_decal('Mask_Eye_Left', [(-.5, -4.08), (-.18, -4.17), (-.37, -4.23)], white)
hood_decal('Mask_Eye_Right', [(.5, -4.08), (.37, -4.23), (.18, -4.17)], white)
box('Front_Bumper', (0, -4.68, .7), (2.9, .3, .45), dark, chassis, .12)
box('Front_Light', (0, -4.76, .95), (1.25, .06, .16), cyan, chassis, .04)
for side in (-1, 1):
    box('Front_Armor', (side * 1.45, -4.05, .97), (.64, 1.45, .75), blue, chassis, .2)
    box('Corner_Light', (side * 1.4, -4.78, 1.11), (.4, .07, .14), cyan, chassis, .04)
    ring('Recovery_Loop', (side * 1.05, -4.92, .62), .17, .055, yellow, chassis, (math.pi / 2, 0, 0))
    box('Side_Sill', (side * 1.68, .4, .83), (.25, 7.9, .35), blue, chassis)
    box('Hazard_Rail', (side * 1.79, 1.55, 1.12), (.15, 5.7, .36), dark, chassis, .04)
    box('White_Rail', (side * 1.78, 1.55, 1.42), (.2, 5.8, .22), white, chassis)
    for j in range(9):
        stripe = box('Hazard_Stripe', (side * 1.88, -.95 + j * .61, 1.12), (.025, .28, .32), yellow, chassis, .01)
        stripe.rotation_euler.x = -.4
    box('Rear_Armor', (side * 1.55, 4.3, 1.0), (.6, .8, .65), blue, chassis, .14)
    box('Rear_Light', (side * 1.3, 4.72, 1.0), (.55, .05, .2), red, chassis, .04)
    box('Cab_Step', (side * 1.74, -2.15, .83), (.35, 2.4, .18), steel, chassis)
    box('Cab_Rail', (side * 1.68, -2.05, 1.12), (.16, 2.4, .25), white, chassis)
    post = box('Windscreen_Frame', (side * 1.34, -3.62, 1.78), (.14, .16, 1.05), dark, chassis)
    post.rotation_euler.x = -.3
box('Windscreen', (0, -3.72, 1.75), (2.55, .06, .7), glass, chassis, .025).rotation_euler.x = -.3
for side in (-1, 1):
    start = Vector((side * 1.49, -3.65, 1.82))
    end = Vector((side * 1.49, -2.2, 2.72))
    beam = box('Cockpit_Rollbar', (start + end) * .5, (.22, .22, (end - start).length), dark, chassis)
    beam.rotation_euler = (end - start).to_track_quat('Z', 'Y').to_euler()
    box('Worklight_Housing', (side * .65, -2.38, 2.8), (.75, .42, .35), dark, chassis, .12)
    box('Worklight_Lens', (side * .65, -2.61, 2.8), (.53, .035, .16), cyan, chassis, .04)
box('Cockpit_Crossbar', (0, -2.2, 2.69), (3.15, .24, .24), dark, chassis)

# Six independent propulsion units with named glow groups.
for row, y in enumerate((-3.55, -.2, 3.35)):
    for side, x in enumerate((-1.3, 1.3)):
        index = row * 2 + side
        engine = empty(f'HoverEngine_{index}', (x, y, .4), chassis)
        cylinder('Engine_Housing', (0, 0, 0), .68, .34, dark, engine)
        cylinder('Engine_Armor', (0, 0, .15), .64, .16, blue, engine)
        glow = empty(f'HoverGlow_{index}', (0, 0, -.21), engine)
        cylinder('Ion_Core', (0, 0, 0), .57, .12, cyan, glow)
        ring('Ion_Ring', (0, 0, -.07), .53, .035, cyan, glow)
        empty(f'HoverProbe_{index}', (x, y, .7), chassis)

seat_positions = [(-.8, -2.9), (.8, -2.9), (-.8, -1.35), (.8, -1.35)]
for index, (x, y) in enumerate(seat_positions):
    seat = empty(f'Seat_{index}', (x, y, 1.06), chassis)
    box('Seat_Base', (0, 0, .12), (.8, .9, .24), dark, seat)
    box('Seat_Cushion', (0, -.05, .29), (.73, .72, .23), orange, seat, .12)
    box('Seat_BackFrame', (0, .34, .7), (.82, .2, 1.05), white, seat, .1)
    box('Seat_Back', (0, .16, .73), (.75, .32, 1.02), orange, seat, .13)
    box('Seat_Headrest', (0, .2, 1.22), (.66, .26, .28), orange, seat, .1)
    box('Seat_Badge', (0, -.012, 1.05), (.3, .025, .07), white, seat, .02)
    for side in (-1, 1):
        box('Seat_Arm', (side * .4, 0, .52), (.12, .7, .14), white, seat)
    # Player root offset is -0.8 m, matching the existing seated animation.
    empty(f'SeatAnchor_{index}', (x, y, 1.9), chassis)
    if index > 0:
        outward = -1 if x < 0 else 1
        turret = empty(f'TurretYaw_{index - 1}', (outward * 1.38, y - .1, 1.62), chassis)
        cylinder('Turret_Base', (0, 0, 0), .23, .22, steel, turret)
        box('Turret_Shield', (0, -.06, .17), (.45, .3, .4), white, turret)
        box('Turret_Display', (0, -.23, .17), (.26, .025, .23), cyan, turret)
        pitch = empty(f'TurretPitch_{index - 1}', (0, 0, .47), turret)
        cylinder('Turret_Barrel', (0, -.32, 0), .13, .7, dark, pitch, (math.pi / 2, 0, 0))
        cylinder('Turret_Collar', (0, -.1, 0), .17, .16, yellow, pitch, (math.pi / 2, 0, 0))
        cylinder('Turret_Muzzle', (0, -.69, 0), .115, .06, cyan, pitch, (math.pi / 2, 0, 0))
        empty(f'TurretMuzzle_{index - 1}', (0, -.78, 0), pitch)

steering = empty('Steering_Wheel', (-.8, -3.55, 1.65), chassis)
ring('Steering_Rim', (0, 0, 0), .26, .045, dark, steering, (.8, 0, 0))
box('Steering_Hub', (0, 0, 0), (.34, .07, .07), steel, steering)
box('Steering_Column', (-.8, -3.5, 1.37), (.12, .14, .55), dark, chassis)

for index, (x, y) in enumerate([(-.88, 1.05), (.88, 1.05), (-.88, 3.45), (.88, 3.45)]):
    dock = empty(f'MagneticDock_{index}', (x, y, 1.08), chassis)
    box('Dock_Tile', (0, 0, -.02), (1.62, 2.18, .14), steel, dock)
    cylinder('Magnet_Platform', (0, 0, .07), .68, .11, dark, dock)
    ring('Magnet_OuterRing', (0, 0, .14), .6, .035, cyan, dock)
    ring('Magnet_InnerRing', (0, 0, .14), .32, .018, cyan, dock)
    empty(f'CargoPoint_{index}', (x, y, 1.23), chassis)
    for side in (-1, 1):
        clamp = empty(f'DockClamp_{index * 2 + (1 if side > 0 else 0)}', (side * .73, 0, .05), dock)
        box('Clamp_Arm', (0, 0, .28), (.13, .26, .6), white, clamp)
        box('Clamp_Tip', (-side * .1, 0, .58), (.3, .3, .15), dark, clamp)
        box('Clamp_Light', (-side * .075, -.15, .28), (.035, .02, .3), cyan, clamp, .005)
        cylinder('Clamp_Hinge', (0, -.18, .04), .1, .08, yellow, clamp, (math.pi / 2, 0, 0))

box('Rear_Bumper', (0, 4.62, .76), (3.2, .28, .33), white, chassis)
box('Rear_Magnet_Emblem', (0, 4.8, 1.05), (.6, .1, .5), dark, chassis)
for x in (-.15, .15):
    box('Emblem_Upright', (x, 4.86, 1.1), (.07, .02, .23), cyan, chassis, .02)
box('Emblem_Bottom', (0, 4.86, .97), (.3, .02, .07), cyan, chassis, .02)

# Only the model hierarchy is exported. Lighting and cameras remain in the blend
# for inspection and are excluded from the FBX selection.
# Combine static surfaces sharing a parent and material while keeping every
# functional assembly and its pivot separately editable.
groups = {}
for obj in parts:
    if obj.type == 'MESH' and obj.name != 'Front_Nose' and len(obj.data.materials) == 1:
        groups.setdefault((obj.parent, obj.data.materials[0]), []).append(obj)
for (parent, mat), objects in groups.items():
    if len(objects) < 2:
        continue
    bpy.ops.object.select_all(action='DESELECT')
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.object.join()
    bpy.context.object.name = parent.name + '_' + mat.name
parts = [chassis] + list(chassis.children_recursive)
bpy.ops.object.select_all(action='DESELECT')
for obj in parts:
    obj.select_set(True)
bpy.context.view_layer.objects.active = chassis
# Unity's FBX importer maps Blender Y directly to Unity Z. Mirror the authoring
# forward axis for export and reverse winding so normals remain outward.
mirror = Matrix.Diagonal((1, -1, 1, 1))
local_matrices = {obj: obj.matrix_local.copy() for obj in parts}
for obj in parts:
    obj.matrix_local = mirror @ local_matrices[obj] @ mirror
    if obj.type == 'MESH':
        obj.data.transform(mirror)
        obj.data.flip_normals()
bpy.ops.export_scene.fbx(filepath=str(OUTPUT / 'HoverTruck.fbx'), use_selection=True,
                         object_types={'MESH', 'EMPTY'}, global_scale=1, apply_unit_scale=True,
                         axis_forward='-Z', axis_up='Y', bake_anim=False, add_leaf_bones=False)
for obj in parts:
    obj.matrix_local = local_matrices[obj]
    if obj.type == 'MESH':
        obj.data.transform(mirror)
        obj.data.flip_normals()
camera_render('Final_Front', (11, -15, 11), (0, 0, 1.0), SOURCE / 'HoverTruckPreview.png', 1100)
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE / 'HoverTruck.blend'))
stats = {'objects': len(parts), 'triangles': sum(len(o.data.loop_triangles) for o in parts if o.type == 'MESH')}
for obj in parts:
    if obj.type == 'MESH':
        obj.data.calc_loop_triangles()
stats['triangles'] = sum(len(o.data.loop_triangles) for o in parts if o.type == 'MESH')
(SOURCE / 'MeshStatistics.json').write_text(json.dumps(stats, indent=2))
print('HOVER TRUCK EXPORT', stats, flush=True)
