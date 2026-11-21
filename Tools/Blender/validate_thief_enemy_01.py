"""Reimport the shipped FBX and check its rest pose and skinning."""
from pathlib import Path
import bpy,json,math
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[2]
SRC=ROOT/'ArtSource/Characters/ThiefEnemy01'
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.fbx(filepath=str(ROOT/'Assets/_Project/Models/Thieves/thief_enemy_01.fbx'))
obj=next(o for o in bpy.context.scene.objects if o.type=='MESH')
rig=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
bpy.context.view_layer.update()
report={'triangles':sum(len(p.vertices)-2 for p in obj.data.polygons),'dimensions_m':list(obj.dimensions),'bones':len(rig.data.bones),'uv_layers':len(obj.data.uv_layers),'unweighted_vertices':sum(not any(g.weight>1e-5 for g in v.groups) for v in obj.data.vertices),'rest_pose':all(abs(b.matrix_basis.to_quaternion().angle)<.0001 for b in rig.pose.bones)}
assert report['triangles']==20000 and report['unweighted_vertices']==0 and report['rest_pose']
assert abs(report['dimensions_m'][2]-1.65)<.001
(SRC/'FBXRoundtripValidation.json').write_text(json.dumps(report,indent=2))
# Render a posed copy of the packed master; no pose is saved into the deliverable.
bpy.ops.wm.open_mainfile(filepath=str(SRC/'thief_enemy_01.blend'))
rig=bpy.data.objects['thief_enemy_01_Rig']
for side,sign in [('L',1),('R',-1)]:
    b=rig.pose.bones['UpperArm.'+side];b.rotation_mode='XYZ';b.rotation_euler[2]=math.radians(-sign*35)
rig.pose.bones['Shin.L'].rotation_mode='XYZ';rig.pose.bones['Shin.L'].rotation_euler[0]=math.radians(35)
scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.device='CPU';scene.cycles.samples=20
scene.render.resolution_x=700;scene.render.resolution_y=700;scene.render.resolution_percentage=100
scene.world.color=(.25,.25,.25);scene.view_settings.view_transform='Standard'
for name,loc,power in [('Key',(3,-4,5),450),('Fill',(-4,-2,3),300),('Rim',(1,4,4),400)]:
    d=bpy.data.lights.new(name,'AREA');d.energy=power;d.shape='DISK';d.size=4
    o=bpy.data.objects.new(name,d);scene.collection.objects.link(o);o.location=loc;o.rotation_euler=(Vector((0,0,.85))-o.location).to_track_quat('-Z','Y').to_euler()
d=bpy.data.cameras.new('QA');cam=bpy.data.objects.new('QA',d);scene.collection.objects.link(cam);scene.camera=cam
d.type='ORTHO';d.ortho_scale=2.1;cam.location=(2,-4,1.4);cam.rotation_euler=(Vector((0,0,.85))-cam.location).to_track_quat('-Z','Y').to_euler()
scene.render.filepath=str(SRC/'thief_enemy_01_deformation.png');bpy.ops.render.render(write_still=True)
print('ROUNDTRIP_PASS',json.dumps(report))
