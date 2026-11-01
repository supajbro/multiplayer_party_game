"""Explicit, in-place concept pass on the existing depot. Run with Blender -b --python."""
import bpy, math, random
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[2]
folder=ROOT/'Assets/_Project/Models/depot'
bpy.ops.wm.open_mainfile(filepath=str(folder/'depot.blend'),load_ui=False)
s=bpy.context.scene
s.frame_set(1)
# Retain structural meshes, conveyor, gameplay markers and original footprint.
retire=('ExitRollerDoor_', 'BrickFacade_Mortar', 'Left_WoodWall_Vertical', 'Right_WoodWall_Vertical', 'InteriorPendantLED', 'Front_Bay_Canopy', 'CanopySupportStem', 'DepotPolish_Canopy', 'DepotPolish_Jamb', 'ExitDoor_ExteriorButton_', 'ExitDoor_InteriorButton_', 'CouchLogo_', 'Hub_')
for o in list(s.objects):
    if o.name.startswith(retire) or (o.type=='FONT'):
        bpy.data.objects.remove(o,do_unlink=True)
for o in list(s.objects):
    if o.name.startswith('WarehouseCornerSupport') and abs(o.location.x)<2: bpy.data.objects.remove(o,do_unlink=True)
C=bpy.data.collections.get('CGD_Concept_Hub')
if not C:
    C=bpy.data.collections.new('CGD_Concept_Hub'); s.collection.children.link(C)
def mat(name,c,metal=0,rough=.5,emit=0):
    m=bpy.data.materials.get('CGD_M_'+name) or bpy.data.materials.new('CGD_M_'+name)
    m.diffuse_color=(*c,1); m.use_nodes=True
    bs=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
    bs.inputs['Base Color'].default_value=(*c,1);bs.inputs['Metallic'].default_value=metal;bs.inputs['Roughness'].default_value=rough
    e=bs.inputs.get('Emission Color') or bs.inputs.get('Emission')
    if e: e.default_value=(*c,1)
    if bs.inputs.get('Emission Strength'):bs.inputs['Emission Strength'].default_value=emit
    return m
teal=mat('CouchGuysTeal',(.025,.43,.51),.25,.32)
dark=mat('DarkTeal',(.018,.18,.23),.2)
white=mat('WarmWhite',(.88,.91,.92))
yellow=mat('SunnyYellow',(1,.66,.025),.12,.32)
metal=mat('SatinMetal',(.53,.62,.7),.65,.32)
navy=mat('DeepNavy',(.035,.065,.1))
glow=mat('HubWarmLight',(1,.79,.24),0,.3,4)
blue=mat('TintedGlass',(.16,.65,.81),.25,.15)
# Opaque stylized glass in Blender preview; Unity configures real transparent glass.
mat('PaleHoneyTimber',(.82,.86,.9)); mat('WarmIvoryBrick',(.84,.87,.91));mat('WoodEdge',(.05,.31,.38));mat('PolishedWarehouseFloor',(.37,.47,.59),.24,.32)
card=mat('Cardboard',(.63,.36,.13));leaf=mat('HubLeaves',(.19,.5,.055));orange=mat('HubOrange',(1,.32,.015))
def own(o,name,m):
    o.name='Hub_'+name
    for c in list(o.users_collection):c.objects.unlink(o)
    C.objects.link(o)
    if m:o.data.materials.append(m)
    return o
def block(name,p,d,m,bevel=.08):
    bpy.ops.mesh.primitive_cube_add(size=1,location=p);o=own(bpy.context.object,name,m);o.dimensions=d
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    if bevel:
        q=o.modifiers.new('Rounded edges','BEVEL');q.width=min(bevel,min(d)*.4);q.segments=3
    return o
def cyl(name,p,r,h,m):
    bpy.ops.mesh.primitive_cylinder_add(vertices=16,radius=r,depth=h,location=p)
    return own(bpy.context.object,name,m)
def text(name,body,p,size,m,inside=True,floor=False):
    cu=bpy.data.curves.new(name,'FONT');cu.body=body;cu.align_x='CENTER';cu.align_y='CENTER';cu.size=size;cu.extrude=.008;cu.bevel_depth=.003;cu.resolution_u=3
    o=bpy.data.objects.new('Hub_'+name,cu);C.objects.link(o);o.location=p;cu.materials.append(m)
    o.rotation_euler=(0,0,math.pi) if floor else (math.pi/2,0,math.pi if inside else 0)
    bpy.context.view_layer.objects.active=o;o.select_set(True);bpy.ops.object.convert(target='MESH');o.select_set(False)
    return o
