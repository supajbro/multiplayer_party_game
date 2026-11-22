"""Explicit, offline authoring. Run with Blender --background --python this_file.
No Unity import hooks or runtime geometry generation.
"""
import bpy
import bmesh
import math
import json
from pathlib import Path
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'ArtSource/Environment/JumpingPillow'
OUT.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
s = bpy.context.scene
s.unit_settings.system = 'METRIC'
s.unit_settings.scale_length = 1

def material(name, color, rough=.34):
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*color, 1)
    m.use_nodes = True
    bs = next(n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    bs.inputs['Base Color'].default_value = (*color, 1)
    bs.inputs['Roughness'].default_value = rough
    bs.inputs['Coat Weight'].default_value = .16
    bs.inputs['Coat Roughness'].default_value = .3
    return m

mats = [material('Pillow_Red', (.72,.025,.018)),
        material('Pillow_Blue', (.012,.29,.67)),
        material('Pillow_Yellow', (1,.64,.025)),
        material('Pillow_Anchor', (.18,.13,.065), .6)]
root = bpy.data.objects.new('AustralianJumpingPillow', None)
s.collection.objects.link(root)
root['dimensions_metres'] = '12 x 9; crown 1.34 above ground'
root['ground_level'] = 0.0
root['implementation'] = 'Static authoring asset; no deformation or physics implemented'

def point(u,v):
    return (6*u*math.sqrt(1-.18*v**6),
            4.5*v*math.sqrt(1-.18*u**6),
            .09+1.25*max(0,math.cos(u*math.pi/2)*math.cos(v*math.pi/2))**.64)

def mesh_object(name, verts, faces, materials):
    me=bpy.data.meshes.new(name)
    me.from_pydata(verts, [], faces); me.update()
    ob=bpy.data.objects.new(name, me); s.collection.objects.link(ob); ob.parent=root
    for m in materials: me.materials.append(m)
    for p in me.polygons: p.use_smooth=True
    bm=bmesh.new(); bm.from_mesh(me); bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces)); bm.to_mesh(me); bm.free()
    return ob

NX,NY=72,54  # 16.7 cm nominal spacing; bands align with shared grid rows.
verts=[point(-1+2*i/NX,-1+2*j/NY) for j in range(NY+1) for i in range(NX+1)]
faces=[(j*(NX+1)+i,j*(NX+1)+i+1,(j+1)*(NX+1)+i+1,(j+1)*(NX+1)+i) for j in range(NY) for i in range(NX)]
upper_count=len(faces)
# Counter-clockwise perimeter as seen from above. Closed underside below terrain.
edge=list(range(NX+1))+[j*(NX+1)+NX for j in range(1,NY+1)]+[NY*(NX+1)+i for i in range(NX-1,-1,-1)]+[j*(NX+1) for j in range(NY-1,0,-1)]
bottom=len(verts)
verts.extend([(verts[k][0],verts[k][1],-.07) for k in edge])
for k,a in enumerate(edge):
    n=(k+1)%len(edge); faces.append((a,bottom+k,bottom+n,edge[n]))
faces.append(tuple(reversed(range(bottom,bottom+len(edge)))))
surface=mesh_object('JumpingSurface',verts,faces,mats[:3])
for p in surface.data.polygons:
    y=sum(surface.data.vertices[i].co.y for i in p.vertices)/len(p.vertices)
    # Large red foreground, blue centre and yellow rear, matching the reference.
    if p.index<upper_count:
        row=p.index//NX
        p.material_index=0 if row<21 else (1 if row<39 else 2)
    elif p.index<upper_count+len(edge):
        row=edge[p.index-upper_count]//(NX+1)
        p.material_index=0 if row<21 else (1 if row<39 else 2)
    else: p.material_index=0
uv=surface.data.uv_layers.new(name='SurfaceUV')
for p in surface.data.polygons:
    for li in p.loop_indices:
        co=surface.data.vertices[surface.data.loops[li].vertex_index].co
        uv.data[li].uv=(co.x/12+.5,co.y/9+.5)
group=surface.vertex_groups.new(name='DeformableSurface')
pin=surface.vertex_groups.new(name='PinnedPerimeter')
for j in range(NY+1):
    for i in range(NX+1):
        k=j*(NX+1)+i; d=min(i,NX-i,j,NY-j)
        weight=min(1,d/4)
        group.add([k],weight,'REPLACE'); pin.add([k],1-weight,'REPLACE')
colors=surface.data.color_attributes.new(name='DeformationMask',type='FLOAT_COLOR',domain='POINT')
for k,item in enumerate(colors.data):
    if k<bottom:
        j,i=divmod(k,NX+1); w=min(1,min(i,NX-i,j,NY-j)/4)
    else: w=0
    item.color=(w,w,w,1)
surface['topology']='72 x 54 shared quad grid; 0.167 m nominal spacing; material boundaries share vertices'
surface['deformation_mask']='DeformationMask RGB: 0 anchored, 1 free; also Blender vertex groups'

