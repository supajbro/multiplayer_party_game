"""Explicit standalone authoring. Reads depot_02 materials; never saves that file."""
import bpy, math, json, hashlib
from pathlib import Path
from mathutils import Vector

ROOT=Path(__file__).resolve().parents[2]
SRC=ROOT/'ArtSource/Props/RecyclingBin'
OUT=ROOT/'Assets/_Project/Models/Props/RecyclingBin'
SRC.mkdir(parents=True,exist_ok=True); OUT.mkdir(parents=True,exist_ok=True)
reference=ROOT/'Assets/_Project/Models/depot_02/depot_02.blend'
before=hashlib.sha256(reference.read_bytes()).hexdigest()
bpy.ops.wm.read_factory_settings(use_empty=True)
with bpy.data.libraries.load(str(reference),link=False) as (a,b):
    b.materials=[n for n in a.materials if n in ['D02_Silver','D02_Shutter','D02_Steel','D02_Teal','D02_Yellow','D02_Rubber','D02_Light']]
mats={m.name:m for m in b.materials}
def emissive(name,color,strength):
    m=bpy.data.materials.new(name);m.use_nodes=True
    bs=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
    bs.inputs['Base Color'].default_value=(*color,1)
    bs.inputs['Emission Color'].default_value=(*color,1);bs.inputs['Emission Strength'].default_value=strength
    bs.inputs['Roughness'].default_value=.32;m.diffuse_color=(*color,1);mats[name]=m
emissive('RB_GreenEmission',(.10,.95,.025),2.6)
emissive('RB_InteriorGlow',(.015,.16,.025),.8)
s=bpy.context.scene;s.unit_settings.system='METRIC';s.unit_settings.scale_length=1
visual=bpy.data.collections.new('RecyclingBin_Visual');s.collection.children.link(visual)
collision=bpy.data.collections.new('RecyclingBin_Collision');s.collection.children.link(collision)
parts=[];colliders=[]
def own(o,name,mat,coll=False):
    o.name=name
    for c in list(o.users_collection):c.objects.unlink(o)
    (collision if coll else visual).objects.link(o)
    if mat:o.data.materials.append(mats[mat])
    (colliders if coll else parts).append(o);return o
def bevel(o,w=.035,segments=3):
    mod=o.modifiers.new('Soft industrial edges','BEVEL');mod.width=w;mod.segments=segments
    mod=o.modifiers.new('Weighted corner normals','WEIGHTED_NORMAL');mod.keep_sharp=True
def box(name,p,d,mat,bev=.025,rot=0):
    bpy.ops.mesh.primitive_cube_add(size=1,location=p);o=own(bpy.context.object,name,mat);o.dimensions=d
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True);o.rotation_euler.z=rot
    if bev:bevel(o,bev)
    return o
def mesh(name,verts,faces,mat,bev=0,coll=False):
    me=bpy.data.meshes.new(name);me.from_pydata(verts,[],faces);me.update()
    o=bpy.data.objects.new(name,me);s.collection.objects.link(o);own(o,name,mat,coll)
    if bev:bevel(o,bev)
    return o
# Octagonal profile of a closed annular cross-section. No cap spans the mouth.
def ring(name,profile,mat,bev=.025):
    verts=[(r*math.cos(math.pi/8+i*math.pi/4),r*math.sin(math.pi/8+i*math.pi/4),z) for r,z in profile for i in range(8)]
    faces=[]
    for j in range(len(profile)):
        k=(j+1)%len(profile)
        for i in range(8):faces.append((j*8+i,j*8+(i+1)%8,k*8+(i+1)%8,k*8+i))
    return mesh(name,verts,faces,mat,bev)
ring('Body_HollowShell',[(1.45,.20),(1.63,1.34),(1.43,1.34),(1.25,.20)],'D02_Steel',.035)
ring('Base_YellowBumper',[(1.51,.04),(1.57,.12),(1.57,.28),(1.47,.32),(1.27,.32),(1.27,.04)],'D02_Yellow',.04)
ring('Base_RubberFoot',[(1.45,0),(1.47,.05),(1.47,.09),(1.25,.09),(1.25,0)],'D02_Rubber',.018)
ring('Rim_ReinforcedSilver',[(1.63,1.29),(1.72,1.39),(1.72,1.56),(1.65,1.63),(1.39,1.63),(1.34,1.56),(1.34,1.40),(1.43,1.29)],'D02_Silver',.032)
ring('Rim_TealInset',[(1.66,1.57),(1.66,1.61),(1.43,1.61),(1.43,1.57)],'D02_Teal',.012)
ring('Interior_Liner',[(1.40,1.40),(1.23,.27),(1.20,.27),(1.37,1.40)],'RB_InteriorGlow',.012)
# Recessed floor is physical, well below the open rim.
bpy.ops.mesh.primitive_cylinder_add(vertices=8,radius=1.28,depth=.16,location=(0,0,.20),rotation=(0,0,math.pi/8))
floor=own(bpy.context.object,'Interior_RecessedFloor','D02_Rubber');bevel(floor,.025)
for n,z,r in [(0,1.28,1.366),(1,.78,1.292),(2,.30,1.215)]:
    ring('EnergyRing_%02d'%n,[(r,z),(r,z+.025),(r-.027,z+.025),(r-.027,z)],'RB_GreenEmission',.008)
