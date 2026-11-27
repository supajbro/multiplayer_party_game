"""Explicit offline Blender authoring. Keeps the existing Wheelie model and AI rig intact.

Run with Blender's bpy Python runtime. Writes a separate editable player animation
source and animation-only FBX; never changes the regular player's Blender file.
"""
import bpy, math, json, re
from pathlib import Path
from mathutils import Vector, Matrix, Quaternion

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Assets/_Project/Animations/Wheelie'
SOURCE = ROOT / 'ArtSource/Wheelie'
OUT.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(ROOT / 'Assets/_Project/Models/CouchGuy/CouchGuy_Redesigned.blend'))
scene = bpy.context.scene
scene.render.fps = 30
source = bpy.data.objects['CouchGuy_Rig']
source_actions = list(bpy.data.actions)
with bpy.data.libraries.load(str(SOURCE / 'Wheelie.blend'), link=False) as (available, loaded):
    loaded.objects = [n for n in available.objects if n in ('Wheelie', 'Wheelie_Body', 'FaceDisplay')]
for obj in loaded.objects:
    if obj is not None and obj.name not in scene.collection.objects:
        scene.collection.objects.link(obj)
rig = next(o for o in loaded.objects if o is not None and o.type == 'ARMATURE')
rig.name = 'Wheelie'
meshes = [o for o in loaded.objects if o is not None and o.type == 'MESH']
rig.animation_data_create()
mirror = Matrix.Diagonal((-1, 1, 1))
mapping = {'Torso': 'Torso', 'Head': 'Head',
           'UpperArm.R': 'UpperArm_L', 'Forearm.R': 'LowerArm_L', 'Wrist.R': 'Hand_L',
           'UpperArm.L': 'UpperArm_R', 'Forearm.L': 'LowerArm_R', 'Wrist.L': 'Hand_R'}
rest = {b.name: b.matrix_local.to_quaternion() for b in rig.data.bones}
source_rest = {b.name: b.matrix_local.to_quaternion() for b in source.data.bones}
for bone in rig.pose.bones:
    bone.rotation_mode = 'QUATERNION'

def set_world_rotation(bone, rotation):
    old = bone.matrix.copy()
    bone.matrix = Matrix.LocRotScale(old.translation, rotation, Vector((1, 1, 1)))
    bpy.context.view_layer.update()

def rotate_towards(bone, child, direction):
    current = child.head - bone.head
    if current.length < 1e-6 or direction.length < 1e-6:
        return
    delta = current.rotation_difference(direction)
    set_world_rotation(bone, delta @ bone.matrix.to_quaternion())

def solve_arm(side, hand_target, pole):
    upper = rig.pose.bones['UpperArm.' + side]
    lower = rig.pose.bones['Forearm.' + side]
    wrist = rig.pose.bones['Wrist.' + side]
    start = upper.head.copy()
    a, b = (lower.head-start).length, (wrist.head-lower.head).length
    line = hand_target-start
    if line.length < 1e-6:
        return
    length = max(abs(a-b)+.001, min(line.length, a+b-.001))
    direction = line.normalized()
    bend = pole - start
    bend -= direction*bend.dot(direction)
    if bend.length < .001:
        bend = Vector((1 if side == 'R' else -1, 0, -.3))
        bend -= direction*bend.dot(direction)
    bend.normalize()
    along = (a*a + length*length-b*b)/(2*length)
    elbow = start + direction*along + bend*math.sqrt(max(0, a*a-along*along))
    rotate_towards(upper, lower, elbow-start)
    rotate_towards(lower, wrist, hand_target-lower.head)

def set_fingers(armed, two_handed):
    for side in ('R', 'L'):
        amount = .72 if armed and (side == 'R' or two_handed) else .15
        for bone in rig.pose.bones:
            if bone.name.endswith('.'+side) and bone.name.startswith(('Finger', 'Thumb')):
                bone.rotation_quaternion = Quaternion(Vector((1, 0, 0)), amount)

