"""Explicit clothing-only update. Preserves FBX geometry, rigs and animation data."""
from pathlib import Path
from collections import defaultdict
import bpy, json, hashlib, shutil, array, sys
from io_scene_fbx import parse_fbx, encode_bin, data_types
from mathutils import Vector, Matrix

ROOT=Path(__file__).resolve().parents[2]
SRC=ROOT/'ArtSource/Characters/Character01Thief'
ASSET=ROOT/'Assets/_Project/Models/Thieves/Character01Thief'
OUT=SRC/'GetawayPalette'
OUT.mkdir(exist_ok=True)
BACK=OUT/'Before';BACK.mkdir(exist_ok=True)
variants=['','Pistol','AssaultRifle','Shotgun']
def source(v):return SRC/('Armed' if v else '')/('Character01_Thief'+('_'+v if v else '')+'.blend')
def asset(v):return ASSET/('Armed' if v else '')/('Character01_Thief'+('_'+v if v else '')+'.fbx')
palette=ASSET/'Character01_Thief_Palette.png'
files=[palette]+[source(v) for v in variants]+[asset(v) for v in variants]
for p in files:
    dest=BACK/p.relative_to(ROOT)
    if dest.exists() and '--restart' not in sys.argv:raise RuntimeError('Backup already exists; inspect before running again: '+str(dest))
for p in files:
    dest=BACK/p.relative_to(ROOT);dest.parent.mkdir(parents=True,exist_ok=True)
    if '--restart' in sys.argv:shutil.copy2(dest,p)
    else:shutil.copy2(p,dest)