ring('Floor_EnergyHalo',[(1.08,.293),(1.08,.309),(.99,.309),(.99,.293)],'RB_GreenEmission',.008)
# Each broad face has an inset light grey panel, with visible dark seams.
for i in range(8):
    a=-math.pi/2+i*math.pi/4
    n=Vector((math.cos(a),math.sin(a),0));t=Vector((-math.sin(a),math.cos(a),0))
    # Sloping planar trapezoid with thickness; upper edge follows widening body.
    verts=[]
    for offset in [0,.055]:
        for z,apothem,half in [(.34,1.377,.50),(1.27,1.516,.57)]:
            for side in [-1,1]:verts.append(tuple(n*(apothem+offset)+t*(half*side)+Vector((0,0,z))))
    panel=mesh('Panel_%02d'%i,verts,[(0,2,3,1),(4,5,7,6),(0,1,5,4),(2,6,7,3),(0,4,6,2),(1,3,7,5)],'D02_Shutter',.028)
    rot=a-math.pi/2
    # Front badge, alternate side maintenance inserts, and amber strips.
    if i==0:
        box('Front_BadgeHousing',tuple(n*1.55+Vector((0,0,.83))),(.83,.075,.86),'D02_Steel',.07,rot)
        for sign in [-1,1]:
            p=n*1.573+t*(sign*.45)+Vector((0,0,.82))
            box('Front_LampSocket',tuple(p),(.15,.075,.68),'D02_Rubber',.035,rot)
            box('Front_AmberLamp',tuple(p+n*.045),(.075,.035,.54),'D02_Light',.025,rot)
    elif i%2==0:
        box('Service_TealInset',tuple(n*1.56+Vector((0,0,.84))),(.73,.055,.53),'D02_Teal',.045,rot)
        for z in [.70,.80,.90]:box('Service_Louvre',tuple(n*1.60+Vector((0,0,z))),(.46,.035,.028),'D02_Steel',.008,rot)
    else:
        box('Corner_AmberSocket',tuple(n*1.57+Vector((0,0,1.19))),(.57,.07,.13),'D02_Rubber',.025,rot)
        box('Corner_AmberStrip',tuple(n*1.615+Vector((0,0,1.19))),(.43,.03,.055),'D02_Light',.015,rot)
    # Two large readable industrial fasteners, no noisy micro-detail.
    for sign in [-1,1]:
        p=n*1.575+t*(sign*.47)+Vector((0,0,1.20))
        bpy.ops.mesh.primitive_uv_sphere_add(segments=8,ring_count=4,radius=.033,location=p)
        own(bpy.context.object,'Panel_Fastener','D02_Silver')
# Real vector recycling glyph, converted to mesh for portable FBX export.
cu=bpy.data.curves.new('Recycling emblem','FONT');cu.body='♻';cu.align_x='CENTER';cu.align_y='CENTER'
cu.font=bpy.data.fonts.load('C:/Windows/Fonts/seguisym.ttf');cu.size=1.35;cu.extrude=.006;cu.bevel_depth=.003;cu.bevel_resolution=1;cu.resolution_u=4
o=bpy.data.objects.new('Front_RecyclingEmblem',cu);s.collection.objects.link(o);own(o,o.name,'RB_GreenEmission')
o.location=(0,-1.607,.83);o.rotation_euler=(math.pi/2,0,0)
bpy.context.view_layer.objects.active=o;o.select_set(True);bpy.ops.object.convert(target='MESH');o.select_set(False)
# Eight independent watertight convex collision prisms, each following one wall.
# A convex hull over the entire bin would incorrectly seal the opening.
for i in range(8):
    a=math.pi/8+i*math.pi/4;b=a+math.pi/4
    v=[]
    for z,inner,outer in [(.12,1.21,1.58),(1.63,1.34,1.72)]:
        for r,ang in [(inner,a),(outer,a),(outer,b),(inner,b)]:v.append((r*math.cos(ang),r*math.sin(ang),z))
    mesh('COL_Wall_%02d'%i,v,[(3,2,1,0),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)],None,coll=True)
