"""Explicit CityHatch source cleanup, LOD export and visual QA. No import hooks."""
from pathlib import Path
import bpy, bmesh, numpy as np, json, shutil
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
SRC = ROOT / 'ArtSource/Vehicles/NPCTraffic'
OUT = ROOT / 'Assets/_Project/Models/Vehicles/NPCTraffic'
before = SRC / 'CityHatch_BeforeCleanup'
before.mkdir(exist_ok=True)
for p in [SRC/'NPC_CityHatch.blend', OUT/'NPC_CityHatch.fbx', OUT/'Textures/NPC_CityHatch_BaseColor.png', ROOT/'Assets/_Project/Prefabs/Vehicles/NPCTraffic/NPC_CityHatch.prefab']:
    if not (before/p.name).exists(): shutil.copy2(p, before/p.name)
bpy.ops.wm.open_mainfile(filepath=str(before/'NPC_CityHatch.blend'))
bpy.context.preferences.filepaths.save_version = 0
obj = bpy.data.objects['NPC_CityHatch']
# Work from the archived atlas, so this explicit tool is repeatable.
atlas = bpy.data.images.load(str(before/'NPC_CityHatch_BaseColor.png'), check_existing=False)
w,h = atlas.size
p = np.array(atlas.pixels[:], dtype=np.float32).reshape(h,w,4)
rgb = p[:,:,:3]; value = rgb.max(2); low = rgb.min(2)
sat = (value-low)/np.maximum(value, 1e-6)
# Rasterize the existing UVs to locate cabin glass and side panels in the atlas.
# This avoids treating blue reflections on windows as body paint.
xyz=np.zeros((h,w,3),np.float32);valid=np.zeros((h,w),bool)
obj.data.calc_loop_triangles();uv=obj.data.uv_layers.active.data
for tri in obj.data.loop_triangles:
    t=np.array([uv[i].uv[:] for i in tri.loops])*[w,h]-.5
    a,b,c=t;den=(b[1]-c[1])*(a[0]-c[0])+(c[0]-b[0])*(a[1]-c[1])
    if abs(den)<1e-8:continue
    x0,y0=np.maximum(np.floor(t.min(0)).astype(int),0)
    x1,y1=np.minimum(np.ceil(t.max(0)).astype(int),[w-1,h-1])
    xx,yy=np.meshgrid(np.arange(x0,x1+1),np.arange(y0,y1+1))
    u=((b[1]-c[1])*(xx-c[0])+(c[0]-b[0])*(yy-c[1]))/den
    v=((c[1]-a[1])*(xx-c[0])+(a[0]-c[0])*(yy-c[1]))/den
    inside=(u>=-.001)&(v>=-.001)&(u+v<=1.001)
    co=np.array([obj.data.vertices[i].co[:] for i in tri.vertices])
    patch=u[:,:,None]*co[0]+v[:,:,None]*co[1]+(1-u-v)[:,:,None]*co[2]
    xyz[y0:y1+1,x0:x1+1][inside]=patch[inside]
    valid[y0:y1+1,x0:x1+1]|=inside
x,y,z=xyz[:,:,0],xyz[:,:,1],xyz[:,:,2]
cabin=valid&(z>1.08)&(z<1.79)&(y>-1.25)&(y<1.65)
window=cabin&((abs(x)>.65)|(y<-.52)|(y>1.12))&(value<.55)
# Blue paint has substantially more chroma than the glass and rubber.
blue = (rgb[:,:,2]>rgb[:,:,0]*1.35)&(rgb[:,:,1]>rgb[:,:,0]*1.35)&(sat>.35)
strength = (blue&(value>.24)&~window).astype(float)
target = np.array([.094,.565,1.0])
# Remove baked highlights/shadows while keeping a trace of fine panel definition.
paint = target[None,None,:]*(.97+.03*np.clip(value/.72,0,1))[:,:,None]
rgb[:] = rgb*(1-strength[:,:,None])+paint*strength[:,:,None]
# Quiet the blotchy low-chroma window reflections without erasing lamps/decals.
glass=window
rgb[glass] = np.array([.12,.19,.25])
pillars=glass&(abs(x)>.7)&(y>.12)&(y<.29)
rgb[pillars]=[.055,.065,.075]
# Remove pale generated brush streaks on the doors, away from hood/roof decals.
side=valid&(abs(x)>.79)&(abs(y)<.85)&(z>.75)&(z<1.16)&(value>.32)&(rgb[:,:,2]>=rgb[:,:,0])
rgb[side]=target*.98
# Pale fragments outside the two centre stripes are generated paint streaks.
streak=valid&(abs(x)>.48)&(z>1.12)&(z<1.86)&(value>.48)&(sat<.36)&~window
rgb[streak]=target*.98
rails=valid&(z>1.87)&(abs(x)>.43)
rgb[rails]=[.12,.15,.18]
atlas.pixels.foreach_set(p.ravel())
atlas.filepath_raw = str(OUT/'Textures/NPC_CityHatch_BaseColor.png')
atlas.file_format = 'PNG'; atlas.save()
for mat in obj.data.materials:
    # Generated normal/roughness maps exaggerate the lumpy painted surface.
    nodes = mat.node_tree.nodes; nodes.clear()
    output = nodes.new('ShaderNodeOutputMaterial')
    bsdf = nodes.new('ShaderNodeBsdfPrincipled')
    tex = nodes.new('ShaderNodeTexImage'); tex.image = atlas
    bsdf.inputs['Roughness'].default_value = .6
    mat.node_tree.links.new(tex.outputs['Color'],bsdf.inputs['Base Color'])
    mat.node_tree.links.new(bsdf.outputs[0],output.inputs[0])
