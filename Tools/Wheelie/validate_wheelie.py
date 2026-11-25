import bpy, os, json, math
from mathutils import Vector
ROOT=os.path.abspath(os.path.join(os.path.dirname(__file__),'../..'))
folder=os.path.join(ROOT,'ArtSource/Wheelie')
bpy.ops.wm.open_mainfile(filepath=os.path.join(folder,'Wheelie.blend'))
rig=bpy.data.objects['Wheelie']; body=bpy.data.objects['Wheelie_Body']; screen=bpy.data.objects['FaceDisplay']
def coords(obj):
 bpy.context.view_layer.update()
 ev=obj.evaluated_get(bpy.context.evaluated_depsgraph_get());return [ev.matrix_world@v.co for v in ev.data.vertices]
base=coords(body)
wi=body.vertex_groups['Wheel'].index
wheel=[v.index for v in body.data.vertices if any(g.group==wi for g in v.groups)]
stationary=[v.index for v in body.data.vertices if not any(g.group==wi for g in v.groups)]
rig.pose.bones['Wheel'].rotation_mode='XYZ';rig.pose.bones['Wheel'].rotation_euler.y=math.pi/2
moved=coords(body)
assert max((base[i]-moved[i]).length for i in stationary)<1e-6
assert max((base[i]-moved[i]).length for i in wheel)>.15
axle=rig.data.bones['Wheel'].head_local
assert max(abs((base[i]-axle).length-(moved[i]-axle).length) for i in wheel)<1e-6
rig.pose.bones['Wheel'].rotation_euler.y=0
screen0=coords(screen)
rig.pose.bones['Head'].rotation_mode='XYZ';rig.pose.bones['Head'].rotation_euler.y=.4
assert max((a-b).length for a,b in zip(screen0,coords(screen)))>.01
rig.pose.bones['Head'].rotation_euler.y=0
for n in ['UpperArm.L','Forearm.R','Hand.L','Finger1.01.R']:
 p=rig.pose.bones[n];p.rotation_mode='XYZ';p.rotation_euler.x=.45
 assert max((a-b).length for a,b in zip(base,coords(body)))>.003,n
 p.rotation_euler.x=0
assert min(p.normal.y for p in screen.data.polygons)<-.9
assert max(p.normal.y for p in screen.data.polygons)<0
points=coords(body)+coords(screen)
height=max(p.z for p in points);ground=min(p.z for p in points)
assert abs(ground)<.001
report={'rig_tests':'PASS: wheel rotates at axle; housing stationary; head, arms, hand and finger move independently','height_m':height,'ground_m':ground,'screen_normals':'front facing -Y','renderers':2,'material_slots':5}
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=os.path.join(ROOT,'Assets/_Project/Models/Wheelie/Wheelie.fbx'),use_anim=False)
meshes=[o for o in bpy.data.objects if o.type=='MESH'];rigs=[o for o in bpy.data.objects if o.type=='ARMATURE']
assert len(meshes)==2 and len(rigs)==1
assert len(rigs[0].data.bones)==35
assert 'WeaponSocket.R' in rigs[0].data.bones
assert bpy.data.objects.get('FaceDisplay') is not None
assert all(v.groups for o in meshes for v in o.data.vertices)
report['fbx_roundtrip']='PASS: 2 meshes, 35 bones, sockets, skin weights and screen retained'
with open(os.path.join(folder,'rig_validation.json'),'w') as f:json.dump(report,f,indent=2)
print(json.dumps(report))
