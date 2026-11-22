"""Update packed source material only, asserting unchanged rig/mesh/actions.

Explicit one-shot authoring; the gameplay FBX is deliberately never reexported.
"""
from pathlib import Path
import bpy, numpy as np, hashlib, json, shutil
ROOT=Path(__file__).resolve().parents[2];SRC=ROOT/'ArtSource/Characters/ThiefEnemy01';OUT=SRC/'GetawayCrew'
master=SRC/'thief_enemy_01.blend';backup=OUT/'Before/thief_enemy_01.blend'
assert (OUT/'UnityCrewValidation.txt').exists(),'Validate candidate maps in Unity before installation'
assert not (OUT/'UnityCrewFailure.txt').exists(),'Resolve validation failure first'
if not backup.exists():shutil.copy2(master,backup)
bpy.ops.wm.open_mainfile(filepath=str(master))
def fingerprint():
    h=hashlib.sha256()
    def add(v):h.update(str(v).encode())
    for o in sorted(bpy.context.scene.objects,key=lambda o:o.name):
        add((o.name,o.type,list(map(tuple,o.matrix_world))))
        if o.type=='MESH':
            for v in o.data.vertices:add((tuple(v.co),[(g.group,g.weight) for g in v.groups]))
            for p in o.data.polygons:add(tuple(p.vertices))
            for uv in o.data.uv_layers:
                for p in uv.data:add(tuple(p.uv))
            add([(g.name,g.index) for g in o.vertex_groups])
            add([(m.name,m.type,m.object.name if m.type=='ARMATURE' else None) for m in o.modifiers])
        elif o.type=='ARMATURE':
            for b in o.data.bones:add((b.name,b.parent.name if b.parent else None,list(map(tuple,b.matrix_local))))
    for a in sorted(bpy.data.actions,key=lambda a:a.name):
        add((a.name,tuple(a.frame_range)))
        for layer in a.layers:
            for strip in layer.strips:
                for bag in strip.channelbags:
                    for curve in bag.fcurves:
                        add((curve.data_path,curve.array_index))
                        for k in curve.keyframe_points:add((tuple(k.co),tuple(k.handle_left),tuple(k.handle_right),k.interpolation))
    return h.hexdigest()
before=fingerprint()
maps={}
for key,name in [('Image_0','thief_enemy_01_BaseColor.png'),('Image_2','thief_enemy_01_Normal.png')]:
    im=bpy.data.images.load(str(ROOT/'Assets/_Project/Textures/Thieves/ThiefEnemy01'/name),check_existing=True)
    if key=='Image_2':im.colorspace_settings.name='Non-Color'
    maps[key]=im
# Blender's existing glTF nodes expect AO / roughness / metallic, while Unity
# uses metallic / AO / unused / smoothness. Convert only the source companion.
mos=bpy.data.images.load(str(ROOT/'Assets/_Project/Textures/Thieves/ThiefEnemy01/thief_enemy_01_MetallicOcclusionSmoothness.png'))
mos.colorspace_settings.name='Non-Color';a=np.empty(mos.size[0]*mos.size[1]*4,np.float32);mos.pixels.foreach_get(a);a=a.reshape(-1,4)
orm=bpy.data.images.new('Crew_Source_OcclusionRoughnessMetallic',width=mos.size[0],height=mos.size[1]);orm.colorspace_settings.name='Non-Color'
pixels=np.stack([a[:,1],1-a[:,3],a[:,0],np.ones(len(a))],axis=1).astype(np.float32)
orm.pixels.foreach_set(pixels.ravel());orm.filepath_raw=str(OUT/'Source_OcclusionRoughnessMetallic.png');orm.file_format='PNG';orm.save();maps['Image_1']=orm
for mat in bpy.data.materials:
    if not mat.node_tree:continue
    for node in mat.node_tree.nodes:
        if node.type=='TEX_IMAGE' and node.image and node.image.name in maps:node.image=maps[node.image.name]
for im in maps.values():im.pack()
assert fingerprint()==before,'Material edit changed animation/geometry'
bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=str(master))
bpy.ops.wm.open_mainfile(filepath=str(master))
after=fingerprint();assert after==before,'Saved/reloaded source changed animation or geometry'
report={'source_master':'thief_enemy_01.blend','geometry_rig_uv_weights_actions_before_sha256':before,'geometry_rig_uv_weights_actions_after_sha256':after,'identical':before==after,'fbx_reexported':False,'actions':len(bpy.data.actions),'meshes':len([o for o in bpy.context.scene.objects if o.type=='MESH'])}
(OUT/'SourcePreservationValidation.json').write_text(json.dumps(report,indent=2))
print('CREW_SOURCE_MATERIAL_UPDATE_PASS',json.dumps(report))
