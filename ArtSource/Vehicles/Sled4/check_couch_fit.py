import bpy,os,json
from mathutils import Vector
ROOT=r'C:/Users/Jaden/Documents/MyUnityProjects/multiplayer_party_game'
SRC=os.path.join(ROOT,'ArtSource/Vehicles/Sled4')
bpy.ops.wm.open_mainfile(filepath=os.path.join(ROOT,'Assets/_Project/Models/Sled/CouchGuys_4Couch_Sled.blend'))
bpy.data.collections['Studio_Preview_Only'].hide_viewport=False
results=[]
for x,y,model in [(-1.28,-.88,'Couch/couch_03.fbx'),(1.28,-.88,'Couch/Easy/easy_couch_01.fbx'),(-1.28,.88,'Couch/couch_01.fbx'),(1.28,.88,'Couch/couch_02.fbx')]:
    before=set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=os.path.join(ROOT,'Assets/_Project/Models',model))
    objs=[o for o in bpy.data.objects if o not in before]
    points=[o.matrix_world@Vector(v) for o in objs if o.type=='MESH' for v in o.bound_box]
    lo=Vector([min(p[i] for p in points) for i in range(3)]);hi=Vector([max(p[i] for p in points) for i in range(3)])
    dims=hi-lo
    assert dims.x<=2.32 and dims.y<=1.20,model
    delta=Vector((x,y,.93))-Vector(((lo.x+hi.x)/2,(lo.y+hi.y)/2,lo.z))
    for o in objs:
        if o.parent not in objs:o.location+=delta
    results.append({'model':model,'footprint_m':[dims.x,dims.y],'dock_center_m':[x,y]})
bpy.context.scene.camera.data.ortho_scale=9.4
bpy.context.scene.render.filepath=os.path.join(SRC,'Sled4_CouchFit.png')
bpy.context.scene.cycles.samples=24
bpy.ops.render.render(write_still=True)
with open(os.path.join(SRC,'CouchFitValidation.json'),'w') as f:json.dump(results,f,indent=2)
print('Four unscaled project couches fit the four clear dock envelopes.')