def sign(name,body,x,y,z,w,h,size):
    block(name+'_Rim',(x,y,z),(w,.18,h),white,.22)
    block(name+'_Face',(x,y+.115,z),(w-.13,.10,h-.13),dark,.19)
    text(name+'_Lettering',body,(x,y+.18,z),size,white)
def couch_logo(name,x,y,z,scale=1):
    for label,dx,dz,w,h in [('Back',0,.18,1.45,.65),('Seat',0,-.18,1.55,.33),('ArmL',-.8,-.01,.3,.65),('ArmR',.8,-.01,.3,.65)]:
        block(name+label,(x+dx*scale,y,z+dz*scale),(w*scale,.12,h*scale),yellow,.15*scale)
    for dx in (-.22,.22):
        o=cyl(name+'Eye',(x+dx*scale,y+.09,z+.22*scale),.045*scale,.03,dark);o.rotation_euler[0]=math.pi/2
    text(name+'Smile','u',(x,y+.1,z+.05*scale),.22*scale,dark)
# Shrink the existing second bay to a 4m clear automatic entrance.
for x in (4.1,10):block('Entrance_WallInfill',(x,-11.495,2.7),(1.9,.45,5.4),white)
block('Entrance_Header',(7.05,-11.495,4.55),(4,.45,1.7),white)
for x in (4.91,9.19):block('GlassFrame_Jamb',(x,-11.29,1.9),(.28,.65,3.8),teal,.12)
block('GlassFrame_Header',(7.05,-11.29,3.8),(4.56,.7,.5),teal,.18)
block('GlassFrame_Operator',(7.05,-11.1,4.0),(4.0,.65,.32),dark)
# Each panel is one editable, animated object including frame and handle.
for i,x in enumerate((6.05,8.05)):
    parts=[block('GlassPanel_'+str(i),(x,-11.48,1.84),(1.96,.12,3.5),blue,.035)]
    for dx in (-.96,.96):parts.append(block('GlassEdge',(x+dx,-11.47,1.84),(.065,.18,3.52),teal,.02))
    for z in (.1,1.05,3.58):parts.append(block('GlassEdge',(x,-11.47,z),(1.96,.18,.065),teal,.02))
    parts.append(block('GlassHandle',(x+(.64 if i==0 else -.64),-11.34,1.6),(.075,.1,.55),yellow,.025))
    bpy.ops.object.select_all(action='DESELECT')
    for o in parts:o.select_set(True)
    bpy.context.view_layer.objects.active=parts[0];bpy.ops.object.join();o=parts[0];o.name='Hub_GlassPanel_'+str(i)
    o.keyframe_insert(data_path='location',frame=1);o.location.x+=(-2.05 if i==0 else 2.05);o.keyframe_insert(data_path='location',frame=41)
    o.keyframe_insert(data_path='location',frame=51);o.location.x=x;o.keyframe_insert(data_path='location',frame=91)
# Roller opening stays 8m wide and 5.35m tall. Original articulated animation retained.
for x in (-11.48,-2.92):block('TruckFrame_Jamb',(x,-11.05,2.85),(.48,.8,5.7),teal,.19)
block('TruckFrame_Housing',(-7.2,-11.05,5.95),(8.95,1.0,1.05),teal,.28)
for x in (-10.5,-7.2,-3.9):block('TruckDoor_Light',(x,-10.49,5.62),(1.05,.12,.12),glow,.05)
for x in (-11.75,-2.65):
    cyl('Bollard',(x,-10.1,.78),.23,1.5,yellow);cyl('BollardBoot',(x,-10.1,.10),.29,.16,metal)
# High contrast layered branding on both faces.
for inside,y in ((True,-11.16),(False,-11.91)):
    if inside:
        sign('TruckExit','TRUCK EXIT  >>>',-7.2,y,6.95,8.4,.83,.5)
        sign('StaffEntrance','STAFF\nENTRANCE / EXIT',7.05,y,4.75,4.4,.87,.27)
        sign('Brand','Couch Guys',.6,y,5.95,5.9,1.35,.78)
        block('DepotBadge',(.6,y+.12,4.98),(3.2,.18,.62),yellow,.2)
        text('DepotLetters','D E P O T',(.6,y+.24,4.98),.37,dark)
        couch_logo('BrandCouch',.6,y+.22,7.08,1.15)
    else:
        text('ExteriorBrand','COUCH GUYS DEPOT',(0,y-.12,6.85),.65,dark,False)
# Teal/yellow horizontal wall bands, columns and overhead steelwork.
for x,w in ((-12.6,2.8),(-.025,6.35),(12.475,3.05)):
    for z,h,m in ((.72,1.4,teal),(3.55,.4,teal),(4.05,.48,yellow)):
        block('FrontBand',(x,-11.2,z),(w,.10,h),m,.02)
