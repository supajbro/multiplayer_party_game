"""Couch Guys concept sheet 01-10. Run with Blender --background --python this_file.
Metres; X width, -Y front, Z up; floor-centre pivot. Exports three joined LOD
meshes per FBX and ordinary PNG/UV materials (no procedural shader dependency).
Only the dedicated generated scene is used. Re-running is an explicit authoring action.
"""
import bpy, math, os, json
import numpy as np
from mathutils import Vector
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Assets/_Project/Models/Couch/Easy'
TEX = OUT / 'Textures'
REVIEW = ROOT / 'Tools/Blender/Exports/EasyCouches'
for p in (OUT, TEX, REVIEW): p.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.render.engine = 'CYCLES'
scene.cycles.samples = 32
scene.cycles.use_denoising = True
scene.view_settings.view_transform = 'AgX'
scene.view_settings.look = 'AgX - Medium High Contrast'
scene.world = bpy.data.worlds.new('EasyCouches_World')
scene.world.use_nodes = True
scene.world.node_tree.nodes['Background'].inputs[0].default_value=(0.75,0.8,0.9,1)
scene.world.node_tree.nodes['Background'].inputs[1].default_value=.45

def material(name, rgb, pattern=None):
    m = bpy.data.materials.new('EC_'+name); m.diffuse_color=(*rgb,1); m.use_nodes=True
    bs=m.node_tree.nodes.get('Principled BSDF'); bs.inputs['Base Color'].default_value=(*rgb,1)
    bs.inputs['Roughness'].default_value=.82
    if pattern:
        n=512; y,x=np.mgrid[0:n,0:n]/n; c=np.zeros((n,n,4),np.float32); c[:]=(*rgb,1)
        cream=np.array((1.,.85,.56)); orange=np.array((1.,.36,.045))
        if pattern=='stripe':
            mask=((x>.28)&(x<.36))|((x>.43)&(x<.49)); c[mask,:3]=cream
        elif pattern=='horizontal':
            mask=((y>.21)&(y<.35))|((y>.57)&(y<.71)); c[mask,:3]=cream
        elif pattern=='plaid':
            a=(x*5%1<.24); b=(y*5%1<.24)
            c[a|b,:3]=orange; c[a&b,:3]=(1.,.65,.12)
        elif pattern=='gingham':
            a=(x*6).astype(int)%2; b=(y*6).astype(int)%2
            c[:,:,:3]=cream; c[(a+b)==1,:3]=(1.,.58,.16); c[(a+b)==2,:3]=orange
        elif pattern=='dots':
            mask=((x*3%1-.5)**2+(y*3%1-.5)**2)<.095; c[mask,:3]=cream
        elif pattern=='flowers':
            for cx,cy,r in ((.28,.28,.19),(.78,.76,.21),(.8,.15,.11),(.17,.87,.12)):
                dx=x-cx; dy=y-cy; a=np.arctan2(dy,dx); d=np.sqrt(dx*dx+dy*dy)
                c[d<r*(.8+.2*np.cos(5*a)),:3]=cream
                c[d<r*.28,:3]=(1.,.58,.025)
        elif pattern=='blanket':
            c[(y>.16)&(y<.23),:3]=(0.12,.4,.68)
            c[(y>.29)&(y<.34),:3]=(0.12,.4,.68)
        # Fine woven colour variation is baked into the PNG.
        weave=(np.sin(x*n*math.pi)*np.sin(y*n*math.pi)*.012)[...,None]
        c[:,:,:3]=np.clip(c[:,:,:3]+weave,0,1)
        im=bpy.data.images.new(m.name, width=n,height=n,alpha=True)
        im.pixels.foreach_set(c.ravel()); im.filepath_raw=str(TEX/(m.name+'.png')); im.file_format='PNG'; im.save()
        node=m.node_tree.nodes.new('ShaderNodeTexImage'); node.image=im
        m.node_tree.links.new(node.outputs['Color'],bs.inputs['Base Color'])
    return m