def retarget(action, frame):
    source.animation_data_create()
    source.animation_data.action = action
    if action.slots:
        source.animation_data.action_slot = action.slots[0]
    scene.frame_set(frame)
    for bone in rig.pose.bones:
        bone.matrix_basis.identity()
    for target_name, source_name in mapping.items():
        src = source.pose.bones[source_name]
        delta = source_rest[source_name] @ src.matrix_basis.to_quaternion() @ source_rest[source_name].inverted()
        delta = (mirror @ delta.to_matrix() @ mirror).to_quaternion()
        rig.pose.bones[target_name].rotation_quaternion = rest[target_name].inverted() @ delta @ rest[target_name]
    bpy.context.view_layer.update()
    # Re-solve both short mechanical arms against the source gesture. This retains
    # the authored elbow bend without stretching Wheelie's very different proportions.
    for side, src_side in [('R','L'), ('L','R')]:
        src_upper, src_lower, src_hand = [source.pose.bones[n+'_'+src_side] for n in ('UpperArm','LowerArm','Hand')]
        upper, lower, wrist = [rig.pose.bones[n+'.'+side] for n in ('UpperArm','Forearm','Wrist')]
        source_length = (src_lower.head-src_upper.head).length + (src_hand.head-src_lower.head).length
        ratio = ((lower.head-upper.head).length+(wrist.head-lower.head).length)/source_length
        target = upper.head + mirror @ (src_hand.head-src_upper.head)*ratio
        pole = upper.head + mirror @ (src_lower.head-src_upper.head)*ratio
        solve_arm(side,target,pole)

def weapon_pose(name, phase):
    weapon = next((w for w in ('Pistol','AssaultRifle','Shotgun') if name.startswith(w+'_')), None)
    if not weapon:
        set_fingers(False, False)
        return
    two = weapon != 'Pistol'
    suffix = name[len(weapon)+1:]
    carry = .8 if suffix=='Sprint' else .6 if suffix=='Run' else .4 if suffix in ('Walk','Backward','Strafe_L','Strafe_R','Raise') else 0
    target = Vector((.205, -.29*(1-carry)+(-.12)*carry, .69*(1-carry)+.53*carry))
    if suffix=='Shoot':
        target.y += .045*math.sin(math.pi*min(1,phase*3))
    if suffix=='Reload':
        target = Vector((.12,-.19,.62))
    solve_arm('R',target,Vector((.35,.02,.59)))
    down = Vector((0,0,-1))
    forward = Vector((0,-1,-carry*1.5)).normalized()
    wrist_rotation = down.rotation_difference(forward) @ rest['Wrist.R']
    set_world_rotation(rig.pose.bones['Wrist.R'],wrist_rotation)
    if two or suffix=='Reload':
        left_target = Vector((.11,-.36,.67)) if two else Vector((.08,-.18,.6))
        if suffix=='Reload':
            left_target.z -= .08*math.sin(math.pi*phase)**2
        solve_arm('L',left_target,Vector((-.28,-.08,.54)))
        set_world_rotation(rig.pose.bones['Wrist.L'],down.rotation_difference(Vector((0,-1,0))) @ rest['Wrist.L'])
    set_fingers(True,two or suffix=='Reload')

manifest = []
new_actions = []
for action in source_actions:
    name = action.name
    start,end = [int(v) for v in action.frame_range]
    target_action = bpy.data.actions.new('WheelieBake_' + name)
    target_action.use_fake_user = True
    rig.animation_data.action = target_action
    moving = any(k in name for k in ('Move','Walk','Run','Sprint','Backward','Strafe'))
    looping = moving or name in ('Idle','Carry_Idle','Hold_Item','Exhausted') or name.endswith(('_Idle','_Raise'))
    duration = (end-start)/30
    previous = {}
    max_angle = 0
    for frame in range(start,end+1):
        phase = (frame-start)/max(1,end-start)
        retarget(action,frame)
        weapon_pose(name,phase)
        if moving:
            turns = 3 if 'Sprint' in name else 2 if 'Run' in name else 1
            sign = -1 if 'Backward' in name else 1
            rig.pose.bones['Wheel'].rotation_quaternion = Quaternion(Vector((0,1,0)), sign*turns*2*math.pi*phase)
            rig.pose.bones['Waist'].rotation_quaternion = Quaternion(Vector((0,1,0)), (.04 if 'Sprint' in name else .025)*math.sin(phase*2*math.pi))
        # Animation never moves the collision root or places the wheel below the floor.
        for bone in rig.pose.bones:
            q=bone.rotation_quaternion
            if bone.name in previous and q.dot(previous[bone.name])<0:
                q.negate()
            previous[bone.name]=q.copy()
            bone.keyframe_insert(data_path='rotation_quaternion',frame=frame-start+1,group=bone.name)
        max_angle=max(max_angle,rig.pose.bones['UpperArm.R'].rotation_quaternion.angle)
    new_actions.append((name,target_action))
    manifest.append({'name':name,'frames':end-start+1,'duration':duration,'loop':looping,'source':'CouchGuy Blender action','right_arm_max_angle':max_angle})

extras={'Bat_Ready':(.8,True),'Bat_Swing':(.72,False),'Carry_Start':(.35,False),'Carry_Release':(.3,False)}
for path in (ROOT/'Assets/_Project/Animations/Emotes').glob('*.anim'):
    text=path.read_text()
    stop=re.search(r'm_StopTime: ([\d.]+)',text)
    extras[path.stem]=(float(stop[1]) if stop else 2, path.stem not in ('Backflip',))
