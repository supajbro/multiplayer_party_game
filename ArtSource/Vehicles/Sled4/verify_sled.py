import bpy,bmesh,os,json
from mathutils import Vector
ROOT=r'C:/Users/Jaden/Documents/MyUnityProjects/multiplayer_party_game'
SRC=os.path.join(ROOT,'ArtSource/Vehicles/Sled4')
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=os.path.join(ROOT,'Assets/_Project/Models/Sled/CouchGuys_4Couch_Sled.fbx'))
meshes=[o for o in bpy.context.scene.objects if o.type=='MESH']
report={'mesh_count':len(meshes),'meshes':{},'triangles':0}
points=[o.matrix_world@Vector(p) for o in meshes for p in o.bound_box]
report['dimensions_m']=[round(max(p[i] for p in points)-min(p[i] for p in points),4) for i in range(3)]
for o in meshes:
    bm=bmesh.new();bm.from_mesh(o.data)
    degenerate=sum(1 for f in bm.faces if f.calc_area()<1e-10)
    loose=sum(1 for v in bm.verts if not v.link_faces)
    tris=sum(len(p.vertices)-2 for p in o.data.polygons);report['triangles']+=tris
    report['meshes'][o.name]={'triangles':tris,'degenerate_faces':degenerate,'loose_vertices':loose,'uv_layers':len(o.data.uv_layers),'scale':list(o.scale)}
    assert degenerate==0 and loose==0 and len(o.data.uv_layers)>0,o.name
    bm.free()
assert len(meshes)==13
report['clear_couch_envelope_m']=[2.32,1.20]
report['sampled_max_couch_footprint_m']=[2.2772,.9913]
report['footprint_margin_m']=[round(2.32-2.2772,4),round(1.20-.9913,4)]
report['comfyui_status']='Completed local Pixal3D image-to-3D; lower shell simplified in Blender'
report['unity_editor_import_verified']=False
with open(os.path.join(SRC,'ExportValidation.json'),'w') as f:json.dump(report,f,indent=2)
print(json.dumps(report))
