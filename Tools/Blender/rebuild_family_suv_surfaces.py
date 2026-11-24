"""Explicit FamilySUV surface/LOD authoring using the CityHatch modelling helpers."""
from pathlib import Path
import bpy,bmesh,math,json,ast,shutil,hashlib
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[2];SRC=ROOT/'ArtSource/Vehicles/NPCTraffic';OUT=ROOT/'Assets/_Project/Models/Vehicles/NPCTraffic'
NAME='NPC_FamilySUV';BACKUP=SRC/'FamilySUV_BeforeCleanup';BACKUP.mkdir(exist_ok=True)
assets=[SRC/(NAME+'.blend'),OUT/(NAME+'.fbx'),OUT/(NAME+'.fbx.meta'),ROOT/('Assets/_Project/Prefabs/Vehicles/NPCTraffic/'+NAME+'.prefab'),ROOT/('Assets/_Project/Prefabs/Vehicles/NPCTraffic/'+NAME+'.prefab.meta'),SRC/(NAME+'_MeshReport.json'),SRC/(NAME+'_Front.png'),SRC/(NAME+'_Rear.png')]
assets+=list((OUT/'Materials').glob(NAME+'*'))
for p in assets:
    if not (BACKUP/p.name).exists():shutil.copy2(p,BACKUP/p.name)
if not (BACKUP/'Hashes.json').exists():(BACKUP/'Hashes.json').write_text(json.dumps({p.relative_to(ROOT).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in assets},indent=2))
bpy.ops.wm.read_factory_settings(use_empty=True);bpy.context.preferences.filepaths.save_version=0
specs=[('BodyPaint',(.82,.42,.03,1),.32,.27,0),('Glass',(.016,.028,.042,1),.12,.16,0),('Rubber',(.013,.016,.02,1),0,.84,0),('Details',(.036,.045,.057,1),.12,.48,0),('Alloy',(.49,.54,.6,1),.8,.23,0),('Headlamp',(.88,.95,1,1),.12,.19,.25),('TailLamp',(.48,.004,.009,1),.1,.22,.16),('Indicator',(.95,.22,.004,1),.1,.24,.12),('Accent',(.82,.88,.94,1),.15,.29,0)]
mats=[]
for suffix,col,metal,rough,emission in specs:
    m=bpy.data.materials.new(NAME+'_'+suffix);m.use_nodes=True
    bs=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED');bs.inputs['Base Color'].default_value=col
    bs.inputs['Metallic'].default_value=metal;bs.inputs['Roughness'].default_value=rough
    if suffix=='BodyPaint':bs.inputs['Coat Weight'].default_value=.4;bs.inputs['Coat Roughness'].default_value=.2
    if emission:bs.inputs['Emission Color'].default_value=col;bs.inputs['Emission Strength'].default_value=emission
    m.diffuse_color=col;mats.append(m)
# Reuse only the pure modelling helpers; do not execute CityHatch authoring/exports.
helpers={'material','mesh_obj','apply','bevel','box','path','panel','loft','cylinder'}
tree=ast.parse((Path(__file__).with_name('rebuild_city_hatch_surfaces.py')).read_text())
exec(compile(ast.Module(body=[n for n in tree.body if isinstance(n,ast.FunctionDef) and n.name in helpers],type_ignores=[]),'CityHatch modelling helpers','exec'),globals())
base_path=path
def path(name,coords,radius,mat,resolution):
    o=base_path(name,coords,radius,mat,resolution)
    bm=bmesh.new();bm.from_mesh(o.data);boundary=[e for e in bm.edges if e.is_boundary]
    if boundary:bmesh.ops.holes_fill(bm,edges=boundary)
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(o.data);bm.free();return o

def arch(sign,y,segments):
    verts=[]
    for j in range(segments+1):
        a=-.1+(math.pi+.2)*j/segments
        for x,r in [(sign*1.185,.538),(sign*1.225,.538),(sign*1.225,.595),(sign*1.185,.595)]:
            verts.append((x,y+math.cos(a)*r,.51+math.sin(a)*r))
    faces=[(3,2,1,0),tuple(segments*4+i for i in range(4))]
    for j in range(segments):
        for i in range(4):faces.append((j*4+i,j*4+(i+1)%4,(j+1)*4+(i+1)%4,(j+1)*4+i))
    o=mesh_obj('Moulded wheel arch',verts,faces,3)
    for p in o.data.polygons:p.use_smooth=True

