"""One-shot dispatcher authoring. Never invoked by Unity import or Play mode."""
import bpy, bmesh, math, json
from pathlib import Path
from mathutils import Vector

ROOT = Path(r'C:/Users/Jaden/Documents/MyUnityProjects/multiplayer_party_game')
OUT = ROOT / 'Assets/_Project/Models/Dispatcher'
OUT.mkdir(parents=True, exist_ok=True)
previous_scene = bpy.context.scene
scene = bpy.data.scenes.new('dispatcher_01_authoring')
bpy.context.window.scene = scene
scene.unit_settings.system = 'METRIC'
scene.unit_settings.scale_length = 1.0

def material(name, color, metallic=0, rough=.4, emission=0):
    m = bpy.data.materials.new('Dispatcher_' + name)
    m.diffuse_color = (*color, 1)
    m.use_nodes = True
    p = next(n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    p.inputs['Base Color'].default_value = (*color, 1)
    p.inputs['Metallic'].default_value = metallic
    p.inputs['Roughness'].default_value = rough
    if emission:
        p.inputs['Emission Color'].default_value = (*color, 1)
        p.inputs['Emission Strength'].default_value = emission
    return m

blue = material('LightBlue', (.30,.49,.80))
rear = material('RearBlue', (.16,.25,.46))
bezel = material('Bezel', (.025,.035,.075))
black = material('Screen', (.003,.008,.006), rough=.23)
metal = material('Pole', (.52,.49,.44), metallic=.65, rough=.3)
base_mat = material('Base', (.46,.45,.64))
rubber = material('WheelRubber', (.018,.022,.027), rough=.7)
hub = material('WheelHub', (.045,.050,.060), rough=.6)
orange = material('OrangeRedPanel', (1,.105,.025))
button = material('OrangeButtons', (1,.40,.015), rough=.3)
yellow = material('YellowBeacon', (1,.69,.015), rough=.26)

def finish(o, mat):
    o.data.materials.append(mat)
    bm=bmesh.new(); bm.from_mesh(o.data)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(o.data); bm.free()
    return o

def mesh(name, verts, faces, mat):
    data=bpy.data.meshes.new(name); data.from_pydata(verts, [], faces); data.update()
    o=bpy.data.objects.new(name,data); scene.collection.objects.link(o)
    return finish(o,mat)

def box(name, loc, dims, mat, radius, segments):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc)
    o=bpy.context.object; o.name=name; o.dimensions=dims
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    mod=o.modifiers.new('Soft edges','BEVEL'); mod.width=radius; mod.segments=segments
    bpy.ops.object.modifier_apply(modifier=mod.name)
    finish(o,mat)
    for p in o.data.polygons: p.use_smooth=True
    mod=o.modifiers.new('Weighted corner normals','WEIGHTED_NORMAL'); mod.keep_sharp=True; mod.weight=50
    bpy.ops.object.modifier_apply(modifier=mod.name)
    return o

def cylinder(name, loc, radius, depth, mat, sides, bevel_width=0, segments=1, wheel=False):
    bpy.ops.mesh.primitive_cylinder_add(vertices=sides, radius=radius, depth=depth, location=loc)
    o=bpy.context.object; o.name=name
    if wheel: o.rotation_euler[1]=math.pi/2
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    if bevel_width:
        m=o.modifiers.new('Rounded rim','BEVEL'); m.width=bevel_width; m.segments=segments
        bpy.ops.object.modifier_apply(modifier=m.name)
    if wheel and name == 'Wheel':
        # Keep low-sided bevelled tires touching the shared ground plane.
        lowest=min(v.co.z for v in o.data.vertices)
        for v in o.data.vertices: v.co.z *= radius / abs(lowest)
    finish(o,mat)
    for p in o.data.polygons: p.use_smooth=len(p.vertices)==4
    return o