for x in (-13.74,13.74):
    for z,h,m in ((.7,1.4,teal),(3.55,.4,teal),(4.05,.48,yellow)):
        block('SideBand',(x,0,z),(.12,22.5,h),m,.02)
for x in (-13.4,13.4):
    for y in (-10.8,2.8,10.8):block('SteelColumn',(x,y,3.65),(.32,.4,7.3),teal)
for y in (-8,-1,6):
    block('CeilingBeam',(0,y,7.12),(27.3,.28,.42),teal)
    for x in (-10,-3,4,11):
        block('CeilingPurlin',(x,0,7.3),(.15,22.6,.18),dark,.03)
for x,y in ((-9,-6),(-2,-4),(5,-7),(11,-3),(-8,3),(0,3)):
    cyl('PendantStem',(x,y,6.78),.045,.85,metal)
    bpy.ops.mesh.primitive_cone_add(vertices=24,radius1=.58,radius2=.25,depth=.35,location=(x,y,6.2));own(bpy.context.object,'PendantShade',teal)
    cyl('PendantGlow',(x,y,6.01),.49,.045,glow)
    ld=bpy.data.lights.new('Hub_Pendant','POINT');ld.energy=170;ld.color=(1,.83,.56);ld.shadow_soft_size=1.2
    ob=bpy.data.objects.new('Hub_PendantLight',ld);C.objects.link(ob);ob.location=(x,y,5.8)
for x,m in ((-12,orange),(12,yellow)):
    o=cyl('OverheadPipe',(x,0,6.8),.12,22,m);o.rotation_euler[0]=math.pi/2
    o=cyl('PipeRiser',(x,-10.7,5.5),.12,2.6,m)
    for y in (-8,-3,2,7):
        q=cyl('PipeCollar',(x,y,6.8),.155,.08,metal);q.rotation_euler[0]=math.pi/2
# Conveyor remains at the exact original path and pickup height.
for o in s.objects:
    if o.type=='MESH' and o.name.startswith(('Conveyor_MainBody','Conveyor_VisibilityBaffle','Conveyor_LowOpaque','WarehouseCeilingBeam','WarehouseCornerSupport')):
        o.data.materials.clear();o.data.materials.append(teal)
sign('Receiving','COUCH RECEIVING  v',4.5,1.0,3.7,4.25,.7,.32)
for y in (-4,-2,0):
    for x in (2.72,6.28):block('ConveyorSafetyLamp',(x,y,.85),(.08,.75,.10),glow,.03)
# Floor receiving bay and truck route. Open gameplay space stays empty.
block('ReceivingZone',(4.5,-6.95,.063),(5.2,3.1,.018),teal,.3)
text('ReceivingLabel','COUCH\nDROP ZONE',(4.5,-7.05,.078),.42,white,floor=True)
for x in (1.9,7.1):block('ZoneBorder',(x,-6.95,.08),(.10,3,.02),yellow,.01)
for y in (-8.5,-5.4):block('ZoneBorder',(4.5,y,.08),(5.25,.1,.02),yellow,.01)
for x in (-11.1,-3.3):block('TruckLane',(x,-2,.07),(.13,17.6,.02),yellow,.01)
for y in (-7,-3,1):text('LaneArrow','>>>',(-7.2,y,.081),.8,white,floor=True).rotation_euler[2]=-math.pi/2
text('LaneLabel','TRUCK EXIT',(-7.2,5,.082),.55,white,floor=True)
# Independently adjustable shelf, cartons, storage cart and plants beside front wall.
for x in (11.7,):
    for xx in (x-1,x+1):
        for yy in (-9.8,-8.9):block('ShelfUpright',(xx,yy,1.8),(.10,.1,3.6),dark,.02)
    for z in (.35,1.45,2.55,3.55):block('ShelfTray',(x,-9.35,z),(2.3,1.05,.12),orange,.03)
    for i,(dx,z) in enumerate(((-.55,.8),(.55,.8),(-.5,1.92),(.55,2.98))):
        block('Carton',(x+dx,-9.35,z),(.85,.76,.8),card,.045)
        block('CartonTape',(x+dx,-9.35,z+.405),(.16,.76,.012),yellow,.005)
        text('BoxLabel','CG',(x+dx,-8.96,z),.22,dark)
for x,y in ((1.4,-10),(12.5,-6.5),(-12.7,-6.8)):
    cyl('PlantPot',(x,y,.42),.4,.75,white);cyl('PlantPotBand',(x,y,.31),.41,.18,yellow)
    for i in range(7):
        a=i*math.tau/7
        bpy.ops.mesh.primitive_uv_sphere_add(segments=8,ring_count=4,radius=1,location=(x+.25*math.cos(a),y+.25*math.sin(a),1.12))
        o=own(bpy.context.object,'PlantLeaf',leaf);o.scale=(.12,.21,.65);o.rotation_euler=(.45*math.cos(a),.45*math.sin(a),a)
