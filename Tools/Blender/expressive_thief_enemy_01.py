"""Author clearer arm acting on the existing saved rig, meshes and clip timings."""
from pathlib import Path
import bpy,math,json,shutil
import numpy as np
from mathutils import Vector,Matrix,Quaternion
ROOT=Path(__file__).resolve().parents[2]
SRC=ROOT/'ArtSource/Characters/ThiefEnemy01'
OUT=SRC/'ExpressivePoses';OUT.mkdir(exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(SRC/'thief_enemy_01.blend'))
rig=bpy.data.objects['thief_enemy_01_Rig'];scene=bpy.context.scene;scene.render.fps=30
meshes=[bpy.data.objects['thief_enemy_01']]+[bpy.data.objects['thief_enemy_01_LOD'+str(i)] for i in range(1,4)]
baseline={}
for action in list(bpy.data.actions):
    rig.animation_data.action=action;rig.animation_data.action_slot=action.slots[0]
    first,last=map(int,action.frame_range)
    baseline[action.name]=(first,last,{f:None for f in range(first,last+1)})
    for frame in range(first,last+1):
        scene.frame_set(frame)
        baseline[action.name][2][frame]={p.name:p.matrix_basis.copy() for p in rig.pose.bones}
rig.animation_data.action=None
for action in list(bpy.data.actions):bpy.data.actions.remove(action)

def aim_bone(p,head,direction):
    q=p.matrix.to_quaternion()
    q=(q@Vector((0,1,0))).rotation_difference(direction.normalized())@q
    p.matrix=Matrix.LocRotScale(head,q,Vector((1,1,1)))
    bpy.context.view_layer.update()

def solve_arm(side,wrist,hand_rotation):
    upper=rig.pose.bones['UpperArm.'+side];fore=rig.pose.bones['Forearm.'+side];hand=rig.pose.bones['Hand.'+side]
    shoulder=upper.head.copy();a=upper.bone.length;b=fore.bone.length
    delta=wrist-shoulder;distance=max(.025,min(delta.length,a+b-.004));axis=delta.normalized()
    wrist=shoulder+axis*distance
    pole=Vector((.58 if side=='L' else -.58,.02,.88))
    bend=pole-shoulder;bend-=axis*bend.dot(axis);bend.normalize()
    along=(a*a-b*b+distance*distance)/(2*distance)
    elbow=shoulder+axis*along+bend*math.sqrt(max(0,a*a-along*along))
    aim_bone(upper,shoulder,elbow-shoulder)
    aim_bone(fore,fore.head.copy(),wrist-elbow)
    hand.matrix=Matrix.LocRotScale(hand.head.copy(),hand_rotation,Vector((1,1,1)))
    bpy.context.view_layer.update()

def relaxed_hand(side,direction,up=False):
    q=rig.data.bones['Hand.'+side].matrix_local.to_quaternion()
    q=(q@Vector((0,1,0))).rotation_difference(Vector(direction).normalized())@q
    if up:q=Quaternion(Vector(direction).normalized(),math.pi)@q
    return q

def world_turn(name,rotation):
    p=rig.pose.bones[name]
    p.matrix=Matrix.LocRotScale(p.head.copy(),rotation@p.matrix.to_quaternion(),Vector((1,1,1)))
    bpy.context.view_layer.update()

group_indices={}
for side in ['L','R']:
    index=meshes[0].vertex_groups['Hand.'+side].index
    group_indices[side]=[v.index for v in meshes[0].data.vertices if any(g.group==index and g.weight>.65 for g in v.groups)]
