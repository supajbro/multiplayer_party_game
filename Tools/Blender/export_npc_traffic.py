"""Offline cleanup/export of the four ComfyUI-generated NPC cars, never a prefab builder."""
import bpy
import bmesh
import colorsys
import json
import math
import sys
import struct
from pathlib import Path
import numpy as np
from mathutils import Matrix, Vector

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'ArtSource/Vehicles/NPCTraffic'
OUTPUT = ROOT / 'Assets/_Project/Models/Vehicles/NPCTraffic'
OUTPUT.mkdir(parents=True, exist_ok=True)
args = sys.argv[sys.argv.index('--')+1:]
name = args[0]
lengths = {'NPC_CityHatch':3.7,'NPC_FamilySUV':4.5,'NPC_UtilityPickup':4.9,'NPC_SportCoupe':4.3}
HUES = {'NPC_CityHatch':(.48,.605),'NPC_FamilySUV':(.075,.18),'NPC_UtilityPickup':(-.045,.045),'NPC_SportCoupe':(.70,.86)}
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.preferences.filepaths.save_version=0
glb = next((SOURCE/'Generated').rglob(name+'*.glb'))
bpy.ops.import_scene.gltf(filepath=str(glb))
meshes = [o for o in bpy.context.scene.objects if o.type=='MESH']
bpy.ops.object.select_all(action='DESELECT')
for obj in meshes: obj.select_set(True)
bpy.context.view_layer.objects.active=meshes[0]
bpy.ops.object.join()
obj=bpy.context.object
obj.name=name
bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
# Fit the longest horizontal principal axis to Blender Y; preserve all generated geometry.
coords=np.array([v.co[:] for v in obj.data.vertices])
_, axes=np.linalg.eigh(np.cov(coords[:,:2].T))
long=axes[:,-1]
yaw=math.pi/2-math.atan2(long[1],long[0])
if '--flip' in args: yaw+=math.pi
obj.data.transform(Matrix.Rotation(yaw,4,'Z'))
coords=np.array([v.co[:] for v in obj.data.vertices])
lo,hi=coords.min(axis=0),coords.max(axis=0)
scale=lengths[name]/(hi[1]-lo[1])
offset=Vector((-(lo[0]+hi[0])/2,-(lo[1]+hi[1])/2,-lo[2]))
for v in obj.data.vertices: v.co=(v.co+offset)*scale
bm=bmesh.new();bm.from_mesh(obj.data)
bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.00001)
bmesh.ops.dissolve_degenerate(bm,dist=.000001,edges=list(bm.edges))
bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
health={'nonmanifold_edges':sum(not e.is_manifold for e in bm.edges),'loose_vertices':sum(not v.link_faces for v in bm.verts)}
bm.to_mesh(obj.data);bm.free();obj.data.update()
if name=='NPC_UtilityPickup':
    # Remove hallucinated cargo using a bed-interior cut, preserving the generated
    # cab, roll bar, three lamps, side walls, tailgate, chassis and all four tyres.
    bed_mat=bpy.data.materials.new('Temporary_BedCut')
    obj.data.materials.append(bed_mat)
    bpy.ops.mesh.primitive_cube_add(size=1,location=(0,1.27,2.8))
    cutter=bpy.context.object;cutter.name='Offline_BedCleanup'
    cutter.dimensions=(2.04,1.62,3.0)
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    for mat in obj.data.materials:cutter.data.materials.append(mat)
    for polygon in cutter.data.polygons:polygon.material_index=1
    bpy.context.view_layer.objects.active=obj
    modifier=obj.modifiers.new('OpenCargoBed','BOOLEAN');modifier.operation='DIFFERENCE';modifier.object=cutter
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    # Clear residual cargo above the side rails across the full bed width.
    cutter.location=(0,1.325,2.75);cutter.dimensions=(3.3,1.85,2.0)
    bpy.context.view_layer.objects.active=cutter
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    bpy.context.view_layer.objects.active=obj
    modifier=obj.modifiers.new('ClearAboveBedRails','BOOLEAN');modifier.operation='DIFFERENCE';modifier.object=cutter
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    bpy.data.objects.remove(cutter,do_unlink=True)
    # A constant UV from a dark tyre texel gives cut faces a minimal dark interior.
    original=obj.data.materials[0]
    bsdf=next(n for n in original.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
    atlas=bsdf.inputs['Base Color'].links[0].from_node.image
    w,h=atlas.size;pix=np.array(atlas.pixels[:]).reshape(h,w,4)
    candidates=np.where((pix[:,:,:3].max(axis=2)<.17)&(pix[:,:,:3].max(axis=2)>.06)&(pix[:,:,3]>.9))
    if not len(candidates[0]):raise RuntimeError('No dark bed texel')
    dark_uv=((float(candidates[1][0])+.5)/w,(float(candidates[0][0])+.5)/h)
    for polygon in obj.data.polygons:
        if polygon.material_index==1:
            polygon.material_index=0
            for loop_index in polygon.loop_indices:obj.data.uv_layers.active.data[loop_index].uv=dark_uv
    obj.data.materials.pop(index=1)
scene=bpy.context.scene
scene.render.engine='CYCLES';scene.cycles.device='CPU';scene.cycles.samples=12
scene.world=bpy.data.worlds.new('Studio');scene.world.use_nodes=True
next(n for n in scene.world.node_tree.nodes if n.type=='BACKGROUND').inputs[0].default_value=(.22,.25,.3,1)
for pos,energy in [((3,-4,7),850),((-4,1,5),650),((0,6,6),900)]:
    bpy.ops.object.light_add(type='AREA',location=pos)
    light=bpy.context.object;light.data.energy=energy;light.data.shape='DISK';light.data.size=5
    light.rotation_euler=(-light.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.object.camera_add()
cam=bpy.context.object;cam.data.type='ORTHO';cam.data.ortho_scale=lengths[name]*1.3;scene.camera=cam
scene.render.resolution_x=768;scene.render.resolution_y=576;scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG'
def render(suffix,pos):
    cam.location=Vector(pos)*lengths[name]
    cam.rotation_euler=(Vector((0,0,.9))-cam.location).to_track_quat('-Z','Y').to_euler()
    scene.render.filepath=str(SOURCE/(name+suffix+'.png'))
    bpy.ops.render.render(write_still=True)
if '--inspect' in args:
    print('ALIGNED_BOUNDS',list(obj.dimensions),'YAW',yaw,flush=True)
    render('_Front',(1,-1.5,.85));render('_Rear',(-1,1.5,.85))
    sys.exit()

# Split paint by atlas colour; keep white decals, glass, tyres, trim and lamps on Details.
original=obj.data.materials[0]
bsdf=next(n for n in original.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
image_node=bsdf.inputs['Base Color'].links[0].from_node
atlas=image_node.image
width,height=atlas.size
pixels=np.array(atlas.pixels[:],dtype=np.float32).reshape(height,width,4)
low,high=HUES[name]
def is_paint(rgb):
    h,s,v=colorsys.rgb_to_hsv(*rgb[:3])
    if low<0 and h>.5:h-=1
    return low<=h<=high and s>.34 and v>.11
uv=obj.data.uv_layers.active.data
paint_faces=[]
for poly in obj.data.polygons:
    samples=[]
    for loop_index in poly.loop_indices:
        u,v=uv[loop_index].uv
        samples.append(pixels[min(height-1,max(0,int(v*height))),min(width-1,max(0,int(u*width)))])
    center=np.mean([uv[i].uv[:] for i in poly.loop_indices],axis=0)
    samples.append(pixels[min(height-1,max(0,int(center[1]*height))),min(width-1,max(0,int(center[0]*width)))])
    if any(is_paint(c) for c in samples):paint_faces.append(poly.index)
if len(paint_faces)<len(obj.data.polygons)*.08:raise RuntimeError('Paint classification too small')
texdir=OUTPUT/'Textures';texdir.mkdir(exist_ok=True)
original.name=name+'_Details'
atlas.filepath_raw=str(texdir/(name+'_BaseColor.png'));atlas.file_format='PNG';atlas.save()
# Per-texel mask preserves white stripes and other details inside mixed triangles.
mask=np.zeros_like(pixels);mask[:,:,3]=1
for source_pixel,target_pixel in zip(pixels.reshape(-1,4),mask.reshape(-1,4)):
    if is_paint(source_pixel):target_pixel[:3]=1
mask_image=bpy.data.images.new(name+'_PaintMask',width=width,height=height,alpha=True)
mask_image.pixels.foreach_set(mask.ravel())
mask_image.filepath_raw=str(texdir/(name+'_PaintMask.png'));mask_image.file_format='PNG';mask_image.save()
paint=original.copy();paint.name=name+'_BodyPaint'
rgb={'NPC_CityHatch':(.094,.565,1),'NPC_FamilySUV':(1,.58,.035),'NPC_UtilityPickup':(1,.029,.053),'NPC_SportCoupe':(.511,.039,1)}[name]
paint['Unity_PaintColour']=rgb
obj.data.materials.append(paint)
for i in paint_faces:obj.data.polygons[i].material_index=1
# Extract the original embedded maps byte-for-byte, including lazily loaded images.
blob=glb.read_bytes();json_size=struct.unpack_from('<I',blob,12)[0]
gltf=json.loads(blob[20:20+json_size]);bin_start=20+json_size+8
for entry,suffix in zip(gltf['images'],('BaseColor','ORM','Normal')):
    view=gltf['bufferViews'][entry['bufferView']];start=bin_start+view.get('byteOffset',0)
    (texdir/(name+'_'+suffix+'.png')).write_bytes(blob[start:start+view['byteLength']])
bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj
obj['source']='Local ComfyUI Pixal3D image-to-3D';obj['forward']='Blender -Y / Unity +Z';obj['pivot']='Ground centre';obj['paint_material']=paint.name
bm=bmesh.new();bm.from_mesh(obj.data)
# Separate touching closed shells at welded junctions (for example, tyre/body).
# This preserves both surfaces and their UVs without leaving open boundary edges.
bmesh.ops.split_edges(bm,edges=[e for e in bm.edges if len(e.link_faces)>2])
health={'nonmanifold_edges':sum(not e.is_manifold for e in bm.edges),'boundary_edges':sum(e.is_boundary for e in bm.edges),'loose_vertices':sum(not v.link_faces for v in bm.verts)}
bm.to_mesh(obj.data);bm.free();obj.data.update()
if health['nonmanifold_edges'] or health['loose_vertices']:raise RuntimeError('Mesh cleanup incomplete: '+str(health))
bpy.ops.export_scene.fbx(filepath=str(OUTPUT/(name+'.fbx')),use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,bake_space_transform=True,bake_anim=False,path_mode='RELATIVE',use_mesh_modifiers=True)
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/(name+'.blend')))
report={'name':name,'triangles':sum(len(p.vertices)-2 for p in obj.data.polygons),'vertices':len(obj.data.vertices),'paint_faces':len(paint_faces),'size':list(obj.dimensions),'yaw':yaw,'paint_color':rgb,'materials':[m.name for m in obj.data.materials],**health}
(SOURCE/(name+'_MeshReport.json')).write_text(json.dumps(report,indent=2))
render('_Front',(1,-1.5,.85));render('_Rear',(-1,1.5,.85))
print(json.dumps(report),flush=True)
