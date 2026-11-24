"""Explicit UtilityPickup surface/LOD authoring using the CityHatch modelling helpers."""
from pathlib import Path
import bpy,bmesh,math,json,ast,shutil,hashlib
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[2];SRC=ROOT/'ArtSource/Vehicles/NPCTraffic';OUT=ROOT/'Assets/_Project/Models/Vehicles/NPCTraffic'
NAME='NPC_UtilityPickup';BACKUP=SRC/'UtilityPickup_BeforeCleanup';BACKUP.mkdir(exist_ok=True)
assets=[SRC/(NAME+'.blend'),OUT/(NAME+'.fbx'),OUT/(NAME+'.fbx.meta'),ROOT/('Assets/_Project/Prefabs/Vehicles/NPCTraffic/'+NAME+'.prefab'),ROOT/('Assets/_Project/Prefabs/Vehicles/NPCTraffic/'+NAME+'.prefab.meta'),SRC/(NAME+'_MeshReport.json'),SRC/(NAME+'_Front.png'),SRC/(NAME+'_Rear.png')]
assets+=list((OUT/'Materials').glob(NAME+'*'))
for p in assets:
    if not (BACKUP/p.name).exists():shutil.copy2(p,BACKUP/p.name)
if not (BACKUP/'Hashes.json').exists():(BACKUP/'Hashes.json').write_text(json.dumps({p.relative_to(ROOT).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in assets},indent=2))
bpy.ops.wm.read_factory_settings(use_empty=True);bpy.context.preferences.filepaths.save_version=0
specs=[('BodyPaint',(.72,.012,.025,1),.32,.27,0),('Glass',(.016,.028,.042,1),.12,.16,0),('Rubber',(.013,.016,.02,1),0,.84,0),('Details',(.036,.045,.057,1),.12,.48,0),('Alloy',(.49,.54,.6,1),.8,.23,0),('Headlamp',(.88,.95,1,1),.12,.19,.25),('TailLamp',(.48,.004,.009,1),.1,.22,.16),('Indicator',(.95,.22,.004,1),.1,.24,.12),('Stripe',(.95,.50,.025,1),.15,.29,0)]
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

current_level=0
base_box=box
def box(name,loc,size,mat,radius=.02,segments=3):
    return base_box(name,loc,size,mat,0 if current_level==2 else radius,segments)
base_cylinder=cylinder
def cylinder(name,loc,radius,depth,mat,segments,rotation=(0,math.pi/2,0)):
    if current_level<2:return base_cylinder(name,loc,radius,depth,mat,segments,rotation)
    bpy.ops.mesh.primitive_cylinder_add(vertices=segments,radius=radius,depth=depth,location=loc,rotation=rotation)
    o=bpy.context.object;o.name=name;material(o,mat)
    for p in o.data.polygons:p.use_smooth=len(p.vertices)==4
    return o

