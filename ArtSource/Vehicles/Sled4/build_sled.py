import bpy, bmesh, math, os, json
from mathutils import Vector
ROOT=r'C:/Users/Jaden/Documents/MyUnityProjects/multiplayer_party_game'
OUT=os.path.join(ROOT,'Assets/_Project/Models/Sled')
SRC=os.path.join(ROOT,'ArtSource/Vehicles/Sled4')
os.makedirs(OUT,exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
scene=bpy.context.scene
scene.unit_settings.system='METRIC'
scene.unit_settings.scale_length=1
def mat(name,col,metal=0,rough=.4,emit=0):
    m=bpy.data.materials.new(name); m.diffuse_color=(*col,1); m.use_nodes=True
    p=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
    p.inputs['Base Color'].default_value=(*col,1); p.inputs['Metallic'].default_value=metal; p.inputs['Roughness'].default_value=rough
    if emit: p.inputs['Emission Color'].default_value=(*col,1); p.inputs['Emission Strength'].default_value=emit
    return m
white=mat('Sled_Porcelain',(.88,.86,.81),.12)
blue=mat('Sled_Cobalt',(.025,.12,.68),.35,.3)
dark=mat('Sled_Graphite',(.035,.049,.072),.3)
rubber=mat('Sled_Rubber',(.018,.022,.03),0,.75)
steel=mat('Sled_Alloy',(.26,.34,.41),.65)
yellow=mat('Sled_SafetyYellow',(1,.61,.012),.2,.32)
orange=mat('Sled_Orange',(.96,.19,.025),.2)
cyan=mat('Sled_Cyan',(.005,.76,1),.1,.25,3)
parts=[]
def finish(o,name,m,bev=0,segments=2):
    o.name=name; o.data.materials.append(m)
    if bev:
        mod=o.modifiers.new('Soft manufactured edges','BEVEL'); mod.width=bev; mod.segments=segments
        bpy.context.view_layer.objects.active=o; bpy.ops.object.modifier_apply(modifier=mod.name)
    if o.type=='MESH':
        for p in o.data.polygons:p.use_smooth=True
        mod=o.modifiers.new('Weighted normals','WEIGHTED_NORMAL'); mod.keep_sharp=True; mod.weight=40
        try:bpy.context.view_layer.objects.active=o; bpy.ops.object.modifier_apply(modifier=mod.name)
        except: pass
    parts.append(o);return o
def box(name,loc,dims,m,bev=.06):
    bpy.ops.mesh.primitive_cube_add(size=1,location=loc); o=bpy.context.object; o.dimensions=dims
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    return finish(o,name,m,bev)
def cyl(name,loc,r,depth,m,axis='Z',verts=16):
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts,radius=r,depth=depth,location=loc)
    o=bpy.context.object
    if axis=='X':o.rotation_euler[1]=math.pi/2
    if axis=='Y':o.rotation_euler[0]=math.pi/2
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    return finish(o,name,m,.012,1)
def line(name,pts,r,m):
    c=bpy.data.curves.new(name,'CURVE');c.dimensions='3D'; c.resolution_u=1;c.bevel_depth=r;c.bevel_resolution=1;c.resolution_u=1
    s=c.splines.new('POLY');s.points.add(len(pts)-1)
    for p,v in zip(s.points,pts):p.co=(*v,1)
    o=bpy.data.objects.new(name,c);scene.collection.objects.link(o);bpy.context.view_layer.objects.active=o;o.select_set(True)
    bpy.ops.object.convert(target='MESH');o=bpy.context.object
    return finish(o,name,m)
def poly(name,pts,m):
    mesh=bpy.data.meshes.new(name);mesh.from_pydata(pts,[],[list(range(len(pts)))]);mesh.update()
    o=bpy.data.objects.new(name,mesh);scene.collection.objects.link(o);o.data.materials.append(m);parts.append(o);return o
