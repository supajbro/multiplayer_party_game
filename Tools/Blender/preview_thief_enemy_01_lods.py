"""Render actual LOD surfaces and retargeted actions for offline visual QA."""
from pathlib import Path
import bpy, math,json
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[2];SRC=ROOT/'ArtSource/Characters/ThiefEnemy01'
bpy.ops.wm.open_mainfile(filepath=str(SRC/'thief_enemy_01.blend'))
rig=bpy.data.objects['thief_enemy_01_Rig'];scene=bpy.context.scene
lods=[bpy.data.objects['thief_enemy_01']]+[bpy.data.objects['thief_enemy_01_LOD'+str(i)] for i in range(1,4)]
scene.render.engine='CYCLES';scene.cycles.device='CPU';scene.cycles.samples=16
scene.render.resolution_x=512;scene.render.resolution_y=512;scene.render.resolution_percentage=100
scene.world.color=(.25,.25,.25);scene.view_settings.view_transform='Standard'
for name,loc,power in [('Key',(3,-4,5),450),('Fill',(-4,-2,3),300),('Rim',(1,4,4),400)]:
    d=bpy.data.lights.new(name,'AREA');d.energy=power;d.shape='DISK';d.size=4
    o=bpy.data.objects.new(name,d);scene.collection.objects.link(o);o.location=loc;o.rotation_euler=(Vector((0,0,.85))-o.location).to_track_quat('-Z','Y').to_euler()
d=bpy.data.cameras.new('QA');cam=bpy.data.objects.new('QA',d);scene.collection.objects.link(cam);scene.camera=cam
d.type='ORTHO';d.ortho_scale=2.1
previews=SRC/'LODAnimationPreviews';previews.mkdir(exist_ok=True)
def view(lod,label,angle=0):
    for i,o in enumerate(lods):o.hide_render=i!=lod;o.hide_set(i!=lod)
    a=math.radians(angle);cam.location=(4*math.sin(a),-4*math.cos(a),1.3)
    cam.rotation_euler=(Vector((0,0,.80))-cam.location).to_track_quat('-Z','Y').to_euler()
    scene.render.filepath=str(previews/(label+'.png'));bpy.ops.render.render(write_still=True)
rig.animation_data.action=None
for p in rig.pose.bones:p.matrix_basis.identity()
for i in range(4):view(i,'LOD'+str(i)+'_Tpose')
view(0,'LOD0_Back',180)
for a in sorted(bpy.data.actions,key=lambda a:a.name):
    rig.animation_data.action=a;rig.animation_data.action_slot=a.slots[0]
    start,end=a.frame_range
    scene.frame_set(int(end if 'Death' in a.name else 12 if 'Frustration' in a.name else (start+end)/2))
    view(0,a.name,30)
    if a.name=='Pistol_Thief_Walk':
        for i in range(1,4):view(i,'LOD'+str(i)+'_Walk',30)
print('VISUAL_QA_RENDERS_COMPLETE')
