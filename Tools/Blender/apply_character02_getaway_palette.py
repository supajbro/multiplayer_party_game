"""Match Character01's outfit colours; preserve geometry/rig/clips and save T-pose."""
from pathlib import Path
from collections import defaultdict
import bpy,json,hashlib,shutil,array,ast
from io_scene_fbx import parse_fbx,encode_bin,data_types
from mathutils import Vector,Matrix
ROOT=Path(__file__).resolve().parents[2];SRC=ROOT/'ArtSource/Characters/Character02Thief';ASSET=ROOT/'Assets/_Project/Models/Thieves/Character02Thief'
OUT=SRC/'GetawayPalette';OUT.mkdir(exist_ok=True);BACK=OUT/'Before';BACK.mkdir(exist_ok=True)
variants=['','Pistol','AssaultRifle','Shotgun']
def source(v):return SRC/('Armed' if v else '')/('Character02_Thief'+('_'+v if v else '')+'.blend')
def asset(v):return ASSET/('Armed' if v else '')/('Character02_Thief'+('_'+v if v else '')+'.fbx')
palette=ASSET/'Character02_Thief_Palette.png'
for p in [palette]+[source(v) for v in variants]+[asset(v) for v in variants]:
    backup=BACK/p.relative_to(ROOT);assert not backup.exists(),'Already authored; preserve later edits before rerunning'
    backup.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(p,backup)
meta_hashes={p.relative_to(ROOT).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in ASSET.rglob('*.meta')}
# Reuse the proven UV-only FBX editing helpers without running Character01's authoring.
tree=ast.parse(Path(__file__).with_name('apply_character01_getaway_palette.py').read_text())
names={'uv_for','slot_for','geom_key','face_key','fingerprint','child','stable','encode'}
nodes=[n for n in tree.body if isinstance(n,ast.FunctionDef) and n.name in names or isinstance(n,ast.Assign) and any(isinstance(t,ast.Name) and t.id=='type_methods' for t in n.targets)]
exec(compile(ast.Module(body=nodes,type_ignores=[]),'Palette editing helpers','exec'),globals())
shutil.copy2(ROOT/'Assets/_Project/Models/Thieves/Character01Thief/Character01_Thief_Palette.png',palette)
slots={'burgundy':128,'plum':129,'charcoal':130,'gold':131,'white':132}
bpy.ops.wm.open_mainfile(filepath=str(source('')));before=fingerprint()
body=bpy.data.objects['Character02_Thief'];mesh=body.data;uv=mesh.uv_layers.active
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
for p in mesh.polygons:
    old=slot_for(uv.data[p.loop_start].uv);size=sizes[p.vertices[0]]
    center=sum((body.matrix_world@mesh.vertices[i].co for i in p.vertices),Vector())/len(p.vertices);colour=None
    if size==958:colour='burgundy' if old==72 else 'charcoal' if old==93 else None
    elif size==298:colour='plum'
    elif size in [36,79,312,30]:colour='charcoal'
    elif size==218:colour='gold' if .028<center.z<.045 else 'white' if old==42 else 'charcoal'
    elif size==144:colour='burgundy'
    elif size in [96,924,24]:colour='charcoal'
    elif size==32 and old==96:colour='plum'
    elif size==32 and old==97:colour='gold' if center.z>1.29 else 'plum'
    if colour:
        for i in p.loop_indices:uv.data[i].uv=uv_for(slots[colour])
assert before==fingerprint(),'Non-UV source data changed'
face_map={face_key(mesh,p):slot_for(uv.data[p.loop_start].uv) for p in mesh.polygons}
report={'palette_matches_character01':True,'variants':{},'metadata_preserved':True}
def finish_source():
    for mat in bpy.data.materials:
        if not mat.name.startswith('Character02_Thief') or not mat.node_tree:continue
        for node in mat.node_tree.nodes:
            if node.type=='TEX_IMAGE':
                node.image=bpy.data.images.load(str(palette),check_existing=False);node.image.pack()
    rig=bpy.data.objects['Character02_Thief_Rig']
    if rig.animation_data:rig.animation_data.action=None
    for p in rig.pose.bones:p.matrix_basis.identity()
    bpy.context.view_layer.update();assert all(p.matrix_basis==Matrix.Identity(4) for p in rig.pose.bones)
    bpy.context.preferences.filepaths.save_version=0
