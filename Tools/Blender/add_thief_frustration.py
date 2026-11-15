"""Explicitly add a one-second frustration gesture to the existing armed thief FBXs."""
from pathlib import Path
import math
import bpy
from mathutils import Matrix, Quaternion, Vector

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'ArtSource/Characters/Character01Thief/Armed'
OUTPUT = ROOT / 'Assets/_Project/Models/Thieves/Character01Thief/Armed'


def solve_arm(rig, target):
    upper = rig.pose.bones['mixamorig:LeftArm']
    lower = rig.pose.bones['mixamorig:LeftForeArm']
    hand = rig.pose.bones['mixamorig:LeftHand']
    shoulder = upper.head.copy()
    delta = Vector(target) - shoulder
    length = min(delta.length, upper.length + lower.length - 0.005)
    axis = delta.normalized()
    along = (upper.length ** 2 - lower.length ** 2 + length ** 2) / (2 * length)
    pole = Vector((1, 0, 0.2))
    bend = (pole - axis * pole.dot(axis)).normalized()
    elbow = shoulder + axis * along + bend * math.sqrt(max(0, upper.length ** 2 - along ** 2))
    wrist = shoulder + axis * length
    for bone, head, tail in ((upper, shoulder, elbow), (lower, elbow, wrist)):
        rest = bone.bone.matrix_local.to_quaternion()
        swing = (rest @ Vector((0, 1, 0))).rotation_difference((tail - head).normalized())
        bone.matrix = Matrix.Translation(head) @ (swing @ rest).to_matrix().to_4x4()
        bpy.context.view_layer.update()
    hand.rotation_quaternion = Quaternion((1, 0, 0), math.radians(-15))
    bpy.context.view_layer.update()


for variant in ('Pistol', 'AssaultRifle', 'Shotgun'):
    source = SOURCE / f'Character01_Thief_{variant}.blend'
    bpy.ops.wm.open_mainfile(filepath=str(source))
    rig = bpy.data.objects['Character01_Thief_Rig']
    rig.animation_data.action = bpy.data.actions['Thief_Idle']
    bpy.context.scene.frame_set(1)
    baseline = {bone.name: bone.matrix_basis.copy() for bone in rig.pose.bones}
    old = bpy.data.actions.get('Thief_Frustration')
    if old:
        bpy.data.actions.remove(old)
    action = bpy.data.actions.new('Thief_Frustration')
    action.use_fake_user = True
    rig.animation_data.action = action
    # Anticipate, throw the free hand up, shake it twice with the head, recover.
    for frame, amount, shake in ((1, 0, 0), (5, .2, 0), (10, 1, -1),
                                 (14, .88, 1), (18, 1, -1), (22, .75, .6), (31, 0, 0)):
        for bone in rig.pose.bones:
            bone.rotation_mode = 'QUATERNION'
            bone.matrix_basis = baseline[bone.name]
        bpy.context.view_layer.update()
        head = rig.pose.bones['mixamorig:Head']
        head.rotation_quaternion = head.rotation_quaternion @ Quaternion((0, 1, 0), math.radians(12 * shake))
        chest = rig.pose.bones['mixamorig:Spine2']
        chest.rotation_quaternion = chest.rotation_quaternion @ Quaternion((1, 0, 0), math.radians(-5 * amount))
        bpy.context.view_layer.update()
        if amount:
            idle_wrist = rig.pose.bones['mixamorig:LeftHand'].head.copy()
            raised = Vector((.43 + .04 * shake, 1.72, .17 + .04 * shake))
            solve_arm(rig, idle_wrist.lerp(raised, amount))
        for bone in rig.pose.bones:
            bone.keyframe_insert('rotation_quaternion', frame=frame)
            bone.keyframe_insert('location', frame=frame)
            bone.keyframe_insert('scale', frame=frame)
    for layer in action.layers:
        for strip in layer.strips:
            for bag in strip.channelbags:
                for curve in bag.fcurves:
                    for key in curve.keyframe_points:
                        key.handle_left_type = 'AUTO_CLAMPED'
                        key.handle_right_type = 'AUTO_CLAMPED'
    bpy.context.scene.render.fps = 30
    rig.animation_data.action = bpy.data.actions['Thief_Idle']
    bpy.context.scene.frame_set(1)
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(source))
    bpy.ops.object.select_all(action='DESELECT')
    for obj in bpy.context.scene.objects:
        if obj.type in {'ARMATURE', 'MESH', 'EMPTY'}:
            obj.hide_set(False)
            obj.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.export_scene.fbx(filepath=str(OUTPUT / f'Character01_Thief_{variant}.fbx'),
        use_selection=True, object_types={'ARMATURE', 'MESH', 'EMPTY'},
        axis_forward='-Z', axis_up='Y', add_leaf_bones=False,
        bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
        bake_anim_simplify_factor=0, path_mode='AUTO')
    print('UPDATED', variant, 'actions', [(a.name, list(a.frame_range)) for a in bpy.data.actions])
    if variant == 'Pistol':
        rig.animation_data.action = action
        bpy.context.scene.render.resolution_x = 600
        bpy.context.scene.render.resolution_y = 600
        bpy.context.scene.render.resolution_percentage = 100
        for frame in (1, 10, 18, 31):
            bpy.context.scene.frame_set(frame)
            bpy.context.scene.render.filepath = str(ROOT / f'Temp/thief_frustration_{frame}.png')
            bpy.ops.render.render(write_still=True)
