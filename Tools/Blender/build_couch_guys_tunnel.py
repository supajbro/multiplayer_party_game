"""Explicit Blender authoring; metre-scale, floorless modules for the existing roads."""
import bpy, bmesh, math, json, sys
from pathlib import Path
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Assets/_Project/Models/Environment/Tunnel'
SOURCE = ROOT / 'ArtSource/Environment/Tunnel'
OUT.mkdir(parents=True, exist_ok=True)
SOURCE.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.preferences.filepaths.save_version = 0
scene = bpy.context.scene
scene.unit_settings.system = 'METRIC'
mats = []
for name, color in [('Tunnel_Concrete',(.53,.55,.61,1)), ('Tunnel_Interior',(.29,.31,.34,1)),
                    ('Tunnel_Yellow',(.95,.56,.025,1)), ('Tunnel_Dark',(.075,.085,.10,1)),
                    ('Tunnel_Light',(1,.82,.36,1)), ('Tunnel_Embankment',(.30,.32,.28,1)),
                    ('Tunnel_Grass',(.12,.32,.035,1))]:
    m = bpy.data.materials.new(name); m.diffuse_color = color; m.use_nodes = True
    bs = next(n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    bs.inputs['Base Color'].default_value = color
    bs.inputs['Roughness'].default_value = .82
    if name == 'Tunnel_Light':
        bs.inputs['Emission Color'].default_value = color
        bs.inputs['Emission Strength'].default_value = 2
    mats.append(m)

def mesh(name, verts, faces, mat, bevel=0):
    me=bpy.data.meshes.new(name); me.from_pydata(verts,[],faces); me.update()
    bm=bmesh.new(); bm.from_mesh(me); bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    # Open grassy slope strips have no enclosed volume to establish their outside.
    # Keep their visible/collidable side facing upward explicitly.
    if mat == 6:
        bm.normal_update()
        bmesh.ops.reverse_faces(bm,faces=[f for f in bm.faces if f.normal.z < 0])
    bm.to_mesh(me); bm.free()
    ob=bpy.data.objects.new(name,me); scene.collection.objects.link(ob); ob.data.materials.append(mats[mat])
    bpy.context.view_layer.objects.active=ob; ob.select_set(True)
    if bevel:
        mod=ob.modifiers.new('Soft concrete edges','BEVEL'); mod.width=bevel; mod.segments=1
        bpy.ops.object.modifier_apply(modifier=mod.name)
        mod=ob.modifiers.new('Weighted corner normals','WEIGHTED_NORMAL'); mod.keep_sharp=True
        bpy.ops.object.modifier_apply(modifier=mod.name)
    ob.select_set(False)
    return ob

def box(name, pos, size, mat, bevel=.025):
    x,y,z=pos; a,b,c=[v/2 for v in size]
    vs=[(x+i*a,y+j*b,z+k*c) for i,j,k in [(-1,-1,-1),(1,-1,-1),(1,1,-1),(-1,1,-1),(-1,-1,1),(1,-1,1),(1,1,1),(-1,1,1)]]
    return mesh(name,vs,[(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)],mat,bevel)

def arch(name, depth, mat, gap=0, inner=(6.4,4.6), outer=(7.15,5.35), center=0):
    objects=[]
    for i in range(12):
        a=i*math.pi/12+gap; b=(i+1)*math.pi/12-gap
        poly=[(rx*math.cos(t),3.4+rz*math.sin(t)) for rx,rz,t in
              [(inner[0],inner[1],a),(outer[0],outer[1],a),(outer[0],outer[1],b),(inner[0],inner[1],b)]]
        vs=[(x,center+y,z) for y in [-depth/2,depth/2] for x,z in poly]
        objects.append(mesh(name+'_%02d'%i,vs,[(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)],2 if name=='Portal' and i in [5,6] else mat,.025 if gap else 0))
    return objects

def join_export(name, objects):
    bpy.ops.object.select_all(action='DESELECT')
    for ob in objects: ob.select_set(True)
    bpy.context.view_layer.objects.active=objects[0]; bpy.ops.object.join(); ob=bpy.context.object; ob.name=name
    scene.cursor.location=(0,0,0); bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
    if '--embankment-only' not in sys.argv or name == 'Tunnel_Embankment':
        bpy.ops.export_scene.fbx(filepath=str(OUT/(name+'.fbx')),use_selection=True,object_types={'MESH'},
            axis_forward='-Z',axis_up='Y',global_scale=1,apply_unit_scale=True,bake_space_transform=True,
            use_mesh_modifiers=True,mesh_smooth_type='FACE',bake_anim=False,add_leaf_bones=False)
    return ob

# One repeatable 3 m lining bay; closed outer surfaces, absolutely no floor across the road.
parts=arch('Lining',3,1)
for side in [-1,1]:
    parts.append(box('Wall',(side*6.775,0,1.4),(.75,3,4),1,0))
    parts.append(box('Yellow safety plinth',(side*6.35,0,.46),(.15,3,.65),2))
    parts.append(box('Panel joint',(side*6.37,-1.47,1.85),(.045,.045,3.1),3,0))
    parts.append(box('Low lamp frame',(side*6.24,0,.48),(.12,.55,.28),3))
    parts.append(box('Low lamp lens',(side*6.17,0,.48),(.035,.39,.15),4,.01))
    # Two small chevrons on each wall, parallel to the road.
    for offset in [-.32,.32]:
        points=[(-.30,0),(-.02,0),(.30,.42),(-.02,.84),(-.30,.84),(.02,.42)]
        vs=[(side*x,y+offset,1.7+z) for x in [6.385,6.365] for y,z in points]
        n=len(points); faces=[tuple(reversed(range(n))),tuple(range(n,2*n))]+[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
        parts.append(mesh('Direction chevron',vs,faces,2))
parts.append(box('Ceiling light housing',(0,0,7.91),(1.2,.85,.20),3,.045))
parts.append(box('Ceiling light lens',(0,0,7.79),(.96,.63,.055),4))
bay=join_export('Tunnel_Bay_3m',parts)

parts=arch('Portal',1.15,0,.003,inner=(6.4,4.6),outer=(7.5,5.7))
for side in [-1,1]:
    parts.append(box('Portal pier',(side*6.95,0,1.40),(1.1,1.15,4),0,.06))
    parts.append(box('Pier yellow foot',(side*7.0,-.05,.16),(1.3,1.35,.24),2))
    parts.append(box('Hazard inset',(side*6.98,-.601,.94),(.73,.08,1.25),3))
    # Restrained single diagonal hazard slash per pier.
    ob=box('Hazard stripe',(side*6.98,-.65,.94),(.21,.025,1.28),2,.01)
    center=Vector((side*6.98,-.65,.94))
    from mathutils import Matrix
    rot=Matrix.Rotation(.42,4,'Y')
    for v in ob.data.vertices: v.co=center+rot.to_3x3()@(v.co-center)
    parts.append(ob)
    # Buried triangular side skirts hide small terrain sampling gaps, inside reserved footprint.
    vs=[(side*x,y,z) for y in [-.55,1.5] for x,z in [(7.1,-1.2),(7.86,-1.2),(7.86,-.15),(7.1,2.9)]]
    parts.append(mesh('Sloping terrain skirt',vs,[(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)],0))
portal=join_export('Tunnel_Portal',parts)
# Small faceted hill envelope: rock entrance rims, grass slopes, deeply buried outer edges.
# The opening stays hollow; no face crosses the road or the tunnel interior.
parts=[]
for front in [-1,1]:
    inner=[(7.49*math.cos(i*math.pi/12),front*5.0,3.4+5.69*math.sin(i*math.pi/12)) for i in range(13)]
    outer=[(18.0*math.cos(i*math.pi/12),front*3.9,3.4+7.4*math.sin(i*math.pi/12)) for i in range(13)]
    parts.append(mesh('Rock entrance transition',inner+outer,[(i,i+1,i+14,i+13) for i in range(12)],5))
for i in range(12):
    a=i*math.pi/12; b=(i+1)*math.pi/12
    verts=[(18*math.cos(t),y,3.4+7.4*math.sin(t)+(0.6 if y==0 else 0)) for y in [-3.9,0,3.9] for t in [a,b]]
    parts.append(mesh('Grass hill crown',verts,[(0,1,3,2),(2,3,5,4)],6))
for side in [-1,1]:
    # Deep inner backfill closes the gap below the road-height shell on raised approaches.
    verts=[(side*x,y,z) for y in [-5,5] for x,z in [(7.10,-22.0),(18,-22.0),(18,3.4),(7.49,3.4)]]
    parts.append(mesh('Buried shoulder core',verts,[(0,3,2,1),(4,5,6,7),(1,2,6,5),(2,3,7,6),(3,0,4,7)],5))
    # Broad grass wings reach into the existing hills rather than ending beside the portal.
    # Both lateral and longitudinal edges taper underground, leaving no exposed slab edge.
    profiles=[(7.49,5,3.4,3.4),(18,5,3.4,3.4),(26,9,1,-12),(36,14,-5.8,-18),(50,20,-22,-22)]
    fractions=[-1,-.65,0,.65,1]
    verts=[]
    for x,half_length,crest,edge in profiles:
        for fraction in fractions:
            height=crest+(edge-crest)*(abs(fraction)**2)
            verts.append((side*x,fraction*half_length,height))
    faces=[]
    for i in range(len(profiles)-1):
        for j in range(len(fractions)-1):
            a=i*len(fractions)+j; b=a+len(fractions)
            faces.append((a,a+1,b+1,b) if side<0 else (a,b,b+1,a+1))
    parts.append(mesh('Extended grassy hill shoulder',verts,faces,6))
    for front in [-1,1]:
        edge=[(side*x,front*half_length,height) for x,half_length,crest,height in profiles]
        skirt=edge+[(x,y,-22) for x,y,z in edge]
        parts.append(mesh('Buried wing edge',skirt,[(i,i+1,i+6,i+5) for i in range(4)],5))
embankment=join_export('Tunnel_Embankment',parts)
# Saved source displays a single assembled 12 m tunnel, with source modules linked and editable.
portal.location.y=-5.5
back=portal.copy(); back.data=portal.data; scene.collection.objects.link(back); back.location.y=5.5; back.rotation_euler.z=math.pi
bay.location.y=-4.5
for y in [-1.5,1.5,4.5]:
    ob=bay.copy(); ob.data=bay.data; scene.collection.objects.link(ob); ob.location.y=y
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/'CouchGuys_Tunnel.blend'))
stats={ob.name:sum(len(p.vertices)-2 for p in ob.data.polygons) for ob in [bay,portal,embankment]}
(SOURCE/'mesh_report.json').write_text(json.dumps(stats,indent=2))
print('TUNNEL_EXPORTED',stats)