bpy.ops.mesh.primitive_cylinder_add(vertices=8,radius=1.30,depth=.25,location=(0,0,.125),rotation=(0,0,math.pi/8))
own(bpy.context.object,'COL_Floor',None,True)
# Finalize only newly authored meshes. Preserve separate modular pieces and materials.
for o in parts+colliders:
    bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o
    for mod in list(o.modifiers):bpy.ops.object.modifier_apply(modifier=mod.name)
    bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
    bpy.context.scene.cursor.location=(0,0,0);bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
    bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');bpy.ops.mesh.normals_make_consistent(inside=False)
    if o in parts:bpy.ops.uv.smart_project(angle_limit=1.1519,island_margin=.025)
    bpy.ops.object.mode_set(mode='OBJECT')
def export(path,objs):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:o.select_set(True)
    bpy.ops.export_scene.fbx(filepath=str(path),use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,bake_space_transform=True,bake_anim=False,path_mode='RELATIVE',use_mesh_modifiers=True)
exportparts=[]
bpy.ops.object.select_all(action='DESELECT')
for o in parts:
    dup=o.copy();dup.data=o.data.copy();s.collection.objects.link(dup);dup.select_set(True);exportparts.append(dup)
bpy.context.view_layer.objects.active=exportparts[0];bpy.ops.object.join()
lod0=bpy.context.object;lod0.name='RecyclingBin_LOD0'
# Consolidate identical slots to nine material batches. Source components stay editable.
unique=[];remap=[]
for m in lod0.data.materials:
    if m not in unique:unique.append(m)
    remap.append(unique.index(m))
indices=[remap[p.material_index] for p in lod0.data.polygons]
lod0.data.materials.clear()
for m in unique:lod0.data.materials.append(m)
for p,i in zip(lod0.data.polygons,indices):p.material_index=i
lods=[lod0]
for level,ratio in [(1,.45),(2,.18)]:
    o=lod0.copy();o.data=lod0.data.copy();s.collection.objects.link(o);o.name='RecyclingBin_LOD%d'%level
    bpy.context.view_layer.objects.active=o
    mod=o.modifiers.new('Distance simplification','DECIMATE');mod.ratio=ratio
    bpy.ops.object.modifier_apply(modifier=mod.name);lods.append(o)
lodstats=[]
for o in lods:
    o.data.calc_loop_triangles();lodstats.append({'name':o.name,'triangles':len(o.data.loop_triangles),'materialSlots':len(o.data.materials)})
export(OUT/'recycling_bin.fbx',lods)
for o in lods:bpy.data.objects.remove(o,do_unlink=True)
export(OUT/'recycling_bin_collision.fbx',colliders)
collision.hide_render=True;collision.hide_viewport=True
# Independent source file has no depot objects or gameplay components.
world=bpy.data.worlds.new('Preview Studio');world.use_nodes=True
next(n for n in world.node_tree.nodes if n.type=='BACKGROUND').inputs[0].default_value=(.12,.16,.21,1);s.world=world
for p,power,size in [((2,-4,6),650,5),((-4,-1,3),450,4),((1,3,5),800,3)]:
    bpy.ops.object.light_add(type='AREA',location=p);o=bpy.context.object;o.name='PREVIEW_Light';o.data.energy=power;o.data.shape='DISK';o.data.size=size;o.rotation_euler=(-o.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.object.camera_add(location=(3.6,-6,4.2));camera=bpy.context.object;camera.name='PREVIEW_Camera'
camera.rotation_euler=(Vector((0,0,.78))-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.type='ORTHO';camera.data.ortho_scale=4.65;s.camera=camera
s.render.engine='CYCLES';s.cycles.samples=32;s.cycles.use_denoising=True
s.render.resolution_x=1100;s.render.resolution_y=1000;s.render.resolution_percentage=100
s.render.image_settings.file_format='PNG';s.render.film_transparent=True
bpy.ops.wm.save_as_mainfile(filepath=str(SRC/'recycling_bin.blend'))
s.render.filepath=str(SRC/'recycling_bin_preview.png');bpy.ops.render.render(write_still=True)
camera.location=(0,-5,5.8);camera.rotation_euler=(Vector((0,0,.75))-camera.location).to_track_quat('-Z','Y').to_euler()
s.render.filepath=str(SRC/'recycling_bin_opening.png');bpy.ops.render.render(write_still=True)
stats={}
for name,objs in [('visual',parts),('collision',colliders)]:
    for o in objs:o.data.calc_loop_triangles()
    stats[name]={'objects':len(objs),'triangles':sum(len(o.data.loop_triangles) for o in objs)}
stats.update(lods=lodstats,referenceHash=before,referenceUnchanged=hashlib.sha256(reference.read_bytes()).hexdigest()==before,openingAcrossFlats=2*1.34*math.cos(math.pi/8),height=1.63,sourceUnits='metres',origin='bottom centre',materials=list(mats))
(SRC/'validation.json').write_text(json.dumps(stats,indent=2))
assert stats['referenceUnchanged'];print('BIN_COMPLETE',json.dumps(stats))