def build(level):
    global current_level
    current_level=level
    start=set(bpy.data.objects);seg=[24,16,10][level];edge=[2,1,1][level]
    body_rings=[(-2.35,1.13,1.39,.65),(-2.09,1.25,1.49,.61),(-1.23,1.27,1.56,.61),(.63,1.27,1.56,.61),(1.92,1.25,1.56,.61),(2.35,1.20,1.53,.64)]
    body=loft('Pickup body and bed',body_rings,0,edge)
    for y in [-1.50,1.50]:
        bpy.ops.mesh.primitive_cylinder_add(vertices=seg,radius=.605,depth=3.8,location=(0,y,.56),rotation=(0,math.pi/2,0));cut=bpy.context.object
        d=body.modifiers.new('Wheel opening','BOOLEAN');d.operation='DIFFERENCE';d.object=cut;apply(body,d);bpy.data.objects.remove(cut,do_unlink=True)
    # Cut an actual empty bed into the body. The liner is saved geometry too.
    bpy.ops.mesh.primitive_cube_add(size=1,location=(0,1.55,2.0));cut=bpy.context.object;cut.dimensions=(2.10,1.48,2.06)
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    d=body.modifiers.new('Open cargo bed','BOOLEAN');d.operation='DIFFERENCE';d.object=cut;apply(body,d);bpy.data.objects.remove(cut,do_unlink=True)
    bevel(body,.012,1)
    box('Bed floor liner',(0,1.55,.982),(2.08,1.46,.025),2,.008,edge)
    box('Bed front liner',(0,.815,1.265),(2.08,.025,.57),2,.008,1)
    box('Tailgate inner liner',(0,2.282,1.265),(2.08,.02,.57),2,.008,1)
    for sign in [-1,1]:
        box('Bed side liner',(sign*1.044,1.55,1.265),(.025,1.46,.57),2,.008,1)
        box('Wheel well cover',(sign*.90,1.50,1.105),(.37,1.1,.36),3,.14,edge)
        box('Bed rail cap',(sign*1.17,1.56,1.575),(.16,1.62,.045),2,.015,edge)
    box('Tailgate rail cap',(0,2.325,1.575),(2.32,.09,.045),2,.015,edge)
    if level<2:
        for x in [-.72,-.36,0,.36,.72]:box('Bed floor rib',(x,1.55,1.003),(.018,1.39,.014),2,.004,1)
    rings=[(-1.30,1.58),(-.56,2.40),(-.43,2.48),(.52,2.48),(.72,2.34),(.78,1.56)]
    side_x=lambda z:1.21-.27*(z-1.50)
    v=[]
    for y,z in rings:v.extend([(-side_x(z),y,z),(side_x(z),y,z),(side_x(1.49),y,1.49),(-side_x(1.49),y,1.49)])
    f=[(3,2,1,0),(20,21,22,23)]
    for j in range(5):
        for i in range(4):f.append((j*4+i,j*4+(i+1)%4,(j+1)*4+(i+1)%4,(j+1)*4+i))
    cabin=mesh_obj('Single cab',v,f,0);bevel(cabin,.035,edge)
    def roof(y):
        for a,b in zip(rings,rings[1:]):
            if a[0]<=y<=b[0]:return a[1]+(b[1]-a[1])*(y-a[0])/(b[0]-a[0])
        return rings[-1][1]
    panel('Windscreen',[(x,y,roof(y)+.007) for x,y in [(-.89,-.62),(.89,-.62),(1.10,-1.215),(-1.10,-1.215)]],1)
    panel('Rear cab glass',[(x,y,roof(y)+.007) for x,y in [(-.91,.735),(.91,.735),(1.09,.772),(-1.09,.772)]],1)
    def body_width(y):
        for a,b in zip(body_rings,body_rings[1:]):
            if a[0]<=y<=b[0]:return a[1]+(b[1]-a[1])*(y-a[0])/(b[0]-a[0])
        return body_rings[-1][1]
    for sign in [-1,1]:
        yz=[(-1.11,1.72),(-.52,2.34),(.40,2.34),(.61,2.17),(.63,1.72)]
        panel('Door glass',[(sign*(side_x(z)+.007),y,z) for y,z in yz],1,.005)
        box('Mirror stem',(sign*1.25,-.90,1.65),(.22,.08,.065),3,.02,edge)
        box('Mirror housing',(sign*1.31,-.94,1.76),(.25,.28,.22),4,.045,edge)
        box('Mirror glass',(sign*1.31,-.789,1.76),(.19,.014,.16),1,.025,edge)
        box('Door handle',(sign*1.283,.36,1.48),(.03,.18,.045),3,.014,edge)
        box('Running board',(sign*1.29,-.26,.55),(.18,1.63,.10),3,.025,edge)
        if level<2:
            box('Step tread',(sign*1.31,-.26,.608),(.11,1.45,.013),2,.004,1)
            path('Door seam',[(sign*1.274,.63,1.54),(sign*1.274,.63,.80)],.004,3,0)
        # Real yellow stripe panels follow the side-wall breaks exactly.
        for y0,y1 in [(-1.95,-1.23),(-1.23,.63),(.63,1.92),(1.92,2.25)]:
            panel('Yellow side stripe',[(sign*(body_width(y)+.006),y,z) for y,z in [(y0,1.27),(y0,1.38),(y1,1.38),(y1,1.27)]],8,.002)
        for y in [-1.50,1.50]:
            verts=[];faces=[];arc=[16,10,7][level]
            for j in range(arc+1):
                a=-.1+(math.pi+.2)*j/arc
                for x,r in [(sign*1.272,.603),(sign*1.318,.603),(sign*1.318,.66),(sign*1.272,.66)]:verts.append((x,y+math.cos(a)*r,.56+math.sin(a)*r))
            faces=[(3,2,1,0),tuple(arc*4+i for i in range(4))]
            for j in range(arc):
                for i in range(4):faces.append((j*4+i,j*4+(i+1)%4,(j+1)*4+(i+1)%4,(j+1)*4+i))
            cladding=mesh_obj('Wheel arch cladding',verts,faces,3)
            for p in cladding.data.polygons:p.use_smooth=True
            profile=[(-.15,.34),(-.155,.45),(-.115,.545),(.09,.56),(.155,.47),(.155,.34)]
            v=[];f=[]
            for dx,r in profile:
                for j in range(seg):
                    a=j*2*math.pi/seg;v.append((sign*(1.13+dx),y+math.sin(a)*r,.56+math.cos(a)*r))
            for k in range(len(profile)):
                for j in range(seg):f.append((k*seg+j,k*seg+(j+1)%seg,((k+1)%len(profile))*seg+(j+1)%seg,((k+1)%len(profile))*seg+j))
            tyre=mesh_obj('Utility tyre',v,f,2)
            for p in tyre.data.polygons:p.use_smooth=True
            cylinder('Alloy rim',(sign*1.285,y,.56),.34,.03,4,seg)
            cylinder('Wheel recess',(sign*1.307,y,.56),.28,.014,3,seg)
            if level<2:
                for j in range(5):
                    a=j*2*math.pi/5;spoke=box('Alloy spoke',(sign*1.322,y+math.sin(a)*.16,.56+math.cos(a)*.16),(.027,.06,.27),4,.012,edge);spoke.rotation_euler.x=-a
            cylinder('Hub cap',(sign*1.336,y,.56),.11,.03,4,seg)
            if level==0:
                for j in range(5):
                    a=j*2*math.pi/5;cylinder('Lug',(sign*1.355,y+math.sin(a)*.068,.56+math.cos(a)*.068),.014,.009,3,8)
    box('Chrome front bumper',(0,-2.36,.71),(2.40,.17,.22),4,.065,edge)
    box('Grille chrome surround',(0,-2.365,1.18),(1.19,.085,.34),4,.04,edge)
    box('Main grille',(0,-2.414,1.18),(1.03,.02,.24),3,.025,edge)
    cylinder('Badge',(0,-2.432,1.20),.078,.015,4,seg,(math.pi/2,0,0))
    if level<2:
        for z in [1.11,1.25]:box('Grille slat',(0,-2.429,z),(.92,.012,.016),4,.004,1)
    for sign in [-1,1]:
        box('Chrome headlight housing',(sign*.84,-2.36,1.18),(.45,.10,.27),4,.045,edge)
        box('Headlight',(sign*.84,-2.417,1.18),(.36,.026,.19),5,.03,edge)
        box('Headlight inset',(sign*.84,-2.434,1.185),(.255,.012,.105),1,.02,edge)
        box('Front indicator',(sign*1.08,-2.405,.98),(.13,.025,.10),7,.018,edge)
        box('Tail lamp housing',(sign*1.08,2.36,1.28),(.23,.075,.38),3,.035,edge)
        box('Tail lamp',(sign*1.08,2.405,1.30),(.17,.02,.28),6,.025,edge)
        box('Reverse lamp',(sign*1.08,2.419,1.225),(.13,.009,.045),5,.006,1)
    box('Chrome rear bumper',(0,2.36,.69),(2.44,.17,.22),4,.06,edge)
    box('Tailgate handle',(0,2.365,1.43),(.29,.04,.065),3,.013,edge)
    box('Rear plate surround',(0,2.385,1.09),(.42,.025,.15),3,.015,edge)
    box('Number plate',(0,2.404,1.09),(.34,.014,.10),4,.007,1)
    box('Tow receiver',(0,2.43,.55),(.13,.14,.08),3,.015,edge)
    path('Cab roll bar',[(-1.02,.86,1.57),(-1.02,.86,2.45),(-.87,.86,2.60),(.87,.86,2.60),(1.02,.86,2.45),(1.02,.86,1.57)],.062,3,edge-1)
    for sign in [-1,1]:path('Roll bar brace',[(sign*1.02,.88,2.43),(sign*1.10,1.42,1.59)],.045,3,edge-1)
    for x in [-.60,0,.60]:
        box('Spotlight bracket',(x,.83,2.545),(.12,.10,.15),3,.014,edge)
        cylinder('Spotlight housing',(x,.80,2.72),.165,.14,3,seg,(math.pi/2,0,0))
        cylinder('Spotlight chrome rim',(x,.722,2.72),.147,.022,4,seg,(math.pi/2,0,0))
        cylinder('Spotlight amber rim',(x,.707,2.72),.132,.012,7,seg,(math.pi/2,0,0))
        cylinder('Spotlight lens',(x,.697,2.72),.112,.012,5,seg,(math.pi/2,0,0))
    if level<2:
        for sign in [-1,1]:path('Wiper',[(sign*.54,-1.228,1.695),(sign*.14,-1.10,1.83)],.012,3,1)
    parts=[o for o in bpy.data.objects if o not in start and o.type=='MESH']
    bpy.ops.object.select_all(action='DESELECT')
    for o in parts:o.select_set(True)
    bpy.context.view_layer.objects.active=body;bpy.ops.object.join();o=bpy.context.object
    o.name=NAME+('' if level==0 else '_LOD'+str(level));bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
    old=list(o.data.materials);indices=[mats.index(m) if m else 3 for m in old];labels=[indices[p.material_index] for p in o.data.polygons]
    o.data.materials.clear()
    for m in mats:o.data.materials.append(m)
    for p,i in zip(o.data.polygons,labels):p.material_index=i
    bm=bmesh.new();bm.from_mesh(o.data);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(o.data);bm.free()
    normal=o.modifiers.new('Final panel normals','WEIGHTED_NORMAL');normal.keep_sharp=True;apply(o,normal)
    o['Surface workflow']='Texture-free PBR; genuinely open lined bed; modelled stripe and spotlights; opaque glass'
    return o

