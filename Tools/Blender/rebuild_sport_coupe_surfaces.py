"""Explicit SportCoupe hard-surface/PBR/LOD authoring; no runtime builders."""
from pathlib import Path
import bpy,bmesh,math,json,ast,shutil,hashlib
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[2];SRC=ROOT/'ArtSource/Vehicles/NPCTraffic';OUT=ROOT/'Assets/_Project/Models/Vehicles/NPCTraffic'
NAME='NPC_SportCoupe';BACKUP=SRC/'SportCoupe_BeforeCleanup';BACKUP.mkdir(exist_ok=True)
assets=[SRC/(NAME+'.blend'),OUT/(NAME+'.fbx'),OUT/(NAME+'.fbx.meta'),ROOT/('Assets/_Project/Prefabs/Vehicles/NPCTraffic/'+NAME+'.prefab'),ROOT/('Assets/_Project/Prefabs/Vehicles/NPCTraffic/'+NAME+'.prefab.meta'),SRC/(NAME+'_MeshReport.json'),SRC/(NAME+'_Front.png'),SRC/(NAME+'_Rear.png')]+list((OUT/'Materials').glob(NAME+'*'))
for p in assets:
    if not (BACKUP/p.name).exists():shutil.copy2(p,BACKUP/p.name)
if not (BACKUP/'Hashes.json').exists():(BACKUP/'Hashes.json').write_text(json.dumps({p.relative_to(ROOT).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in assets},indent=2))
bpy.ops.wm.read_factory_settings(use_empty=True);bpy.context.preferences.filepaths.save_version=0
specs=[('BodyPaint',(.23,.025,.60,1),.32,.27,0),('Glass',(.016,.028,.042,1),.12,.16,0),('Rubber',(.013,.016,.02,1),0,.84,0),('Details',(.036,.045,.057,1),.12,.48,0),('Alloy',(.49,.54,.6,1),.8,.23,0),('Headlamp',(.88,.95,1,1),.12,.19,.25),('TailLamp',(.48,.004,.009,1),.1,.22,.16),('Indicator',(.95,.22,.004,1),.1,.24,.12),('Stripe',(.82,.88,.94,1),.15,.29,0)]
mats=[]
for suffix,col,metal,rough,emission in specs:
    m=bpy.data.materials.new(NAME+'_'+suffix);m.use_nodes=True
    bs=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED');bs.inputs['Base Color'].default_value=col
    bs.inputs['Metallic'].default_value=metal;bs.inputs['Roughness'].default_value=rough
    if suffix=='BodyPaint':bs.inputs['Coat Weight'].default_value=.4;bs.inputs['Coat Roughness'].default_value=.2
    if emission:bs.inputs['Emission Color'].default_value=col;bs.inputs['Emission Strength'].default_value=emission
    m.diffuse_color=col;mats.append(m)
helpers={'material','mesh_obj','apply','bevel','box','path','panel','loft','cylinder'}
tree=ast.parse(Path(__file__).with_name('rebuild_city_hatch_surfaces.py').read_text())
exec(compile(ast.Module(body=[n for n in tree.body if isinstance(n,ast.FunctionDef) and n.name in helpers],type_ignores=[]),'CityHatch modelling helpers','exec'),globals())
base_path=path
def path(name,coords,radius,mat,resolution):
    o=base_path(name,coords,radius,mat,resolution);bm=bmesh.new();bm.from_mesh(o.data)
    boundary=[e for e in bm.edges if e.is_boundary]
    if boundary:bmesh.ops.holes_fill(bm,edges=boundary)
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(o.data);bm.free();return o

