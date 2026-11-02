"""Remove only the temporary waveform, preserving the saved dispatcher geometry."""
import bpy, json
from pathlib import Path

out=Path(r'C:/Users/Jaden/Documents/MyUnityProjects/multiplayer_party_game/Assets/_Project/Models/Dispatcher')
bpy.ops.wm.open_mainfile(filepath=str(out/'dispatcher_01.blend'))
for obj in list(bpy.data.objects):
    if obj.name.startswith('dispatcher_01_LOD') and obj.name.endswith('_Waveform_TEMP'):
        data=obj.data
        bpy.data.objects.remove(obj,do_unlink=True)
        if data.users==0: bpy.data.meshes.remove(data)
for mat in list(bpy.data.materials):
    if mat.name.startswith('Dispatcher_Waveform_TEMP') and mat.users==0:
        bpy.data.materials.remove(mat)
scene=bpy.context.scene
stats={}
for lod in range(3):
    prefix='dispatcher_01_LOD'+str(lod)
    root=bpy.data.objects[prefix]
    meshes=list(root.children)
    assert len(meshes)==2 and all(o.type=='MESH' for o in meshes)
    bpy.ops.object.select_all(action='DESELECT')
    for obj in [root]+meshes:
        obj.hide_set(False); obj.hide_render=False; obj.select_set(True)
    bpy.context.view_layer.objects.active=root
    stats[prefix]={o.name:sum(len(p.vertices)-2 for p in o.data.polygons) for o in meshes}
    bpy.ops.export_scene.fbx(filepath=str(out/(prefix+'.fbx')),use_selection=True,object_types={'MESH','EMPTY'},global_scale=1,apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',axis_forward='-Z',axis_up='Y',use_mesh_modifiers=True,mesh_smooth_type='FACE',use_tspace=False,bake_anim=False,add_leaf_bones=False)
    if lod>0:
        for obj in [root]+meshes: obj.hide_set(True); obj.hide_render=True
scene['asset_notes']='Static dispatcher model only. Separate blank black screen at every LOD; waveform reserved for a future shader.'
bpy.ops.object.select_all(action='DESELECT')
root=bpy.data.objects['dispatcher_01_LOD0']; root.select_set(True); bpy.context.view_layer.objects.active=root
bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=str(out/'dispatcher_01.blend'))
(out/'dispatcher_01_geometry.json').write_text(json.dumps(stats,indent=2))
print('REMOVED_STATIC_WAVEFORM',json.dumps(stats))