def prism(name,profile,width,axis,m,bev=.06):
    # profile in yz, extrusion in x
    verts=[(axis+s*width/2,y,z) for s in [-1,1] for y,z in profile];n=len(profile)
    faces=[tuple(reversed(range(n))),tuple(range(n,2*n))]+[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
    me=bpy.data.meshes.new(name);me.from_pydata(verts,[],faces);me.update();o=bpy.data.objects.new(name,me);scene.collection.objects.link(o)
    return finish(o,name,m,bev)
def join_since(idx,name,pivot):
    group=parts[idx:];bpy.ops.object.select_all(action='DESELECT')
    for o in group:o.select_set(True)
    bpy.context.view_layer.objects.active=group[0];bpy.ops.object.join();o=bpy.context.object;o.name=name
    scene.cursor.location=pivot;bpy.ops.object.origin_set(type='ORIGIN_CURSOR');return o
final=[]
def generated_chassis():
    # Retain the ComfyUI lower-shell silhouette as a simplified, closed convex
    # chassis. Rebuild the docking equipment separately for exact couch fit.
    before=set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=os.path.join(SRC,'Generated/4424511a_009.glb'))
    imported=[o for o in bpy.data.objects if o not in before]
    points=[o.matrix_world@v.co for o in imported if o.type=='MESH' for v in o.data.vertices]
    zmin=min(p.z for p in points);zh=max(p.z for p in points)-zmin
    band=[p for p in points if zmin+.15*zh<=p.z<=zmin+.38*zh]
    bm=bmesh.new()
    for p in band:bm.verts.new(p)
    result=bmesh.ops.convex_hull(bm,input=list(bm.verts),use_existing_faces=False)
    unused=[g for g in result['geom_unused']+result['geom_interior'] if isinstance(g,bmesh.types.BMVert) and g.is_valid]
    if unused:bmesh.ops.delete(bm,geom=list(set(unused)),context='VERTS')
    bmesh.ops.dissolve_limit(bm,angle_limit=.07,verts=list(bm.verts),edges=list(bm.edges),use_dissolve_boundaries=False)
    me=bpy.data.meshes.new('ComfyUI simplified lower chassis');bm.to_mesh(me);bm.free()
    for o in imported:bpy.data.objects.remove(o,do_unlink=True)
    o=bpy.data.objects.new('Generated chassis',me);scene.collection.objects.link(o)
    bpy.context.view_layer.objects.active=o;o.select_set(True)
    if len(me.polygons)>160:
        d=o.modifiers.new('Reduce generated shell','DECIMATE');d.ratio=160/len(me.polygons);bpy.ops.object.modifier_apply(modifier=d.name)
    # Normalise generated arbitrary scale to the measured four-couch envelope.
    lo=[min(v.co[i] for v in o.data.vertices) for i in range(3)];hi=[max(v.co[i] for v in o.data.vertices) for i in range(3)]
    for v in o.data.vertices:
        for a,dim in enumerate([5.55,4.25,.48]):v.co[a]=((v.co[a]-lo[a])/(hi[a]-lo[a])-.5)*dim
        v.co.z+=.31
    return finish(o,'ComfyUI lower chassis refined',dark,.018)
# Main low rounded hauler chassis; forward is -Y in Blender.
i=len(parts)
generated_chassis()
box('Floating ivory hull',(0,0,.54),(5.65,4.3,.37),white,.23)
box('Recessed graphite deck',(0,0,.735),(5.15,3.75,.16),dark,.13)
box('Centre longitudinal spine',(0,0,.83),(.13,3.50,.10),steel,.035)
box('Centre transverse spine',(0,0,.83),(4.95,.12,.10),steel,.035)
for s in [-1,1]:
    box('Side armour rail',(s*2.68,0,.69),(.28,3.38,.45),white,.10)
    box('Hazard rail backing',(s*2.837,0,.68),(.025,2.75,.24),dark,.01)
    # Slanted yellow hazard inlays, clipped inside the rail rectangle.
    for k in range(9):
        ya=-1.34+k*.30
        poly('Diagonal hazard',[(s*2.853,ya,.565),(s*2.853,ya+.15,.565),(s*2.853,min(ya+.30,1.37),.795),(s*2.853,ya+.15,.795)],yellow)
    box('Cyan side hover slit',(s*2.81,0,.30),(.13,3.36,.065),cyan,.025)
    box('Side magnetic badge',(s*2.87,0,.72),(.08,.52,.34),dark,.08)
    # U magnet pictogram on side badge
    line('U magnet sigil',[(s*2.917,-.13,.81),(s*2.917,-.13,.65),(s*2.917,-.08,.60),(s*2.917,.08,.60),(s*2.917,.13,.65),(s*2.917,.13,.81)],.022,cyan)
    for y in [-1.83,1.83]:
        box('Cobalt corner impact cap',(s*2.53,y,.62),(.62,.60,.47),blue,.13)
        box('Amber corner inset',(s*2.81,y,.78),(.07,.20,.09),orange,.02)
        # sparse enamel chips
        poly('Corner paint chip',[(s*2.845,y-.16,.52),(s*2.845,y-.05,.54),(s*2.845,y-.11,.62)],steel)