def build(level):
    start=set(bpy.data.objects);seg=[24,16,10][level];edge=[2,1,1][level]
    body=loft('SUV body shell',[(-2.14,.98,1.30,.55),(-1.94,1.15,1.43,.53),(-1.15,1.18,1.48,.53),(1.32,1.18,1.48,.53),(1.98,1.13,1.42,.55),(2.14,1.04,1.30,.58)],0,edge)
    for y in [-1.4,1.4]:
        bpy.ops.mesh.primitive_cylinder_add(vertices=seg,radius=.548,depth=3.5,location=(0,y,.51),rotation=(0,math.pi/2,0));cut=bpy.context.object
        mod=body.modifiers.new('Wheel opening','BOOLEAN');mod.operation='DIFFERENCE';mod.object=cut;apply(body,mod);bpy.data.objects.remove(cut,do_unlink=True)
    bevel(body,.012,1)
    rings=[(-1.20,1.43),(-.58,2.18),(-.45,2.25),(1.5,2.25),(1.85,2.05),(2.07,1.45)]
    side_x=lambda z:1.135-.26*(z-1.39)
    verts=[]
    for y,z in rings:verts.extend([(-side_x(z),y,z),(side_x(z),y,z),(1.135,y,1.39),(-1.135,y,1.39)])
    faces=[(3,2,1,0),(20,21,22,23)]
    for j in range(5):
        for i in range(4):faces.append((j*4+i,j*4+(i+1)%4,(j+1)*4+(i+1)%4,(j+1)*4+i))
    cabin=mesh_obj('SUV cabin',verts,faces,0);bevel(cabin,.032,edge)
    def roof(y):
        for a,b in zip(rings,rings[1:]):
            if a[0]<=y<=b[0]:return a[1]+(b[1]-a[1])*(y-a[0])/(b[0]-a[0])
        return rings[-1][1]
    panel('Windscreen',[(x,y,roof(y)+.007) for x,y in [(-.86,-.64),(.86,-.64),(1.015,-1.13),(-1.015,-1.13)]],1)
    panel('Rear glass',[(x,y,roof(y)+.007) for x,y in [(-.91,1.89),(.91,1.89),(.99,2.035),(-.99,2.035)]],1)
    for sign in [-1,1]:
        windows=[('Front door glass',[(-1.055,1.57),(-.54,2.12),(.0,2.12),(.0,1.57)]),('Rear door glass',[(.15,1.57),(.15,2.12),(.93,2.12),(.93,1.57)]),('Cargo quarter glass',[(1.08,1.57),(1.08,2.12),(1.49,2.12),(1.75,1.98),(1.9,1.57)])]
        for label,yz in windows:panel(label,[(sign*(side_x(z)+.007),y,z) for y,z in yz],1,.005)
        for y0,y1 in [(.015,.135),(.95,1.065)]:panel('Window pillar',[(sign*(side_x(z)+.009),y,z) for y,z in [(y0,1.55),(y0,2.14),(y1,2.14),(y1,1.55)]],3)
        box('Running board',(sign*1.195,0,.51),(.16,1.73,.105),3,.025,edge)
        if level<2:
            box('Step tread',(sign*1.22,0,.569),(.10,1.5,.012),2,.005,1)
            for y in [.08,1.0]:path('Door seam',[(sign*1.182,y,1.47),(sign*1.182,y,.85)],.004,3,0)
        for y in [-.24,.78]:box('Door handle',(sign*1.191,y,1.37),(.034,.18,.045),3,.015,edge)
        box('Mirror stem',(sign*1.17,-.79,1.53),(.21,.08,.065),3,.02,edge)
        box('Mirror housing',(sign*1.23,-.82,1.63),(.21,.28,.21),0,.05,edge)
        box('Mirror glass',(sign*1.23,-.672,1.63),(.16,.014,.15),1,.025,edge)
        if level<2:box('Mirror indicator',(sign*1.34,-.84,1.61),(.008,.14,.03),7,.006,1)
        for y in [-1.4,1.4]:
            arch(sign,y,[16,10,7][level])
            profile=[(-.15,.32),(-.15,.415),(-.11,.495),(.085,.51),(.15,.43),(.15,.32)]
            v=[];f=[]
            for dx,r in profile:
                for j in range(seg):
                    a=j*2*math.pi/seg;v.append((sign*(1.07+dx),y+math.sin(a)*r,.51+math.cos(a)*r))
            for k in range(len(profile)):
                for j in range(seg):f.append((k*seg+j,k*seg+(j+1)%seg,((k+1)%len(profile))*seg+(j+1)%seg,((k+1)%len(profile))*seg+j))
            tyre=mesh_obj('SUV tyre',v,f,2)
            for p in tyre.data.polygons:p.use_smooth=True
            cylinder('Alloy rim',(sign*1.22,y,.51),.32,.028,4,seg)
            cylinder('Wheel recess',(sign*1.241,y,.51),.26,.014,3,seg)
            if level<2:
                for j in range(5):
                    a=j*2*math.pi/5
                    spoke=box('Alloy spoke',(sign*1.253,y+math.sin(a)*.15,.51+math.cos(a)*.15),(.026,.058,.252),4,.012,edge);spoke.rotation_euler.x=-a
            cylinder('Hub cap',(sign*1.265,y,.51),.105,.026,4,seg)
            if level==0:
                for j in range(5):
                    a=j*2*math.pi/5;cylinder('Lug',(sign*1.284,y+math.sin(a)*.065,.51+math.cos(a)*.065),.014,.009,3,8)
    box('Front bumper',(0,-2.14,.72),(2.12,.18,.23),3,.07,edge)
    box('Front grille',(0,-2.15,1.18),(1.03,.06,.24),3,.045,edge)
    if level<2:
        for z in [1.12,1.20]:box('Grille slat',(0,-2.187,z),(.88,.018,.014),4,.005,1)
    cylinder('Badge',(0,-2.193,1.20),.078,.02,4,seg,(math.pi/2,0,0))
    path('Bull bar',[(-.61,-2.22,.69),(-.61,-2.22,1.02),(-.50,-2.22,1.10),(.50,-2.22,1.10),(.61,-2.22,1.02),(.61,-2.22,.69)],.052,3,edge-1)
    box('Skid plate',(0,-2.225,.67),(.86,.045,.18),4,.025,edge)
    for sign in [-1,1]:
        box('Headlight housing',(sign*.79,-2.115,1.21),(.43,.10,.27),3,.065,edge)
        box('LED headlight',(sign*.79,-2.173,1.213),(.36,.027,.20),5,.043,edge)
        box('Headlight inset',(sign*.79,-2.191,1.218),(.25,.012,.112),1,.028,edge)
        box('Front indicator',(sign*.94,-2.18,.79),(.17,.022,.06),7,.017,edge)
        box('Tail lamp',(sign*.915,2.092,1.23),(.23,.10,.34),6,.05,edge)
        box('Reverse lamp',(sign*.915,2.151,1.19),(.17,.016,.045),5,.008,1)
        for y in [-.30,1.45]:
            path('Rack foot',[(sign*.84,y,2.235),(sign*.84,y,2.39)],.045,3,edge-1)
        path('Roof rack side rail',[(sign*.84,-.40,2.40),(sign*.84,1.65,2.40)],.038,3,edge-1)
    for y in [-.40,1.65]:path('Roof rack end rail',[(-.84,y,2.40),(.84,y,2.40)],.038,3,edge-1)
    for y in [-.20,.60,1.43]:path('Roof rack crossbar',[(-.83,y,2.365),(.83,y,2.365)],.03,3,edge-1)
    path('Antenna',[(.30,1.51,2.26),(.30,1.77,2.51)],.008,3,1 if level==0 else 0)
    box('Rear bumper',(0,2.143,.72),(2.11,.18,.23),3,.065,edge)
    box('Hatch handle',(0,2.155,1.40),(.25,.04,.045),3,.012,edge)
    box('Rear plate surround',(0,2.17,1.14),(.43,.03,.155),3,.018,edge)
    box('Number plate',(0,2.19,1.14),(.35,.012,.105),8,.008,1)
    box('Tow receiver',(0,2.17,.55),(.12,.20,.095),3,.012,edge)
    if level<2:path('Rear wiper',[(.0,2.04,1.55),(.37,1.978,1.73)],.014,3,1)
    parts=[o for o in bpy.data.objects if o not in start and o.type=='MESH']
    bpy.ops.object.select_all(action='DESELECT')
    for o in parts:o.select_set(True)
    bpy.context.view_layer.objects.active=body;bpy.ops.object.join();o=bpy.context.object
    o.name=NAME+('' if level==0 else '_LOD'+str(level));bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
    old=list(o.data.materials);indices=[mats.index(m) if m else 3 for m in old];labels=[indices[p.material_index] for p in o.data.polygons]
    o.data.materials.clear()
    for m in mats:o.data.materials.append(m)
    for p,i in zip(o.data.polygons,labels):p.material_index=i
    if level==2:
        keep=o.vertex_groups.new(name='Silhouette extremities');keep.add([v.index for v in o.data.vertices if v.co.z>2.30],1,'REPLACE')
        d=o.modifiers.new('Distant detail reduction','DECIMATE');d.ratio=.56;d.use_collapse_triangulate=True;d.vertex_group=keep.name;d.invert_vertex_group=True;apply(o,d)
    bm=bmesh.new();bm.from_mesh(o.data);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(o.data);bm.free()
    o['Surface workflow']='CityHatch-style authored hard surfaces and texture-free PBR materials; opaque glass'
    return o