bm=bmesh.new(); bm.from_mesh(obj.data)
bmesh.ops.dissolve_degenerate(bm,dist=.000001,edges=list(bm.edges))
bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
bm.to_mesh(obj.data);bm.free()
for poly in obj.data.polygons: poly.use_smooth=True
obj.data.update()
obj['cleanup']='De-lit paint atlas, subdued glass reflections, clean geometric normals'
lods=[obj]; report=[]
for level,ratio in [(1,.45),(2,.16)]:
    lod=obj.copy();lod.data=obj.data.copy();bpy.context.collection.objects.link(lod)
    lod.name=f'NPC_CityHatch_LOD{level}'
    bpy.context.view_layer.objects.active=lod
    dec=lod.modifiers.new('Offline silhouette reduction','DECIMATE');dec.ratio=ratio
    dec.use_collapse_triangulate=True
    bpy.ops.object.modifier_apply(modifier=dec.name)
    lods.append(lod)
for level,lod in enumerate(lods):
    bpy.ops.object.select_all(action='DESELECT');lod.select_set(True)
    bpy.context.view_layer.objects.active=lod
    filename='NPC_CityHatch' if level==0 else lod.name
    bpy.ops.export_scene.fbx(filepath=str(OUT/(filename+'.fbx')),use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,bake_space_transform=True,bake_anim=False,path_mode='RELATIVE',use_mesh_modifiers=True)
    bm=bmesh.new();bm.from_mesh(lod.data)
    health={'boundary_edges':sum(e.is_boundary for e in bm.edges),'nonmanifold_edges':sum(not e.is_manifold for e in bm.edges),'loose_vertices':sum(not v.link_faces for v in bm.verts)};bm.free()
    report.append({'level':level,'triangles':sum(len(f.vertices)-2 for f in lod.data.polygons),'vertices':len(lod.data.vertices),**health})
    assert not any(health.values()),report[-1]
    lod.hide_render=level!=0;lod.hide_set(level!=0)
    lod['LOD_level']=level
scene=bpy.context.scene
scene.render.resolution_x=900;scene.render.resolution_y=650
scene.render.resolution_percentage=100;scene.cycles.samples=24
cam=scene.camera
for suffix,position in [('Clean_Front',(1,-1.5,.85)),('Clean_Rear',(-1,1.5,.85))]:
    cam.location=Vector(position)*3.7
    cam.rotation_euler=(Vector((0,0,.9))-cam.location).to_track_quat('-Z','Y').to_euler()
    scene.render.filepath=str(SRC/('NPC_CityHatch_'+suffix+'.png'))
    bpy.ops.render.render(write_still=True)
for level in [1,2]:
    obj.hide_render=True;lods[level].hide_render=False
    scene.render.filepath=str(SRC/f'NPC_CityHatch_Clean_LOD{level}.png')
    bpy.ops.render.render(write_still=True)
    lods[level].hide_render=True
obj.hide_render=False
bpy.ops.object.select_all(action='DESELECT');obj.select_set(True)
bpy.context.view_layer.objects.active=obj
atlas.filepath=bpy.path.relpath(str(OUT/'Textures/NPC_CityHatch_BaseColor.png'),start=str(SRC))
bpy.ops.wm.save_as_mainfile(filepath=str(SRC/'NPC_CityHatch.blend'))
(SRC/'NPC_CityHatch_CleanupReport.json').write_text(json.dumps(report,indent=2))
print('CITYHATCH_CLEANUP',json.dumps(report),flush=True)