box('Front mask panel',(0,-2.04,.64),(3.85,.24,.40),white,.12)
box('Rear power casing',(0,2.02,.63),(3.90,.25,.36),blue,.1)
box('Front hover slit',(0,-2.183,.32),(1.8,.055,.055),cyan,.02)
for x in [-.65,0,.65]:box('Rear ventilation',(x,2.16,.67),(.40,.025,.065),dark,.02)
# Playful angular domino-mask graphic, flush on the forward face.
outline=[(-.64,.79),(-.44,.74),(0,.67),(.44,.74),(.64,.79),(.55,.54),(.28,.49),(0,.56),(-.28,.49),(-.55,.54)]
poly('Domino mask',[(x,-2.168,z) for x,z in outline],dark)
for s in [-1,1]:
    poly('Mischief eye',[(s*.13,-2.173,.626),(s*.47,-2.173,.695),(s*.36,-2.173,.587)],white)
    line('Mask cheek slash',[(s*.34,-2.176,.52),(s*.39,-2.176,.46)],.012,dark)
# deck fasteners and subtle wear, restrained for game readability
for x in [-2.36,2.36]:
    for y in [-1.58,0,1.58]:cyl('Captive deck bolt',(x,y,.833),.048,.02,steel,verts=8)
for x,y in [(-1.9,-1.98),(1.7,-1.98),(-2.69,.74),(2.69,-.7)]:
    box('Small enamel scuff',(x,y,.837),(.10,.025,.004),steel,.0)
body=join_since(i,'Main_Sled_Body',(0,0,0));final.append(body)
# Two continuous curled runners, separate and independently editable.
for s,label in [(-1,'Left'),(1,'Right')]:
    i=len(parts)
    profile=[(-2.20,.46),(-2.22,.27),(-2.08,.09),(-1.86,.025),(1.88,.025),(2.10,.12),(2.20,.29),(2.15,.48),(1.95,.33),(-1.85,.30),(-2.02,.36)]
    prism('Rounded runner ski',profile,.46,s*2.22,rubber,.065)
    box('Runner spine',(s*2.22,0,.30),(.29,3.38,.23),steel,.075)
    for y in [-1.25,1.25]:box('Hover assist insert',(s*2.22,y,.16),(.25,.65,.045),cyan,.025)
    final.append(join_since(i,label+'_Runner',(s*2.22,0,.1)))
# Four generous rectangular docking cassettes, each with two clamps.
for y,row in [(-.88,'Front'),(.88,'Rear')]:
    for x,col in [(-1.28,'Left'),(1.28,'Right')]:
        i=len(parts)
        box('Dock frame',(x,y,.825),(2.42,1.58,.10),steel,.09)
        box('Magnetic traction bed',(x,y,.888),(2.32,1.44,.065),dark,.075)
        # Double luminous target rings sit above the bed.
        for rad,thick in [(.43,.018),(.29,.009)]:
            pts=[(x+rad*math.cos(a*math.tau/48),y+rad*math.sin(a*math.tau/48),.927) for a in range(49)]
            line('Magnetic target ring',pts,thick,cyan)
        for sy in [-1,1]:
            yy=y+sy*.69
            box('Clamp pedestal',(x,yy,.98),(.45,.18,.22),white,.055)
            box('Clamp jaw',(x,yy,1.14),(.30,.14,.20),dark,.045)
            box('Magnetic jaw light',(x,yy-sy*.078,1.15),(.18,.024,.105),cyan,.01)
            box('Amber hinge foot',(x-.22,yy,.97),(.07,.17,.10),yellow,.02)
        for sx in [-1,1]:
            for sy in [-1,1]:cyl('Dock captive bolt',(x+sx*1.06,y+sy*.62,.933),.033,.018,steel,verts=8)
        final.append(join_since(i,'Magnetic_Dock_'+row+'_'+col,(x,y,.9)))
# Four grab handles: a tough white arch with a thick black grip.
for x,col in [(-2.59,'Left'),(2.59,'Right')]:
    for y,row in [(-1.35,'Front'),(1.35,'Rear')]:
        i=len(parts)
        for dy in [-.38,.38]:
            box('Handle mounting shoe',(x,y+dy,.91),(.32,.25,.20),white,.055)
            cyl('Amber handle pivot',(x+(-.17 if x<0 else .17),y+dy,.99),.095,.065,yellow,'X')
        line('Handle ivory arch',[(x,y-.38,.96),(x,y-.44,1.42),(x,y-.32,1.58),(x,y+.32,1.58),(x,y+.44,1.42),(x,y+.38,.96)],.095,white)
        cyl('Rubber grab grip',(x,y,1.58),.108,.55,rubber,'Y')
        for dy in [-.22,-.11,0,.11,.22]:cyl('Grip raised ribs',(x,y+dy,1.58),.115,.025,dark,'Y',12)
        final.append(join_since(i,'Grab_Handle_'+row+'_'+col,(x,y,.96)))