finish_source();bpy.ops.wm.save_as_mainfile(filepath=str(source('')))
for v in variants:
    path=asset(v);tree,version=parse_fbx.parse(str(path));old=stable(tree);matched=0
    for geo in child(tree,b'Objects').elems:
        if geo.id!=b'Geometry' or geo.props[-1]!=b'Mesh':continue
        verts=child(geo,b'Vertices').props[0];indices=child(geo,b'PolygonVertexIndex').props[0];points=[verts[i:i+3] for i in range(0,len(verts),3)]
        layer=next((e for e in geo.elems if e.id==b'LayerElementUV'),None)
        if layer is None:continue
        faces=[];current=[]
        for i in indices:
            current.append(i if i>=0 else -i-1)
            if i<0:faces.append(current);current=[]
        keys=[geom_key(points[i] for i in f) for f in faces];hits=sum(k in face_map for k in keys)
        if hits<100:continue
        assert hits==len(faces),(v,hits,len(faces))
        values=array.array('d');uv_indices=array.array(data_types.ARRAY_INT32);lookup={}
        for f,k in zip(faces,keys):
            slot=face_map[k]
            if slot not in lookup:lookup[slot]=len(lookup);values.extend(uv_for(slot))
            uv_indices.extend([lookup[slot]]*len(f))
        child(layer,b'UV').props[0]=values;child(layer,b'UVIndex').props[0]=uv_indices;matched+=hits
    assert matched==len(face_map),(v,matched,len(face_map))
    encode_bin.write(str(path),encode(tree),version);check,_=parse_fbx.parse(str(path));assert old==stable(check),'Non-UV FBX data changed'
    if v:
        bpy.ops.wm.open_mainfile(filepath=str(source(v)));before=fingerprint();mesh=bpy.data.objects['Character02_Thief'].data;uv=mesh.uv_layers.active
        for p in mesh.polygons:
            for i in p.loop_indices:uv.data[i].uv=uv_for(face_map[face_key(mesh,p)])
        assert before==fingerprint();finish_source();bpy.ops.wm.save_as_mainfile(filepath=str(source(v)))
    report['variants'][v or 'Unarmed']={'faces_updated':matched,'all_non_uv_fbx_data_identical':True,'source_saved_in_t_pose':True}
assert all(hashlib.sha256((ROOT/p).read_bytes()).hexdigest()==h for p,h in meta_hashes.items())
(OUT/'Validation.json').write_text(json.dumps(report,indent=2))
# Preview uses the saved T-pose and never changes the source pose.
bpy.ops.wm.open_mainfile(filepath=str(source('')))
scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=24
scene.render.resolution_x=750;scene.render.resolution_y=900;scene.render.resolution_percentage=100
if not scene.world:scene.world=bpy.data.worlds.new('Preview world')
scene.world.color=(.25,.25,.25)
for pos,power in [((3,-4,5),500),((-4,-2,3),250),((1,4,4),400)]:
    bpy.ops.object.light_add(type='AREA',location=pos);o=bpy.context.object;o.data.energy=power;o.data.size=4;o.rotation_euler=(Vector((0,0,1))-o.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.object.camera_add();cam=bpy.context.object;scene.camera=cam;cam.data.type='ORTHO';cam.data.ortho_scale=2.65
cam.location=(3,-5,1.7);cam.rotation_euler=(Vector((0,0,1.05))-cam.location).to_track_quat('-Z','Y').to_euler();scene.render.filepath=str(OUT/'Tpose_Quarter.png');bpy.ops.render.render(write_still=True)
print('CHARACTER02_PALETTE_AND_TPOSE_PASS',json.dumps(report),flush=True)