for name,(duration,loop) in extras.items():
    action=bpy.data.actions.new('WheelieBake_'+name);action.use_fake_user=True
    rig.animation_data.action=action
    count=max(2,round(duration*30)+1)
    for frame in range(count):
        t=frame/max(1,count-1);phase=t*2*math.pi
        scene.frame_set(frame+1)
        for bone in rig.pose.bones:bone.matrix_basis.identity()
        if name.startswith('Bat_'):
            swing=math.sin(math.pi*min(1,t*1.5)) if name=='Bat_Swing' else 0
            solve_arm('R',Vector((.17-.34*swing,-.16-.19*swing,.78)),Vector((.33,.02,.65)))
            solve_arm('L',Vector((.1-.29*swing,-.18-.16*swing,.72)),Vector((-.28,-.02,.61)))
            rig.pose.bones['Torso'].rotation_quaternion=Quaternion(Vector((0,0,1)), .65*swing-.15)
            set_fingers(True,True)
        elif name in ('Carry_Start','Carry_Release'):
            amount=t if name=='Carry_Start' else 1-t
            for side,sign in [('R',1),('L',-1)]:
                solve_arm(side,Vector((sign*.24,-.18*amount,.5+.18*amount)),Vector((sign*.31,.02,.6)))
        elif name=='FingerGuns':
            for side,sign in [('R',1),('L',-1)]:
                solve_arm(side,Vector((sign*.22,-.27,.7+.02*math.sin(phase*2))),Vector((sign*.32,.01,.58)))
                set_world_rotation(rig.pose.bones['Wrist.'+side],Quaternion(Vector((1,0,0)),math.pi/2)@rest['Wrist.'+side])
        elif name=='Backflip':
            for side,sign in [('R',1),('L',-1)]:solve_arm(side,Vector((sign*.2,-.09,.75)),Vector((sign*.32,.02,.61)))
        else:
            amplitude=.35 if name in ('SillyDance','CelebrationWiggle') else .16
            rig.pose.bones['Torso'].rotation_quaternion=Quaternion(Vector((0,1,0)),amplitude*math.sin(phase*2))
            rig.pose.bones['Head'].rotation_quaternion=Quaternion(Vector((0,0,1)),amplitude*.6*math.sin(phase*2+.5))
            for side,sign in [('R',1),('L',-1)]:
                solve_arm(side,Vector((sign*(.24+.08*math.cos(phase)),-.08,.68+sign*.12*math.sin(phase))),Vector((sign*.34,.01,.6)))
            if name=='MoonwalkShuffle':rig.pose.bones['Wheel'].rotation_quaternion=Quaternion(Vector((0,1,0)),-4*math.pi*t)
        for bone in rig.pose.bones:bone.keyframe_insert(data_path='rotation_quaternion',frame=frame+1,group=bone.name)
    new_actions.append((name,action))
    manifest.append({'name':name,'frames':count,'duration':(count-1)/30,'loop':loop,'source':'Wheelie mechanical pose authored in Blender'})

# Keep only the player rig and the original two meshes in this independent source file.
for obj in list(bpy.data.objects):
    if obj!=rig and obj not in meshes:bpy.data.objects.remove(obj,do_unlink=True)
for action in source_actions:
    bpy.data.actions.remove(action)
for name,action in new_actions:
    action.name=name
    for layer in action.layers:
        for strip in layer.strips:
            for slot in action.slots:
                for curve in strip.channelbag(slot).fcurves:
                    for key in curve.keyframe_points:key.interpolation='LINEAR'
rig.animation_data.action=next(a for name,a in new_actions if name=='Idle')
scene.frame_start=1;scene.frame_end=96;scene.frame_set(1)
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/'Wheelie_PlayerAnimations.blend'))
for obj in bpy.context.selected_objects:obj.select_set(False)
rig.select_set(True);bpy.context.view_layer.objects.active=rig
bpy.ops.export_scene.fbx(filepath=str(OUT/'Wheelie_PlayerAnimations.fbx'),use_selection=True,
    object_types={'ARMATURE'},apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',
    axis_forward='-Z',axis_up='Y',add_leaf_bones=False,use_armature_deform_only=False,
    bake_anim=True,bake_anim_use_all_actions=True,bake_anim_use_nla_strips=False,bake_anim_force_startend_keying=True,
    bake_anim_simplify_factor=0,path_mode='AUTO')
(SOURCE/'player_animation_manifest.json').write_text(json.dumps({'fps':30,'actions':manifest,'bones':len(rig.data.bones)},indent=2))
print('WHEELIE PLAYER ANIMATIONS:',len(manifest),'actions saved to Blender and FBX.')
