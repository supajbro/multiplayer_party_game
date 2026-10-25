"""Build House 03 from the polished Medium house's authored mesh modules/palette.

Run build_mansion(project_directory) in Blender. Front is -Y, Z is up;
exports use the same selection/unit/axis conventions as House 02.
The reference assets are never saved or modified.
"""
import bpy
import math
from mathutils import Matrix


def build_mansion(project):
    output = project + '/Assets/_Project/Models/Houses/'
    previous = bpy.data.objects.get('CG_House_03_ROOT')
    if previous:
        for obj in list(previous.children_recursive):
            bpy.data.objects.remove(obj, do_unlink=True)
        bpy.data.objects.remove(previous, do_unlink=True)
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=output + 'CouchGuys_House_01.fbx')
    imported = set(bpy.data.objects) - before
    reference = next(o for o in imported if o.type == 'EMPTY' and o.parent is None)
    reference.name = 'CG_House_03_ROOT'
    # Stretch the authored plan, preserving height and all bevels/material assignments.
    # 1.38 * 1.45 = 2.001 times the Medium model's plan area.
    # Bake the plan expansion so new facade details can use ordinary metre coordinates.
    expand = Matrix.Diagonal((1.38, 1.45, 1.0, 1.0))
    transforms = {obj: expand @ obj.matrix_world for obj in imported if obj.type == 'MESH'}
    reference.scale = (1, 1, 1)
    bpy.context.view_layer.update()
    for obj in list(imported):
        if obj.type == 'MESH':
            obj.data = obj.data.copy()
            obj.data.transform(transforms[obj])
            obj.parent = reference
            obj.matrix_world.identity()
    reference.scale = (1, 1, 1)
    bpy.context.view_layer.update()
    palette = {m.name.split('.')[0]: bpy.data.materials.get(m.name.split('.')[0]) or m
               for o in imported if o.type == 'MESH' for m in o.data.materials if m}
    for obj in imported:
        if obj.type == 'MESH':
            for i, mat in enumerate(obj.data.materials):
                if mat: obj.data.materials[i] = palette[mat.name.split('.')[0]]
    trim = palette['MAT_Trim_WarmWhite']
    stone = palette['MAT_Stone_LightGrey']
    dark = palette['MAT_Hardware_Black']
    glass = palette['MAT_Window_Glass']
    garage = palette['MAT_Garage_OffWhite']

    def box(name, centre, size, mat, bevel=.035):
        # Same centred box mesh and baked bevel convention as generate_cartoon_house.
        x, y, z = centre
        a, b, c = (v / 2 for v in size)
        verts = [(x+sx*a,y+sy*b,z+sz*c) for sz in (-1,1) for sy in (-1,1) for sx in (-1,1)]
        mesh = bpy.data.meshes.new(name)
        mesh.from_pydata(verts, [], [(0,2,3,1),(4,5,7,6),(0,1,5,4),(2,6,7,3),(0,4,6,2),(1,3,7,5)])
        mesh.materials.append(mat)
        mesh.update()
        obj = bpy.data.objects.new(name, mesh)
        bpy.context.collection.objects.link(obj)
        obj.parent = reference
        mod = obj.modifiers.new('Soft cartoon edges', 'BEVEL')
        mod.width = min(bevel, min(size)*.2)
        mod.segments = 2
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.modifier_apply(modifier=mod.name)
        return obj

    # Replace the small pitched porch with an expensive broad, columned balcony.
    for obj in list(reference.children_recursive):
        if obj.name.startswith(('Porch_Fascia', 'Porch_Gable', 'Porch_Roof', 'PorchRoof', 'Shingles_Porch')):
            bpy.data.objects.remove(obj, do_unlink=True)
    box('Entrance_Balcony_Slab', (0,-6.1,3.55), (7.4,2.5,.26), stone)
    box('Entrance_Balcony_Cornice', (0,-6.1,3.35), (7.6,2.6,.18), trim)
    for x in (-3.25,3.25):
        box('Porch_Column_Grand', (x,-6.8,1.93), (.42,.42,2.9), trim)
        box('Porch_Column_GrandBase', (x,-6.8,.55), (.66,.66,.22), stone)
        box('Porch_Column_GrandCapital', (x,-6.8,3.28), (.68,.68,.24), trim)
        box('Detail_BalconySideRail', (x,-6.1,4.65), (.12,2.25,.12), trim)
    for z in (3.85,4.65):
        box('Detail_BalconyFrontRail', (0,-7.2,z), (6.6,.14,.14), trim)
    for i in range(17):
        box('Detail_BalconyBaluster', (-3.2+i*.4,-7.2,4.25), (.085,.085,.76), trim,.012)
    # Tall French windows overlooking the entrance balcony.
    box('Window_Frame_Balcony', (0,-4.72,4.85), (3.25,.18,2.3), trim)
    box('Window_Glass_Balcony', (0,-4.84,4.85), (2.95,.07,2.05), glass,.015)
    for x in (-.98,0,.98):
        box('Detail_BalconyWindowMullion', (x,-4.90,4.85), (.085,.06,2.08), trim,.01)
    # The widened garage has two separate doors and a central architectural pier.
    garage_parts = [o for o in reference.children_recursive if 'Garage' in o.name and
                    any(s in o.name for s in ('Door','Panel','Handle','Window','Frame','Slat','Casing','Header','Recess','Warm'))]
    for obj in garage_parts:
        bpy.data.objects.remove(obj, do_unlink=True)
    for x in (7.25,10.25):
        box('GarageDoor_Double_Frame', (x,-4.85,1.65), (3.0,.22,2.8), trim)
        box('GarageDoor_Double_Panel', (x,-5.0,1.64), (2.75,.12,2.54), garage)
        for row in range(5):
            box('Detail_GarageSlat', (x,-5.08,.55+row*.44), (2.7,.035,.035), stone,.006)
        for dx in (-.9,0,.9):
            box('Window_Glass_Garage', (x+dx,-5.10,2.54), (.62,.04,.30), glass,.01)
        box('Detail_GarageHandle', (x,-5.13,1.12), (.38,.07,.08), dark,.012)
    # Two formal planters flank the approach without occupying the carry path.
    for x in (-2.65,2.65):
        box('Detail_EntrancePlanter', (x,-8,.32), (.95,.95,.64), stone)
        bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=2, radius=.68, location=(x,-8,1.12))
        shrub = bpy.context.object
        shrub.name = 'Detail_Shrub_Formal'
        shrub.scale = (.8,.8,1.1)
        shrub.data.materials.append(palette['MAT_Leaf_Green'])
        shrub.parent = reference
    anchor = bpy.data.objects.new('FrontDoorAnchor', None)
    bpy.context.collection.objects.link(anchor)
    anchor.parent = reference
    anchor.location = (0,-6.1,.55)
    reference['footprint_area_relative_to_medium'] = 2.001
    reference['delivery_tier'] = 'Hard'
    bpy.ops.object.select_all(action='DESELECT')
    reference.select_set(True)
    for obj in reference.children_recursive:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = reference
    bpy.context.preferences.filepaths.save_version = 0
    # Keep the editable source out of Unity's native .blend importer; FBX is authoritative.
    source = project + '/Tools/Blender/Sources/'
    bpy.ops.wm.save_as_mainfile(filepath=source + 'CouchGuys_House_03.blend')
    bpy.ops.export_scene.fbx(filepath=output + 'CouchGuys_House_03.fbx', use_selection=True,
        object_types={'MESH','EMPTY'}, apply_unit_scale=True, axis_forward='-Z', axis_up='Y',
        use_mesh_modifiers=True, bake_anim=False, add_leaf_bones=False)
    print('HOUSE03_BUILD_OK',len(reference.children_recursive),'objects')
    return reference


if __name__ == '__main__':
    from pathlib import Path
    if bpy.app.background:
        bpy.ops.object.select_all(action='SELECT')
        bpy.ops.object.delete(use_global=False)
    build_mansion(Path(__file__).resolve().parents[2].as_posix())