red=material('01_Coral',(.86,.075,.045)); gold=material('Gold', (1.,.55,.025))
blue=material('02_Blue',(.025,.39,.72)); stripes=material('02_CreamStripes',(.025,.39,.72),'stripe')
yellow=material('03_Sunshine', (1.,.57,.015)); olive=material('03_OlivePillow',(.43,.52,.08))
green=material('04_Daisy',(.31,.48,.075),'flowers')
purple=material('05_Lavender',(.48,.24,.69)); pink=material('PinkPillow',(.99,.30,.46))
plaid=material('06_OrangePlaid',(.92,.20,.025),'plaid')
teal=material('07_Turquoise',(.015,.51,.56)); cream=material('Cream', (1.,.84,.59))
horizontal=material('07_PillowStripes',(.025,.39,.72),'horizontal')
rose=material('08_Rose',(.92,.14,.29)); denim=material('09_Denim',(.075,.24,.55))
lime=material('10_Lime',(.44,.62,.045)); gingham=material('10_Gingham', (1.,.5,.1),'gingham')
throw=material('06_BlueBorderThrow',(.96,.83,.61),'blanket')
dotthrow=material('09_PolkaThrow',(1.,.52,.02),'dots')
wood=material('HoneyWood',(.43,.20,.055)); trim=material('IvoryPiping',(1.,.82,.48))
redband=material('03_RedBand',(.8,.08,.04))
parts=[]

def finish(obj,name,mat):
    obj.name=name; obj.data.materials.append(mat)
    for f in obj.data.polygons: f.use_smooth=True
    parts.append(obj); return obj

def uv_project(o, dims):
    uv=o.data.uv_layers.active or o.data.uv_layers.new()
    for poly in o.data.polygons:
        normal=poly.normal; axis=max(range(3),key=lambda i:abs(normal[i]))
        axes=((1,2),(0,2),(0,1))[axis]
        for li in poly.loop_indices:
            co=o.data.vertices[o.data.loops[li].vertex_index].co
            uv.data[li].uv=(co[axes[0]]/dims[axes[0]]+.5,co[axes[1]]/dims[axes[1]]+.5)

def box(name,loc,dims,mat,rounding=.09,rot=(0,0,0)):
    bpy.ops.mesh.primitive_cube_add(size=1,location=loc)
    o=bpy.context.object; o.dimensions=dims
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    uv_project(o,dims)
    be=o.modifiers.new('Soft upholstery edges','BEVEL'); be.width=min(rounding,min(dims)*.47); be.segments=5
    bpy.ops.object.modifier_apply(modifier=be.name)
    no=o.modifiers.new('Upholstery normals','WEIGHTED_NORMAL'); no.keep_sharp=True
    bpy.ops.object.modifier_apply(modifier=no.name)
    o.rotation_euler=rot
    return finish(o,name,mat)

def ball(name,loc,scale,mat):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=16,ring_count=8,location=loc)
    o=bpy.context.object; o.scale=scale
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    return finish(o,name,mat)

def cord(name,points,radius,mat):
    cu=bpy.data.curves.new(name,'CURVE'); cu.dimensions='3D'; cu.bevel_depth=radius; cu.bevel_resolution=2
    s=cu.splines.new('POLY'); s.points.add(len(points)-1)
    for v,p in zip(s.points,points): v.co=(*p,1)
    o=bpy.data.objects.new(name,cu); scene.collection.objects.link(o)
    bpy.context.view_layer.objects.active=o; o.select_set(True)
    bpy.ops.object.convert(target='MESH'); o=bpy.context.object
    o.select_set(False); return finish(o,name,mat)

def piping(x,y,z,w,h,mat):
    pts=[]; r=.095
    for cx,cz,start in ((x+w/2-r,z+h/2-r,0),(x-w/2+r,z+h/2-r,90),(x-w/2+r,z-h/2+r,180),(x+w/2-r,z-h/2+r,270)):
        for k in range(9):
            a=math.radians(start+k*90/8); pts.append((cx+r*math.cos(a),y,cz+r*math.sin(a)))
    cord('Piping',pts+[pts[0]],.009,mat)

