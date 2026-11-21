"""Explicit offline Pixal3D cleanup, Unity export and multi-angle visual QA."""
from pathlib import Path
import bpy, math, json, bmesh
from mathutils import Vector

ROOT=Path(__file__).resolve().parents[2]
SRC=ROOT/'ArtSource/Characters/ThiefEnemy01'
OUT=ROOT/'Assets/_Project/Models/Thieves'
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
glb=next((SRC/'Generated').rglob('*.glb'))
bpy.ops.import_scene.gltf(filepath=str(glb))
objects=[o for o in bpy.context.scene.objects if o.type=='MESH']
print('IMPORTED',[(o.name, list(o.dimensions),len(o.data.vertices),len(o.data.polygons)) for o in objects])
# Combine the generated surface; no procedural replacement geometry.
bpy.ops.object.select_all(action='DESELECT')
for o in objects:o.select_set(True)
bpy.context.view_layer.objects.active=objects[0]
bpy.ops.object.join()
obj=bpy.context.object
obj.name='thief_enemy_01'
bpy.ops.object.transform_apply(location=False,rotation=True,scale=True)
coords=[obj.matrix_world@v.co for v in obj.data.vertices]
lo=Vector(tuple(min(v[i] for v in coords) for i in range(3)))
hi=Vector(tuple(max(v[i] for v in coords) for i in range(3)))
height=hi.z-lo.z
scale=1.65/height
for v in obj.data.vertices:
    v.co=(obj.matrix_world@v.co-Vector(((lo.x+hi.x)/2,(lo.y+hi.y)/2,lo.z)))*scale
obj.matrix_world.identity()
bm=bmesh.new();bm.from_mesh(obj.data)
bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=0.000005)
bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
bm.to_mesh(obj.data);bm.free()
for p in obj.data.polygons:p.use_smooth=True
obj.data.update()
report={'source':'Pixal3D INT8 through local ComfyUI','height_m':1.65,'vertices':len(obj.data.vertices),'triangles':sum(len(p.vertices)-2 for p in obj.data.polygons),'uv_layers':len(obj.data.uv_layers),'dimensions_m':list(obj.dimensions),'materials':len(obj.data.materials)}
bm=bmesh.new();bm.from_mesh(obj.data)
report.update(boundary_edges=sum(e.is_boundary for e in bm.edges),nonmanifold_edges=sum(not e.is_manifold for e in bm.edges),loose_vertices=sum(not v.link_faces for v in bm.verts))
bm.free()
print('REPORT',json.dumps(report))
(SRC/'GeometryValidation.json').write_text(json.dumps(report,indent=2))
# Save a standalone master before cameras/lights; external images are packed.
for mat in obj.data.materials:
    if mat and mat.use_nodes:
        for n in mat.node_tree.nodes:
            if n.type=='TEX_IMAGE' and n.image:n.image.pack()
bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=str(SRC/'thief_enemy_01.blend'))
# A neutral studio turntable for checking every side of the generated surface.
scene=bpy.context.scene
try:scene.render.engine='CYCLES'
except TypeError:pass
scene.cycles.device='CPU';scene.cycles.samples=20
scene.render.resolution_x=700;scene.render.resolution_y=700;scene.render.resolution_percentage=100
scene.world.color=(0.25,0.25,0.25)
scene.view_settings.view_transform='Standard'
for name,location,power,size in [('Key',(3,-4,5),450,4),('Fill',(-4,-2,3),300,4),('Rim',(1,4,4),400,3)]:
    data=bpy.data.lights.new(name,'AREA');data.energy=power;data.shape='DISK';data.size=size
    light=bpy.data.objects.new(name,data);scene.collection.objects.link(light);light.location=location
    light.rotation_euler=(Vector((0,0,0.85))-light.location).to_track_quat('-Z','Y').to_euler()
data=bpy.data.cameras.new('QA');cam=bpy.data.objects.new('QA',data);scene.collection.objects.link(cam);scene.camera=cam
data.type='ORTHO';data.ortho_scale=2.45
for label,angle in [('front',0),('quarter',45),('right',90),('back',180),('left',270)]:
    a=math.radians(angle);cam.location=(4*math.sin(a),-4*math.cos(a),1.1)
    cam.rotation_euler=(Vector((0,0,.83))-cam.location).to_track_quat('-Z','Y').to_euler()
    scene.render.filepath=str(SRC/('thief_enemy_01_'+label+'.png'))
    bpy.ops.render.render(write_still=True)
print('THIEF_PREVIEW_COMPLETE')
