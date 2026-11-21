from pathlib import Path
import bpy
ROOT=Path(__file__).resolve().parents[2]
bpy.ops.wm.open_mainfile(filepath=str(ROOT/'ArtSource/Characters/Character01Thief/Armed/Character01_Thief_Pistol.blend'))
r=bpy.data.objects['Character01_Thief_Rig']
print('OBJ',r.matrix_world,'SCALE',r.scale)
for a in bpy.data.actions:
 r.animation_data.action=a;r.animation_data.action_slot=a.slots[0]
 for f in [1,12,int(a.frame_range[1])]:
  bpy.context.scene.frame_set(f)
  print(a.name,f,'objloc',list(r.location),'hip_local',list(r.pose.bones['mixamorig:Hips'].matrix.translation),'hip_world',list((r.matrix_world@r.pose.bones['mixamorig:Hips'].matrix).translation),'hip_location',list(r.pose.bones['mixamorig:Hips'].location))
