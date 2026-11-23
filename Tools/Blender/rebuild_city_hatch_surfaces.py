"""Clean hard-surface CityHatch mesh with authored PBR parts and three editable LODs."""
from pathlib import Path
import bpy,bmesh,math,json
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[2];SRC=ROOT/'ArtSource/Vehicles/NPCTraffic';OUT=ROOT/'Assets/_Project/Models/Vehicles/NPCTraffic'
bpy.ops.wm.read_factory_settings(use_empty=True);bpy.context.preferences.filepaths.save_version=0
specs=[('BodyPaint',(.018,.27,.55,1),.32,.27,0),('Glass',(.016,.028,.042,1),.12,.16,0),('Rubber',(.013,.016,.02,1),0,.84,0),('Details',(.036,.045,.057,1),.12,.48,0),('Alloy',(.49,.54,.6,1),.8,.23,0),('Headlamp',(.88,.95,1,1),.12,.19,.25),('TailLamp',(.48,.004,.009,1),.1,.22,.16),('Indicator',(.95,.22,.004,1),.1,.24,.12),('Stripe',(.82,.88,.94,1),.15,.29,0)]
mats=[]
for suffix,col,metal,rough,emission in specs:
    m=bpy.data.materials.new('NPC_CityHatch_'+suffix);m.use_nodes=True
    bs=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
    bs.inputs['Base Color'].default_value=col;bs.inputs['Metallic'].default_value=metal;bs.inputs['Roughness'].default_value=rough
    if suffix=='BodyPaint':bs.inputs['Coat Weight'].default_value=.4;bs.inputs['Coat Roughness'].default_value=.2
    if emission:bs.inputs['Emission Color'].default_value=col;bs.inputs['Emission Strength'].default_value=emission
    m.diffuse_color=col;mats.append(m)

def material(obj,index):
    obj.data.materials.append(mats[index]);return obj
def mesh_obj(name,verts,faces,mat):
    mesh=bpy.data.meshes.new(name);mesh.from_pydata(verts,[],faces);mesh.update()
    o=bpy.data.objects.new(name,mesh);bpy.context.collection.objects.link(o);material(o,mat);return o
def apply(o,mod):
    bpy.context.view_layer.objects.active=o;bpy.ops.object.modifier_apply(modifier=mod.name)
def bevel(o,width,segments):
    if width:
        b=o.modifiers.new('Machined edge radius','BEVEL');b.width=width;b.segments=segments
        apply(o,b)
    for p in o.data.polygons:p.use_smooth=True
    n=o.modifiers.new('Panel normals','WEIGHTED_NORMAL');n.keep_sharp=True;apply(o,n)
def box(name,loc,size,mat,radius=.02,segments=3):
    bpy.ops.mesh.primitive_cube_add(size=1,location=loc);o=bpy.context.object;o.name=name
    o.dimensions=size;bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    material(o,mat);bevel(o,radius,segments);return o
def path(name,coords,radius,mat,resolution):
    bpy.ops.object.select_all(action='DESELECT')
    c=bpy.data.curves.new(name,'CURVE');c.dimensions='3D';c.bevel_depth=radius;c.bevel_resolution=resolution
    s=c.splines.new('POLY');s.points.add(len(coords)-1)
    for p,co in zip(s.points,coords):p.co=(*co,1)
    o=bpy.data.objects.new(name,c);bpy.context.collection.objects.link(o)
    bpy.context.view_layer.objects.active=o;o.select_set(True);bpy.ops.object.convert(target='MESH');o.select_set(False)
    material(o,mat);return o
def panel(name,verts,mat,thick=.008):
    o=mesh_obj(name,verts,[tuple(range(len(verts)))],mat)
    s=o.modifiers.new('Panel thickness','SOLIDIFY');s.thickness=thick;apply(o,s)
    return o
def loft(name,rings,mat,segments):
    # y, half width, top, bottom; rounded eight-sided body sections.
    verts=[]
    for y,x,top,bottom in rings:
        verts.extend([(0,y,top),(.84*x,y,top),(x,y,top-.10),(x,y,bottom+.09),(.82*x,y,bottom),(-.82*x,y,bottom),(-x,y,bottom+.09),(-x,y,top-.10),(-.84*x,y,top)])
    count=9;faces=[tuple(reversed(range(count))),tuple((len(rings)-1)*count+i for i in range(count))]
    for j in range(len(rings)-1):
        for i in range(count):faces.append((j*count+i,j*count+(i+1)%count,(j+1)*count+(i+1)%count,(j+1)*count+i))
    o=mesh_obj(name,verts,faces,mat)
    bm=bmesh.new();bm.from_mesh(o.data);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(o.data);bm.free()
    bevel(o,.045,segments);return o
