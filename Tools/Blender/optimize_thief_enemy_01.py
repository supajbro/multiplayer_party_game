"""Explicit LOD and animation authoring; never executed by Unity asset hooks."""
from pathlib import Path
import bpy, json, math
from mathutils import Vector, Matrix

ROOT=Path(__file__).resolve().parents[2]
SRC=ROOT/'ArtSource/Characters/ThiefEnemy01'
OUT=ROOT/'Assets/_Project/Models/Thieves'
MASTER=SRC/'thief_enemy_01.blend'
BACKUP=SRC/'thief_enemy_01_20000_source.blend'
if not BACKUP.exists():
    import shutil
    shutil.copy2(MASTER,BACKUP)
bpy.ops.wm.open_mainfile(filepath=str(BACKUP))
rig=bpy.data.objects['thief_enemy_01_Rig']
original=bpy.data.objects['thief_enemy_01']
rig.animation_data_clear()
for b in rig.pose.bones:b.matrix_basis.identity()
scene=bpy.context.scene;scene.render.fps=30
lods=[];reports=[]
for i,budget in enumerate([10000,4000,1500,700]):
    obj=original.copy();obj.data=original.data.copy();bpy.context.collection.objects.link(obj)
    obj.name='thief_enemy_01_LOD'+str(i)
    bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj
    mod=obj.modifiers.new('Offline triangle reduction','DECIMATE');mod.ratio=budget/20000;mod.use_collapse_triangulate=True
    # Apply before skin deformation, retaining UVs and interpolated skin weights.
    while obj.modifiers.find(mod.name)>0:bpy.ops.object.modifier_move_up(modifier=mod.name)
    bpy.ops.object.modifier_apply(modifier=mod.name)
    bpy.ops.object.mode_set(mode='WEIGHT_PAINT')
    bpy.ops.object.vertex_group_limit_total(limit=4 if i<2 else 2)
    bpy.ops.object.vertex_group_normalize_all(lock_active=False)
    bpy.ops.object.mode_set(mode='OBJECT')
    tri=sum(len(p.vertices)-2 for p in obj.data.polygons)
    assert tri<=budget and all(any(g.weight>1e-5 for g in v.groups) for v in obj.data.vertices)
    assert len(obj.data.uv_layers)==1
    reports.append({'lod':i,'triangles':tri,'vertices':len(obj.data.vertices),'max_influences':max(sum(g.weight>1e-5 for g in v.groups) for v in obj.data.vertices)})
    lods.append(obj)
bpy.data.objects.remove(original,do_unlink=True)
lods[0].name='thief_enemy_01'

mapping={'Hips':'Hips','Spine':'Spine','Chest':'Spine2','Neck':'Neck','Head':'Head'}
for s,side in [('L','Left'),('R','Right')]:
    for dst,src in [('Shoulder','Shoulder'),('UpperArm','Arm'),('Forearm','ForeArm'),('Hand','Hand'),('Thigh','UpLeg'),('Shin','Leg'),('Foot','Foot'),('Toes','ToeBase')]:
        mapping[dst+'.'+s]=side+src