def rounded(w,h,r,n):
    pts=[]
    for cx,cz,start in [(w/2-r,h/2-r,0),(-w/2+r,h/2-r,90),(-w/2+r,-h/2+r,180),(w/2-r,-h/2+r,270)]:
        for i in range(n+1):
            a=math.radians(start+i*90/n)
            pts.append((cx+r*math.cos(a),cz+r*math.sin(a)))
    return pts

def loft(name, layers, mat, n, cap=True):
    # Layers: width, height, corner radius, depth Y, center Z.
    verts=[]
    for w,h,r,y,z in layers: verts.extend((x,y,z+v) for x,v in rounded(w,h,r,n))
    k=len(verts)//len(layers); faces=[]
    for j in range(len(layers)-1):
        for i in range(k): faces.append((j*k+i,j*k+(i+1)%k,(j+1)*k+(i+1)%k,(j+1)*k+i))
    if cap: faces.extend([tuple(reversed(range(k))),tuple((len(layers)-1)*k+i for i in range(k))])
    o=mesh(name,verts,faces,mat)
    for p in o.data.polygons: p.use_smooth=True
    return o

def screen_y(x,z):
    return -.271-.018*max(0,1-(x/.421)**2)*max(0,1-((z-1.84)/.334)**2)

roots=[]; stats={}
for lod,seg,n,sides in [(0,3,5,20),(1,2,3,12),(2,1,1,8)]:
    prefix='dispatcher_01_LOD'+str(lod)
    collection=bpy.data.collections.new(prefix); scene.collection.children.link(collection)
    root=bpy.data.objects.new(prefix,None); collection.objects.link(root); roots.append(root)
    root['units']='metres'; root['front']='Blender -Y / Unity +Z'; root['pivot']='ground centre'
    start=set(scene.objects)
    box('TVHousing',(0,-.025,1.82),(1.08,.43,.92),blue,.105,seg)
    loft('CRTRear',[(.94,.79,.11,.14,1.83),(.66,.53,.09,.51,1.83),(.64,.51,.08,.54,1.83)],rear,n)
    loft('ScreenBezel',[(.91,.752,.135,-.242,1.84),(.875,.718,.125,-.266,1.84),(.843,.666,.12,-.270,1.84)],bezel,n,False)
    # Independent, gently convex screen with planar normalized UV0.
    boundary=rounded(.843,.666,.12,n)
    vertices=[(0,screen_y(0,1.84),1.84)]; faces=[]; k=len(boundary)
    rings=4 if lod==0 else 2
    for j in range(1,rings+1):
        f=j/rings
        vertices.extend((x*f,screen_y(x*f,1.84+z*f),1.84+z*f) for x,z in boundary)
    for i in range(k): faces.append((0,1+i,1+(i+1)%k))
    for j in range(rings-1):
        a=1+j*k; b=a+k
        for i in range(k): faces.append((a+i,b+i,b+(i+1)%k,a+(i+1)%k))
    screen=mesh(prefix+'_Screen',vertices,faces,black)
    uv=screen.data.uv_layers.new(name='ScreenUV')
    for poly in screen.data.polygons:
        poly.use_smooth=True
        for li in poly.loop_indices:
            v=screen.data.vertices[screen.data.loops[li].vertex_index].co
            uv.data[li].uv=(v.x/.843+.5,(v.z-1.84)/.666+.5)
    screen['shader_ready']='UV0: left-bottom (0,0), right-top (1,1). Separate black material.'
    for x in [.325,.425]:
        box('ButtonRim',(x,-.247,1.438),(.081,.032,.081),bezel,.015,seg)
        box('OrangeButton',(x,-.269,1.44),(.059,.022,.059),button,.010,seg)
    if lod<2:
        for z in [1.63,1.69,1.75]:
            box('SideVent',(.541,-.005,z),(.009,.125,.022),bezel,.008,seg)
            box('SideVent',(-.541,-.005,z),(.009,.125,.022),bezel,.008,seg)
    cylinder('Pole',(0,.025,.915),.045,.91,metal,sides)
    cylinder('BeaconFoot',(0,.015,2.30),.093,.045,rear,sides,.008,seg)
    # Domed beacon using stacked circular rings, with no extra mechanism.
    verts=[]; fs=[]
    profile=[(2.318,.074),(2.405,.074),(2.438,.061),(2.458,.035),(2.464,.008)]
    for z,r in profile:
        verts.extend((r*math.cos(2*math.pi*i/sides),.015+r*math.sin(2*math.pi*i/sides),z) for i in range(sides))
    for j in range(len(profile)-1):
        for i in range(sides): fs.append((j*sides+i,j*sides+(i+1)%sides,(j+1)*sides+(i+1)%sides,(j+1)*sides+i))
    fs += [tuple(reversed(range(sides))),tuple((len(profile)-1)*sides+i for i in range(sides))]
    dome=mesh('Beacon',verts,fs,yellow)
    for p in dome.data.polygons: p.use_smooth=True
    # Compact sloped platform: wider footprint and smaller top like reference.
    loft('Base',[(.96,.22,.055,-.34,.31),(.96,.22,.055,.34,.31)],base_mat,n)
    # A bevelled trapezoidal chassis using front/back cross sections.
    chassis=box('BaseUpper',(0,0,.355),(.91,.63,.19),base_mat,.055,seg)
    for v in chassis.data.vertices:
        if v.co.z>0: v.co.x*=.88; v.co.y*=.82
    panel=box('FrontPanel',(0,-.332,.34),(.245,.022,.105),orange,.014,seg)
    for x in [-.413,.413]:
        for y in [-.218,.218]:
            cylinder('Wheel',(x,y,.13),.13,.105,rubber,sides,.018,seg,True)
            if lod<2:
                cylinder('WheelHub',(x+(.054 if x>0 else -.054),y,.13),.076,.008,hub,sides,.006,1,True)
    parts=[o for o in scene.objects if o not in start and o.type=='MESH' and o not in [screen]]
    bpy.ops.object.select_all(action='DESELECT')
    for o in parts: o.select_set(True)
    bpy.context.view_layer.objects.active=parts[0]
    bpy.ops.object.join(); body=bpy.context.object; body.name=prefix+'_Body'
    for o in [body,screen]:
        # Mesh vertices carry shape; all exported origins are the common ground pivot.
        bpy.context.scene.cursor.location=(0,0,0)
        bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active=o
        bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
        for c in list(o.users_collection): c.objects.unlink(o)
        collection.objects.link(o); o.parent=root
    stats[prefix]={o.name:sum(len(p.vertices)-2 for p in o.data.polygons) for o in [body,screen]}
    bpy.ops.object.select_all(action='DESELECT')
    for o in [root,body,screen]: o.select_set(True)
    bpy.ops.export_scene.fbx(filepath=str(OUT/(prefix+'.fbx')),use_selection=True,object_types={'MESH','EMPTY'},global_scale=1,apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',axis_forward='-Z',axis_up='Y',use_mesh_modifiers=True,mesh_smooth_type='FACE',use_tspace=False,bake_anim=False,add_leaf_bones=False)
    if lod>0:
        for o in [root,body,screen]: o.hide_set(True); o.hide_render=True

scene['asset_notes']='Static dispatcher model only. No gameplay integration. LOD roots share a ground-centre pivot. Separate blank black screen at every LOD; waveform reserved for a future shader.'
bpy.ops.object.select_all(action='DESELECT')
roots[0].select_set(True); bpy.context.view_layer.objects.active=roots[0]
# Save only the new asset scene; preserve the user's existing Blender file and other scenes.
bpy.data.libraries.write(str(OUT/'dispatcher_01.blend'),{scene},fake_user=True)
(OUT/'dispatcher_01_geometry.json').write_text(json.dumps(stats,indent=2))
print('EXPORTED:',json.dumps(stats))

