"""Read-only UV/surface inventory for the appearance-only crew reskin."""
from pathlib import Path
import bpy, numpy as np, json
ROOT=Path(__file__).resolve().parents[2]
SRC=ROOT/'ArtSource/Characters/ThiefEnemy01'
bpy.ops.wm.open_mainfile(filepath=str(SRC/'thief_enemy_01.blend'))
o=bpy.data.objects['thief_enemy_01'];o.data.calc_loop_triangles()
print('OBJECTS',[(x.name,x.type) for x in bpy.context.scene.objects])
print('MATERIALS',[(m.name,[(n.type,n.image.name if n.type=='TEX_IMAGE' and n.image else '') for n in m.node_tree.nodes]) for m in o.data.materials])
print('BOUNDS',[(a,min(v.co[a] for v in o.data.vertices),max(v.co[a] for v in o.data.vertices)) for a in range(3)])
np.savez_compressed(SRC/'GetawayCrew/surface.npz',
    xyz=np.array([[tuple(o.data.vertices[i].co) for i in t.vertices] for t in o.data.loop_triangles]),
    uv=np.array([[tuple(o.data.uv_layers.active.data[i].uv) for i in t.loops] for t in o.data.loop_triangles]))
for im in bpy.data.images:print('IMAGE',im.name,im.size[:],im.filepath)
