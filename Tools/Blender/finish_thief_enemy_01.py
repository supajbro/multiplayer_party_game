"""Skin the generated thief and export its unchanged T-pose for Unity."""
from pathlib import Path
import bpy, json, math
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[2]
SRC=ROOT/'ArtSource/Characters/ThiefEnemy01'
OUT=ROOT/'Assets/_Project/Models/Thieves'
bpy.ops.wm.open_mainfile(filepath=str(SRC/'thief_enemy_01.blend'))
obj=bpy.data.objects['thief_enemy_01']
obj.data.materials[0].name='thief_enemy_01'
bpy.context.view_layer.update()
print('DIMENSIONS',list(obj.dimensions))
bpy.ops.object.select_all(action='DESELECT')
data=bpy.data.armatures.new('thief_enemy_01_skeleton')
rig=bpy.data.objects.new('thief_enemy_01_Rig',data)
bpy.context.collection.objects.link(rig);rig.select_set(True);bpy.context.view_layer.objects.active=rig
bpy.ops.object.mode_set(mode='EDIT')
def bone(name,head,tail,parent=None,deform=True):
    b=data.edit_bones.new(name);b.head=head;b.tail=tail;b.use_deform=deform
    if parent:b.parent=data.edit_bones[parent]
bone('Root',(0,0,0),(0,0,.12),deform=False)
bone('Hips',(0,0,.64),(0,0,.73),'Root')
bone('Spine',(0,0,.73),(0,0,.85),'Hips')
bone('Chest',(0,0,.85),(0,0,1.025),'Spine')
bone('Neck',(0,0,1.025),(0,0,1.13),'Chest')
bone('Head',(0,0,1.13),(0,0,1.57),'Neck')
for suffix,s in [('L',1),('R',-1)]:
    bone('Shoulder.'+suffix,(s*.055,0,1.025),(s*.19,0,1.025),'Chest')
    bone('UpperArm.'+suffix,(s*.19,0,1.025),(s*.405,0,1.025),'Shoulder.'+suffix)
    bone('Forearm.'+suffix,(s*.405,0,1.025),(s*.62,0,1.025),'UpperArm.'+suffix)
    bone('Hand.'+suffix,(s*.62,0,1.025),(s*.82,-.025,1.025),'Forearm.'+suffix)
    bone('Thigh.'+suffix,(s*.105,0,.65),(s*.135,-.015,.40),'Hips')
    bone('Shin.'+suffix,(s*.135,-.015,.40),(s*.16,0,.15),'Thigh.'+suffix)
    bone('Foot.'+suffix,(s*.16,0,.15),(s*.16,-.19,.075),'Shin.'+suffix)
    bone('Toes.'+suffix,(s*.16,-.19,.075),(s*.16,-.26,.075),'Foot.'+suffix)
bpy.ops.object.mode_set(mode='OBJECT')
obj.select_set(True);bpy.context.view_layer.objects.active=rig
bpy.ops.object.parent_set(type='ARMATURE_AUTO')
missing=[v.index for v in obj.data.vertices if not any(g.weight>1e-5 for g in v.groups)]
if missing:raise RuntimeError('Unweighted vertices: '+str(len(missing)))
# Four normalized bone influences per vertex, suitable for Unity skinning.
bpy.context.view_layer.objects.active=obj
bpy.ops.object.mode_set(mode='WEIGHT_PAINT')
bpy.ops.object.vertex_group_limit_total(limit=4)
bpy.ops.object.vertex_group_normalize_all(lock_active=False)
bpy.ops.object.mode_set(mode='OBJECT')
report=json.loads((SRC/'GeometryValidation.json').read_text())
report.update(dimensions_m=list(obj.dimensions),bones=len(data.bones),unweighted_vertices=len(missing),max_influences=max(len(v.groups) for v in obj.data.vertices),rig_type='Generic; no animation clips or finger bones')
(SRC/'GeometryValidation.json').write_text(json.dumps(report,indent=2))
bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=str(SRC/'thief_enemy_01.blend'))
bpy.ops.object.select_all(action='DESELECT')
obj.select_set(True);rig.select_set(True);bpy.context.view_layer.objects.active=rig
bpy.ops.export_scene.fbx(filepath=str(OUT/'thief_enemy_01.fbx'),use_selection=True,object_types={'ARMATURE','MESH'},axis_forward='-Z',axis_up='Y',add_leaf_bones=False,bake_anim=False,path_mode='AUTO',use_mesh_modifiers=True)
bpy.ops.export_scene.gltf(filepath=str(SRC/'thief_enemy_01.glb'),use_selection=True,export_format='GLB')
# Save a quick posed deformation sample without changing the saved master.
for side,sign in [('L',1),('R',-1)]:
    b=rig.pose.bones['UpperArm.'+side];b.rotation_mode='XYZ';b.rotation_euler[0]=math.radians(0);b.rotation_euler[2]=math.radians(-sign*35)
rig.pose.bones['Shin.L'].rotation_mode='XYZ';rig.pose.bones['Shin.L'].rotation_euler[0]=math.radians(35)
bpy.context.view_layer.update()
deps=bpy.context.evaluated_depsgraph_get();evaluated=obj.evaluated_get(deps);mesh=evaluated.to_mesh()
report['deformation_sample_finite']=all(math.isfinite(c) for v in mesh.vertices for c in v.co)
report['deformation_sample_max_displacement_m']=max((mesh.vertices[i].co-v.co).length for i,v in enumerate(obj.data.vertices))
evaluated.to_mesh_clear()
(SRC/'GeometryValidation.json').write_text(json.dumps(report,indent=2))
print('EXPORT_COMPLETE',json.dumps(report))