def cylinder(name,loc,radius,depth,mat,segments,rotation=(0,math.pi/2,0)):
    bpy.ops.mesh.primitive_cylinder_add(vertices=segments,radius=radius,depth=depth,location=loc,rotation=rotation)
    o=bpy.context.object;o.name=name;material(o,mat);bevel(o,.008,1);return o

def build(level):
    start=set(bpy.data.objects);seg=[24,16,10][level];edge=[2,1,1][level]
    body=loft('Body shell',[(-1.8,.77,1.02,.48),(-1.65,.9,1.15,.46),(-.93,.94,1.23,.46),(.85,.94,1.23,.46),(1.58,.87,1.14,.47),(1.8,.76,1.01,.5)],0,edge)
    # Real wheel openings, no painted wheel arches.
    for y in [-1.12,1.12]:
        bpy.ops.mesh.primitive_cylinder_add(vertices=seg,radius=.455,depth=3,location=(0,y,.43),rotation=(0,math.pi/2,0))
        cutter=bpy.context.object
        b=body.modifiers.new('Wheel arch cut','BOOLEAN');b.operation='DIFFERENCE';b.object=cutter;apply(body,b)
        bpy.data.objects.remove(cutter,do_unlink=True)
    bevel(body,.012,1)
    cabin_rings=[(-1.03,.87,1.24),(-.44,.73,1.8),(-.31,.72,1.86),(.82,.72,1.86),(1.22,.77,1.7),(1.57,.83,1.23)]
    # One continuous planar side makes genuinely flat glass and clean reflections.
    cabin_rings=[(y,.915-.3*(top-1.15),top) for y,width,top in cabin_rings]
    verts=[]
    for y,topwidth,top in cabin_rings:verts.extend([(-topwidth,y,top),(topwidth,y,top),(.915,y,1.15),(-.915,y,1.15)])
    faces=[(3,2,1,0),(20,21,22,23)]
    for j in range(5):
        for k in range(4):faces.append((j*4+k,j*4+(k+1)%4,(j+1)*4+(k+1)%4,(j+1)*4+k))
    cabin=mesh_obj('Cabin',verts,faces,0);bevel(cabin,.025,edge)
    def roof(y):
        for a,b in zip(cabin_rings,cabin_rings[1:]):
            if a[0]<=y<=b[0]:
                t=(y-a[0])/(b[0]-a[0]);return (a[1]*(1-t)+b[1]*t,a[2]*(1-t)+b[2]*t)
        return cabin_rings[-1][1:]
    # These windows are separate surfaces with clean, deliberately placed edges.
    panel('Windscreen',[(x,y,roof(y)[1]+.006) for x,y in [(-.68,-.48),(.68,-.48),(.775,-.96),(-.775,-.96)]],1)
    panel('Rear glass',[(x,y,roof(y)[1]+.006) for x,y in [(-.69,1.265),(.69,1.265),(.745,1.515),(-.745,1.515)]],1)
    for sign in [-1,1]:
        # Side window sidewalls slope inward towards the roof.
        def sx(z,y):
            topwidth,top=roof(y)
            return sign*(.915+(topwidth-.915)*(z-1.15)/(top-1.15)+.006)
        def side_glass(name,yz,mat):
            o=mesh_obj(name,[(sx(z,y),y,z) for y,z in yz],[tuple(range(len(yz)))],mat)
            bm=bmesh.new();bm.from_mesh(o.data)
            bmesh.ops.triangulate(bm,faces=list(bm.faces))
            for v in bm.verts:
                hit,co,normal,face=cabin.ray_cast(Vector((sign*2,v.co.y,v.co.z)),Vector((-sign,0,0)))
                if hit:v.co.x=co.x+sign*.006
            bm.to_mesh(o.data);bm.free()
            thick=o.modifiers.new('Window thickness','SOLIDIFY');thick.thickness=.004;apply(o,thick)
            for f in o.data.polygons:f.use_smooth=False
            return o
        for name,ys in [('Front side window',[-.88,-.40,.06,.08]),('Rear side window',[.23,.23,1.14,1.42])]:
            if name.startswith('Front'):
                yz=[(-.87,1.29),(-.4,1.76),(.06,1.76),(.08,1.29)]
            else:yz=[(.23,1.29),(.23,1.76),(.79,1.76),(1.14,1.62),(1.42,1.29)]
            side_glass(name,yz,1)
        panel('B pillar',[(sx(z,y)+sign*.003,y,z) for y,z in [(.085,1.27),(.068,1.79),(.225,1.79),(.225,1.27)]],3)
        # Lower door/sill and panel seams.
        box('Sill',(sign*.899,0,.53),(.06,1.48,.09),3,.025,edge)
        if level<2:
            path('Door shut line',[(sign*.946,.15,1.18),(sign*.946,.18,.7),(sign*.93,.11,.62)],.004,3,0)
            for y in [-.2,.85]:box('Door handle',(sign*.953,y,1.13),(.025,.15,.035),3,.012,edge)
        box('Mirror stem',(sign*.965,-.66,1.29),(.19,.075,.05),3,.02,edge)
        box('Mirror housing',(sign*1.04,-.69,1.35),(.18,.24,.16),0,.047,edge)
        box('Mirror glass',(sign*1.04,-.562,1.353),(.13,.012,.104),1,.02,edge)
        if level<2:box('Mirror indicator',(sign*1.132,-.705,1.33),(.008,.12,.027),7,.005,1)
        # Rubber tyres are lathed geometry; hubs and spokes are independent alloy parts.
        for y in [-1.12,1.12]:
            verts=[];faces=[]
            profile=[(-.125,.275),(-.13,.335),(-.095,.414),(.075,.425),(.126,.365),(.13,.275)]
            for dx,r in profile:
                for j in range(seg):
                    a=j*2*math.pi/seg;verts.append((sign*(.88+dx),y+math.sin(a)*r,.43+math.cos(a)*r))
            for k in range(len(profile)):
                for j in range(seg):faces.append((k*seg+j,k*seg+(j+1)%seg,((k+1)%len(profile))*seg+(j+1)%seg,((k+1)%len(profile))*seg+j))
            tyre=mesh_obj('Tyre',verts,faces,2)
            for f in tyre.data.polygons:f.use_smooth=True
            cylinder('Alloy rim',(sign*1.009,y,.43),.274,.025,4,seg)
            cylinder('Wheel recess',(sign*1.028,y,.43),.219,.012,3,seg)
            if level<2:
                for j in range(5):
                    a=j*2*math.pi/5
                    spoke=box('Alloy spoke',(sign*1.043,y+math.sin(a)*.123,.43+math.cos(a)*.123),(.022,.047,.215),4,.011,edge)
                    spoke.rotation_euler.x=-a
            cylinder('Hub cap',(sign*1.052,y,.43),.091,.028,4,seg)
            if level==0:
                for j in range(5):
                    a=j*2*math.pi/5
                    cylinder('Lug',(sign*1.069,y+math.sin(a)*.057,.43+math.cos(a)*.057),.012,.008,3,8)
    # Contrasting bumpers, grille and clean LED lamp surrounds.
    box('Front bumper',(0,-1.779,.58),(1.53,.13,.18),3,.065,edge)
    box('Lower grille',(0,-1.852,.62),(.78,.022,.105),2,.03,edge)
    box('Main grille',(0,-1.81,.9),(.73,.05,.19),3,.055,edge)
    cylinder('Grille badge',(0,-1.847,.926),.069,.016,4,seg,(math.pi/2,0,0))
    for sign in [-1,1]:
        box('Headlight housing',(sign*.61,-1.765,.992),(.34,.075,.255),3,.075,edge)
        box('LED headlight',(sign*.61,-1.81,.995),(.277,.028,.192),5,.058,edge)
        box('Headlight inset',(sign*.61,-1.83,1.002),(.19,.014,.114),1,.035,edge)
        if level<2:
            box('Fog lamp',(sign*.57,-1.84,.593),(.12,.025,.055),7,.02,edge)
        box('Tail lamp',(sign*.718,1.742,1.024),(.17,.075,.29),6,.045,edge)
        box('Reverse lamp',(sign*.718,1.786,1.01),(.115,.012,.038),5,.008,edge)
    box('Rear bumper',(0,1.785,.578),(1.5,.13,.18),3,.065,edge)
    box('Rear number plate',(0,1.816,.825),(.32,.018,.11),3,.012,edge)
    if level<2:
        box('Hatch handle',(0,1.765,1.133),(.18,.04,.03),3,.008,edge)
        path('Rear wiper',[(.03,1.52,1.30),(.33,1.412,1.43)],.012,3,1)
        for sign in [-1,1]:
            path('Wiper',[(sign*.46,-.982,1.30),(sign*.13,-.87,1.40)],.011,3,1)
    # Stripes are actual thin panels on the roof and bonnet, with no albedo image.
    for x in [-.22,.22]:
        panel('Bonnet stripe',[(x-.095,-1.655,1.159),(x+.095,-1.655,1.159),(x+.095,-1.045,1.226),(x-.095,-1.045,1.226)],8,.002)
        panel('Roof stripe',[(x-.095,-.27,1.865),(x+.095,-.27,1.865),(x+.095,.79,1.865),(x-.095,.79,1.865)],8,.002)
    for sign in [-1,1]:
        path('Roof rail',[(sign*.57,-.18,1.87),(sign*.57,-.12,1.94),(sign*.57,.71,1.94),(sign*.57,.78,1.87)],.027,3,edge-1)
    path('Antenna',[(0,.76,1.87),(0,1.03,2.055)],.008,3,1 if level==0 else 0)
    # Apply all construction; the saved asset contains ordinary editable meshes.
    parts=[o for o in bpy.data.objects if o not in start and o.type=='MESH']
    bpy.ops.object.select_all(action='DESELECT')
    for o in parts:o.select_set(True)
    bpy.context.view_layer.objects.active=body;bpy.ops.object.join()
    result=bpy.context.object;result.name='NPC_CityHatch'+('' if level==0 else f'_LOD{level}')
    bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
    # Consistent material slots on every mesh, including the lowest LOD.
    old=list(result.data.materials);indices=[mats.index(m) if m else 3 for m in old]
    mapping=[indices[p.material_index] for p in result.data.polygons]
    result.data.materials.clear()
    for m in mats:result.data.materials.append(m)
    for p,index in zip(result.data.polygons,mapping):p.material_index=index
    bm=bmesh.new();bm.from_mesh(result.data);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(result.data);bm.free()
    if level==2:
        # Collapse tiny bevels at distance; preserve the antenna to retain LOD bounds.
        protected=result.vertex_groups.new(name='Keep silhouette extremities')
        protected.add([v.index for v in result.data.vertices if v.co.z>1.97],1.0,'REPLACE')
        dec=result.modifiers.new('Distant detail reduction','DECIMATE');dec.ratio=.6
        dec.vertex_group=protected.name;dec.invert_vertex_group=True
        dec.use_collapse_triangulate=True;apply(result,dec)
    result['materials']='Authored PBR; no generated textures; opaque glass'
    return result

