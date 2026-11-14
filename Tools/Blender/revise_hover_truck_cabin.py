"""Refine the saved truck cabin and export the same named Unity meshes."""
import bpy
import bmesh
from mathutils import Matrix, Vector
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'ArtSource/Vehicles/HoverTruck'
OUTPUT = ROOT / 'Assets/_Project/Models/Vehicles/HoverTruck/HoverTruck.fbx'
bpy.ops.wm.open_mainfile(filepath=str(SOURCE / 'HoverTruck.blend'))
chassis = bpy.data.objects['Main_Chassis']

# The light bar was combined into the static chassis materials. Remove its
# connected mesh islands, including the high diagonal supports and light lenses.
for obj in list(chassis.children):
    if obj.type != 'MESH' or obj.name == 'Pixal3D_LowerHull':
        continue
    mesh = bmesh.new()
    mesh.from_mesh(obj.data)
    remaining = set(mesh.verts)
    removed = []
    while remaining:
        start = remaining.pop()
        island = {start}
        pending = [start]
        while pending:
            current = pending.pop()
            for edge in current.link_edges:
                other = edge.other_vert(current)
                if other not in remaining:
                    continue
                remaining.remove(other)
                island.add(other)
                pending.append(other)
        points = [obj.matrix_world @ v.co for v in island]
        if max(p.z for p in points) > 2.42:
            removed.extend(island)
        # Keep the steering column aligned with the enlarged driver's chair.
        centre = sum(points, Vector()) / len(points)
        if abs(centre.x + .8) < .12 and abs(centre.y + 3.5) < .12 and abs(centre.z - 1.37) < .15:
            for vertex in island:
                vertex.co += obj.matrix_world.inverted().to_3x3() @ Vector((-.05, .35, 0))
    if removed:
        bmesh.ops.delete(mesh, geom=removed, context='VERTS')
    mesh.to_mesh(obj.data)
    mesh.free()

positions = [(-.85, -2.55), (.85, -2.55), (-.85, -.65), (.85, -.65)]
for index, (x, y) in enumerate(positions):
    seat = bpy.data.objects[f'Seat_{index}']
    seat.location = (x, y, 1.06)
    seat.scale = (1.3, 1.3, 1.15)
    if not seat.get('cabin_backrest_lowered'):
        for obj in seat.children:
            if obj.type != 'MESH':
                continue
            to_seat = seat.matrix_world.inverted() @ obj.matrix_world
            from_seat = to_seat.inverted()
            for vertex in obj.data.vertices:
                point = to_seat @ vertex.co
                if point.z > .42:
                    point.z = .42 + (point.z - .42) * .6
                    vertex.co = from_seat @ point
            obj.data.update()
            bpy.ops.object.select_all(action='DESELECT')
            obj.select_set(True)
            bpy.context.view_layer.objects.active = obj
            normals = obj.modifiers.new('Cabin weighted normals', 'WEIGHTED_NORMAL')
            bpy.ops.object.modifier_apply(modifier=normals.name)
        seat['cabin_backrest_lowered'] = True
    bpy.data.objects[f'SeatAnchor_{index}'].location = (x, y, 1.9)
    if index > 0:
        bpy.data.objects[f'TurretYaw_{index - 1}'].location = (1.68 if x > 0 else -1.68, y - .6, 1.52)
bpy.data.objects['Steering_Wheel'].location = (-.85, -3.2, 1.65)

parts = [chassis] + list(chassis.children_recursive)
bpy.ops.object.select_all(action='DESELECT')
for obj in parts:
    obj.select_set(True)
bpy.context.view_layer.objects.active = chassis
mirror = Matrix.Diagonal((1, -1, 1, 1))
matrices = {obj: obj.matrix_local.copy() for obj in parts}
for obj in parts:
    obj.matrix_local = mirror @ matrices[obj] @ mirror
    if obj.type == 'MESH':
        obj.data.transform(mirror)
        obj.data.flip_normals()
bpy.ops.export_scene.fbx(filepath=str(OUTPUT), use_selection=True,
    object_types={'MESH', 'EMPTY'}, global_scale=1, apply_unit_scale=True,
    axis_forward='-Z', axis_up='Y', bake_anim=False, add_leaf_bones=False)
for obj in parts:
    obj.matrix_local = matrices[obj]
    if obj.type == 'MESH':
        obj.data.transform(mirror)
        obj.data.flip_normals()
bpy.context.scene.render.filepath = str(SOURCE / 'HoverTruckPreview.png')
bpy.ops.render.render(write_still=True)
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE / 'HoverTruck.blend'))
print('Saved open cabin with larger chairs and outboard passenger turrets.', flush=True)
