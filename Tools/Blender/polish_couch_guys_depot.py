"""Polish the existing depot.blend in place; run with Blender --background --python.

Keeps gameplay markers, door actions and interior geometry. Re-exports depot.fbx.
"""
import bpy
from pathlib import Path

folder = Path(__file__).resolve().parents[2] / 'Assets/_Project/Models/depot'
bpy.ops.wm.open_mainfile(filepath=str(folder / 'depot.blend'), load_ui=False)
scene = bpy.context.scene
scene.frame_set(1)

# Retire the external receiving tunnel; the interior dispatch lane stays intact.
retired = ('RearTruckPortal_', 'RearPortal_', 'RearArrival_', 'ArrivalServiceTunnel_')
for obj in list(bpy.data.objects):
    if obj.name.startswith(retired) or obj.name.startswith('DepotPolish_'):
        bpy.data.objects.remove(obj, do_unlink=True)
coll = bpy.data.collections.get('CGD_DepotPolish')
if coll is None:
    coll = bpy.data.collections.new('CGD_DepotPolish')
    scene.collection.children.link(coll)

def block(name, pos, size, material, bevel=.045):
    bpy.ops.mesh.primitive_cube_add(size=1, location=pos)
    obj = bpy.context.object
    obj.name = 'DepotPolish_' + name
    obj.dimensions = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    for old in list(obj.users_collection):
        old.objects.unlink(obj)
    coll.objects.link(obj)
    obj.data.materials.append(bpy.data.materials['CGD_M_' + material])
    if bevel:
        mod = obj.modifiers.new('Soft architectural edges', 'BEVEL')
        mod.width = min(bevel, min(size) * .24)
        mod.segments = 2
    return obj

block('RearWall_Closure', (-7.2, 11.505, 2.74), (7.66, .45, 5.52), 'PaleHoneyTimber')
block('RearPlinth', (0, 11.77, .32), (28, .18, .64), 'WarmConcrete')
block('RearCornice', (0, 11.79, 7.12), (28.2, .22, .28), 'WarmWhite')
for x in (-11.2, -7.2, -3.2, 1.0, 5.4, 9.8):
    block('RearBatten', (x, 11.77, 3.78), (.13, .12, 6.64), 'WoodEdge', .025)

# Layered piers and cornice give the original facade depth without changing bays.
for x in (-13.72, -2.95, 2.87, 13.72):
    block('FacadePier', (x, -11.84, 3.68), (.44, .42, 7.34), 'WarmWhite', .07)
    block('PierBoot', (x, -11.92, .38), (.66, .57, .76), 'RoofMetalTrim', .075)
    block('PierCapital', (x, -11.91, 7.13), (.65, .56, .32), 'WarmWhite', .055)
block('FacadeCornice', (0, -11.90, 7.3), (28.55, .54, .32), 'WarmWhite', .06)
block('FacadeTealFrieze', (0, -12.19, 7.08), (28.0, .10, .16), 'DarkTeal', .02)
for x, width in ((-7.2, 8.0), (7.05, 7.8)):
    block('CanopyFascia', (x, -12.88, 6.42), (width+.84, .18, .32), 'DarkTeal')
    block('CanopyHighlight', (x, -12.98, 6.52), (width+.7, .055, .055), 'CouchGuysTeal', .01)
    for side in (-1, 1):
        block('CanopyBracket', (x+side*(width/2-.18), -12.12, 6.10), (.18, 1.12, .25), 'RoofMetalTrim')
        for z in (.85, 1.10, 1.35):
            block('JambSafetyBand', (x+side*(width/2+.12), -11.98, z), (.28, .08, .11), 'SunnyYellow', .012)

# A raised roof rim, sparse standing seams and vent hoods keep the flat silhouette.
for x in (-14.38, 14.38):
    block('RoofCopingSide', (x, 0, 8.02), (.40, 24.36, .23), 'RoofMetalTrim', .05)
for y in (-12.08, 12.08):
    block('RoofCopingEnd', (0, y, 8.02), (29.1, .40, .23), 'RoofMetalTrim', .05)
for x in (-10, -5, 0, 5, 10):
    block('RoofSeam', (x, 0, 7.965), (.055, 22.8, .045), 'RoofMetalTrim', .009)
for x, y in ((-6, 5), (6, 6)):
    block('VentCurb', (x, y, 8.04), (1.4, 1.6, .25), 'DarkTeal')
    block('VentHood', (x, y, 8.34), (1.55, 1.75, .23), 'RoofMetalTrim', .08)
    for i in range(3):
        block('VentLouvre', (x, y-.82, 8.12+i*.065), (1.25, .07, .025), 'SatinMetal', .005)
for x in (-14.3, 14.3):
    for y in (-10.7, 10.7):
        block('RainPipe', (x, y, 3.7), (.14, .17, 7.25), 'RoofMetalTrim', .03)
        for z in (1.3, 4.1, 6.7):
            block('PipeClip', (x, y, z), (.23, .24, .09), 'WarmWhite', .015)

bpy.ops.wm.save_as_mainfile(filepath=str(folder / 'depot.blend'))
bpy.ops.object.select_all(action='DESELECT')
for obj in scene.objects:
    if obj.type in {'MESH', 'EMPTY'}:
        obj.select_set(True)
bpy.ops.export_scene.fbx(filepath=str(folder / 'depot.fbx'), use_selection=True,
    object_types={'MESH', 'EMPTY'}, use_mesh_modifiers=True, add_leaf_bones=False,
    axis_forward='-Z', axis_up='Y', bake_anim=True,
    bake_anim_use_all_bones=False, bake_anim_use_nla_strips=False,
    bake_anim_use_all_actions=False, bake_anim_step=1.0, bake_anim_simplify_factor=0.0)
print('DEPOT_POLISH_COMPLETE', len(scene.objects))