lods=[build(i) for i in range(3)]
factor=4.9/lods[0].dimensions.y;ground=min(v.co.z for v in lods[0].data.vertices);reports=[]
for i,o in enumerate(lods):
    for v in o.data.vertices:v.co=(v.co-Vector((0,0,ground)))*factor
    o.data.update();bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o
    bpy.ops.export_scene.fbx(filepath=str(OUT/(o.name+'.fbx')),use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,bake_space_transform=True,bake_anim=False)
    reports.append({'level':i,'triangles':sum(len(p.vertices)-2 for p in o.data.polygons),'vertices':len(o.data.vertices),'size':list(o.dimensions),'materials':len(mats)})
    o.hide_render=i>0;o.hide_set(i>0)
scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=40
scene.world=bpy.data.worlds.new('Pickup Studio');scene.world.use_nodes=True
next(n for n in scene.world.node_tree.nodes if n.type=='BACKGROUND').inputs[0].default_value=(.25,.28,.32,1)
for pos,energy in [((4,-5,8),1200),((-5,1,7),950),((0,7,8),1200)]:
    bpy.ops.object.light_add(type='AREA',location=pos);o=bpy.context.object;o.data.energy=energy;o.data.shape='DISK';o.data.size=5
    o.rotation_euler=(Vector((0,0,1.2))-o.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.object.camera_add();cam=bpy.context.object;cam.data.type='ORTHO';cam.data.ortho_scale=6.3;scene.camera=cam
scene.render.resolution_x=1000;scene.render.resolution_y=750;scene.render.resolution_percentage=100;scene.render.image_settings.file_format='PNG'
for suffix,pos in [('PBR_Front',(1,-1.5,.85)),('PBR_Rear',(-1,1.5,.85))]:
    cam.location=Vector(pos)*4.9;cam.rotation_euler=(Vector((0,0,1.2))-cam.location).to_track_quat('-Z','Y').to_euler()
    scene.render.filepath=str(SRC/(NAME+'_'+suffix+'.png'));bpy.ops.render.render(write_still=True)
for i in [1,2]:
    lods[0].hide_render=True;lods[i].hide_render=False;scene.render.filepath=str(SRC/(NAME+f'_PBR_LOD{i}.png'));bpy.ops.render.render(write_still=True);lods[i].hide_render=True
lods[0].hide_render=False;bpy.ops.object.select_all(action='DESELECT');lods[0].select_set(True);bpy.context.view_layer.objects.active=lods[0]
bpy.ops.wm.save_as_mainfile(filepath=str(SRC/(NAME+'.blend')))
(SRC/(NAME+'_CleanupReport.json')).write_text(json.dumps(reports,indent=2))
(SRC/(NAME+'_PBRMaterials.json')).write_text(json.dumps([{'name':NAME+'_'+s,'color_linear':c,'metallic':m,'roughness':r,'emission':e} for s,c,m,r,e in specs],indent=2))
print('UTILITY_PICKUP_PBR_COMPLETE',json.dumps(reports),flush=True)

