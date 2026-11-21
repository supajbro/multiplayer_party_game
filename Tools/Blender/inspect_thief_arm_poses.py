from pathlib import Path
import bpy,json
ROOT=Path(__file__).resolve().parents[2]
bpy.ops.wm.open_mainfile(filepath=str(ROOT/'ArtSource/Characters/ThiefEnemy01/thief_enemy_01.blend'))
rig=bpy.data.objects['thief_enemy_01_Rig'];report={}
for a in sorted(bpy.data.actions,key=lambda a:a.name):
    if 'Death' in a.name:continue
    rig.animation_data.action=a;rig.animation_data.action_slot=a.slots[0]
    bpy.context.scene.frame_set(12)
    report[a.name]={n:{'head':list(rig.pose.bones[n].head),'tail':list(rig.pose.bones[n].tail),'q':list(rig.pose.bones[n].matrix.to_quaternion())} for n in ['UpperArm.L','Forearm.L','Hand.L','UpperArm.R','Forearm.R','Hand.R','Chest','Head']}
(ROOT/'ArtSource/Characters/ThiefEnemy01/ArmPoseInspection.json').write_text(json.dumps(report,indent=2))
print('ARM_INSPECTION',[(n,{k:[round(c,3) for c in v['head']] for k,v in x.items() if k.startswith('Hand')}) for n,x in report.items()])
