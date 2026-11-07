"""Explicit, in-place animation authoring; never runs automatically in Unity.

Updates only Punch_Left (jab) / Punch_Right (cross) on the existing character.
Exports the same animation FBX with the same hierarchy and action names.
Run: blender --background --python Tools/Blender/update_couch_guy_melee.py
"""
import bpy
import math
from pathlib import Path
from mathutils import Euler, Matrix, Vector

ROOT = Path(__file__).resolve().parents[2]
MODEL = ROOT / 'Assets/_Project/Models/CouchGuy'
bpy.ops.wm.open_mainfile(filepath=str(MODEL / 'CouchGuy_Redesigned.blend'))
rig = bpy.data.objects['CouchGuy_Rig']
root = bpy.data.objects['CouchGuy_Root']


def solve_arm(side, target):
    upper = rig.pose.bones['UpperArm_' + side]
    lower = rig.pose.bones['LowerArm_' + side]
    hand = rig.pose.bones['Hand_' + side]
    shoulder = upper.bone.head_local.copy()
    delta = target - shoulder
    length = min(delta.length, upper.length + lower.length - .025)
    axis = delta.normalized()
    target = shoulder + axis * length
    a, b = upper.bone.length, lower.bone.length
    along = (a*a - b*b + length*length) / (2*length)
    pole = Vector((-1 if side == 'L' else 1, 0, -.4))
    bend = (pole - axis * pole.dot(axis)).normalized()
    elbow = shoulder + axis * along + bend * math.sqrt(max(0, a*a - along*along))
    for bone, head, tail in ((upper, shoulder, elbow), (lower, elbow, target)):
        rest = bone.bone.matrix_local.to_quaternion()
        swing = (rest @ Vector((0, 1, 0))).rotation_difference((tail-head).normalized())
        bone.matrix = Matrix.Translation(head) @ (swing @ rest).to_matrix().to_4x4()
        bpy.context.view_layer.update()
    hand.matrix = Matrix.Translation(target) @ hand.bone.matrix_local.to_quaternion().to_matrix().to_4x4()
    bpy.context.view_layer.update()


# Ready -> anticipation -> contact -> overshoot -> settle -> recovery.
# Blender character faces -Y, Z up; imported L bones are on the player's right.
poses = {
    'Punch_Left': ('R', [
        (0, .62, -.32, 1.13, 0, 0, 0),
        (.08, .70, -.13, 1.17, -7, 19, -4),
        (.18, .46, -.94, 1.21, 12, -22, 5),
        (.26, .38, -.88, 1.18, 16, -30, 7),
        (.34, .57, -.46, 1.14, 5, -9, 2),
        (.4, .62, -.32, 1.13, 0, 0, 0)]),
    'Punch_Right': ('L', [
        (0, .62, -.32, 1.13, 0, 0, 0),
        (.10, .72, -.08, 1.22, -10, -28, 7),
        (.20, .40, -.98, 1.25, 20, 36, -10),
        (.30, .26, -.90, 1.20, 24, 46, -13),
        (.38, .53, -.47, 1.14, 9, 17, -4),
        (14/30, .62, -.32, 1.13, 0, 0, 0)])
}

for name, (side, keys) in poses.items():
    old = bpy.data.actions.get(name)
    if old:
        bpy.data.actions.remove(old)
    action = bpy.data.actions.new(name)
    action.use_fake_user = True
    action.use_frame_range = True
    action.frame_start, action.frame_end = 1, 1 + keys[-1][0] * 30
    rig.animation_data.action = action
    previous = {}
    for seconds, x, y, z, lean, twist, roll in keys:
        frame = 1 + seconds * 30
        for bone in rig.pose.bones:
            bone.matrix_basis = Matrix.Identity(4)
            bone.rotation_mode = 'XYZ'
        bpy.context.view_layer.update()
        for arm_side in ('L', 'R'):
            sign = -1 if arm_side == 'L' else 1
            active = arm_side == side
            solve_arm(arm_side, Vector((sign * (x if active else .60),
                                       y if active else -.29, z if active else 1.14)))
        # Arm solutions are in the rest torso frame. Rotate their common parent
        # afterwards, keeping the authored shoulder/elbow chain lengths intact.
        torso = rig.pose.bones['Torso']
        delta = Euler(tuple(math.radians(v) for v in (lean, roll, twist)), 'XYZ').to_matrix().to_4x4()
        torso.matrix = Matrix.Translation(torso.bone.head_local) @ delta @ torso.bone.matrix_local.to_quaternion().to_matrix().to_4x4()
        head = rig.pose.bones['Head']
        head.rotation_euler = (math.radians(-lean * .35), math.radians(-roll * .4), math.radians(-twist * .25))
        bpy.context.view_layer.update()
        for bone in rig.pose.bones:
            if bone.name in previous:
                bone.rotation_euler.make_compatible(previous[bone.name])
            previous[bone.name] = bone.rotation_euler.copy()
            bone.keyframe_insert(data_path='rotation_euler', frame=frame)
        print('POSE', name, round(seconds, 3), 'hand', tuple(round(v, 3) for v in rig.pose.bones['Hand_' + side].head))
    for layer in action.layers:
        for strip in layer.strips:
            for bag in strip.channelbags:
                for curve in bag.fcurves:
                    for key in curve.keyframe_points:
                        key.handle_left_type = 'AUTO_CLAMPED'
                        key.handle_right_type = 'AUTO_CLAMPED'
    print('CLIP', name, list(action.frame_range), 'seconds', (action.frame_end-action.frame_start)/30)

rig.animation_data.action = bpy.data.actions['Idle']
bpy.context.scene.frame_set(1)
bpy.ops.wm.save_as_mainfile(filepath=str(MODEL / 'CouchGuy_Redesigned.blend'))
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
print('UPDATED existing source actions and existing punch FBX; rig and meshes unchanged')
