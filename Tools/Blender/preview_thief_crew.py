"""Render the staged reskin on the unchanged original rig and four LOD meshes."""
from pathlib import Path
import bpy, math, sys
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[2];SRC=ROOT/'ArtSource/Characters/ThiefEnemy01';OUT=SRC/'GetawayCrew'
bpy.ops.wm.open_mainfile(filepath=str(SRC/'thief_enemy_01.blend'))
rig=bpy.data.objects['thief_enemy_01_Rig'];lods=[bpy.data.objects['thief_enemy_01'+('_LOD'+str(i) if i else '')] for i in range(4)]
mat=lods[0].data.materials[0];bsdf=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
tex=mat.node_tree.nodes.new('ShaderNodeTexImage');tex.image=bpy.data.images.load(str(OUT/'thief_enemy_01_BaseColor.png'));mat.node_tree.links.new(tex.outputs['Color'],bsdf.inputs['Base Color'])
normal=next(n for n in mat.node_tree.nodes if n.type=='NORMAL_MAP')
nt=mat.node_tree.nodes.new('ShaderNodeTexImage');nt.image=bpy.data.images.load(str(OUT/'thief_enemy_01_Normal.png'));nt.image.colorspace_settings.name='Non-Color';mat.node_tree.links.new(nt.outputs['Color'],normal.inputs['Color'])
for name,value in [('Roughness',.65),('Metallic',.05)]:
    for link in list(bsdf.inputs[name].links):mat.node_tree.links.remove(link)
    bsdf.inputs[name].default_value=value
scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.device='CPU';scene.cycles.samples=24
scene.render.resolution_x=800;scene.render.resolution_y=800;scene.render.resolution_percentage=100
scene.world.color=(.30,.30,.30);scene.view_settings.view_transform='Standard'
for name,loc,power in [('Key',(3,-4,5),450),('Fill',(-4,-2,3),300),('Rim',(1,4,4),400)]:
    d=bpy.data.lights.new(name,'AREA');d.energy=power;d.shape='DISK';d.size=4
    o=bpy.data.objects.new(name,d);scene.collection.objects.link(o);o.location=loc;o.rotation_euler=(Vector((0,0,.85))-o.location).to_track_quat('-Z','Y').to_euler()
d=bpy.data.cameras.new('QA');cam=bpy.data.objects.new('QA',d);scene.collection.objects.link(cam);scene.camera=cam;d.type='ORTHO';d.ortho_scale=2.05
def view(label,angle=0,lod=0):
    for i,o in enumerate(lods):o.hide_render=i!=lod;o.hide_set(i!=lod)
    a=math.radians(angle);cam.location=(4*math.sin(a),-4*math.cos(a),1.02);cam.rotation_euler=(Vector((0,0,.82))-cam.location).to_track_quat('-Z','Y').to_euler()
    scene.render.filepath=str(OUT/(label+'.png'));bpy.ops.render.render(write_still=True)
rig.animation_data.action=None
for p in rig.pose.bones:p.matrix_basis.identity()
scene.frame_set(1);view('Front');view('Back',180);view('Quarter',35)
if '--full' in sys.argv:
    for i in range(1,4):view('LOD'+str(i),0,i)
    for name in ['Unarmed_Thief_Idle','Unarmed_Thief_Walk','Pistol_Thief_Shoot','AssaultRifle_Thief_Shoot','Shotgun_Thief_Shoot','Speaker_Thief_Idle','Unarmed_Thief_Frustration','Unarmed_Thief_Death']:
        a=bpy.data.actions[name];rig.animation_data.action=a;rig.animation_data.action_slot=a.slots[0];first,last=a.frame_range;scene.frame_set(round(first+(last-first)*.5));view(name,25)
print('CREW_PREVIEW_COMPLETE')