meta_hashes={str(p.relative_to(ROOT)):hashlib.sha256(p.read_bytes()).hexdigest() for p in ASSET.rglob('*.meta')}
colours={'burgundy':'8E203D','plum':'381A32','charcoal':'1A181F','gold':'FFB615','white':'E5E0D6'}
slots={k:128+i for i,k in enumerate(colours)}
def uv_for(slot):return ((slot%16+.5)/16,(slot//16+.5)/16)
def slot_for(uv):return min(15,int(uv[0]*16))+16*min(15,int(uv[1]*16))
def geom_key(points):return tuple(sorted(tuple(round(float(x),5) for x in v) for v in points))
def face_key(mesh,poly):return geom_key(mesh.vertices[i].co for i in poly.vertices)
def fingerprint():
    result={}
    for o in bpy.data.objects:
        if o.type=='MESH':
            result[o.name]=hashlib.sha256(repr(([tuple(v.co) for v in o.data.vertices], [tuple(p.vertices) for p in o.data.polygons], [[(g.group,g.weight) for g in v.groups] for v in o.data.vertices])).encode()).hexdigest()
        elif o.type=='ARMATURE':
            result[o.name]=hashlib.sha256(repr([(b.name,b.parent.name if b.parent else '',[tuple(r) for r in b.matrix_local]) for b in o.data.bones]).encode()).hexdigest()
    result['actions']=hashlib.sha256(repr([(a.name,a.frame_range[:],[(c.data_path,c.array_index,[(tuple(k.co),k.interpolation) for k in c.keyframe_points]) for l in a.layers for s in l.strips for bag in s.channelbags for c in bag.fcurves]) for a in bpy.data.actions]).encode()).hexdigest()
    return result

bpy.ops.wm.open_mainfile(filepath=str(source('')))
before=fingerprint()
image=bpy.data.images['Character01_Thief_Palette.png'];pixels=list(image.pixels)
old_colours={i:tuple(pixels[((i//16*16+8)*256+i%16*16+8)*4+k] for k in range(3)) for i in range(128)}
for name,hex_value in colours.items():
    slot=slots[name];rgb=[int(hex_value[k:k+2],16)/255 for k in (0,2,4)]
    for y in range(slot//16*16,slot//16*16+16):
        for x in range(slot%16*16,slot%16*16+16):pixels[(y*256+x)*4:(y*256+x)*4+4]=rgb+[1]
image.pixels[:]=pixels;image.filepath_raw=str(palette);image.save()
if image.packed_file:image.unpack(method='REMOVE')
image.filepath=str(palette);image.reload();image.pack()

# Derive precise garment regions from the editable original's disconnected pieces.
for obj in list(bpy.data.objects):
    if obj.type!='MESH' or obj.name.startswith('Original_'):continue
    mesh=obj.data;uv=mesh.uv_layers.active
    adj=defaultdict(set)
    for e in mesh.edges:
        a,b=e.vertices;adj[a].add(b);adj[b].add(a)
    sizes={};seen=set()
    for v in mesh.vertices:
        if v.index in seen:continue
        stack=[v.index];seen.add(v.index);part=[]
        while stack:
            i=stack.pop();part.append(i)
            for j in adj[i]-seen:seen.add(j);stack.append(j)
        for i in part:sizes[i]=len(part)
    for poly in mesh.polygons:
        old=slot_for(uv.data[poly.loop_start].uv);rgb=old_colours[old];colour=None
        center=sum((obj.matrix_world@mesh.vertices[i].co for i in poly.vertices),Vector())/len(poly.vertices)
        size=sizes[poly.vertices[0]]
        if obj.name=='Character01_Thief_Body':
            if size==921:
                if rgb[0]<rgb[2]*1.3:colour='charcoal' if max(rgb)<.17 else 'burgundy'
                # Existing front-centre coat panels form the concept's dark placket.
                if abs(poly.center.x)<.04 and .81<poly.center.y<1.4 and poly.center.z>.14:colour='charcoal'
            elif size==346:colour='plum'
            elif size==28:colour='charcoal' if center.z>1.05 else 'gold'
            elif size==8:
                colour='gold' if center.z>1.25 and poly.normal.y<-.2 else 'charcoal'
            elif size==24 and old==7:colour='charcoal' # Socks.
            elif size==155 and old==3:colour='charcoal' # Gloves, preserve bare arms.
            elif size==230:
                colour='charcoal' if old==41 else ('gold' if .028<center.z<.045 else 'white')
        elif obj.name=='Thief_Beanie':colour='burgundy'
        elif obj.name in ('Thief_Beanie_RolledCuff','Thief_FittedMask') or 'BackpackStrap' in obj.name:colour='charcoal'
        elif obj.name=='Thief_Backpack':colour='plum'
        elif obj.name=='Thief_BackpackPocket':colour='gold' if center.z>1.23 else 'plum'
        if colour:
            for loop in poly.loop_indices:uv.data[loop].uv=uv_for(slots[colour])
assert before==fingerprint(),'Geometry, weights, rig or animation changed'
bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=str(source('')))
# Joining a temporary copy matches the exact mesh space of the existing exported files.
bpy.ops.object.select_all(action='DESELECT')
for o in bpy.context.scene.objects:
    if o.type=='MESH':o.select_set(True)
bpy.context.view_layer.objects.active=bpy.data.objects['Character01_Thief_Body']
bpy.ops.object.join()
joined=bpy.context.object;mesh=joined.data
face_map={face_key(mesh,p):slot_for(mesh.uv_layers.active.data[p.loop_start].uv) for p in mesh.polygons}

def child(e,name):return next(x for x in e.elems if x.id==name)
def stable(e):
    # These are the only FBX fields permitted to change.
    if e.id in (b'UV',b'UVIndex'):return (e.id,)
    return (e.id,tuple(bytes(p) if isinstance(p,array.array) else p for p in e.props),tuple(stable(c) for c in e.elems))
type_methods={getattr(data_types,n):'add_'+m for n,m in [('BOOL','bool'),('CHAR','char'),('INT8','int8'),('INT16','int16'),('INT32','int32'),('INT64','int64'),('FLOAT32','float32'),('FLOAT64','float64'),('STRING','string'),('BYTES','bytes'),('BOOL_ARRAY','bool_array'),('BYTE_ARRAY','byte_array'),('INT32_ARRAY','int32_array'),('INT64_ARRAY','int64_array'),('FLOAT32_ARRAY','float32_array'),('FLOAT64_ARRAY','float64_array')] if hasattr(data_types,n)}
def encode(e):
    out=encode_bin.FBXElem(e.id)
    for t,p in zip(e.props_type,e.props):getattr(out,type_methods[t])(p)
    out.elems=[encode(c) for c in e.elems]
    return out
report={'palette':colours,'variants':{},'geometry_weights_rigs_and_animations_preserved':True}
for variant in variants:
    path=asset(variant);tree,version=parse_fbx.parse(str(path));original=stable(tree)
    objects=child(tree,b'Objects');matched=0
    for geo in objects.elems:
        if geo.id!=b'Geometry' or geo.props[-1]!=b'Mesh':continue
        vertices=child(geo,b'Vertices').props[0];polygons=child(geo,b'PolygonVertexIndex').props[0]
        points=[vertices[i:i+3] for i in range(0,len(vertices),3)]
        layer=next((x for x in geo.elems if x.id==b'LayerElementUV'),None)
        if layer is None:continue
        faces=[];current=[]
        for i in polygons:
            current.append(i if i>=0 else -i-1)
            if i<0:faces.append(current);current=[]
        keys=[geom_key(points[i] for i in f) for f in faces]
        hits=sum(k in face_map for k in keys)
        if hits<100:continue # Weapons retain all original data.
        assert hits==len(faces),(variant,hits,len(faces))
        uv_values=array.array('d');uv_indices=array.array(data_types.ARRAY_INT32);lookup={}
        for f,key in zip(faces,keys):
            slot=face_map[key]
            if slot not in lookup:
                lookup[slot]=len(lookup);uv_values.extend(uv_for(slot))
            uv_indices.extend([lookup[slot]]*len(f))
        child(layer,b'UV').props[0]=uv_values;child(layer,b'UVIndex').props[0]=uv_indices
        matched+=hits
    assert matched==len(face_map),(variant,matched,len(face_map))
    encode_bin.write(str(path),encode(tree),version)
    check,_=parse_fbx.parse(str(path));assert original==stable(check),'Non-UV FBX data changed'
    report['variants'][variant or 'Unarmed']={'fbx_faces_updated':matched,'all_non_uv_fbx_data_identical':True}

# Update the armed editable masters using the same per-face assignments.
for variant in variants[1:]:
    bpy.ops.wm.open_mainfile(filepath=str(source(variant)));before=fingerprint()
    body=bpy.data.objects['Character01_Thief'];mesh=body.data;uv=mesh.uv_layers.active
    assert all(face_key(mesh,p) in face_map for p in mesh.polygons)
    for p in mesh.polygons:
        for loop in p.loop_indices:uv.data[loop].uv=uv_for(face_map[face_key(mesh,p)])
    for im in bpy.data.images:
        if im.name=='Character01_Thief_Palette.png':
            if im.packed_file:im.unpack(method='REMOVE')
            im.filepath=str(palette);im.reload()
    assert before==fingerprint()
    bpy.ops.wm.save_as_mainfile(filepath=str(source(variant)))
assert all(hashlib.sha256((ROOT/p).read_bytes()).hexdigest()==h for p,h in meta_hashes.items())
report['all_unity_metadata_unchanged']=True
(OUT/'Validation.json').write_text(json.dumps(report,indent=2))

# Preview only: pose and cameras are never saved into the production master.
bpy.ops.wm.open_mainfile(filepath=str(source('')))
rig=bpy.data.objects['Character01_Thief_Rig']
for b in rig.pose.bones:
    if b.name.endswith(('LeftArm','RightArm')):
        head=b.head.copy();angle=.95 if (rig.matrix_world@head).x>0 else -.95
        rotation=rig.matrix_world.inverted()@Matrix.Rotation(angle,4,'Y')@rig.matrix_world
        b.matrix=Matrix.Translation(head)@rotation.to_3x3().to_4x4()@Matrix.Translation(-head)@b.matrix
bpy.context.view_layer.update()
scene=bpy.context.scene
scene.render.resolution_x=750;scene.render.resolution_y=900;scene.render.resolution_percentage=100
for name,position in [('Front',(0,-4,1.25)),('ThreeQuarter',(3,-4,1.7)),('Back',(0,4,1.25))]:
    camera=scene.camera;camera.location=position;camera.rotation_euler=(Vector((0,0,1.05))-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.type='ORTHO';camera.data.ortho_scale=2.55
    scene.render.filepath=str(OUT/(name+'.png'));bpy.ops.render.render(write_still=True)
print('GETAWAY PALETTE COMPLETE',json.dumps(report))
