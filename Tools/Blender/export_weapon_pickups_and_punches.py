"""Run with Blender --background --python. Never writes the original character files."""
import bpy
import math
from pathlib import Path
from mathutils import Matrix, Vector

ROOT = Path(__file__).resolve().parents[2]
MODEL = ROOT / 'Assets/_Project/Models/CouchGuy'
bpy.ops.wm.open_mainfile(filepath=str(MODEL / 'CouchGuy_Redesigned.blend'))
rig = bpy.data.objects['CouchGuy_Rig']
root = bpy.data.objects['CouchGuy_Root']
print('SOURCE', [(o.name, o.type) for o in root.children_recursive if o.name.startswith('Weapon_')])
print('BONES', [(b.name, tuple(b.head_local), tuple(b.tail_local)) for b in rig.data.bones if 'Arm' in b.name])

out = MODEL / 'Weapons'
out.mkdir(exist_ok=True)
rig.animation_data.action = bpy.data.actions.get('Idle')
bpy.context.scene.frame_set(1)
for kind in ('Pistol', 'AssaultRifle', 'Shotgun'):
    weapon = bpy.data.objects['Weapon_' + kind]
    meshes = [o for o in [weapon] + list(weapon.children_recursive) if o.type == 'MESH']
    bpy.ops.object.select_all(action='DESELECT')
    copies = []
    original_names = []
    # Rotate into Unity's forward direction; retain authored dimensions and materials.
    orientation = Matrix.Identity(4)
    origin = weapon.matrix_world.translation.copy()
    for obj in meshes:
        original_name = obj.name
        obj.name = original_name + "_SourceForExport"
        original_names.append((obj, original_name))
        dup = obj.copy()
        dup.name = original_name
        dup.data = obj.data.copy()
        bpy.context.collection.objects.link(dup)
        dup.parent = None
        dup.animation_data_clear()
        dup.matrix_world = orientation @ Matrix.Translation(-origin) @ obj.matrix_world
        dup.hide_set(False)
        dup.hide_render = False
        dup.select_set(True)
        copies.append(dup)
    bpy.ops.export_scene.fbx(filepath=str(out / (kind + '.fbx')), use_selection=True,
        object_types={'MESH'}, axis_forward='-Z', axis_up='Y', bake_anim=False,
        add_leaf_bones=False, path_mode='AUTO')
    print('EXPORTED', kind, len(meshes), 'meshes')
    for obj in copies:
        bpy.data.objects.remove(obj, do_unlink=True)
    for obj, name in original_names:
        obj.name = name

# Bake a two-bone arm solve onto the existing rig, without constraints or bone edits.
def solve_arm(side, target):
    upper = rig.pose.bones['UpperArm_' + side]
    lower = rig.pose.bones['LowerArm_' + side]
    hand = rig.pose.bones['Hand_' + side]
    shoulder = upper.bone.head_local.copy()
    delta = target - shoulder
    length = min(delta.length, upper.length + lower.length - 0.025)
    axis = delta.normalized()
    target = shoulder + axis * length
    a, b = upper.bone.length, lower.bone.length
    along = (a*a - b*b + length*length) / (2*length)
    pole = Vector((-1 if side == 'L' else 1, 0, -0.4))
    bend = (pole - axis * pole.dot(axis)).normalized()
    elbow = shoulder + axis * along + bend * math.sqrt(max(0, a*a - along*along))
    for bone, head, tail in ((upper, shoulder, elbow), (lower, elbow, target)):
        rest = bone.bone.matrix_local.to_quaternion()
        swing = (rest @ Vector((0,1,0))).rotation_difference((tail-head).normalized())
        bone.matrix = Matrix.Translation(head) @ (swing @ rest).to_matrix().to_4x4()
        bpy.context.view_layer.update()
    hand.matrix = Matrix.Translation(target) @ hand.bone.matrix_local.to_quaternion().to_matrix().to_4x4()
    bpy.context.view_layer.update()

for side, name in (('R', 'Punch_Left'), ('L', 'Punch_Right')):
    action = bpy.data.actions.new(name)
    action.use_fake_user = True
    action.use_frame_range = True
    action.frame_start, action.frame_end = 1, 17
    rig.animation_data.action = action
    previous = {}
    for frame, extension in ((1, 0), (4, -0.15), (8, 1), (11, 0.8), (17, 0)):
        for bone in rig.pose.bones:
            bone.matrix_basis = Matrix.Identity(4)
            bone.rotation_mode = 'XYZ'
        bpy.context.view_layer.update()
        for arm_side in ('L', 'R'):
            active = arm_side == side
            sign = -1 if arm_side == 'L' else 1
            solve_arm(arm_side, Vector((sign * 0.62, -0.32 - (0.50 * extension if active else 0), 1.13)))
            for part in ('UpperArm_', 'LowerArm_', 'Hand_'):
                bone = rig.pose.bones[part + arm_side]
                if bone.name in previous:
                    bone.rotation_euler.make_compatible(previous[bone.name])
                previous[bone.name] = bone.rotation_euler.copy()
                bone.keyframe_insert(data_path='rotation_euler', frame=frame)
                assert abs(bone.length - bone.bone.length) < 0.0001
        print('PUNCH QA', name, frame, tuple(rig.pose.bones['Hand_'+side].head))
    print('ACTION', name, list(action.frame_range))

rig.animation_data.action = bpy.data.actions['Idle']
bpy.context.scene.frame_set(1)
bpy.ops.object.select_all(action='DESELECT')
for obj in [root] + list(root.children_recursive):
    obj.hide_set(False)
    obj.hide_render = False
    obj.select_set(True)
bpy.context.view_layer.objects.active = root
bpy.ops.export_scene.fbx(filepath=str(MODEL / 'CouchGuy_WithPunches.fbx'), use_selection=True,
    object_types={'ARMATURE', 'MESH', 'EMPTY'}, axis_forward='-Z', axis_up='Y',
    add_leaf_bones=False, bake_anim=True, bake_anim_use_all_actions=True,
    bake_anim_simplify_factor=0.0, path_mode='AUTO')
print('DONE: original blend and FBX untouched; animation-compatible copy exported')
