"""Build depot_02 from an empty scene. Explicit authoring only; never imports old depot geometry."""
import bpy, math, random
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Assets/_Project/Models/depot_02'
PREVIEW=ROOT/'ArtSource/Depot02'
bpy.ops.wm.read_factory_settings(use_empty=True)
s=bpy.context.scene;s.unit_settings.system='METRIC';s.render.fps=30;s.frame_start=1;s.frame_end=121
C={}
for name in ['Structure','Roof','RollerDoor','GlassDoors','Conveyor','Signage','LightingFixtures','StorageProps','FloorMarkings']:
    c=bpy.data.collections.new('Depot02_'+name);s.collection.children.link(c);C[name]=c
M={}
def material(name,col,metal=0,rough=.55,emit=0):
    m=bpy.data.materials.new('D02_'+name);m.use_nodes=True;m.diffuse_color=(*col,1)
    bs=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
    bs.inputs['Base Color'].default_value=(*col,1);bs.inputs['Metallic'].default_value=metal;bs.inputs['Roughness'].default_value=rough
    e=bs.inputs.get('Emission Color') or bs.inputs.get('Emission');e.default_value=(*col,1)
    if bs.inputs.get('Emission Strength'):bs.inputs['Emission Strength'].default_value=emit
    M[name]=m;return m
for args in [('Ivory',(.82,.85,.86)),('Cream',(.96,.94,.84)),('Teal',(.008,.36,.43),.15,.32),('Navy',(.015,.13,.2)),('Yellow',(1,.64,.015),.1,.36),('Steel',(.08,.28,.38),.4,.42),('Floor',(.36,.44,.55),.15,.32),('Silver',(.47,.56,.66),.6,.35),('Shutter',(.58,.65,.74),.45,.44),('Rubber',(.035,.055,.07)),('Glass',(.18,.58,.75),.15,.17),('Orange',(.98,.23,.025)),('Cardboard',(.66,.39,.17)),('Tape',(.88,.63,.33)),('Leaves',(.12,.42,.04)),('LeavesLight',(.32,.62,.06)),('Light',(1,.78,.23),0,.25,4),('Sky',(.14,.58,.86),0,.4,.5)]:material(*args)
glass=next(n for n in M['Glass'].node_tree.nodes if n.type=='BSDF_PRINCIPLED')
glass.inputs['Alpha'].default_value=.28
trans=glass.inputs.get('Transmission Weight')
if trans:trans.default_value=.25
font=bpy.data.fonts.load('C:/Windows/Fonts/arialbi.ttf')
fontbold=bpy.data.fonts.load('C:/Windows/Fonts/arialbd.ttf')
def own(o,name,mat,group):
    o.name=name
    for c in list(o.users_collection):c.objects.unlink(o)
    C[group].objects.link(o)
    if mat:o.data.materials.append(M[mat] if isinstance(mat,str) else mat)
    return o
def mesh(name,verts,faces,mat,group):
    me=bpy.data.meshes.new(name);me.from_pydata(verts,[],faces);me.update();o=bpy.data.objects.new(name,me);C[group].objects.link(o)
    if mat:me.materials.append(M[mat] if isinstance(mat,str) else mat)
    return o
def box(name,p,d,mat,group='Structure',bevel=.04):
    bpy.ops.mesh.primitive_cube_add(size=1,location=p);o=own(bpy.context.object,name,mat,group);o.dimensions=d
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    if bevel:
        mod=o.modifiers.new('Soft edges','BEVEL');mod.width=min(bevel,min(d)*.3);mod.segments=2
    return o
def cyl(name,p,r,h,mat,group='StorageProps',axis='Z',n=16):
    bpy.ops.mesh.primitive_cylinder_add(vertices=n,radius=r,depth=h,location=p);o=own(bpy.context.object,name,mat,group)
    if axis=='X':o.rotation_euler[1]=math.pi/2
    if axis=='Y':o.rotation_euler[0]=math.pi/2
    return o
def tube(name,points,r,mat,group='Structure'):
    cu=bpy.data.curves.new(name,'CURVE');cu.dimensions='3D';cu.resolution_u=1;cu.bevel_depth=r;cu.bevel_resolution=1
    sp=cu.splines.new('POLY');sp.points.add(len(points)-1)
    for p,v in zip(sp.points,points):p.co=(*v,1)
    o=bpy.data.objects.new(name,cu);C[group].objects.link(o);cu.materials.append(M[mat]);return o
