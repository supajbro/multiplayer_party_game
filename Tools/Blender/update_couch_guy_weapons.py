"""Edit the EXISTING CouchGuy_Redesigned scene; never regenerate the robot.

Open Assets/_Project/Models/CouchGuy/CouchGuy_Redesigned.blend first.
Run this script in Blender, inspect the weapon actions, then call export_weapons().
Only weapon meshes and weapon actions are edited. Muzzle transforms, rest bones,
robot meshes, hover assembly, and non-weapon actions are checked for preservation.
Unity's FBX conversion puts the authored Hand_L on the player's physical right.
"""
import bpy
import bmesh
import math
from mathutils import Matrix, Vector, Euler

WEAPONS = ('Pistol', 'AssaultRifle', 'Shotgun')
# Keep geometry edits opt-in when correcting poses on the existing finished model.
REBUILD_WEAPON_GEOMETRY = False
PRIMARY_HAND = 'L'
SECONDARY_HAND = 'R'
RIG = bpy.data.objects['CouchGuy_Rig']
ROOT = bpy.data.objects['CouchGuy_Root']
BONES = ('Torso', 'Head', 'Eye_L', 'Eye_R', 'UpperArm_L', 'LowerArm_L',
         'Hand_L', 'UpperArm_R', 'LowerArm_R', 'Hand_R')


def mesh_digest(obj):
    return (
        [tuple(v.co) for v in obj.data.vertices],
        [tuple(p.vertices) for p in obj.data.polygons],
        [tuple(row) for row in obj.matrix_basis],
        [(g.name, g.index) for g in obj.vertex_groups],
    )


def is_weapon(obj):
    while obj is not None:
        if obj.name.startswith('Weapon_'):
            return True
        obj = obj.parent
    return False


PRESERVED_MESHES = {o.name: mesh_digest(o) for o in ROOT.children_recursive
                    if o.type == 'MESH' and not is_weapon(o)}
PRESERVED_BONES = {b.name: tuple(tuple(row) for row in b.matrix_local)
                   for b in RIG.data.bones}
PRESERVED_SOCKETS = {}
for weapon_name in WEAPONS:
    w = bpy.data.objects['Weapon_' + weapon_name]
    for o in [w] + [c for c in w.children if c.type == 'EMPTY']:
        PRESERVED_SOCKETS[o.name] = (o.parent.name, o.parent_type, o.parent_bone,
                                    tuple(tuple(row) for row in o.matrix_basis))
PRESERVED_ACTIONS = {a.name: a for a in bpy.data.actions
                     if not a.name.startswith(tuple(w + '_' for w in WEAPONS))}


