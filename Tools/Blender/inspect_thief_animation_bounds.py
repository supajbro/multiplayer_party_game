from pathlib import Path
import bpy,json
ROOT=Path(__file__).resolve().parents[2]
bpy.ops.wm.open_mainfile(filepath=str(ROOT/'ArtSource/Characters/ThiefEnemy01/thief_enemy_01.blend'))
rig=bpy.data.objects['thief_enemy_01_Rig'];obj=bpy.data.objects['thief_enemy_01'];obj.hide_set(False)
for a in sorted(bpy.data.actions,key=lambda a:a.name):
 rig.animation_data.action=a;rig.animation_data.action_slot=a.slots[0]
 bpy.context.scene.frame_set(int(a.frame_range[1] if 'Death' in a.name else 12))
 ev=obj.evaluated_get(bpy.context.evaluated_depsgraph_get());m=ev.to_mesh()
 print(a.name, 'hips',list(rig.pose.bones['Hips'].matrix.translation),'bounds',[(min(v.co[i] for v in m.vertices),max(v.co[i] for v in m.vertices)) for i in range(3)])
 ev.to_mesh_clear()