# Two optional yellow tow loops and their dark sockets.
for x,label in [(-1.44,'Left'),(1.44,'Right')]:
    i=len(parts)
    box('Tow socket',(x,-2.17,.48),(.46,.20,.24),dark,.06)
    box('Tow socket cyan marker',(x,-2.282,.57),(.27,.025,.035),cyan,.01)
    line('Forged yellow tow loop',[(x-.14,-2.24,.48),(x-.14,-2.36,.29),(x-.09,-2.40,.25),(x+.09,-2.40,.25),(x+.14,-2.36,.29),(x+.14,-2.24,.48)],.055,yellow)
    final.append(join_since(i,'Front_Tow_Hook_'+label,(x,-2.17,.48)))
# Root and real mesh UVs; all final meshes have applied scale and finite normals.
root=bpy.data.objects.new('CouchGuys_4Couch_Sled',None);scene.collection.objects.link(root)
root['forward']='-Y Blender / +Z Unity';root['dock_clearance_m']='2.42 x 1.58';root['couch_scale_reference_m']='2.2772 x 0.9913 maximum sampled width/depth'
root['ComfyUI_base_job']='4424511a-2d7b-4686-9649-abec7c367465'
root['authoring']='Pixal3D base lower shell simplified in Blender; deck and functional modules rebuilt for measured couch fit'
for o in final:
    o.parent=root
    bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o
    bm=bmesh.new();bm.from_mesh(o.data)
    bmesh.ops.triangulate(bm,faces=list(bm.faces))
    bad=[f for f in bm.faces if f.calc_area()<1e-10]
    if bad:bmesh.ops.delete(bm,geom=bad,context='FACES')
    loose=[v for v in bm.verts if not v.link_faces]
    if loose:bmesh.ops.delete(bm,geom=loose,context='VERTS')
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(o.data);bm.free()
    bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=1.1519,island_margin=.02);bpy.ops.object.mode_set(mode='OBJECT')
# Render-only studio collection, excluded from FBX.
studio=bpy.data.collections.new('Studio_Preview_Only');scene.collection.children.link(studio)
def studio_obj(o):
    for c in list(o.users_collection):c.objects.unlink(o)
    studio.objects.link(o)
floor=box('Studio floor',(0,0,-.10),(200,200,.12),mat('Studio_Backdrop',(.085,.12,.19)),.0);studio_obj(floor)
def camera(name,loc,target,ortho):
    bpy.ops.object.camera_add(location=loc);o=bpy.context.object;o.name=name;o.rotation_euler=(Vector(target)-o.location).to_track_quat('-Z','Y').to_euler();o.data.type='ORTHO';o.data.ortho_scale=ortho;studio_obj(o);return o
cam=camera('Presentation_Camera',(8,-10,9),(0,0,.55),8.8);scene.camera=cam
for loc,power,size in [((1,-4,9),1700,7),((-6,-1,5),1200,5),((2,6,7),2100,5)]:
    bpy.ops.object.light_add(type='AREA',location=loc);o=bpy.context.object;o.data.energy=power;o.data.shape='DISK';o.data.size=size;o.rotation_euler=(-o.location).to_track_quat('-Z','Y').to_euler();studio_obj(o)
scene.world=bpy.data.worlds.new('Studio World');scene.world.color=(.18,.18,.18)
try:scene.render.engine='CYCLES'
except TypeError:pass
scene.cycles.samples=32
scene.render.resolution_x=1400;scene.render.resolution_y=1100;scene.render.resolution_percentage=100
scene.view_settings.view_transform='AgX'
scene.render.image_settings.file_format='PNG'
scene.render.filepath=os.path.join(SRC,'Sled4_Preview.png')
# Export only selected game meshes plus root, in metres and Unity axes.
bpy.ops.object.select_all(action='DESELECT');root.select_set(True)
for o in final:o.select_set(True)
bpy.context.view_layer.objects.active=body
bpy.ops.export_scene.fbx(filepath=os.path.join(OUT,'CouchGuys_4Couch_Sled.fbx'),use_selection=True,object_types={'MESH','EMPTY'},apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',axis_forward='-Z',axis_up='Y',use_mesh_modifiers=True,mesh_smooth_type='FACE',add_leaf_bones=False,bake_anim=False,path_mode='AUTO')
stats={'objects':[o.name for o in final],'triangles':sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in final),'vertices':sum(len(o.data.vertices) for o in final),'materials':8,'dock_dimensions_m':[2.42,1.58],'body_dimensions_m':list(body.dimensions),'forward_blender':'-Y','source':'ComfyUI Pixal3D lower-shell base simplified and assembled in Blender','comfyui_job':'4424511a-2d7b-4686-9649-abec7c367465'}
with open(os.path.join(SRC,'MeshReport.json'),'w') as f:json.dump(stats,f,indent=2)
studio.hide_viewport=True
bpy.ops.outliner.orphans_purge(do_recursive=True)
bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT,'CouchGuys_4Couch_Sled.blend'))
studio.hide_viewport=False
bpy.ops.render.render(write_still=True)
print('SLED_REPORT',json.dumps(stats))
