import bpy,json,os
from mathutils import Vector
ROOT=r'C:/Users/Jaden/Documents/MyUnityProjects/multiplayer_party_game'
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=os.path.join(ROOT,'ArtSource/Vehicles/Sled4/Generated/4424511a_009.glb'))
for o in bpy.context.scene.objects:
    if o.type=='MESH':
        pts=[o.matrix_world@Vector(v) for v in o.bound_box]
        print('BASE',o.name,len(o.data.vertices),len(o.data.polygons),'BOUNDS',[[min(p[i] for p in pts),max(p[i] for p in pts)] for i in range(3)])