report={}
for variant in ['Pistol','AssaultRifle','Shotgun','Unarmed','Speaker']:
    for kind in ['Idle','Walk','Shoot','Death','Frustration']:
        source=('Pistol' if variant in ['Unarmed','Speaker'] else variant)+'_Thief_'+kind
        first,last,poses=baseline[source]
        action=bpy.data.actions.new(variant+'_Thief_'+kind);action.use_fake_user=True
        rig.animation_data.action=action
        minimum=100.;wrist_minimum=100.;prior={}
        for frame in range(first,last+1):
            scene.frame_set(frame)
            for p in rig.pose.bones:
                p.rotation_mode='QUATERNION';p.matrix_basis=poses[frame][p.name]
            bpy.context.view_layer.update()
            phase=2*math.pi*(frame-first)/(last-first)
            wave=math.sin(phase);breath=math.sin(phase)*.004
            original_hand_q={s:rig.pose.bones['Hand.'+s].matrix.to_quaternion().copy() for s in ['L','R']}
            if kind!='Death':
                if kind in ['Idle','Walk']:
                    world_turn('Chest',Quaternion((0,0,1),math.radians((3 if kind=='Idle' else 4)*wave))@Quaternion((0,1,0),math.radians(1.4*wave)))
                    world_turn('Head',Quaternion((0,0,1),math.radians(5*wave))@Quaternion((1,0,0),math.radians(2*wave)))
                hip=rig.pose.bones['Hips'].head-Vector((0,0,.64))
                bob=Vector((hip.x,hip.y,hip.z+breath))
                left=Vector((.28,-.075,.735));right=Vector((-.25,-.06,.765))
                lq=relaxed_hand('L',(.015,-.035,-.18));rq=relaxed_hand('R',(-.015,-.035,-.18))
                if kind=='Walk':
                    left.y+=.115*wave;right.y-=.115*wave
                    left.z+=.015*wave;right.z-=.015*wave
                if variant in ['Pistol','AssaultRifle','Shotgun']:
                    right=Vector((-.24,-.255,.94))
                    rq=original_hand_q['R']
                    if variant!='Pistol':
                        right=Vector((-.23,-.18,.935))
                        left=Vector((.045,-.34 if variant=='AssaultRifle' else -.355,.915))
                        lq=relaxed_hand('L',(-.07,-.17,.015),up=True)
                    if kind=='Shoot':
                        recoil=max(0,math.sin(math.pi*(frame-first)/(last-first)))
                        right.y+=.025*recoil;right.z+=.025*recoil
                        if variant!='Pistol':left.y+=.018*recoil;left.z+=.015*recoil
                elif variant=='Speaker':
                    right=Vector((-.385,-.015,1.12))
                    rq=relaxed_hand('R',(-.17,-.015,.035),up=True)
                if kind=='Frustration':
                    t=(frame-first)/(last-first)
                    amount=math.sin(math.pi*t)**.7
                    left=left.lerp(Vector((.425,-.16,1.30)),amount)
                    lq=lq.slerp(original_hand_q['L'],amount)
                solve_arm('R',right+bob,rq)
                solve_arm('L',left+bob,lq)
                wrist_minimum=min(wrist_minimum,(rig.pose.bones['Hand.L'].head-rig.pose.bones['Hand.R'].head).length)
                ev=meshes[0].evaluated_get(bpy.context.evaluated_depsgraph_get());mesh=ev.to_mesh()
                points={s:np.array([tuple(mesh.vertices[i].co) for i in group_indices[s]]) for s in ['L','R']}
                clearance=float(np.sqrt(((points['L'][:,None,:]-points['R'][None,:,:])**2).sum(axis=2)).min())
                minimum=min(minimum,clearance);ev.to_mesh_clear()
            for p in rig.pose.bones:
                if p.name in prior and p.rotation_quaternion.dot(prior[p.name])<0:p.rotation_quaternion.negate()
                prior[p.name]=p.rotation_quaternion.copy()
                p.keyframe_insert('rotation_quaternion',frame=frame,group=p.name)
                p.keyframe_insert('location',frame=frame,group=p.name)
        for layer in action.layers:
            for strip in layer.strips:
                for bag in strip.channelbags:
                    for curve in bag.fcurves:
                        for key in curve.keyframe_points:key.interpolation='LINEAR'
        report[action.name]={'frames':[first,last],'min_hand_surface_distance_m':minimum if kind!='Death' else None,'min_wrist_distance_m':wrist_minimum if kind!='Death' else None}
        print('AUTHORED',action.name,report[action.name],flush=True)

rig.animation_data.action=None
for p in rig.pose.bones:p.matrix_basis.identity()
scene.frame_set(1);bpy.context.view_layer.update()
bpy.context.preferences.filepaths.save_version=0
for i,o in enumerate(meshes):o.hide_set(i!=0);o.hide_render=i!=0
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'thief_enemy_01.blend'))
bpy.ops.object.select_all(action='DESELECT');rig.select_set(True)
for o in meshes:o.hide_set(False);o.select_set(True)
bpy.context.view_layer.objects.active=rig
bpy.ops.export_scene.fbx(filepath=str(OUT/'thief_enemy_01.fbx'),use_selection=True,object_types={'ARMATURE','MESH'},axis_forward='-Z',axis_up='Y',add_leaf_bones=False,bake_anim=True,bake_anim_use_all_actions=True,bake_anim_use_nla_strips=False,bake_anim_simplify_factor=0,path_mode='AUTO',use_mesh_modifiers=True)
(OUT/'ArmClearanceValidation.json').write_text(json.dumps(report,indent=2))
print('EXPRESSIVE_THIEF_EXPORT_COMPLETE')
