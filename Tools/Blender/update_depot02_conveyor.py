"""Replace only depot_02's conveyor meshes. Preserve all other authored Blender objects."""
import bpy, math, ast, bmesh, sys
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'Tools/Blender'))
from depot02_conveyor import build_conveyor
folder=ROOT/'Assets/_Project/Models/depot_02'
bpy.ops.wm.open_mainfile(filepath=str(folder/'depot_02.blend'),load_ui=False)
s=bpy.context.scene;s.frame_set(1)
collection=bpy.data.collections['Depot02_Conveyor']
for o in list(collection.objects):bpy.data.objects.remove(o,do_unlink=True)
source=ast.parse((ROOT/'Tools/Blender/build_depot_02.py').read_text())
helpers=ast.Module(body=[n for n in source.body if isinstance(n,ast.FunctionDef) and n.name in {'own','mesh','box','cyl','tube','arch'}],type_ignores=[])
e={'bpy':bpy,'math':math,'Vector':Vector,'ROOT':ROOT,'C':{'Conveyor':collection},'M':{m.name[4:]:m for m in bpy.data.materials if m.name.startswith('D02_')}}
exec(compile(helpers,'depot02_geometry_helpers','exec'),e)
build_conveyor(e)
for o in list(collection.objects):
    if o.type=='CURVE':
        bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o;bpy.ops.object.convert(target='MESH')
parts=[o for o in collection.objects if o.type=='MESH' and not o.name.startswith('D02_Collider_')]
bpy.ops.object.select_all(action='DESELECT')
for o in parts:o.select_set(True)
bpy.context.view_layer.objects.active=parts[0];bpy.ops.object.join();parts[0].name='D02_Module_Conveyor'
for o in collection.objects:
    if o.type=='MESH':
        bm=bmesh.new();bm.from_mesh(o.data);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(o.data);bm.free()
bpy.ops.wm.save_as_mainfile(filepath=str(folder/'depot_02.blend'))
bpy.ops.object.select_all(action='DESELECT')
for o in s.objects:
    if o.type=='MESH':o.select_set(True)
bpy.ops.export_scene.fbx(filepath=str(folder/'depot_02.fbx'),use_selection=True,object_types={'MESH'},use_mesh_modifiers=True,add_leaf_bones=False,axis_forward='-Z',axis_up='Y',bake_anim=True,bake_anim_use_all_bones=False,bake_anim_use_nla_strips=False,bake_anim_use_all_actions=False,bake_anim_step=2)
cam=s.camera;cam.location=(6.5,4.5,6.5);target=Vector((11.7,-1.3,1));cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.type='ORTHO';cam.data.ortho_scale=16
s.render.resolution_x=1200;s.render.resolution_y=850;s.cycles.samples=24
s.render.filepath=str(ROOT/'ArtSource/Depot02/depot_02_conveyor.png');bpy.ops.render.render(write_still=True)
print('CURVED_CONVEYOR_COMPLETE')
