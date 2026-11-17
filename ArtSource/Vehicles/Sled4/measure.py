import bpy, os, json
from mathutils import Vector
root = r'C:/Users/Jaden/Documents/MyUnityProjects/multiplayer_party_game'
results={}
for rel in ['Assets/_Project/Models/Couch/couch_01.fbx','Assets/_Project/Models/Couch/couch_02.fbx','Assets/_Project/Models/Couch/couch_03.fbx','Assets/_Project/Models/Couch/Easy/easy_couch_01.fbx']:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=os.path.join(root,rel))
    pts=[o.matrix_world@Vector(v) for o in bpy.context.scene.objects if o.type=='MESH' for v in o.bound_box]
    results[rel]=[round(max(p[i] for p in pts)-min(p[i] for p in pts),4) for i in range(3)]
print('COUCH_DIMENSIONS_METERS', json.dumps(results))