def label(name,body,p,size,mat='Cream',group='Signage',floor=False,italic=False,outline=False,face=True):
    cu=bpy.data.curves.new(name,'FONT');cu.body=body;cu.font=font if italic else fontbold;cu.size=size;cu.align_x='CENTER';cu.align_y='CENTER';cu.extrude=.007;cu.bevel_depth=.002;cu.resolution_u=4
    o=bpy.data.objects.new(name,cu);C[group].objects.link(o);cu.materials.append(M[mat]);o.location=p
    o.rotation_euler=(0,0,math.pi) if floor else (math.pi/2,0,math.pi if face else 0)
    if outline:cu.offset=.038
    return o
def rounded(name,x,y,z,w,h,depth,mat,group='Signage',r=.25):
    # Rounded X/Z profile extruded in depth, independent of thin extrusion thickness.
    r=min(r,w/2-.001,h/2-.001);pts=[]
    for cx,cz,a in [(w/2-r,h/2-r,0),(w/2-r,-h/2+r,90),(-w/2+r,-h/2+r,180),(-w/2+r,h/2-r,270)]:
        for i in range(5):
            t=math.radians(a+i*90/4);pts.append((x+cx+r*math.sin(t),z+cz+r*math.cos(t)))
    n=len(pts);v=[(px,y+dy,pz) for dy in (-depth/2,depth/2) for px,pz in pts]
    # Fix outward winding with recalculation at export.
    faces=[tuple(range(n-1,-1,-1)),tuple(range(n,2*n))]+[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
    return mesh(name,v,faces,mat,group)
def arch(name,x,y,w,h,mat='Teal',group='Structure'):
    r=.65;points=[(x-w/2,y,.08),(x-w/2,y,h-r)]
    for i in range(9):
        a=math.pi-i*math.pi/16;points.append((x-w/2+r+math.cos(a)*r,y,h-r+math.sin(a)*r))
    points.append((x+w/2-r,y,h))
    for i in range(9):
        a=math.pi/2-i*math.pi/16;points.append((x+w/2-r+math.cos(a)*r,y,h-r+math.sin(a)*r))
    points.append((x+w/2,y,.08));return tube(name,points,.22,mat,group)
def couch(name,x,y,z,scale=1,fill='Yellow',group='Signage'):
    # Friendly couch silhouette, with a continuous dark outline.
    for mat,grow,dy in [('Navy',.10,-.02),(fill,0,.04)]:
        for part,dx,dz,w,h in [('Back',0,.2,1.8,.8),('Seat',0,-.2,1.8,.5),('L',-.96,0,.42,.8),('R',.96,0,.42,.8)]:
            rounded(name+part+mat,x+dx*scale,y+dy+(0.03*['Back','Seat','L','R'].index(part) if mat==fill else 0),z+dz*scale,(w+grow)*scale,(h+grow)*scale,.06,mat,group,r=.23*scale)
    tube(name+'SeatLine',[(x-.68*scale,y+.23,z-.16*scale),(x+.68*scale,y+.23,z-.16*scale)],.028*scale,'Navy',group)
    for dx in (-.26,.26):cyl(name+'Eye',(x+dx*scale,y+.20,z+.25*scale),.052*scale,.035,'Navy',group,'Y',12)
    tube(name+'Smile',[(x+math.cos(a)*.16*scale,y+.22,z+.12*scale+math.sin(a)*.16*scale) for a in [math.pi+i*math.pi/8 for i in range(9)]],.027*scale,'Navy',group)
def plaque(name,body,x,y,z,w,h,size):
    rounded(name+'Rim',x,y,z,w,h,.16,'Cream',r=h/2)
    rounded(name+'Face',x,y+.10,z,w-.12,h-.12,.10,'Teal',r=h/2)
    label(name+'Text',body,(x,y+.17,z),size)
def bollard(name,x,y):
    cyl(name+'Foot',(x,y,.09),.28,.16,'Silver');cyl(name,(x,y,.83),.21,1.5,'Yellow');cyl(name+'Cap',(x,y,1.58),.21,.09,'Yellow')
# ================= NEW SHELL: 28 x 20m, 8m height =================
box('D02_Collider_Floor',(0,0,-.12),(28.5,20.5,.24),'Floor',bevel=.03)
box('D02_ExteriorApron',(0,-12.5,-.15),(28.5,5,.25),'Floor')
# Front openings: player x 3..6, truck x -11..-4. No other front openings.
for i,(a,b) in enumerate([(-14,-11),(-4,3),(6,14)]):box('D02_Collider_FrontWall_'+str(i),((a+b)/2,-10,4),(b-a,.38,8),'Ivory')
box('D02_Collider_TruckHeader',(-7.5,-10,6.82),(7,.38,2.36),'Ivory')
box('D02_Collider_PlayerHeader',(4.5,-10,5.8),(3,.38,4.4),'Ivory')
box('D02_Collider_BackWall',(0,10,4),(28,.38,8),'Ivory')
# Right wall, upper windows and solid panels.
for side in (-1,1):
    x=side*14
    if side==-1:box('D02_Collider_RightWallLower',(x,0,2.5),(.38,20,5),'Ivory')
    else:
        # A receiving aperture on the LEFT SIDE wall, not a second vehicle door.
        for a,b in [(-10,-.2),(3.8,10)]:box('D02_Collider_LeftWall',(x,(a+b)/2,2.5),(.38,b-a,5),'Ivory')
        box('D02_Collider_ReceivingHeader',(x,1.8,4.2),(.38,4,1.6),'Ivory')
        box('D02_Collider_ReceivingSill',(x,1.8,.42),(.38,4,.84),'Ivory')
    box('D02_Collider_SideTop',(x,0,7.6),(.38,20,.8),'Ivory')
    for y in (-8,-4,0,4,8):
        box('D02_Collider_SideWindow',(x,y,6.08),(.07,3.72,2.2),'Glass')
        box('D02_WindowMullion',(x,y-1.92,6.1),(.35,.13,2.2),'Steel')
        box('D02_WindowBar',(x,y,6.1),(.16,3.8,.09),'Steel')
    for z in (5,7.2):box('D02_WindowTransom',(x,0,z),(.35,20,.15),'Steel')
# Receiving intake extension, closed around a hidden couch staging pad.
for y in (-.25,3.85):box('D02_Collider_IntakeSide',(15.7,y,1.9),(3.4,.25,3.8),'Teal')
box('D02_Collider_IntakeEnd',(17.4,1.8,1.9),(.25,4.1,3.8),'Ivory')
box('D02_Collider_IntakeRoof',(15.7,1.8,3.9),(3.6,4.2,.24),'Steel')
box('D02_Collider_IntakeFloor',(15.7,1.8,.48),(3.6,4.2,.18),'Silver')
# Wall bands including an upswept graphic beside the truck bay.
for side in (-1,1):
    for z,h,mat in [(1,2,'Teal'),(3.9,.42,'Teal'),(4.42,.5,'Yellow')]:
        for a,b in ([(-10,-.3),(3.9,10)] if side==1 else [(-10,10)]):box('D02_SideStripe',(side*13.79,(a+b)/2,z),(.045,b-a,h),mat,bevel=0)
for z,h,mat in [(1,2,'Teal'),(3.9,.42,'Teal'),(4.42,.5,'Yellow')]:
    box('D02_BackStripe',(0,9.79,z),(27.6,.045,h),mat,bevel=0)
for a,b in [(-14,-11),(-4,3),(6,14)]:
    box('D02_FrontSkirt',((a+b)/2,-9.78,.65),(b-a,.045,1.3),'Teal')
# Swooping brand ribbons on solid front centre wall, with rounded bend.
for z,mat in [(4.15,'Teal'),(4.65,'Yellow')]:
    tube('D02_BrandRibbon',[(-3.9,-9.76,z+.7),(-2.7,-9.76,z+.7),(-1.7,-9.76,z+.5),(-.8,-9.76,z),(.2,-9.76,z-.3),(2.9,-9.76,z-.3)],.23,mat)
# Blue steel columns are confined to the perimeter.
for x in (-13.6,13.6):
    for y in (-9.6,-3,3.8,9.6):box('D02_SteelColumn',(x,y,4),(.28,.36,8),'Steel')
for y in (-8,-1,6):
    box('D02_RoofCrossbeam',(0,y,7.65),(27.5,.3,.42),'Steel','Roof')
    for side in (-1,1):
        tube('D02_CurvedHaunch',[(side*13.5,y,6.2),(side*13.3,y,6.7),(side*12.8,y,7.2),(side*12.1,y,7.55)],.16,'Steel','Roof')
# Roof panels leave two actual skylight strips, with glass and blue mullions.
for a,b in [(-14,-9),(-6,5),(8,14)]:box('D02_Collider_RoofPanel',((a+b)/2,0,8.15),(b-a,20.4,.2),'Cream','Roof')
for x in (-7.5,6.5):
    box('D02_Collider_Skylight',(x,0,8.16),(3,20,.06),'Glass','Roof')
    for xx in (x-1.5,x+1.5):box('D02_SkylightRafter',(xx,0,8.02),(.16,20.4,.24),'Steel','Roof')
    for y in (-10,-5,0,5,10):box('D02_SkylightBar',(x,y,8.05),(3,.14,.22),'Steel','Roof')
# ================= DOORS =================
arch('D02_TruckFrame',-7.5,-9.72,7.35,5.95)
rounded('D02_TruckHousing',-7.5,-9.9,6.0,7.5,.75,1.0,'Teal','RollerDoor',.3)
for x in (-10.2,-7.5,-4.8):rounded('D02_TruckHeaderLight',x,-9.32,5.65,1.0,.14,.10,'Light','LightingFixtures',.07)
# Slats tuck upwards and stack behind the opaque housing; all keys are on saved objects.
for i in range(16):
    z=.08+(i+.5)*5.45/16
    o=box('D02_RollerSlat_%02d'%i,(-7.5,-10.02,z),(6.98,.13,5.45/16-.012),'Shutter','RollerDoor',.018)
    o.keyframe_insert(data_path='location',frame=1)
    o.location.z=5.93+i*.018;o.keyframe_insert(data_path='location',frame=61)
    o.keyframe_insert(data_path='location',frame=71);o.location.z=z;o.keyframe_insert(data_path='location',frame=121)
# Animated hazard band and couch emblem are clipped into slat strips during mesh authoring below.
arch('D02_PlayerFrame',4.5,-9.72,3.35,3.7)
rounded('D02_GlassOperator',4.5,-9.83,3.67,3.6,.35,.65,'Teal','GlassDoors',.16)
rounded('D02_PlayerHeaderLight',4.5,-9.45,3.46,1.25,.1,.08,'Light','LightingFixtures',.05)
for i,x in enumerate((3.75,5.25)):
    parts=[box('D02_GlassPanel_%d'%i,(x,-10.0,1.74),(1.48,.08,3.36),'Glass','GlassDoors',.01)]
    for dx in (-.73,.73):parts.append(box('D02_GlassRail',(x+dx,-9.99,1.74),(.055,.14,3.4),'Teal','GlassDoors',.01))
    for z in (.07,1.0,3.43):parts.append(box('D02_GlassRail',(x,-9.99,z),(1.5,.14,.055),'Teal','GlassDoors',.01))
    parts.append(box('D02_GlassHandle',(x+(.53 if i==0 else -.53),-9.88,1.52),(.06,.10,.48),'Yellow','GlassDoors',.02))
    bpy.ops.object.select_all(action='DESELECT')
    for ob in parts:ob.select_set(True)
    bpy.context.view_layer.objects.active=parts[0];bpy.ops.object.join();o=parts[0];o.name='D02_GlassPanel_%d'%i
    o.keyframe_insert(data_path='location',frame=1);o.location.x+=(-1.55 if i==0 else 1.55);o.keyframe_insert(data_path='location',frame=61)
    o.keyframe_insert(data_path='location',frame=71);o.location.x=x;o.keyframe_insert(data_path='location',frame=121)
# ================= CONVEYOR: LEFT WALL =================
# Receiving section crosses the side wall, then turns towards the pickup bay.
for name,p,d in [('Feed',(14.6,1.8,.8),(5.1,2.9,.5)),('Main',(11.8,-1.6,.8),(2.9,9.7,.5))]:box('D02_Collider_Conveyor'+name,p,d,'Teal','Conveyor',.12)
box('D02_BeltMain',(11.8,-1.6,1.08),(2.6,9.5,.12),'Rubber','Conveyor')
box('D02_BeltFeed',(14.55,1.8,1.08),(5.1,2.6,.12),'Rubber','Conveyor')
for i in range(22):cyl('D02_MainRoller',(11.8,-6.15+i*.43,1.17),.065,2.55,'Silver','Conveyor','X',12)
for x in (13.4,14.1,14.8,15.5,16.2):cyl('D02_FeedRoller',(x,1.8,1.17),.065,2.55,'Silver','Conveyor','Y',12)
for x in (10.35,13.25):
    box('D02_ConveyorRim',(x,-2.1,1.28),(.16,8.6,.28),'Teal','Conveyor',.06)
    box('D02_ConveyorTopRail',(x,-2.1,1.46),(.14,8.6,.09),'Silver','Conveyor')
    for y in (-5.5,-2.3,.3):
        box('D02_ConveyorLamp',(x,y,.78),(.06,.8,.1),'Light','Conveyor')
        box('D02_ConveyorLeg',(x,y,.36),(.18,.22,.72),'Silver','Conveyor')
# Curved receiving portal on the side wall, rotated around Z.
portal=arch('D02_ReceivingPortal',0,0,4.05,3.55,group='Conveyor');portal.rotation_euler[2]=math.pi/2;portal.location=(13.72,1.8,0)
for i in range(9):box('D02_IntakeCurtain',(14.04,.1+i*.42,2.28),(.06,.4,2.25),'Rubber','Conveyor',.015)
for x,y in ((10.1,-6.8),(13.4,-6.8),(-11.6,-8.9),(-3.4,-8.9)):bollard('D02_SafetyBollard',x,y)
# ================= SIGNAGE / MARKINGS =================
plaque('D02_StaffSign','STAFF ENTRANCE / EXIT',4.5,-9.7,4.18,5.05,.72,.29)
plaque('D02_TruckSign','TRUCK EXIT   >>>',-7.5,-9.7,6.83,7.15,.8,.48)
# Main couch logo above entrance. Outline layers keep strong silhouette at distance.
couch('D02_BrandCouch',4.5,-9.63,6.65,1.05)
for mat,offset,y in [('Navy',True,-9.60),('Cream',False,-9.48)]:label('D02_MainBrand','Couch Guys',(4.5,y,5.70),1.10,mat,italic=True,outline=offset)
rounded('D02_DepotBadge',4.5,-9.5,4.93,3.9,.62,.14,'Yellow',r=.28)
label('D02_DepotText','D E P O T',(4.5,-9.39,4.93),.40,'Navy')
# Receiving signage is on the left wall, facing into the room.
plaque('D02_ReceivingSign','COUCH RECEIVING',10.7,-.4,3.95,4.7,.8,.34)
# Large mural on shutter; join clipped emblem geometry to its own moving slats.
couch('D02_ShutterEmblem',-7.5,-9.90,3.0,1.8,'Steel','RollerDoor')
for i in range(15):
    ob=box('D02_HazardStripe',(-10.78+i*.47,-9.91,.26),(.28,.025,.42),'Yellow' if i%2==0 else 'Navy','RollerDoor',0);ob.rotation_euler[1]=-.45
# Couch zone immediately alongside output (to the right of conveyor when facing front).
box('D02_DropZone',(7.9,-5.3,.016),(4.2,4.0,.025),'Teal','FloorMarkings',.01)
for y in (-7.3,-3.3):box('D02_ZoneEdge',(7.9,y,.037),(4.4,.10,.018),'Yellow','FloorMarkings',0)
for x in (5.75,10.05):
    box('D02_ZoneEdge',(x,-5.3,.037),(.10,4.0,.018),'Yellow','FloorMarkings',0)
    for y in (-7,-6.4,-5.8,-5.2,-4.6,-4):
        o=box('D02_ZoneHatch',(x,y,.047),(.36,.17,.012),'Yellow','FloorMarkings',0);o.rotation_euler[2]=-.55
label('D02_DropWords','COUCH\nDROP ZONE',(7.9,-5.9,.055),.44,'Cream','FloorMarkings',floor=True)
# Flat couch stencil on the receiving bay.
for dx,dy,w,h in [(0,0,1.6,.6),(0,-.35,1.65,.22),(-.85,-.18,.22,.6),(.85,-.18,.22,.6)]:box('D02_CouchStencil',(7.9+dx,-4.4+dy,.05),(w,h,.013),'Cream','FloorMarkings',0)
# Truck route stays completely empty of props.
for x in (-11.1,-3.9):box('D02_TruckLane',(x,-1.3,.026),(.14,17.2,.025),'Yellow','FloorMarkings',0)
for y in (-7,-3,1):
    for dx,ang in [(-.28,-.55),(.28,.55)]:
        o=box('D02_LaneChevron',(-7.5+dx,y,.047),(.7,.16,.016),'Cream','FloorMarkings',0);o.rotation_euler[2]=ang
# Recognisable white top-down truck graphic.
for x,y,w,h in [(-7.5,4.0,2.0,2.8),(-7.5,2.3,1.8,.8)]:
    for dx in (-w/2,w/2):box('D02_TruckStencil',(x+dx,y,.045),(.075,h,.015),'Cream','FloorMarkings',0)
    for dy in (-h/2,h/2):box('D02_TruckStencil',(x,y+dy,.045),(w,.075,.015),'Cream','FloorMarkings',0)
for x in (-8.55,-6.45):
    for y in (2.3,4.7):box('D02_TruckStencilWheel',(x,y,.046),(.3,.5,.016),'Cream','FloorMarkings',0)
for y in (-8.8,-8,-7.2,-6.4):box('D02_PedestrianStripe',(-12.35,y,.036),(1.5,.32,.018),'Cream','FloorMarkings',0)
# ================= PROPS: perimeter only =================
rng=random.Random(2026)
def carton(name,x,y,z,size=.7):
    box(name,(x,y,z),(size,size*.85,size),'Cardboard','StorageProps',.035)
    box(name+'Tape',(x,y,z+size/2+.006),(.12,size*.85,.015),'Tape','StorageProps',0)
    label(name+'Logo','CG',(x,y+.31*size/.7,z),.18*size/.7,'Teal','StorageProps')
def shelf(name,x,y):
    for dx in (-1.05,1.05):
        for dy in (-.5,.5):box(name+'Upright',(x+dx,y+dy,1.8),(.08,.08,3.6),'Steel','StorageProps')
    for z in (.22,1.3,2.4,3.5):box(name+'Tray',(x,y,z),(2.4,1.2,.12),'Orange','StorageProps')
    for i,z in enumerate((.65,1.75,2.85)):
        for dx in (-.55,.5):carton(name+'Box',x+dx,y,z,.7+rng.random()*.15)
shelf('D02_FrontShelf',-.8,-9.0)
shelf('D02_BackShelf',8.7,8.8);shelf('D02_BackShelf',3.3,8.8);shelf('D02_BackShelf',-11.9,8.8)
for x,y in ((-.1,-7.8),(1,-8.6),(12.7,7.6),(-12.4,4.4)):carton('D02_Crate',x,y,.45,.9)
for x,y in ((7,-9.05),(2,-9.05),(-12.8,-8.6),(12.8,-8.1)):
    cyl('D02_PlantPot',(x,y,.43),.42,.78,'Cream');cyl('D02_PlantBand',(x,y,.28),.43,.18,'Yellow')
    for i in range(7):
        a=i*math.tau/7;bpy.ops.mesh.primitive_uv_sphere_add(segments=8,ring_count=4,radius=1,location=(x+.23*math.cos(a),y+.23*math.sin(a),1.12))
        ob=own(bpy.context.object,'D02_PlantLeaf','LeavesLight' if i%2 else 'Leaves','StorageProps');ob.scale=(.12,.22,.69);ob.rotation_euler=(.5*math.cos(a),.5*math.sin(a),a)
# Trolley next to shelf, never in the lane.
for x in (-2.0,-1.3):
    tube('D02_TrolleyHandle',[(x,-8, .2),(x,-8.2,1.8),(x,-8.05,1.95)],.045,'Yellow','StorageProps')
    cyl('D02_TrolleyWheel',(x,-8.05,.2),.2,.14,'Rubber','StorageProps','Y')
box('D02_TrolleyFoot',(-1.65,-7.8,.18),(.95,.8,.1),'Yellow','StorageProps')
# Dispatcher counter towards the rear, with a clear approach.
box('D02_Collider_DispatchDesk',(4.4,4.3,.75),(3.4,1.2,1.5),'Teal','StorageProps',.12)
box('D02_DispatchDeskTop',(4.4,4.3,1.55),(3.65,1.4,.16),'Cream','StorageProps')
plaque('D02_DispatchSign','DISPATCH',4.4,4.95,2.4,3.2,.65,.35)
# Banners and truck wall poster.
rounded('D02_PosterFrame',.7,-9.69,2.7,1.65,2.1,.12,'Teal',r=.12)
rounded('D02_PosterSky',.7,-9.59,2.7,1.45,1.9,.07,'Sky',r=.08)
box('D02_PosterTruck',(.7,-9.5,2.65),(1,.04,.58),'Yellow','Signage')
for x in (.32,1.08):cyl('D02_PosterWheel',(x,-9.46,2.3),.13,.04,'Navy','Signage','Y')
# Exposed pipework and stylized hanging lamps.
for x,mat in [(-12,'Orange'),(10,'Yellow')]:
    tube('D02_CeilingPipe',[(x,-9.7,5.1),(x,-9.7,6.85),(x,-9.4,7.12),(x,9.4,7.12)],.11,mat,'Roof')
    for y in (-8,-3,2,7):cyl('D02_PipeCollar',(x,y,7.12),.145,.07,'Silver','Roof','Y')
for x,y in ((-9,-6),(-2,-6),(7,-6),(-8,2),(1,2),(10,2)):
    cyl('D02_PendantStem',(x,y,7.05),.035,1.05,'Silver','LightingFixtures')
    bpy.ops.mesh.primitive_cone_add(vertices=20,radius1=.55,radius2=.23,depth=.34,location=(x,y,6.4));own(bpy.context.object,'D02_PendantShade','Teal','LightingFixtures')
    cyl('D02_PendantGlow',(x,y,6.22),.47,.045,'Light','LightingFixtures')
    ld=bpy.data.lights.new('D02_PreviewPendant','POINT');ld.energy=190;ld.color=(1,.8,.46);ld.shadow_soft_size=1
    ob=bpy.data.objects.new('D02_PreviewPendant',ld);s.collection.objects.link(ob);ob.location=(x,y,6.05)
box('D02_VentUnit',(-.5,-.5,7.15),(2.6,1.8,.65),'Cream','LightingFixtures')
for i in range(8):box('D02_VentLouvre',(-.5,-1.42,6.92+i*.055),(2.3,.06,.022),'Silver','LightingFixtures',0)
# Banner on right wall facing interior.
box('D02_Banner',(-13.74,-3,3.8),(.04,2.3,2.8),'Teal','Signage')
# Exterior front branding, real doors lead to the apron.
label('D02_ExteriorBrand','COUCH GUYS DEPOT 02',(0,-10.24,7.2),.75,'Teal',face=False)
# ================= FINISH / GROUP / EXPORT =================
s.frame_set(1)
# Convert curves and repair normals for every newly authored mesh.
bpy.ops.object.select_all(action='DESELECT')
for o in list(s.objects):
    if o.type in {'CURVE','FONT'}:
        bpy.context.view_layer.objects.active=o;o.select_set(True);bpy.ops.object.convert(target='MESH');o.select_set(False)
import bmesh
# Split the shutter emblem and warning strip at slat boundaries, so every detail follows its slat.
details=[o for o in s.objects if o.name.startswith(('D02_ShutterEmblem','D02_HazardStripe'))]
for ob in details:
    for i in range(16):
        lo=.08+i*5.45/16;hi=.08+(i+1)*5.45/16
        bm=bmesh.new();bm.from_mesh(ob.data);bm.transform(ob.matrix_world)
        for co,no in [((0,0,lo),(0,0,1)),((0,0,hi),(0,0,-1))]:
            bmesh.ops.bisect_plane(bm,geom=list(bm.verts)+list(bm.edges)+list(bm.faces),plane_co=co,plane_no=no,clear_inner=True,dist=.00001)
        if len(bm.faces):
            me=bpy.data.meshes.new('ShutterDetail');bm.to_mesh(me);part=bpy.data.objects.new('D02_SlatDetail',me);C['RollerDoor'].objects.link(part)
            for mat in ob.data.materials:me.materials.append(mat)
            base=bpy.data.objects['D02_RollerSlat_%02d'%i]
            bpy.ops.object.select_all(action='DESELECT');part.select_set(True);base.select_set(True);bpy.context.view_layer.objects.active=base;bpy.ops.object.join()
        bm.free()
    bpy.data.objects.remove(ob,do_unlink=True)
# One renderer per editable sign / shelf / planter, with a small shared palette.
for name,prefixes in {
 'D02_BrandSign':('D02_BrandCouch','D02_MainBrand','D02_DepotBadge','D02_DepotText'),
 'D02_StaffSign':('D02_StaffSign',), 'D02_TruckSign':('D02_TruckSign',),
 'D02_ReceivingSign':('D02_ReceivingSign',), 'D02_Poster':('D02_Poster',),
 'D02_DispatchSign':('D02_DispatchSign',),
}.items():
    parts=[o for o in s.objects if o.type=='MESH' and o.name.startswith(prefixes)]
    if parts:
        bpy.ops.object.select_all(action='DESELECT')
        for o in parts:o.select_set(True)
        bpy.context.view_layer.objects.active=parts[0];bpy.ops.object.join();parts[0].name=name
# Pack each decorative unit into one independently adjustable mesh, sharing palette slots.
def cluster_props(prefixes,centres,label,solid=False):
    groups={i:[] for i in range(len(centres))}
    for ob in list(C['StorageProps'].objects):
        if ob.type!='MESH' or not ob.name.startswith(prefixes):continue
        centre=ob.matrix_world @ (sum((Vector(v) for v in ob.bound_box),Vector())/8)
        index=min(groups,key=lambda i:(Vector((centre.x,centre.y))-Vector(centres[i])).length)
        groups[index].append(ob)
    for i,parts in groups.items():
        if not parts:continue
        bpy.ops.object.select_all(action='DESELECT')
        for ob in parts:ob.select_set(True)
        bpy.context.view_layer.objects.active=parts[0];bpy.ops.object.join()
        parts[0].name=('D02_Collider_' if solid else 'D02_')+label+'_'+str(i)
cluster_props(('D02_FrontShelf','D02_BackShelf'),[(-.8,-9),(8.7,8.8),(3.3,8.8),(-11.9,8.8)],'Shelf',True)
cluster_props(('D02_Plant',),[(7,-9.05),(2,-9.05),(-12.8,-8.6),(12.8,-8.1)],'Plant')
cluster_props(('D02_Trolley',),[(-1.65,-8)],'Trolley')
cluster_props(('D02_Crate',),[(-.1,-7.8),(1,-8.6),(12.7,7.6),(-12.4,4.4)],'Crate',True)
cluster_props(('D02_SafetyBollard',),[(10.1,-6.8),(13.4,-6.8),(-11.6,-8.9),(-3.4,-8.9)],'Bollard',True)
# Group by logical module, retaining separate collider primitives and adjustable props.
for group in ('Structure','Roof','Conveyor','Signage','LightingFixtures','FloorMarkings'):
    parts=[o for o in C[group].objects if o.type=='MESH' and not o.name.startswith('D02_Collider_') and not o.name.startswith('D02_PendantGlow')]
    # Signage stays in separate signs; props and moving doors are never joined here.
    if group=='Signage':continue
    if parts:
        bpy.ops.object.select_all(action='DESELECT')
        for o in parts:o.select_set(True)
        bpy.context.view_layer.objects.active=parts[0];bpy.ops.object.join();parts[0].name='D02_Module_'+group
for ob in [o for o in s.objects if o.type=='MESH']:
    bm=bmesh.new();bm.from_mesh(ob.data);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(ob.data);bm.free()
# Authoring metadata and review camera.
s['Depot02_NewGeometry']=True;s['Dimensions']='28m x 20m / 8m clear shell';s['Front']='-Y';s['Layout']='Inside facing front: conveyor +X (left), entrance +4.5, truck -7.5 (right)'
cam=bpy.data.objects.new('Depot02_ReviewCamera',bpy.data.cameras.new('Depot02_ReviewCamera'));s.collection.objects.link(cam);s.camera=cam
cam.location=(9,7.8,4.4);target=Vector((5,-7,3.2));cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.lens=16
s.world=bpy.data.worlds.new('Depot02_Daylight');s.world.use_nodes=True
bg=next(n for n in s.world.node_tree.nodes if n.type=='BACKGROUND');bg.inputs[0].default_value=(.5,.68,.88,1);bg.inputs[1].default_value=.65
for name,p,power,size in [('Interior',(0,-2,7.6),2000,12),('Rear',(0,7,7.6),1400,10)]:
    ld=bpy.data.lights.new(name,'AREA');ld.energy=power;ld.shape='DISK';ld.size=size;ob=bpy.data.objects.new('Depot02_Preview_'+name,ld);s.collection.objects.link(ob);ob.location=p
sun=bpy.data.objects.new('Depot02_Sun',bpy.data.lights.new('Depot02_Sun','SUN'));s.collection.objects.link(sun);sun.data.energy=2;sun.rotation_euler=(.4,-.3,-.5)
s.render.engine='CYCLES';s.cycles.samples=24;s.render.resolution_x=1500;s.render.resolution_y=900;s.render.resolution_percentage=100
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'depot_02.blend'))
bpy.ops.object.select_all(action='DESELECT')
for o in s.objects:
    if o.type=='MESH':o.select_set(True)
bpy.ops.export_scene.fbx(filepath=str(OUT/'depot_02.fbx'),use_selection=True,object_types={'MESH'},use_mesh_modifiers=True,add_leaf_bones=False,axis_forward='-Z',axis_up='Y',bake_anim=True,bake_anim_use_all_bones=False,bake_anim_use_nla_strips=False,bake_anim_use_all_actions=False,bake_anim_step=2)
s.render.filepath=str(PREVIEW/'depot_02_interior.png');bpy.ops.render.render(write_still=True)
# Top-down review without roof, showing exact placement relationships.
for group in ('Roof','LightingFixtures'):
    for o in C[group].objects:o.hide_render=True
cam.location=(0,0,32);cam.rotation_euler=(0,0,0);cam.data.type='ORTHO';cam.data.ortho_scale=34
# Rotate top-down view so the front wall is at the top, as in the user's plan.
cam.rotation_euler[2]=math.pi
s.render.filepath=str(PREVIEW/'depot_02_topdown.png');bpy.ops.render.render(write_still=True)
print('DEPOT02_COMPLETE',len([o for o in s.objects if o.type=='MESH']))
