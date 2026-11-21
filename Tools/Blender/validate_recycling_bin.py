"""FBX roundtrip checks and a distance-LOD preview; never edits delivered assets."""
import bpy,bmesh,json,hashlib
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
SRC=ROOT/'ArtSource/Props/RecyclingBin';OUT=ROOT/'Assets/_Project/Models/Props/RecyclingBin'
bpy.ops.wm.open_mainfile(filepath=str(SRC/'recycling_bin.blend'),load_ui=False)
visual=bpy.data.collections['RecyclingBin_Visual'];visual.hide_render=True
bpy.ops.import_scene.fbx(filepath=str(OUT/'recycling_bin.fbx'))
lods=[o for o in bpy.context.selected_objects if o.type=='MESH']
report=[]
for o in lods:
    bm=bmesh.new();bm.from_mesh(o.data)
    # FBX splits vertices at shading/UV seams; weld the validation copy only.
    bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.00001)
    loose=sum(1 for v in bm.verts if not v.link_edges)
    nonmanifold=sum(not e.is_manifold for e in bm.edges)
    assert loose==0
    assert len(o.data.uv_layers)>0 and len(o.data.materials)==9
    assert abs(o.dimensions.y-1.63)<.02,(o.name,list(o.dimensions))
    assert all(abs(v)<.0001 for v in o.location)
    report.append(dict(name=o.name,nonmanifoldEdges=nonmanifold,looseVertices=loose,uvLayers=len(o.data.uv_layers),dimensions=list(o.dimensions)))
    bm.free();o.hide_render=not o.name.endswith('LOD2')
    # Exported mesh data is baked Y-up for Unity; rotate only the review copy.
    o.rotation_euler.x=1.5707963267948966
    # Restore source Blender shaders because FBX cannot preserve Blender node graphs.
    for i,m in enumerate(o.data.materials):
        original=bpy.data.materials.get(m.name.rsplit('.',1)[0])
        if original:o.data.materials[i]=original
bpy.context.scene.render.filepath=str(SRC/'recycling_bin_lod2.png');bpy.ops.render.render(write_still=True)
for o in lods:bpy.data.objects.remove(o,do_unlink=True)
bpy.ops.import_scene.fbx(filepath=str(OUT/'recycling_bin_collision.fbx'))
cols=[o for o in bpy.context.selected_objects if o.type=='MESH']
for o in cols:
    bm=bmesh.new();bm.from_mesh(o.data)
    assert all(e.is_manifold for e in bm.edges),o.name
    assert bm.calc_volume()>0,o.name
    bm.free()
assert len(cols)==9
data=json.loads((SRC/'validation.json').read_text());data['fbxRoundtrip']=report;data['watertightCollisionParts']=len(cols)
assert hashlib.sha256((ROOT/'Assets/_Project/Models/depot_02/depot_02.blend').read_bytes()).hexdigest()==data['referenceHash']
(SRC/'validation.json').write_text(json.dumps(data,indent=2));print('ROUNDTRIP_PASS')