def material(name, color, metal=0.15, glow=0):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    mat.diffuse_color = (*color, 1)
    bsdf = next(n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    bsdf.inputs['Base Color'].default_value = (*color, 1)
    bsdf.inputs['Metallic'].default_value = metal
    bsdf.inputs['Roughness'].default_value = 0.28
    emission = bsdf.inputs.get('Emission Color') or bsdf.inputs.get('Emission')
    if emission is not None:
        emission.default_value = (*color, 1)
    strength = bsdf.inputs.get('Emission Strength')
    if strength is not None:
        strength.default_value = glow
    return mat


def install_mesh(weapon, suffix, verts, faces, mat, bevel=0.015):
    name = weapon.name + '_' + suffix
    obj = bpy.data.objects.get(name)
    mesh = bpy.data.meshes.new(name + '_Mesh')
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    if bevel:
        bm = bmesh.new()
        bm.from_mesh(mesh)
        bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
        bmesh.ops.bevel(bm, geom=list(bm.edges), offset=bevel, segments=3,
                        profile=0.5, clamp_overlap=True)
        bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
        bm.to_mesh(mesh)
        bm.free()
    if obj is None:
        obj = bpy.data.objects.new(name, mesh)
        bpy.context.collection.objects.link(obj)
        obj['weapon_redesign_detail'] = True
    else:
        old = obj.data
        obj.data = mesh
        if old.users == 0:
            bpy.data.meshes.remove(old)
    obj.parent = weapon
    obj.matrix_parent_inverse = Matrix.Identity(4)
    obj.matrix_basis = Matrix.Identity(4)
    mesh.materials.append(mat)
    mesh.update()
    # Faceted bevel strips retain broad, clean cartoon panels without pinching.
    for face in mesh.polygons:
        face.use_smooth = False
    obj.hide_set(False)
    obj.hide_render = False
    return obj


def box(w, name, pos, size, mat, bevel=0.02, rot=(0, 0, 0)):
    rotation = Euler(tuple(math.radians(a) for a in rot)).to_matrix()
    verts = [tuple(Vector(pos) + rotation @ Vector((x*size[0]/2, y*size[1]/2, z*size[2]/2)))
             for x, y, z in ((-1,-1,-1),(-1,-1,1),(-1,1,-1),(-1,1,1),
                             (1,-1,-1),(1,-1,1),(1,1,-1),(1,1,1))]
    return install_mesh(w, name, verts,
                        [(0,4,6,2),(1,3,7,5),(0,1,5,4),
                         (2,6,7,3),(0,2,3,1),(4,5,7,6)], mat, bevel)


def tube(w, name, pos, radius, depth, mat, bore=0, axis='Y', sides=16):
    verts = []
    for r, along in ((radius,-depth/2),(radius,depth/2),
                     (bore,-depth/2),(bore,depth/2)):
        for i in range(sides):
            a = math.tau*i/sides
            v = (r*math.cos(a), along, r*math.sin(a)) if axis == 'Y' else (
                r*math.cos(a), r*math.sin(a), along)
            verts.append(tuple(Vector(pos)+Vector(v)))
    faces = []
    if bore == 0:
        verts = verts[:2*sides]
        faces = [tuple(reversed(range(sides))), tuple(range(sides,2*sides))]
        for i in range(sides):
            j = (i+1) % sides
            faces.append((i,j,sides+j,sides+i))
        return install_mesh(w, name, verts, faces, mat, min(0.008,depth/5))
    for i in range(sides):
        j = (i+1) % sides
        faces.extend(((i,j,sides+j,sides+i),
                      (2*sides+i,3*sides+i,3*sides+j,2*sides+j),
                      (i,2*sides+i,2*sides+j,j),
                      (sides+i,sides+j,3*sides+j,3*sides+i)))
    return install_mesh(w, name, verts, faces, mat, min(0.008, depth/5))


def redesign_models():
    blue = material('CG_Weapon_Blue', (0.025,0.32,0.95))
    cyan = material('CG_Weapon_IceBlue', (0.07,0.65,1.0))
    purple = material('CG_Weapon_Purple', (0.27,0.055,0.62))
    deep = material('CG_Weapon_DeepPurple', (0.115,0.025,0.31))
    coral = material('CG_Weapon_Coral', (1.0,0.16,0.12))
    yellow = material('CG_Weapon_Yellow', (1.0,0.67,0.025), glow=0.45)
    light = material('CG_Weapon_YellowLight', (1.0,0.8,0.045), glow=2.1)
    dark = material('CG_Weapon_Ink', (0.025,0.035,0.065), metal=0.4)
    steel = material('CG_Weapon_Steel', (0.24,0.32,0.45), metal=0.65)
    red = material('CG_Weapon_ShellRed', (0.85,0.025,0.055))
    for n in WEAPONS:
        w = bpy.data.objects['Weapon_' + n]
        # Keep existing Body/Grip/etc objects and all empties, preserving FBX IDs.
        for child in list(w.children):
            if child.get('weapon_redesign_detail'):
                bpy.data.objects.remove(child, do_unlink=True)
        main = blue if n == 'Pistol' else coral if n == 'AssaultRifle' else deep
        accent = purple if n == 'Pistol' else blue if n == 'AssaultRifle' else purple
        end = next(c for c in w.children if c.name.startswith('Muzzle')).location.y
        box(w,'Grip',(0,0,-0.085),(0.22,0.20,0.28),accent,0.035,(-16,0,0))
        box(w,'GripHeel',(0,0.045,-0.23),(0.235,0.18,0.065),dark)
        box(w,'TriggerGuard',(0,-0.15,-0.13),(0.13,0.23,0.045),yellow,0.018)
        box(w,'Trigger',(0,-0.115,-0.075),(0.035,0.045,0.10),dark,0.01,(18,0,0))
        if n == 'Pistol':
            box(w,'Body',(0,-0.245,0.09),(0.285,0.48,0.27),main,0.055)
            tube(w,'Barrel',(0,-0.48,0.12),0.16,0.19,accent,0.095,sides=8)
            tube(w,'MuzzleCollar',(0,end+0.055,0.12),0.16,0.09,blue,0.095)
            tube(w,'MuzzleRim',(0,end+0.009,0.12),0.128,0.018,yellow,0.09)
            tube(w,'Bore',(0,end+0.08,0.12),0.092,0.14,dark,0.075)
            box(w,'Accent',(0,-0.23,0.245),(0.19,0.30,0.045),yellow,0.018)
            box(w,'RearSight',(0,-0.02,0.27),(0.13,0.08,0.06),dark)
            box(w,'FrontSight',(0,-0.47,0.265),(0.07,0.06,0.06),light)
        else:
            box(w,'Body',(0,-0.22,0.10),(0.30,0.60,0.29),main,0.05)
            box(w,'Stock',(0,0.19,0.055),(0.24,0.24,0.26),accent,0.04)
            box(w,'StockBridge',(0,0.085,0.12),(0.14,0.26,0.12),steel)
            box(w,'StockPad',(0,0.325,0.055),(0.29,0.055,0.32),dark)
            box(w,'Accent',(0,-0.27,0.275),(0.19,0.47,0.065),accent)
            box(w,'CarryRail',(0,-0.24,0.36),(0.11,0.44,0.065),dark)
            for y in (-0.08,-0.40):
                box(w,'RailPillar_'+str(y),(0,y,0.31),(0.10,0.07,0.08),main)
            if n == 'AssaultRifle':
                box(w,'Barrel',(0,-0.65,0.12),(0.27,0.37,0.22),main,0.035)
                box(w,'Magazine',(0,-0.22,-0.16),(0.21,0.23,0.31),blue,0.035,(-14,0,0))
                box(w,'MagazineFoot',(0,-0.18,-0.315),(0.24,0.20,0.055),yellow)
                tube(w,'MuzzleCollar',(0,end+0.09,0.12),0.15,0.16,blue,0.075)
                tube(w,'MuzzleRim',(0,end+0.012,0.12),0.153,0.024,yellow,0.075)
                tube(w,'Bore',(0,end+0.07,0.12),0.078,0.12,dark,0.06)
                box(w,'UnderRail',(0,-0.62,-0.015),(0.23,0.38,0.075),blue)
                for s in (-1,1):
                    box(w,'Wing_'+str(s),(s*0.155,-0.44,0.17),(0.055,0.30,0.17),blue,0.018,(0,0,s*12))
                    for i in range(3):
                        box(w,'EnergyCell_%s_%d'%(s,i),(s*0.18,-0.19-i*0.09,0.105),
                            (0.035,0.055,0.105),light,0.014,(12,0,0))
                    for i in range(3):
                        box(w,'CoolingVent_%s_%d'%(s,i),(s*0.14,-0.61-i*0.065,0.14),
                            (0.018,0.035,0.10),dark,0.006)
            else:
                tube(w,'Barrel',(0,-0.74,0.12),0.165,0.51,purple,0.105,sides=12)
                tube(w,'MuzzleCollar',(0,end+0.056,0.12),0.185,0.092,deep,0.115,sides=12)
                tube(w,'MuzzleRim',(0,end+0.012,0.12),0.173,0.024,yellow,0.11,sides=12)
                tube(w,'Bore',(0,end+0.095,0.12),0.112,0.15,dark,0.095,sides=16)
                tube(w,'MagazineTube',(0,-0.69,-0.105),0.07,0.48,dark)
                box(w,'Pump',(0,-0.62,-0.02),(0.31,0.29,0.16),purple,0.035)
                for i in range(4):
                    box(w,'PumpRib_'+str(i),(0,-0.515-i*0.064,-0.015),(0.325,0.025,0.15),yellow,0.009)
                for s in (-1,1):
                    box(w,'ShellRack_'+str(s),(s*0.17,-0.24,0.12),(0.045,0.39,0.22),dark)
                    for i in range(3):
                        y = -0.11-i*0.12
                        tube(w,'Shell_%s_%d'%(s,i),(s*0.205,y,0.14),0.047,0.19,red,axis='Z',sides=12)
                        tube(w,'ShellBrass_%s_%d'%(s,i),(s*0.205,y,0.045),0.05,0.045,yellow,axis='Z',sides=12)
        # Large layered side panels, inset seams and fasteners read from behind.
        for s in (-1,1):
            box(w,'SidePlate_'+str(s),(s*0.153,-0.23,0.10),(0.035,0.29,0.15),main)
            box(w,'SideStripe_'+str(s),(s*0.176,-0.22,0.14),(0.018,0.21,0.035),light,0.009)
            for i in range(3):
                box(w,'GripRib_%s_%d'%(s,i),(s*0.112,0.008,-0.06-i*0.05),(0.015,0.12,0.018),dark,0.006)
            for i,y in enumerate((-0.10,-0.35)):
                box(w,'Fastener_%s_%d'%(s,i),(s*0.176,y,0.055),(0.018,0.033,0.033),steel,0.008)
        w['visual_version'] = 'Concept weapons v5'


def rotation(degrees):
    return Euler(tuple(math.radians(a) for a in degrees)).to_matrix().to_4x4()


def correct_imported_weapon_hand():
    for name in WEAPONS:
        weapon=bpy.data.objects['Weapon_'+name]
        weapon.parent_bone='Hand_'+PRIMARY_HAND
        # Mirror the same inward grip offset onto the opposite existing hand.
        # Keep the empty names and all muzzle-local transforms unchanged.
        weapon.location.x=abs(weapon.location.x)
        weapon['physical_player_hand']='Right'


def solve_arm(side, hand_matrix, pole):
    upper = RIG.pose.bones['UpperArm_' + side]
    lower = RIG.pose.bones['LowerArm_' + side]
    hand = RIG.pose.bones['Hand_' + side]
    torso = RIG.pose.bones['Torso']
    shoulder = (torso.matrix @ torso.bone.matrix_local.inverted()) @ upper.bone.head_local
    target = hand_matrix.translation
    delta = target-shoulder
    length = delta.length
    a, b = upper.bone.length, lower.bone.length
    # Keep a small elbow bend at the outer edge of the carry swing. Move the
    # entire hand/socket together rather than stretching either existing bone.
    if length > a+b-0.035:
        length = a+b-0.035
        target = shoulder+delta.normalized()*length
        hand_matrix = hand_matrix.copy()
        hand_matrix.translation = target
        delta = target-shoulder
    axis = delta.normalized()
    along = (a*a-b*b+length*length)/(2*length)
    bend = Vector(pole)-axis*Vector(pole).dot(axis)
    elbow = shoulder+axis*along+bend.normalized()*math.sqrt(max(0,a*a-along*along))
    for bone, head, tail in ((upper,shoulder,elbow),(lower,elbow,target)):
        rest = bone.bone.matrix_local.to_quaternion()
        swing = (rest @ Vector((0,1,0))).rotation_difference((tail-head).normalized())
        bone.matrix = Matrix.Translation(head) @ (swing @ rest).to_matrix().to_4x4()
        bpy.context.view_layer.update()
    hand.matrix = hand_matrix
    bpy.context.view_layer.update()


def author_ready_actions():
    RIG.animation_data.action=bpy.data.actions['Pistol_Idle']
    bpy.context.scene.frame_set(1)
    w=bpy.data.objects['Weapon_Pistol']
    hand_to_weapon=RIG.pose.bones['Hand_'+PRIMARY_HAND].matrix.inverted() @ RIG.matrix_world.inverted() @ w.matrix_world
    for weapon in WEAPONS:
        for suffix in ('Idle','Shoot'):
            name=weapon+'_'+suffix
            old=bpy.data.actions.get(name)
            if old:
                bpy.data.actions.remove(old)
            action=bpy.data.actions.new(name)
            action.use_fake_user=True
            action['unity_loop']=suffix=='Idle'
            action['unity_clip_name']=name
            RIG.animation_data.action=action
            count=60 if suffix=='Idle' else {'Pistol':16,'AssaultRifle':10,'Shotgun':25}[weapon]
            action.use_frame_range=True
            action.frame_start,action.frame_end=1,count
            previous = {}
            for frame in range(1,count+1):
                bpy.context.scene.frame_set(frame)
                t=(frame-1)/(count-1)
                wave=math.sin(math.tau*t)
                kick=(math.sin(math.pi*min(1,t/0.65))**2 if suffix=='Shoot' else 0)
                for bone in RIG.pose.bones:
                    if bone.name in BONES:
                        bone.matrix_basis=Matrix.Identity(4)
                        bone.rotation_mode='XYZ'
                torso=RIG.pose.bones['Torso']
                torso.rotation_euler=Euler(tuple(math.radians(v) for v in (5+wave-7*kick,0,1.2*wave)))
                torso.location=(0,0,0.008*wave)
                RIG.pose.bones['Head'].rotation_euler=Euler((math.radians(-3+2*kick),0,math.radians(-wave)))
                bpy.context.view_layer.update()
                pistol=weapon=='Pistol'
                push=0.085*kick if weapon=='Shotgun' else 0.018*kick
                pos=(-0.90 if pistol else -0.86,-0.50+push,1.05+0.006*wave)
                pitch=-kick*({'Pistol':16,'AssaultRifle':4,'Shotgun':7}[weapon])
                target=Matrix.Translation(pos) @ rotation((pitch,0,0))
                solve_arm(PRIMARY_HAND,target @ hand_to_weapon.inverted(),(-1,0.4,-0.5))
                # The concept uses a one-handed side-ready silhouette on this
                # short-armed robot. Keep the free hand relaxed instead of faking
                # an unreachable two-handed grip across the torso.
                RIG.pose.bones['UpperArm_'+SECONDARY_HAND].rotation_euler=Euler((math.radians(5*wave),0,0))
                RIG.pose.bones['LowerArm_'+SECONDARY_HAND].rotation_euler=Euler((math.radians(-10),0,0))
                for name in BONES:
                    bone=RIG.pose.bones[name]
                    if name in previous:
                        bone.rotation_euler.make_compatible(previous[name])
                    previous[name] = bone.rotation_euler.copy()
                    bone.keyframe_insert(data_path='location',frame=frame,group=name)
                    bone.keyframe_insert(data_path='rotation_euler',frame=frame,group=name)
                    if name.startswith('Eye_'):
                        bone.keyframe_insert(data_path='scale',frame=frame,group=name)
    RIG.animation_data.action=bpy.data.actions['Pistol_Idle']
    bpy.context.scene.frame_set(1)


def author_carry_actions():
    # Solve onto the existing rest rig, baking plain bone keys; no runtime IK.
    RIG.animation_data.action = bpy.data.actions['Pistol_Idle']
    bpy.context.scene.frame_set(1)
    w = bpy.data.objects['Weapon_Pistol']
    hand_to_weapon = RIG.pose.bones['Hand_'+PRIMARY_HAND].matrix.inverted() @ RIG.matrix_world.inverted() @ w.matrix_world
    for weapon in WEAPONS:
        for suffix in ('Walk','Backward','Strafe_L','Strafe_R','Run','Raise','Sprint'):
            name = weapon+'_'+suffix
            old = bpy.data.actions.get(name)
            if old:
                bpy.data.actions.remove(old)
            action = bpy.data.actions.new(name)
            action.use_fake_user = True
            action['unity_loop'] = True
            action['unity_clip_name'] = name
            action['weapon_carry_version'] = 5
            RIG.animation_data.action = action
            count = 24 if suffix == 'Sprint' else 30
            action.use_frame_range = True
            action.frame_start, action.frame_end = 1,count
            previous = {}
            for frame in range(1,count+1):
                bpy.context.scene.frame_set(frame)
                t=(frame-1)/(count-1)
                s=math.sin(math.tau*t)
                c=math.cos(math.tau*t)
                bounce=math.cos(2*math.tau*t)
                sprint = suffix == 'Sprint'
                run = suffix == 'Run'
                for bone in RIG.pose.bones:
                    if bone.name in BONES:
                        bone.matrix_basis = Matrix.Identity(4)
                        bone.rotation_mode = 'XYZ'
                torso=RIG.pose.bones['Torso']
                torso.rotation_euler = Euler(tuple(math.radians(v) for v in (
                    (8 if sprint else 5 if run else 2)+1.4*bounce,
                    2*s, (4 if sprint else 2.5)*s)))
                torso.location = (0.014*s,0,0.018*bounce)
                RIG.pose.bones['Head'].rotation_euler = Euler(tuple(math.radians(v) for v in (
                    -6 if sprint else -3, -2*s,-2*s)))
                bpy.context.view_layer.update()
                if sprint:
                    pos=(-0.98-0.012*s,-0.30+0.018*c,1.55+0.018*bounce)
                    angles=(-80+2*s,-12+2*c,-3+2*s)
                elif suffix == 'Raise':
                    # Front-facing waypoint for the run -> shoulder lift. Without
                    # it, quaternion blending takes the shorter backward wrist flip
                    # from the low/backward run orientation straight to vertical.
                    pos=(-0.94-0.012*s,-0.33+0.018*c,1.34+0.018*bounce)
                    angles=(15+3*s,-5+2*c,-25+2*s)
                elif run:
                    pos=(-0.86-0.02*s,0.025+0.045*s,0.96+0.025*bounce)
                    angles=(130+6*s,3*c,35+4*s)
                else:
                    pos=(-0.87-0.015*s,-0.16+0.045*s,0.93+0.018*bounce)
                    angles=(35+4*s,2*c,-50+3*s)
                target=Matrix.Translation(pos) @ rotation(angles)
                hand=target @ hand_to_weapon.inverted()
                solve_arm(PRIMARY_HAND,hand,(-1,0.45,-0.15))
                # Free left arm counter-swings; both hands return to authored aim
                # through the Animator's shot blend, including the support hand.
                for bone,values in (
                    ('UpperArm_'+SECONDARY_HAND,(-5+(22 if sprint else 15 if run else 11)*-s,3,-5-3*c)),
                    ('LowerArm_'+SECONDARY_HAND,(-18+8*c,0,3*s)),
                    ('Hand_'+SECONDARY_HAND,(5*c,0,-6*s))):
                    RIG.pose.bones[bone].rotation_euler = Euler(tuple(math.radians(a) for a in values))
                bpy.context.view_layer.update()
                for name in BONES:
                    bone=RIG.pose.bones[name]
                    if name in previous:
                        bone.rotation_euler.make_compatible(previous[name])
                    previous[name] = bone.rotation_euler.copy()
                    bone.keyframe_insert(data_path='location',frame=frame,group=name)
                    bone.keyframe_insert(data_path='rotation_euler',frame=frame,group=name)
                    if name.startswith('Eye_'):
                        bone.keyframe_insert(data_path='scale',frame=frame,group=name)
    RIG.animation_data.action=bpy.data.actions['Pistol_Idle']
    bpy.context.scene.frame_set(1)


def validate_preservation():
    assert all(mesh_digest(bpy.data.objects[n]) == value for n,value in PRESERVED_MESHES.items()), 'Robot mesh changed'
    assert all(tuple(tuple(row) for row in RIG.data.bones[n].matrix_local) == value
               for n,value in PRESERVED_BONES.items()), 'Rest rig changed'
    for n,value in PRESERVED_SOCKETS.items():
        o=bpy.data.objects[n]
        if n.startswith('Weapon_'):
            parent,parent_type,unused_bone,basis=value
            mirrored=[list(row) for row in basis]
            mirrored[0][3]=abs(mirrored[0][3])
            expected=(parent,parent_type,'Hand_'+PRIMARY_HAND,tuple(tuple(row) for row in mirrored))
        else:
            expected=value
        assert (o.parent.name,o.parent_type,o.parent_bone,tuple(tuple(row) for row in o.matrix_basis)) == expected, n+' socket changed'
    assert all(bpy.data.actions[n] == value for n,value in PRESERVED_ACTIONS.items()), 'Non-weapon action replaced'
    print('PRESERVED',len(PRESERVED_MESHES),'robot meshes,',len(PRESERVED_BONES),'rest bones,',
          len(PRESERVED_SOCKETS),'grip/muzzle offsets (hand correction intentional),',len(PRESERVED_ACTIONS),'non-weapon actions')


def preview_weapon(name, action='Idle', frame=1):
    for weapon_name in WEAPONS:
        w=bpy.data.objects['Weapon_'+weapon_name]
        for obj in [w]+list(w.children_recursive):
            obj.hide_set(weapon_name != name)
            obj.hide_render=weapon_name != name
    RIG.animation_data.action=bpy.data.actions[name+'_'+action]
    bpy.context.scene.frame_set(frame)


def validate_weapon_actions():
    checked=0
    for weapon_name in WEAPONS:
        w=bpy.data.objects['Weapon_'+weapon_name]
        for suffix in ('Idle','Walk','Backward','Strafe_L','Strafe_R','Run','Raise','Sprint','Shoot'):
            action=bpy.data.actions[weapon_name+'_'+suffix]
            RIG.animation_data.action=action
            start,end=(int(v) for v in action.frame_range)
            first=None
            for frame in range(start,end+1):
                bpy.context.scene.frame_set(frame)
                forward=w.matrix_world.to_quaternion() @ Vector((0,-1,0))
                if suffix=='Idle':
                    assert forward.dot(Vector((0,-1,0))) > 0.9999, action.name
                elif suffix=='Sprint':
                    assert forward.z > 0.94, action.name
                elif suffix=='Raise':
                    assert forward.y < -0.85, action.name+' must lift through the front'
                elif suffix=='Run':
                    assert forward.z < -0.60 and forward.y > 0.40 and forward.x < -0.25, action.name
                elif suffix!='Shoot':
                    assert -0.70 < forward.z < -0.45 and forward.y < -0.45 and forward.x < -0.45, action.name
                for side in ('L','R'):
                    upper=RIG.pose.bones['UpperArm_'+side]
                    lower=RIG.pose.bones['LowerArm_'+side]
                    hand=RIG.pose.bones['Hand_'+side]
                    assert (upper.tail-lower.head).length < 0.0001, action.name+' elbow'
                    assert (lower.tail-hand.head).length < 0.0001, action.name+' wrist'
                    assert abs(upper.length-upper.bone.length) < 0.0001, action.name+' upper stretch'
                    assert abs(lower.length-lower.bone.length) < 0.0001, action.name+' lower stretch'
                current=[b.matrix_basis.copy() for b in RIG.pose.bones if b.name in BONES]
                if first is None:
                    first=current
                if frame==end and suffix!='Shoot':
                    assert max(abs(a[i][j]-b[i][j]) for a,b in zip(first,current)
                               for i in range(4) for j in range(4)) < 0.0001, action.name+' loop seam'
                checked+=1
    preview_weapon('Pistol')
    print('WEAPON QA passed:',checked,'frames; idle aim, low walk/run, vertical sprint, seamless loops, connected unstretched arms')


def export_weapons():
    validate_preservation()
    validate_weapon_actions()
    path=bpy.data.filepath
    assert path.endswith('CouchGuy_Redesigned.blend'), path
    # Save without rotating the user's existing .blend1 backup.
    version=bpy.context.preferences.filepaths.save_version
    bpy.context.preferences.filepaths.save_version=0
    objects=[ROOT]+list(ROOT.children_recursive)
    visibility={o.name:(o.hide_get(),o.hide_render) for o in objects}
    for o in objects:
        o.hide_set(False)
        o.hide_render=False
    bpy.ops.object.select_all(action='DESELECT')
    for o in objects:
        o.select_set(True)
    bpy.context.view_layer.objects.active=ROOT
    bpy.ops.export_scene.fbx(filepath=path[:-6]+'.fbx',use_selection=True,
        object_types={'ARMATURE','MESH','EMPTY'},axis_forward='-Z',axis_up='Y',
        add_leaf_bones=False,bake_anim=True,bake_anim_use_all_actions=True,
        bake_anim_simplify_factor=0.0,path_mode='AUTO')
    for o in objects:
        hidden,render=visibility[o.name]
        o.hide_set(hidden)
        o.hide_render=render
    RIG.animation_data.action=bpy.data.actions['Pistol_Idle']
    bpy.context.scene.frame_set(1)
    bpy.ops.wm.save_as_mainfile(filepath=path,check_existing=False)
    bpy.context.preferences.filepaths.save_version=version
    print('EXPORTED existing CouchGuy_Redesigned.blend + .fbx')


if REBUILD_WEAPON_GEOMETRY:
    redesign_models()
correct_imported_weapon_hand()
author_carry_actions()
author_ready_actions()
validate_preservation()
validate_weapon_actions()
preview_weapon('Pistol')
print('Updated weapon meshes/carry actions in existing model. Inspect, then export_weapons().')