lods=[build(i) for i in range(3)]
# Use one common scale and ground offset to preserve LOD alignment and the 4.5m source length.
factor=4.5/lods[0].dimensions.y;ground=min(v.co.z for v in lods[0].data.vertices)
reports=[]
for i,o in enumerate(lods):
    for v in o.data.vertices:v.co=(v.co-Vector((0,0,ground)))*factor
    o.data.update();bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o
    bpy.ops.export_scene.fbx(filepath=str(OUT/(o.name+'.fbx')),use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,bake_space_transform=True,bake_anim=False)
    reports.append({'level':i,'triangles':sum(len(p.vertices)-2 for p in o.data.polygons),'vertices':len(o.data.vertices),'size':list(o.dimensions),'materials':len(mats)})
    o.hide_render=i>0;o.hide_set(i>0)
scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=40
scene.world=bpy.data.worlds.new('SUV Studio');scene.world.use_nodes=True
next(n for n in scene.world.node_tree.nodes if n.type=='BACKGROUND').inputs[0].default_value=(.25,.28,.32,1)
for pos,energy in [((4,-5,8),1100),((-5,1,6),850),((0,6,7),1100)]:
    bpy.ops.object.light_add(type='AREA',location=pos);o=bpy.context.object;o.data.energy=energy;o.data.shape='DISK';o.data.size=5
    o.rotation_euler=(Vector((0,0,1.1))-o.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.object.camera_add();cam=bpy.context.object;cam.data.type='ORTHO';cam.data.ortho_scale=5.8;scene.camera=cam
scene.render.resolution_x=1000;scene.render.resolution_y=750;scene.render.resolution_percentage=100;scene.render.image_settings.file_format='PNG'
for suffix,pos in [('PBR_Front',(1,-1.5,.85)),('PBR_Rear',(-1,1.5,.85))]:
    cam.location=Vector(pos)*4.5;cam.rotation_euler=(Vector((0,0,1.1))-cam.location).to_track_quat('-Z','Y').to_euler()
    scene.render.filepath=str(SRC/(NAME+'_'+suffix+'.png'));bpy.ops.render.render(write_still=True)
for i in [1,2]:
    lods[0].hide_render=True;lods[i].hide_render=False;scene.render.filepath=str(SRC/(NAME+f'_PBR_LOD{i}.png'));bpy.ops.render.render(write_still=True);lods[i].hide_render=True
lods[0].hide_render=False;bpy.ops.object.select_all(action='DESELECT');lods[0].select_set(True);bpy.context.view_layer.objects.active=lods[0]
bpy.ops.wm.save_as_mainfile(filepath=str(SRC/(NAME+'.blend')))
(SRC/(NAME+'_CleanupReport.json')).write_text(json.dumps(reports,indent=2))
(SRC/(NAME+'_PBRMaterials.json')).write_text(json.dumps([{'name':NAME+'_'+s,'color_linear':c,'metallic':m,'roughness':r,'emission':e} for s,c,m,r,e in specs],indent=2))
print('FAMILY_SUV_PBR_COMPLETE',json.dumps(reports),flush=True)