# Narrow, rounded ground anchor; never a raised wall or rigid platform.
av=[]; af=[]; anchor_edge=edge[::3]; n=len(anchor_edge)
for radial,z in [(1.0,-.065),(1.022,-.04),(1.026,.012),(1.014,.055),(.998,.045)]:
    av.extend([(verts[k][0]*radial,verts[k][1]*radial,z) for k in anchor_edge])
for ring in range(5):
    for i in range(n):
        j=(i+1)%n; q=(ring+1)%5
        af.append((ring*n+i,ring*n+j,q*n+j,q*n+i))
anchor=mesh_object('AnchoredPerimeter',av,af,[mats[3]])
anchor['ground_placement']='Place root at terrain elevation; lower skirt is buried'

# Optional static collision approximation, excluded from visual FBX.
cx,cy=24,18
cv=[point(-1+2*i/cx,-1+2*j/cy) for j in range(cy+1) for i in range(cx+1)]
cf=[(j*(cx+1)+i,j*(cx+1)+i+1,(j+1)*(cx+1)+i+1,(j+1)*(cx+1)+i) for j in range(cy) for i in range(cx)]
coll=mesh_object('COL_JumpingSurface_Static',cv,cf,[])
coll.hide_render=True; coll.hide_set(True)
coll['unity_usage']='Static non-convex MeshCollider; no Rigidbody. Do not render. Not a deformation implementation.'

def export(name,objects):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objects: o.hide_set(False); o.select_set(True)
    bpy.context.view_layer.objects.active=objects[-1]
    bpy.ops.export_scene.fbx(filepath=str(OUT/name),use_selection=True,object_types={'EMPTY','MESH'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',bake_space_transform=True,bake_anim=False,use_custom_props=True,mesh_smooth_type='FACE',use_mesh_modifiers=False,path_mode='RELATIVE')

export('AustralianJumpingPillow.fbx',[root,surface,anchor])
export('AustralianJumpingPillow_Collision.fbx',[coll])
coll.hide_set(True)

# Presentation objects are source-only and excluded from FBX.
groundmat=material('PREVIEW_Sand',(.42,.32,.19),.85)
bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-.025))
ground=bpy.context.object; ground.name='PREVIEW_Ground'; ground.data.materials.append(groundmat)
world=bpy.data.worlds.new('Preview daylight'); world.use_nodes=True
next(n for n in world.node_tree.nodes if n.type=='BACKGROUND').inputs[0].default_value=(.3,.4,.56,1)
s.world=world
for loc,power,size in [((-5,-7,12),2100,8),((7,2,9),1500,7),((-2,8,7),1000,6)]:
    bpy.ops.object.light_add(type='AREA',location=loc)
    o=bpy.context.object; o.name='PREVIEW_Area'; o.data.energy=power; o.data.size=size
    o.rotation_euler=(-o.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.object.camera_add(location=(14,-19,14))
cam=bpy.context.object; cam.name='PREVIEW_Camera'; cam.rotation_euler=(Vector((0,0,.35))-cam.location).to_track_quat('-Z','Y').to_euler()
cam.data.type='ORTHO'; cam.data.ortho_scale=18; s.camera=cam
s.render.engine='CYCLES'; s.cycles.samples=32; s.cycles.use_denoising=True
s.render.resolution_x=1200; s.render.resolution_y=900; s.render.resolution_percentage=100
s.render.image_settings.file_format='PNG'
s.view_settings.view_transform='AgX'
s.view_settings.look='AgX - Medium High Contrast'
s.render.image_settings.color_mode='RGBA'
bpy.ops.object.select_all(action='DESELECT'); surface.select_set(True); bpy.context.view_layer.objects.active=surface
for area in bpy.context.screen.areas:
    if area.type=='VIEW_3D':
        area.spaces.active.region_3d.view_distance=18
        area.spaces.active.region_3d.view_location=(0,0,.5)
        area.spaces.active.shading.color_type='MATERIAL'
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'AustralianJumpingPillow.blend'))
s.render.filepath=str(OUT/'AustralianJumpingPillow_Preview.png'); bpy.ops.render.render(write_still=True)
stats={}
for ob in (surface,anchor,coll):
    me=ob.data; me.calc_loop_triangles()
    bm=bmesh.new(); bm.from_mesh(me)
    stats[ob.name]={'vertices':len(me.vertices),'faces':len(me.polygons),'triangles':len(me.loop_triangles),'boundary_edges':sum(e.is_boundary for e in bm.edges),'nonmanifold_edges':sum(not e.is_manifold for e in bm.edges),'dimensions':list(ob.dimensions)}
    bm.free()
assert stats[surface.name]['nonmanifold_edges']==0
assert stats[anchor.name]['nonmanifold_edges']==0
assert all(p.normal.z>0 for p in surface.data.polygons[:upper_count])
assert all(abs(x-1)<1e-6 for ob in (surface,anchor) for x in ob.scale)
(OUT/'validation.json').write_text(json.dumps(stats,indent=2))
print('JUMPING_PILLOW_COMPLETE',json.dumps(stats))