def build(level):
    start=set(bpy.data.objects);seg=[24,16,10][level];edge=[2,1,1][level]
    body=loft('Coupe body shell',[(-2.05,.98,.92,.40),(-1.82,1.10,1.02,.38),(-1.05,1.15,1.13,.38),(.65,1.16,1.13,.38),(1.80,1.10,1.13,.40),(2.06,.99,.96,.42)],0,edge)
    for y in [-1.31,1.31]:
        bpy.ops.mesh.primitive_cylinder_add(vertices=seg,radius=.475,depth=3.4,location=(0,y,.44),rotation=(0,math.pi/2,0));cut=bpy.context.object
        d=body.modifiers.new('Wheel opening','BOOLEAN');d.operation='DIFFERENCE';d.object=cut;apply(body,d);bpy.data.objects.remove(cut,do_unlink=True)
    bevel(body,.012,1)
    rings=[(-1.0,1.13),(-.45,1.64),(-.28,1.72),(.70,1.72),(1.32,1.20),(1.43,1.13)]
    side_x=lambda z:1.075-.35*(z-1.09)
    verts=[]
    for y,z in rings:verts.extend([(-side_x(z),y,z),(side_x(z),y,z),(1.0855,y,1.06),(-1.0855,y,1.06)])
    faces=[(3,2,1,0),(20,21,22,23)]
    for j in range(5):
        for i in range(4):faces.append((j*4+i,j*4+(i+1)%4,(j+1)*4+(i+1)%4,(j+1)*4+i))
    cabin=mesh_obj('Fastback cabin',verts,faces,0);bevel(cabin,.03,edge)
    def roof(y):
        for a,b in zip(rings,rings[1:]):
            if a[0]<=y<=b[0]:return a[1]+(b[1]-a[1])*(y-a[0])/(b[0]-a[0])
        return rings[-1][1]
    panel('Windscreen',[(x,y,roof(y)+.007) for x,y in [(-.80,-.49),(.80,-.49),(.96,-.94),(-.96,-.94)]],1)
    panel('Fastback rear glass',[(x,y,roof(y)+.007) for x,y in [(-.80,.76),(.80,.76),(.96,1.27),(-.96,1.27)]],1)
    for sign in [-1,1]:
        for label,yz in [('Door glass',[(-.83,1.15),(-.43,1.60),(.38,1.60),(.55,1.49),(.60,1.15)]),('Rear quarter glass',[(.73,1.15),(.67,1.48),(.82,1.44),(1.22,1.15)])]:
            panel(label,[(sign*(side_x(z)+.007),y,z) for y,z in yz],1,.005)
        panel('Rear pillar',[(sign*(side_x(z)+.009),y,z) for y,z in [(.615,1.14),(.565,1.52),(.655,1.48),(.715,1.14)]],3)
        box('Sport sill',(sign*1.116,0,.43),(.09,1.76,.10),3,.025,edge)
        if level<2:
            path('Door seam',[(sign*1.163,.63,1.10),(sign*1.163,.64,.60)],.004,3,0)
            box('Door handle',(sign*1.171,.36,1.02),(.028,.16,.035),3,.012,edge)
        box('Mirror stem',(sign*1.107,-.72,1.15),(.18,.07,.05),3,.018,edge)
        box('Mirror housing',(sign*1.18,-.75,1.22),(.20,.23,.15),0,.043,edge)
        box('Mirror glass',(sign*1.18,-.626,1.22),(.15,.012,.10),1,.02,edge)
        if level<2:box('Mirror indicator',(sign*1.284,-.77,1.20),(.008,.12,.023),7,.005,1)
        for y in [-1.31,1.31]:
            profile=[(-.135,.30),(-.14,.355),(-.10,.43),(.085,.44),(.145,.375),(.145,.30)]
            v=[];f=[]
            for dx,r in profile:
                for j in range(seg):
                    a=j*2*math.pi/seg;v.append((sign*(1.02+dx),y+math.sin(a)*r,.44+math.cos(a)*r))
            for k in range(len(profile)):
                for j in range(seg):f.append((k*seg+j,k*seg+(j+1)%seg,((k+1)%len(profile))*seg+(j+1)%seg,((k+1)%len(profile))*seg+j))
            tyre=mesh_obj('Low profile tyre',v,f,2)
            for p in tyre.data.polygons:p.use_smooth=True
            cylinder('Alloy rim',(sign*1.17,y,.44),.30,.026,4,seg)
            cylinder('Wheel recess',(sign*1.19,y,.44),.252,.014,3,seg)
            if level<2:
                for j in range(5):
                    a=j*2*math.pi/5
                    spoke=box('Sport alloy spoke',(sign*1.204,y+math.sin(a)*.145,.44+math.cos(a)*.145),(.026,.047,.255),4,.012,edge);spoke.rotation_euler.x=-a
            cylinder('Hub cap',(sign*1.216,y,.44),.085,.026,4,seg)
            if level==0:
                for j in range(5):
                    a=j*2*math.pi/5;cylinder('Lug',(sign*1.234,y+math.sin(a)*.055,.44+math.cos(a)*.055),.012,.009,3,8)
    # The scoop, splitters and wing are modelled parts, not texture illusions.
    box('Hood scoop',(0,-1.30,1.145),(.65,.43,.11),0,.025,edge)
    box('Scoop intake',(0,-1.522,1.15),(.52,.017,.055),2,.012,edge)
    box('Upper grille',(0,-2.075,.73),(.87,.035,.13),3,.026,edge)
    cylinder('Grille badge',(0,-2.102,.75),.055,.016,4,seg,(math.pi/2,0,0))
    box('Lower intake',(0,-2.106,.48),(1.13,.04,.16),3,.04,edge)
    box('Front splitter',(0,-2.11,.345),(2.13,.14,.055),3,.018,edge)
    for sign in [-1,1]:
        housing=[(.50,.89),(.96,.86),(.92,.75),(.55,.77)]
        led=[(.55,.865),(.92,.84),(.89,.785),(.58,.797)]
        panel('Swept headlight housing',[(sign*x,-2.072,z) for x,z in housing],3,.035)
        panel('Swept LED headlight',[(sign*x,-2.096,z) for x,z in led],5,.012)
        box('Brake cooling intake',(sign*.82,-2.098,.51),(.25,.045,.105),2,.025,edge)
        box('Tail lamp housing',(sign*.72,2.072,.85),(.50,.055,.18),3,.035,edge)
        box('Tail lamp',(sign*.72,2.105,.87),(.42,.02,.10),6,.022,edge)
        if level<2:box('Reverse lamp',(sign*.72,2.117,.81),(.28,.008,.023),5,.004,1)
        cylinder('Exhaust tip',(sign*.71,2.125,.435),.075,.11,4,seg,(math.pi/2,0,0))
        cylinder('Exhaust dark bore',(sign*.71,2.185,.435),.052,.009,2,seg,(math.pi/2,0,0))
        post=box('Wing support',(sign*.62,1.72,1.58),(.10,.13,.87),3,.014,edge);post.rotation_euler.x=-.14
        box('Wing endplate',(sign*1.10,1.80,2.025),(.06,.46,.31),0,.025,edge)
    box('Rear diffuser',(0,2.081,.42),(1.99,.12,.16),3,.035,edge)
    if level<2:
        for x in [-.38,0,.38]:box('Diffuser fin',(x,2.1,.39),(.026,.15,.12),3,.008,1)
    box('Rear number plate',(0,2.119,.81),(.31,.012,.095),8,.008,1)
    profile=[(1.59,2.04),(1.67,2.115),(1.90,2.09),(2.0,2.04),(1.98,2.012),(1.65,2.018)]
    v=[(x,y,z) for x in [-1.07,1.07] for y,z in profile];n=len(profile)
    f=[tuple(reversed(range(n))),tuple(range(n,2*n))]+[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
    wing=mesh_obj('Aero wing blade',v,f,0);bevel(wing,.012,edge)
    if level<2:
        for sign in [-1,1]:path('Wiper',[(sign*.48,-.945,1.189),(sign*.15,-.855,1.273)],.010,3,1)
    # Real white stripe panels follow the flat roof, deck and bonnet sections.
    def hood_top(y):
        if y<=-1.82:return .92+(y+2.05)/.23*.10
        return 1.02+(y+1.82)/.77*.11
    for x in [-.22,.22]:
        for y0,y1 in [(-1.94,-1.82),(-1.82,-1.545),(-1.065,-1.045)]:
            z0=hood_top(y0)+.005;z1=hood_top(y1)+.005
            panel('Bonnet stripe',[(x-.092,y0,z0),(x+.092,y0,z0),(x+.092,y1,z1),(x-.092,y1,z1)],8,.002)
        panel('Scoop stripe',[(x-.075,-1.46,1.203),(x+.075,-1.46,1.203),(x+.075,-1.14,1.203),(x-.075,-1.14,1.203)],8,.002)
        panel('Roof stripe',[(x-.092,-.24,1.724),(x+.092,-.24,1.724),(x+.092,.64,1.724),(x-.092,.64,1.724)],8,.002)
        for y0,z0,y1,z1 in [(1.47,1.135,1.80,1.135),(1.80,1.135,1.99,1.01077)]:
            panel('Rear deck stripe',[(x-.092,y0,z0),(x+.092,y0,z0),(x+.092,y1,z1),(x-.092,y1,z1)],8,.002)
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
        keep=o.vertex_groups.new(name='Keep wing silhouette')
        keep.add([v.index for v in o.data.vertices if v.co.z>1.86 or (v.co.z>1.03 and -1.06<v.co.y<1.46) or(abs(abs(v.co.x)-.62)<.11 and 1.5<v.co.y<1.9 and v.co.z>1.13)],1,'REPLACE')
        d=o.modifiers.new('Distant detail reduction','DECIMATE');d.ratio=.56;d.use_collapse_triangulate=True;d.vertex_group=keep.name;d.invert_vertex_group=True;apply(o,d)
    bm=bmesh.new();bm.from_mesh(o.data);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(o.data);bm.free()
    normal=o.modifiers.new('Final panel normals','WEIGHTED_NORMAL');normal.keep_sharp=True;apply(o,normal)
    o['Surface workflow']='Texture-free PBR; modelled stripes, scoop and wing; opaque glass'
    return o

lods=[build(i) for i in range(3)]
factor=4.3/lods[0].dimensions.y;ground=min(v.co.z for v in lods[0].data.vertices);reports=[]
for i,o in enumerate(lods):
    for v in o.data.vertices:v.co=(v.co-Vector((0,0,ground)))*factor
    o.data.update();bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o
    bpy.ops.export_scene.fbx(filepath=str(OUT/(o.name+'.fbx')),use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,bake_space_transform=True,bake_anim=False)
    reports.append({'level':i,'triangles':sum(len(p.vertices)-2 for p in o.data.polygons),'vertices':len(o.data.vertices),'size':list(o.dimensions),'materials':len(mats)})
    o.hide_render=i>0;o.hide_set(i>0)
scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=40
scene.world=bpy.data.worlds.new('Coupe Studio');scene.world.use_nodes=True
next(n for n in scene.world.node_tree.nodes if n.type=='BACKGROUND').inputs[0].default_value=(.25,.28,.32,1)
for pos,energy in [((4,-5,7),1000),((-5,1,6),800),((0,6,7),1000)]:
    bpy.ops.object.light_add(type='AREA',location=pos);o=bpy.context.object;o.data.energy=energy;o.data.shape='DISK';o.data.size=5
    o.rotation_euler=(Vector((0,0,1))-o.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.object.camera_add();cam=bpy.context.object;cam.data.type='ORTHO';cam.data.ortho_scale=5.5;scene.camera=cam
scene.render.resolution_x=1000;scene.render.resolution_y=750;scene.render.resolution_percentage=100;scene.render.image_settings.file_format='PNG'
for suffix,pos in [('PBR_Front',(1,-1.5,.85)),('PBR_Rear',(-1,1.5,.85))]:
    cam.location=Vector(pos)*4.3;cam.rotation_euler=(Vector((0,0,.95))-cam.location).to_track_quat('-Z','Y').to_euler()
    scene.render.filepath=str(SRC/(NAME+'_'+suffix+'.png'));bpy.ops.render.render(write_still=True)
for i in [1,2]:
    lods[0].hide_render=True;lods[i].hide_render=False;scene.render.filepath=str(SRC/(NAME+f'_PBR_LOD{i}.png'));bpy.ops.render.render(write_still=True);lods[i].hide_render=True
lods[0].hide_render=False;bpy.ops.object.select_all(action='DESELECT');lods[0].select_set(True);bpy.context.view_layer.objects.active=lods[0]
bpy.ops.wm.save_as_mainfile(filepath=str(SRC/(NAME+'.blend')))
(SRC/(NAME+'_CleanupReport.json')).write_text(json.dumps(reports,indent=2))
(SRC/(NAME+'_PBRMaterials.json')).write_text(json.dumps([{'name':NAME+'_'+s,'color_linear':c,'metallic':m,'roughness':r,'emission':e} for s,c,m,r,e in specs],indent=2))
print('SPORT_COUPE_PBR_COMPLETE',json.dumps(reports),flush=True)
