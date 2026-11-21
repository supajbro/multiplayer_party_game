from pathlib import Path
import bpy,math
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[2];OUT=ROOT/'ArtSource/Characters/ThiefEnemy01/ExpressivePoses'
bpy.ops.wm.open_mainfile(filepath=str(OUT/'thief_enemy_01.blend'))
rig=bpy.data.objects['thief_enemy_01_Rig'];scene=bpy.context.scene
scene.render.engine='CYCLES';scene.cycles.device='CPU';scene.cycles.samples=16
scene.render.resolution_x=600;scene.render.resolution_y=600;scene.render.resolution_percentage=100
scene.world.color=(.25,.25,.25);scene.view_settings.view_transform='Standard'
for name,loc,power in [('Key',(3,-4,5),450),('Fill',(-4,-2,3),300),('Rim',(1,4,4),400)]:
    d=bpy.data.lights.new(name,'AREA');d.energy=power;d.shape='DISK';d.size=4
    o=bpy.data.objects.new(name,d);scene.collection.objects.link(o);o.location=loc;o.rotation_euler=(Vector((0,0,.85))-o.location).to_track_quat('-Z','Y').to_euler()
d=bpy.data.cameras.new('QA');cam=bpy.data.objects.new('QA',d);scene.collection.objects.link(cam);scene.camera=cam
d.type='ORTHO';d.ortho_scale=2.1
for variant in ['Unarmed','Pistol','AssaultRifle','Shotgun','Speaker']:
    for kind,frame,angle in [('Idle',12,0),('Idle',12,35),('Walk',6,35),('Frustration',12,35)]:
        a=bpy.data.actions[variant+'_Thief_'+kind];rig.animation_data.action=a;rig.animation_data.action_slot=a.slots[0];scene.frame_set(frame)
        r=math.radians(angle);cam.location=(4*math.sin(r),-4*math.cos(r),1.25)
        cam.rotation_euler=(Vector((0,0,.83))-cam.location).to_track_quat('-Z','Y').to_euler()
        scene.render.filepath=str(OUT/(variant+'_'+kind+'_'+str(angle)+'.png'));bpy.ops.render.render(write_still=True)
print('EXPRESSIVE_PREVIEWS_COMPLETE')