def motif(name,center,size,mat,kind):
    pts=[]
    if kind=='star':
        for k in range(10):
            a=math.pi/2+k*math.pi/5; r=size*(1 if k%2==0 else .46)
            pts.append((center[0]+r*math.cos(a),center[1],center[2]+r*math.sin(a)))
    else:
        for k in range(48):
            a=k*2*math.pi/48
            pts.append((center[0]+size*math.sin(a)**3,center[1],center[2]+size*(13*math.cos(a)-5*math.cos(2*a)-2*math.cos(3*a)-math.cos(4*a))/16))
    mesh=bpy.data.meshes.new(name); mesh.from_pydata([center]+pts,[],[(0,k+1,(k+1)%len(pts)+1) for k in range(len(pts))]); mesh.update()
    o=bpy.data.objects.new(name,mesh); scene.collection.objects.link(o); finish(o,name,mat)
    sol=o.modifiers.new('Applique thickness','SOLIDIFY'); sol.thickness=.007
    bpy.context.view_layer.objects.active=o; bpy.ops.object.modifier_apply(modifier=sol.name)

def pillow(x,mat,angle=0,motif_kind=None):
    o=box('ThrowPillow',(x,-.19,.88),(.42,.17,.40),mat,.075,(math.radians(-12),0,angle))
    if motif_kind: motif('Pillow_'+motif_kind,(x,-.294,.90),.13,blue if motif_kind=='star' else cream,motif_kind)

def arched_back(mat):
    profile=[(-.87,.72),(.87,.72),(.87,1.19)]
    for k in range(25):
        a=k*math.pi/24
        profile.append((.87*math.cos(a),1.19+.35*math.sin(a)))
    verts=[(x,y,z) for y in (.15,.45) for x,z in profile]; n=len(profile)
    faces=[tuple(range(n-1,-1,-1)),tuple(range(n,2*n))]
    faces += [(k,(k+1)%n,(k+1)%n+n,k+n) for k in range(n)]
    me=bpy.data.meshes.new('RoyalArch'); me.from_pydata(verts,[],faces); me.update()
    o=bpy.data.objects.new('RoyalArchedBack',me); scene.collection.objects.link(o)
    uv_project(o,(1.74,.3,1.54)); finish(o,'RoyalArchedBack',mat)
    bpy.context.view_layer.objects.active=o
    be=o.modifiers.new('Soft arch edges','BEVEL'); be.width=.075; be.segments=5
    bpy.ops.object.modifier_apply(modifier=be.name)
    no=o.modifiers.new('Arch normals','WEIGHTED_NORMAL'); bpy.ops.object.modifier_apply(modifier=no.name)
    pts=[(-.85,.122,.77),(-.85,.122,1.19)]
    for k in range(25):
        a=math.pi-k*math.pi/24; pts.append((.85*math.cos(a),.122,1.19+.34*math.sin(a)))
    pts.append((.85,.122,.77)); cord('RoyalArchPiping',pts,.014,trim)
    for x,z in ((-.48,1.18),(0,1.22),(.48,1.18)): ball('TuftButton',(x,.135,z),(.033,.018,.033),mat)

