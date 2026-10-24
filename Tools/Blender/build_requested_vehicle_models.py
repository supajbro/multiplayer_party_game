"""Run with Blender --background --python. Truck FBX and source stay untouched.
Produces an additive shutter using the source metal and a reference-shaped hatchback.
"""
import bpy, math, json
from pathlib import Path
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
MODELS = ROOT / 'Assets/_Project/Models'

def material(name, color, roughness=.72, metallic=0):
    m = bpy.data.materials.new(name)
    m.diffuse_color = color
    m.use_nodes = True
    n = next(n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    n.inputs['Base Color'].default_value = color
    n.inputs['Roughness'].default_value = roughness
    n.inputs['Metallic'].default_value = metallic
    if color[3] < 1:
        n.inputs['Alpha'].default_value = color[3]
        n.inputs['Transmission Weight'].default_value = .15
        m.surface_render_method = 'DITHERED'
    return m

def box(name, pos, size, mat, bevel=0, parent=None):
    bpy.ops.mesh.primitive_cube_add(size=1, location=pos)
    o = bpy.context.object; o.name = name
    o.dimensions = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    o.data.materials.append(mat)
    if bevel:
        b=o.modifiers.new('Soft edges','BEVEL'); b.width=bevel; b.segments=2
        bpy.ops.object.modifier_apply(modifier=b.name)
        o.modifiers.new('Weighted normals','WEIGHTED_NORMAL')
    if parent: o.parent=parent
    return o

def empty(name, parent=None):
    o=bpy.data.objects.new(name,None); bpy.context.collection.objects.link(o)
    o.parent=parent
    return o

def mesh(name, verts, faces, mat, parent):
    m=bpy.data.meshes.new(name); m.from_pydata(verts,[],faces); m.update()
    o=bpy.data.objects.new(name,m); bpy.context.collection.objects.link(o)
    o.data.materials.append(mat); o.parent=parent
    return o

def export(folder, name):
    folder.mkdir(parents=True,exist_ok=True)
    source_folder=ROOT/'Tools/Blender/SourceModels'
    source_folder.mkdir(parents=True,exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(source_folder/(name+'.blend')))
    bpy.ops.export_scene.fbx(filepath=str(folder/(name+'.fbx')),object_types={'MESH','EMPTY'},
        use_mesh_modifiers=True,bake_anim=False,axis_forward='-Z',axis_up='Y',add_leaf_bones=False)

# Inspect source first. It contains the framing but no shutter or actions.
bpy.ops.wm.open_mainfile(filepath=str(MODELS/'truck/truck.blend'))
assert not any('RollerDoor_Slat' in o.name for o in bpy.data.objects), 'Recover existing source slats instead'
source_metal=bpy.data.objects['Rear_Threshold'].data.materials[0].copy()
source_dark=bpy.data.objects['Rear_DoorTopHeader'].data.materials[0].copy()
source_metal.use_fake_user=True; source_dark.use_fake_user=True
for o in list(bpy.data.objects): bpy.data.objects.remove(o,do_unlink=True)
root=empty('TruckRollerShutter')
for i in range(15):
    z=1.24+(i+.5)*.145
    slat=box('RollerDoor_Slat_%02d'%i,(-3.87,0,z),(.061,2.175,.141),source_metal,.012,root)
    box('RollerDoor_Rib_%02d'%i,(-3.904,0,z+.035),(.020,2.097,.018),source_dark,.004,slat)
    # Parenting above keeps authored local coordinates; preserve the rib's world placement.
    rib=bpy.data.objects['RollerDoor_Rib_%02d'%i]
    rib.location -= slat.location
export(MODELS/'truck','TruckRollerShutter')

# Compact five-door hatchback, front=-Y. Three authored geometry levels.
bpy.ops.wm.read_factory_settings(use_empty=True)
green=material('Hatchback_Green',(0.21,.51,.105,1))
darkgreen=material('Hatchback_PanelSeams',(.09,.23,.045,1))
trim=material('Hatchback_Charcoal',(.025,.032,.035,1))
rubber=material('Hatchback_Rubber',(.016,.020,.022,1),.92)
grey=material('Hatchback_WheelGrey',(.43,.51,.53,1),.55,.15)
glass=material('Hatchback_Glass',(.19,.28,.30,.28),.22)
light=material('Hatchback_Headlights',(.88,.92,.84,1),.38)
amber=material('Hatchback_Amber',(.96,.43,.015,1),.4)
red=material('Hatchback_TailLights',(.65,.045,.015,1),.4)
seatmat=material('Hatchback_Seats',(.055,.05,.043,1),.85)
root=empty('GreenHatchback')

def cylinder(name,pos,radius,depth,mat,segments,parent):
    bpy.ops.mesh.primitive_cylinder_add(vertices=segments,radius=radius,depth=depth,
        location=pos,rotation=(0,math.pi/2,0))
    o=bpy.context.object; o.name=name; o.data.materials.append(mat); o.parent=parent
    return o

audit=[]
for lod,segments in enumerate((24,12,8)):
    group=empty('LOD%d'%lod,root)
    # Wheel arch notches cut into the body outline, retaining the same silhouette in all LODs.
    outline=[(-1.87,.48),(-1.89,.9),(-1.72,1.04),(1.72,1.04),(1.87,.92),(1.87,.48)]
    for axle in (1.28,-1.28):
        outline.append((axle+.43,.48))
        for j in range(segments//2+1):
            a=j*math.pi/(segments//2)
            outline.append((axle+.43*math.cos(a),.37+.43*math.sin(a)))
        outline.append((axle-.43,.48))
    count=len(outline)
    verts=[(side*.85,y,z) for side in (-1,1) for y,z in outline]
    faces=[tuple(range(count-1,-1,-1)),tuple(range(count,2*count))]
    faces += [(i,(i+1)%count,(i+1)%count+count,i+count) for i in range(count)]
    body=mesh('Body_LOD%d'%lod,verts,faces,green,group)
    if lod==0:
        b=body.modifiers.new('Body edge bevel','BEVEL'); b.width=.018; b.segments=2
        bpy.context.view_layer.objects.active=body; body.select_set(True)
        bpy.ops.object.modifier_apply(modifier=b.name)
    # Hood slopes down to the front; the cabin roof stays boxy and flat.
    mesh('Hood_LOD%d'%lod,[(-.85,-1.87,.92),(.85,-1.87,.92),(.85,-.77,1.09),(-.85,-.77,1.09)],[(0,1,2,3)],green,group)
    # Greenhouse: frame around individual transparent panes, open interior behind them.
    corners=[(-.82,-.77,1.05),(.82,-.77,1.05),(.70,-.24,1.84),(-.70,-.24,1.84),
             (-.82,1.72,1.05),(.82,1.72,1.05),(.70,1.40,1.84),(-.70,1.40,1.84)]
    mesh('Roof_LOD%d'%lod,[(-.73,-.29,1.86),(.73,-.29,1.86),(.73,1.45,1.86),(-.73,1.45,1.86)],[(0,1,2,3)],green,group)
    panes=[(0,1,2,3),(4,7,6,5),(0,3,7,4),(1,5,6,2)]
    for pi,inds in enumerate(panes):
        vs=[Vector(corners[i]) for i in inds]; center=sum(vs,Vector())/4
        inset=[tuple(center+(v-center)*.91) for v in vs]
        mesh('Glass_%d_LOD%d'%(pi,lod),inset,[(0,1,2,3)],glass,group)
        # Four frame strips around each window, sharing the sloping profile.
        for j in range(4):
            mesh('WindowFrame_%d_%d_LOD%d'%(pi,j,lod),[tuple(vs[j]),tuple(vs[(j+1)%4]),inset[(j+1)%4],inset[j]],[(0,1,2,3)],green,group)
    for side in (-1,1):
        box('B_Pillar_LOD%d_%d'%(lod,side),(side*.76,.48,1.45),(.065,.085,.77),green,.006 if lod==0 else 0,group)
        if lod<2:
            for y in (.23,1.24):
                box('DoorHandle',(side*.873,y,1.04),(.045,.20,.045),trim,.006,group)
            box('Mirror',(side*.94,-.64,1.13),(.17,.20,.13),trim,.015,group)
        for axle,label in ((-1.28,'Front'),(1.28,'Rear')):
            name='Wheel_%s_%s_LOD%d'%(label,'Left' if side<0 else 'Right',lod)
            wheel=empty(name,group); wheel.location=(side*.83,axle,.36)
            cylinder('Tyre', (0,0,0),.36,.22,rubber,segments,wheel)
            cylinder('Hubcap',(side*.118,0,0),.255,.018,grey,segments,wheel)
            if lod==0:
                cylinder('HubCenter',(side*.132,0,0),.085,.02,grey,12,wheel)
                for j in range(5):
                    a=j*math.tau/5
                    spoke=box('HubRecess',(side*.132,.19*math.sin(a),.19*math.cos(a)),(.012,.065,.045),trim,.008,wheel)
    for y in (-1.88,1.88):
        box('Bumper',(0,y,.57),(1.77,.16,.29),trim,.035 if lod==0 else 0,group)
    for side in (-1,1):
        box('Headlamp',(side*.55,-1.912,.91),(.43,.025,.16),light,0,group)
        box('Indicator',(side*.79,-1.912,.91),(.10,.028,.16),amber,0,group)
        box('Taillamp',(side*.81,1.897,1.02),(.13,.035,.28),red,0,group)
    box('Grille',(0,-1.978,.58),(.81,.018,.095),trim,0,group)
    box('HoodVent',(0,-1.903,.9),(.47,.025,.04),trim,0,group)
    if lod<2:
        box('InteriorFloor',(0,.42,.72),(1.52,2.43,.1),trim,0,group)
        for y in (0,.96):
            for side in (-1,1):
                box('SeatBase',(side*.37,y,.99),(.57,.55,.18),seatmat,.025 if lod==0 else 0,group)
                box('SeatBack',(side*.37,y+.21,1.30),(.56,.15,.60),seatmat,.025 if lod==0 else 0,group)
        box('Dashboard',(0,-.56,1.11),(1.48,.32,.21),trim,.01,group)
    # Combine static surfaces per material, retaining independent wheels at every level.
    static=[o for o in group.children_recursive if o.type=='MESH' and not o.parent.name.startswith('Wheel_')]
    batches=[[o for o in static if len(o.data.materials) and o.data.materials[0]==mat] for mat in list(bpy.data.materials)]
    for parts in batches:
        if not parts: continue
        bpy.ops.object.select_all(action='DESELECT')
        for o in parts: o.select_set(True)
        bpy.context.view_layer.objects.active=parts[0]
        bpy.ops.object.join(); parts[0].name=parts[0].data.materials[0].name+'_LOD%d'%lod
    tris=sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in group.children_recursive if o.type=='MESH')
    audit.append(tris)
export(MODELS/'Cars','GreenHatchback')
print('TRAFFIC_LOD_TRIANGLES',audit)

# A small studio preview, excluded from the saved production asset and FBX.
for o in bpy.data.objects:
    if o.name.startswith(('LOD1','LOD2')):
        o.hide_render=True
        for c in o.children_recursive: c.hide_render=True
floor=material('PreviewFloor',(.13,.23,.27,1))
box('PreviewGround',(0,0,-.065),(200,200,.1),floor)
bpy.ops.object.camera_add(location=(5.5,-7,4.2))
camera=bpy.context.object; camera.rotation_euler=(Vector((0,0,.9))-camera.location).to_track_quat('-Z','Y').to_euler()
camera.data.type='ORTHO'; camera.data.ortho_scale=5.7; bpy.context.scene.camera=camera
bpy.ops.object.light_add(type='AREA',location=(1,-4,7)); bpy.context.object.data.energy=1200; bpy.context.object.data.shape='DISK'; bpy.context.object.data.size=5
bpy.context.scene.world=bpy.data.worlds.new('PreviewWorld')
bpy.context.scene.world.color=(.25,.25,.25)
scene=bpy.context.scene
scene.render.engine='CYCLES'; scene.cycles.samples=24
scene.render.resolution_x=800; scene.render.resolution_y=600; scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG'
scene.render.filepath=str(ROOT/'Temp/GreenHatchback-preview.png')
bpy.ops.render.render(write_still=True)
