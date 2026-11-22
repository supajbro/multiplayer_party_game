"""Read-only FBX round-trip check; writes a report beside the export."""
import bpy
import json
from pathlib import Path
from mathutils import Vector

out=Path(__file__).resolve().parents[2]/'ArtSource/Environment/JumpingPillow'
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(out/'AustralianJumpingPillow.fbx'))
meshes=[o for o in bpy.context.scene.objects if o.type=='MESH']
assert {o.name for o in meshes}=={'JumpingSurface','AnchoredPerimeter'}
report={}
for o in meshes:
    world=[o.matrix_world@Vector(c) for c in o.bound_box]
    dimensions=[max(v[i] for v in world)-min(v[i] for v in world) for i in range(3)]
    report[o.name]={'world_dimensions_metres':dimensions,'materials':[m.name for m in o.data.materials],'parent':o.parent.name if o.parent else None}
    assert o.parent and o.parent.name=='AustralianJumpingPillow'
    assert (o.matrix_world.translation).length<.0001
surface=next(o for o in meshes if o.name=='JumpingSurface')
d=report['JumpingSurface']['world_dimensions_metres']
assert all(abs(a-b)<.001 for a,b in zip(d,[12,9,1.41])),d
assert len(surface.data.materials)==3
assert len(surface.data.uv_layers)==1
assert surface.data.color_attributes.get('DeformationMask') is not None
# Imported rotation conversion must preserve upward normals on every upper quad.
normal_matrix=surface.matrix_world.to_3x3().inverted().transposed()
assert all((normal_matrix@p.normal).z>0 for p in surface.data.polygons[:72*54])
report['checks']='PASS: FBX scale, orientation, hierarchy, materials, UVs, vertex mask, upper normals'
(out/'fbx_validation.json').write_text(json.dumps(report,indent=2))
print(json.dumps(report))