def blanket(side,mat,red=False,over_arm=False):
    # A continuous cloth surface flowing down the front, over a rounded crest,
    # and down the back. Corrugation and a scalloped hem remain in the mesh.
    path=([(-.57,.51),(-.58,.72),(-.56,.91),(-.48,1.01),(-.30,1.02),(-.09,1.01),(.14,1.0),(.35,.96)] if over_arm else
          [(-.17,.75),(-.16,.95),(-.13,1.16),(-.08,1.32),(0,1.39),(.14,1.40),(.29,1.36),(.39,1.23),(.43,1.02),(.44,.83)])
    width=.52; verts=[]; faces=[]; nx=24
    for j,(y,z) in enumerate(path):
        for k in range(nx+1):
            x=side+(k/nx-.5)*width; wave=.012*math.cos(k/nx*math.pi*10)
            verts.append((x,y+wave,z+wave*.3))
    for j in range(len(path)-1):
        for k in range(nx):
            a=j*(nx+1)+k; faces.append((a,a+1,a+nx+2,a+nx+1))
    me=bpy.data.meshes.new('Throw'); me.from_pydata(verts,[],faces); me.update()
    o=bpy.data.objects.new('DrapedThrow',me); scene.collection.objects.link(o); finish(o,'DrapedThrow',mat)
    distances=[0.]
    for j in range(1,len(path)): distances.append(distances[-1]+math.dist(path[j-1],path[j]))
    uv=me.uv_layers.new()
    for p in me.polygons:
        for li in p.loop_indices:
            vi=me.loops[li].vertex_index; uv.data[li].uv=(vi%(nx+1)/nx,distances[vi//(nx+1)]/.52)
    so=o.modifiers.new('Cloth thickness','SOLIDIFY'); so.thickness=.014
    bpy.context.view_layer.objects.active=o; bpy.ops.object.modifier_apply(modifier=so.name)
    if red:
        y,z=path[1]; box('Throw red woven border',(side,y-.018,z),(.52,.014,.045),redband,.004)
    for k in range(11):
        x=side-width*.46+k*width*.092; y,z=path[0]
        cord('KnottedFringe',[(x,y,z),(x+.008,y-.014,z-.075),(x,y-.017,z-.10)],.012,cream if mat!=dotthrow else gold)

palette=[red,blue,yellow,green,purple,plaid,teal,rose,denim,lime]
roots=[]; stats=[]
for i,body in enumerate(palette,1):
    parts=[]
    root=bpy.data.objects.new(f'easy_couch_{i:02}',None); scene.collection.objects.link(root); roots.append(root)
    for x in (-.82,.82):
        for y in (-.32,.31): box('TaperedWoodFoot',(x,y,.13),(.13,.14,.26),wood,.025,(0,math.radians(x*9),0))
    box('UpholsteredBase',(0,0,.35),(2.02,.91,.32),body,.11)
    for x in (-.91,.91):
        box('RoundedArm',(x,-.015,.64),(.30,.99,.63),body,.145)
        if i in (5,8,10): piping(x,-.517,.65,.27,.57,trim)
    seatmat=cream if i in (8,10) else stripes if i==2 else body
    for x in (-.395,.395):
        box('SeatCushion',(x,-.105,.575),(.78,.77,.23),seatmat,.105)
        if i in (5,8): piping(x,-.495,.565,.76,.18,trim)
    if i==3:
        for k in range(5): box('ChannelBack',(-.632+k*.316,.30,1.03),(.315,.28,.70),body,.13,(math.radians(-5),0,0))
    elif i==5:
        arched_back(body)
    else:
        for x in (-.405,.405):
            z=1.035+(0.018 if x>0 else 0)
            box('BackCushion',(x,.30,z),(.81,.31,.73),stripes if i==2 else cream if i==10 else body,.14,(math.radians(-4),0,0))
            if i in (1,8):
                ball('TuftButton',(x,.127,z),(.032,.018,.032),body)
                for sign in (-1,1):
                    cord('TuftSeam',[(x+sign*.11,.139,z+.095),(x+sign*.04,.126,z+.02),(x,.116,z)],.003,body)
            if i in (8,10): piping(x,.123,z,.78,.70,trim if i==8 else lime)
    if i==1:
        pillow(-.57,gold, .16); pillow(.57,gold,-.16)
    if i==3: pillow(-.52,olive,.16); blanket(.51,cream,True)
    if i==5: pillow(-.59,pink,.16,'heart'); pillow(.59,pink,-.16,'heart')
    if i==6: blanket(-.90,throw,over_arm=True)
    if i==7: pillow(-.53,cream,.15,'star'); pillow(.53,horizontal,-.15)
    if i==9: blanket(.47,dotthrow)
    if i==10: pillow(-.55,gingham,.12); pillow(.55,gingham,-.12)
    bpy.ops.object.select_all(action='DESELECT')
    for o in parts: o.select_set(True)
    bpy.context.view_layer.objects.active=parts[0]; bpy.ops.object.join()
    mesh=bpy.context.object; mesh.name=f'EC{i:02}_LOD0'
    scene.cursor.location=(0,0,0); bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
    bpy.ops.object.transform_apply(location=True,rotation=True,scale=True); mesh.parent=root
    lods=[mesh]
    for level,ratio in ((1,.48),(2,.20)):
        o=mesh.copy(); o.data=mesh.data.copy(); scene.collection.objects.link(o); o.name=f'EC{i:02}_LOD{level}'
        bpy.context.view_layer.objects.active=o
        mod=o.modifiers.new('LOD reduction','DECIMATE'); mod.ratio=ratio; mod.use_collapse_triangulate=True
        bpy.ops.object.modifier_apply(modifier=mod.name); lods.append(o)
    counts=[]
    for o in lods:
        o.data.calc_loop_triangles(); counts.append(len(o.data.loop_triangles))
    stats.append({'couch':i,'triangles':counts})
    bpy.ops.object.select_all(action='DESELECT'); root.select_set(True)
    for o in lods: o.select_set(True)
    bpy.context.view_layer.objects.active=root
    bpy.ops.export_scene.fbx(filepath=str(OUT/f'easy_couch_{i:02}.fbx'),use_selection=True,object_types={'EMPTY','MESH'},
        apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',axis_forward='-Z',axis_up='Y',
        bake_space_transform=True,add_leaf_bones=False,bake_anim=False,path_mode='RELATIVE',mesh_smooth_type='FACE')
    for o in lods[1:]: o.hide_render=True; o.hide_set(True)
    root.location=((i-1)%5*2.85,-((i-1)//5)*3.0,0)
    print('Completed',i,counts,flush=True)

# A numbered contact sheet rendered in Blender, with a shared orthographic view
# for all models. Each group rotates so the front and right arm are visible.
for root in roots: root.rotation_euler.z=math.radians(-18)
floor=material('Studio',(0.89,.86,.82))
box('StudioFloor',(5.7,-1.5,-.06),(30,20,.1),floor,.01)
for i,root in enumerate(roots,1):
    cu=bpy.data.curves.new('Number','FONT'); cu.body=f'{i:02}'; cu.size=.25; cu.extrude=.002
    o=bpy.data.objects.new(f'Number_{i:02}',cu); scene.collection.objects.link(o)
    o.location=(root.location.x-1.,root.location.y-.92,.012); cu.materials.append(wood)
def area(name,loc,power,size):
    d=bpy.data.lights.new(name,'AREA'); d.energy=power; d.shape='DISK'; d.size=size
    o=bpy.data.objects.new(name,d); scene.collection.objects.link(o); o.location=loc
    o.rotation_euler=(Vector((5.7,-1.5,0))-o.location).to_track_quat('-Z','Y').to_euler()
area('Softbox',(1,-4,10),2200,8); area('Fill',(13,4,8),1700,7)
d=bpy.data.cameras.new('ContactSheet'); cam=bpy.data.objects.new('ContactSheet',d); scene.collection.objects.link(cam)
cam.location=(5.7,-15,12); target=Vector((5.7,-1.5,.45)); cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler()
d.type='ORTHO'; d.ortho_scale=14.6; scene.camera=cam
scene.render.resolution_x=2000; scene.render.resolution_y=1100; scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG'; scene.render.filepath=str(REVIEW/'easy_couches_contact_sheet.png')
(REVIEW/'mesh_statistics.json').write_text(json.dumps(stats,indent=2))
manifest=[{'name':m.name,'color':list(m.diffuse_color),'texture':('Assets/_Project/Models/Couch/Easy/Textures/'+m.name+'.png') if (TEX/(m.name+'.png')).exists() else ''} for m in bpy.data.materials if m.name.startswith('EC_')]
(REVIEW/'materials.json').write_text(json.dumps({'materials':manifest},indent=2))
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'Tools/Blender/Sources/easy_couches.blend'))
bpy.ops.render.render(write_still=True)
print('EASY_COUCHES_COMPLETE',flush=True)
