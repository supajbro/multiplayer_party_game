from pathlib import Path
import bpy,numpy as np
ROOT=Path(__file__).resolve().parents[2];SRC=ROOT/'ArtSource/Characters/Hotwire'
bpy.ops.wm.open_mainfile(filepath=str(SRC/'Hotwire.blend'))
o=bpy.data.objects['thief_enemy_01'];o.data.calc_loop_triangles()
np.savez_compressed(SRC/'SurfaceMapping.npz',xyz=np.array([[tuple(o.data.vertices[i].co) for i in t.vertices] for t in o.data.loop_triangles]),uv=np.array([[tuple(o.data.uv_layers.active.data[i].uv) for i in t.loops] for t in o.data.loop_triangles]))
print('HOTWIRE_SURFACE_MAPPING_EXPORTED')