for x in (9.95,10.65):
    block('TrolleyRail',(x,-8.0,.8),(.07,.09,1.6),yellow,.025)
    o=cyl('TrolleyWheel',(x,-8.0,.18),.18,.12,navy);o.rotation_euler[0]=math.pi/2
block('TrolleyBase',(10.3,-7.75,.22),(.9,.7,.1),yellow,.03)
# Hazard stripes are raised a few millimetres on the bottom shutter section.
for i in range(16):
    o=block('ShutterHazard',(-10.85+i*.48,-11.32,.23),(.26,.035,.42),yellow if i%2==0 else navy,.005)
    # Parent to the lowest slat so the warning band travels with the door.
    parent=bpy.data.objects.get('TruckGarageDoor_Slat_00')
    o.parent=parent;o.matrix_parent_inverse=parent.matrix_world.inverted()
parts=[o for o in s.objects if o.name.startswith('Hub_ShutterHazard')]
base=bpy.data.objects.get('TruckGarageDoor_Slat_00')
bpy.ops.object.select_all(action='DESELECT')
for o in parts:o.select_set(True)
base.select_set(True);bpy.context.view_layer.objects.active=base;bpy.ops.object.join()
# Render-only camera and lights never go into the Unity model.
s.frame_set(1)
cam=s.camera
if cam is None:
    cam=bpy.data.objects.new('Hub_PreviewCamera',bpy.data.cameras.new('Hub_PreviewCamera'));s.collection.objects.link(cam);s.camera=cam
cam.location=(.0,3.2,4.4);target=Vector((0,-10,3.5));cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.type='PERSP';cam.data.lens=18
s.render.engine='CYCLES';s.cycles.samples=24
s.render.resolution_x=1400;s.render.resolution_y=850;s.render.resolution_percentage=100
s.world.use_nodes=True
bg=next(n for n in s.world.node_tree.nodes if n.type=='BACKGROUND');bg.inputs[0].default_value=(.5,.65,.85,1);bg.inputs[1].default_value=.5
# broad soft fill inside warehouse
ld=bpy.data.lights.new('Hub_PreviewFill','AREA');ld.energy=2200;ld.shape='DISK';ld.size=14
ob=bpy.data.objects.new('Hub_PreviewFill',ld);s.collection.objects.link(ob);ob.location=(0,-3,7)
# Fixed architecture is exported as a few authored modules, while props and doors stay separate.
modules = {
 'DoorFrames': ('Hub_TruckFrame','Hub_GlassFrame'),
 'EntranceWalls': ('Hub_Entrance_',),
 'WallBands': ('Hub_FrontBand','Hub_SideBand'),
 'Steelwork': ('Hub_SteelColumn','Hub_CeilingBeam','Hub_CeilingPurlin'),
 'Pipework': ('Hub_OverheadPipe','Hub_PipeRiser','Hub_PipeCollar'),
 'FloorGraphics': ('Hub_TruckLane','Hub_Lane','Hub_ZoneBorder'),
 'SafetyLights': ('Hub_ConveyorSafetyLamp','Hub_TruckDoor_Light'),
 'SafetyBollards': ('Hub_Bollard',),
}
for name,prefixes in modules.items():
    parts=[o for o in s.objects if o.type=='MESH' and o.name.startswith(prefixes)]
    if not parts: continue
    bpy.ops.object.select_all(action='DESELECT')
    for o in parts:o.select_set(True)
    bpy.context.view_layer.objects.active=parts[0];bpy.ops.object.join();parts[0].name='Hub_Module_'+name
s['CGD_BUILD']='Concept Robot Logistics Hub';s['CGD_NOTE']='One truck shutter and two sliding glass panels. Original gameplay markers preserved.'
bpy.ops.wm.save_as_mainfile(filepath=str(folder/'depot.blend'))
bpy.ops.object.select_all(action='DESELECT')
for o in s.objects:
    if o.type in {'MESH','EMPTY'}:o.select_set(True)
bpy.ops.export_scene.fbx(filepath=str(folder/'depot.fbx'),use_selection=True,object_types={'MESH','EMPTY'},use_mesh_modifiers=True,add_leaf_bones=False,axis_forward='-Z',axis_up='Y',bake_anim=True,bake_anim_use_all_bones=False,bake_anim_use_nla_strips=False,bake_anim_use_all_actions=False,bake_anim_step=2.0)
s.render.filepath=str(ROOT/'Temp/depot-concept-preview.png');bpy.ops.render.render(write_still=True)
print('HUB_COMPLETE',len(s.objects))