lods=[build(i) for i in range(3)]
report=[]
for i,o in enumerate(lods):
    bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o
    bpy.ops.export_scene.fbx(filepath=str(OUT/(o.name+'.fbx')),use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,bake_space_transform=True,bake_anim=False)
    report.append({'level':i,'triangles':sum(len(p.vertices)-2 for p in o.data.polygons),'vertices':len(o.data.vertices),'materials':len(mats),'bounds':list(o.dimensions)})
    o.hide_render=i>0;o.hide_set(i>0)
scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=48
scene.world=bpy.data.worlds.new('Studio');scene.world.use_nodes=True
next(n for n in scene.world.node_tree.nodes if n.type=='BACKGROUND').inputs[0].default_value=(.25,.28,.32,1)
for pos,energy in [((3,-4,7),850),((-4,1,5),650),((0,6,6),900)]:
    bpy.ops.object.light_add(type='AREA',location=pos);o=bpy.context.object;o.data.energy=energy;o.data.shape='DISK';o.data.size=5
    o.rotation_euler=(Vector((0,0,1))-o.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.object.camera_add();cam=bpy.context.object;cam.data.type='ORTHO';cam.data.ortho_scale=4.8;scene.camera=cam
scene.render.resolution_x=1000;scene.render.resolution_y=750;scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG'
for suffix,pos in [('PBR_Front',(1,-1.5,.85)),('PBR_Rear',(-1,1.5,.85))]:
    cam.location=Vector(pos)*3.7;cam.rotation_euler=(Vector((0,0,.9))-cam.location).to_track_quat('-Z','Y').to_euler()
    scene.render.filepath=str(SRC/('NPC_CityHatch_'+suffix+'.png'));bpy.ops.render.render(write_still=True)
for i in [1,2]:
    lods[0].hide_render=True;lods[i].hide_render=False
    scene.render.filepath=str(SRC/f'NPC_CityHatch_PBR_LOD{i}.png');bpy.ops.render.render(write_still=True);lods[i].hide_render=True
lods[0].hide_render=False
bpy.ops.object.select_all(action='DESELECT');lods[0].select_set(True);bpy.context.view_layer.objects.active=lods[0]
bpy.ops.wm.save_as_mainfile(filepath=str(SRC/'NPC_CityHatch.blend'))
(SRC/'NPC_CityHatch_CleanupReport.json').write_text(json.dumps(report,indent=2))
(SRC/'NPC_CityHatch_PBRMaterials.json').write_text(json.dumps([{'name':'NPC_CityHatch_'+s,'color_linear':c,'metallic':m,'roughness':r,'emission':e} for s,c,m,r,e in specs],indent=2))
print('AUTHORED_PBR',json.dumps(report),flush=True)
