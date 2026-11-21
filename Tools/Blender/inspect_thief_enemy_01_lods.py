from pathlib import Path
import bpy,json
ROOT=Path(__file__).resolve().parents[2]
report={}
for name,path in [('new','ArtSource/Characters/ThiefEnemy01/thief_enemy_01.blend'),('base','ArtSource/Characters/Character01Thief/Character01_Thief.blend')]+[(v,f'ArtSource/Characters/Character01Thief/Armed/Character01_Thief_{v}.blend') for v in ['Pistol','AssaultRifle','Shotgun']]:
    bpy.ops.wm.open_mainfile(filepath=str(ROOT/path))
    rig=next(o for o in bpy.data.objects if o.type=='ARMATURE')
    report[name]={'rig':rig.name,'matrix':[list(row) for row in rig.matrix_world],'fps':bpy.context.scene.render.fps,'bones':{b.name:{'parent':b.parent.name if b.parent else None,'head':list(b.head_local),'tail':list(b.tail_local),'matrix':[list(row) for row in b.matrix_local]} for b in rig.data.bones},'actions':{a.name:{'range':list(a.frame_range),'slots':[s.identifier for s in a.slots]} for a in bpy.data.actions},'mesh':[(o.name,list(o.dimensions)) for o in bpy.data.objects if o.type=='MESH']}
(ROOT/'ArtSource/Characters/ThiefEnemy01/AnimationLODSourceInventory.json').write_text(json.dumps(report,indent=2))
print('SOURCE_INVENTORY',[(k,v['rig'],v['fps'],list(v['actions'])) for k,v in report.items()])