mapping={k:'mixamorig:'+v for k,v in mapping.items()}
animation_report={}
rig.animation_data_create()
for variant in ['Pistol','AssaultRifle','Shotgun']:
    path=ROOT/f'ArtSource/Characters/Character01Thief/Armed/Character01_Thief_{variant}.blend'
    with bpy.data.libraries.load(str(path),link=False) as (a,b):
        b.objects=['Character01_Thief_Rig'];b.actions=list(a.actions)
    source=b.objects[0];bpy.context.collection.objects.link(source)
    source.name='Source_'+variant
    bpy.context.view_layer.update() # Appended object's matrix_world is stale until evaluated.
    srcactions=list(b.actions)
    world=rig.matrix_world.inverted()@source.matrix_world
    rest={name:(world@source.data.bones[src].matrix_local) for name,src in mapping.items()}
    hip_rest=rest['Hips'].translation.copy()
    hip_scale=rig.data.bones['Hips'].head_local.z/hip_rest.z
    for action in srcactions:
        clip=action.name.split('.')[0]
        action.name='Source_'+variant+'_'+clip
        source.animation_data_create();source.animation_data.action=action
        source.animation_data.action_slot=action.slots[0]
        target=bpy.data.actions.new(variant+'_'+clip);target.use_fake_user=True
        rig.animation_data.action=target
        for p in rig.pose.bones:p.rotation_mode='QUATERNION';p.matrix_basis.identity()
        first,last=map(int,action.frame_range)
        samples=[]
        for frame in range(first,last+1):
            scene.frame_set(frame)
            for name,src in mapping.items():
                p=rig.pose.bones[name]
                srcpose=world@source.pose.bones[src].matrix
                delta=srcpose.to_quaternion()@rest[name].to_quaternion().inverted()
                rotation=delta@p.bone.matrix_local.to_quaternion()
                if p.parent:
                    position=p.parent.matrix@p.parent.bone.matrix_local.inverted()@p.bone.head_local
                else:position=p.bone.head_local.copy()
                if name=='Hips':position+=(srcpose.translation-hip_rest)*hip_scale
                p.matrix=Matrix.LocRotScale(position,rotation,Vector((1,1,1)))
                bpy.context.view_layer.update()
            # Keep the retargeted smaller character on the floor, including its death pose.
            deps=bpy.context.evaluated_depsgraph_get();evaluated=lods[0].evaluated_get(deps);mesh=evaluated.to_mesh()
            lowest=min(v.co.z for v in mesh.vertices);evaluated.to_mesh_clear()
            assert math.isfinite(lowest) and max(abs(c) for c in rig.pose.bones['Hips'].matrix.translation)<2, (variant,clip,frame)
            hips=rig.pose.bones['Hips'];matrix=hips.matrix.copy();matrix.translation.z-=lowest;hips.matrix=matrix
            bpy.context.view_layer.update()
            for p in rig.pose.bones:
                p.keyframe_insert('rotation_quaternion',frame=frame,group=p.name)
                p.keyframe_insert('location',frame=frame,group=p.name)
            if frame in [first,(first+last)//2,last]:samples.append({'frame':frame,'floor_correction_m':-lowest})
        for layer in target.layers:
            for strip in layer.strips:
                for bag in strip.channelbags:
                    for curve in bag.fcurves:
                        for key in curve.keyframe_points:key.interpolation='LINEAR'
        animation_report[target.name]={'frames':[first,last],'fps':30,'source':str(path.relative_to(ROOT)),'source_action':clip,'samples':samples}
        print('RETARGETED',target.name,flush=True)
        rig.animation_data.action=None
        for p in rig.pose.bones:p.matrix_basis.identity()
    bpy.data.objects.remove(source,do_unlink=True)
    for action in srcactions:bpy.data.actions.remove(action)

rig.animation_data.action=None
for p in rig.pose.bones:p.matrix_basis.identity()
scene.frame_set(1);bpy.context.view_layer.update()
for i,o in enumerate(lods):o.hide_render=i!=0;o.hide_set(i!=0)
scene.frame_start=1;scene.frame_end=52
bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=str(MASTER))
bpy.ops.object.select_all(action='DESELECT')
rig.select_set(True)
for o in lods:o.hide_set(False);o.select_set(True)
bpy.context.view_layer.objects.active=rig
bpy.ops.export_scene.fbx(filepath=str(OUT/'thief_enemy_01.fbx'),use_selection=True,object_types={'ARMATURE','MESH'},axis_forward='-Z',axis_up='Y',add_leaf_bones=False,bake_anim=True,bake_anim_use_all_actions=True,bake_anim_use_nla_strips=False,bake_anim_simplify_factor=0,path_mode='AUTO',use_mesh_modifiers=True)
report={'lods':reports,'animations':animation_report,'bone_count':len(rig.data.bones),'method':'Blender collapse decimation; global-rest-rotation retarget with target bone lengths and per-frame floor contact','source_master':str(BACKUP.relative_to(ROOT))}
(SRC/'LODAnimationValidation.json').write_text(json.dumps(report,indent=2))
print('LOD_ANIMATION_EXPORT_COMPLETE',json.dumps(reports),flush=True)
