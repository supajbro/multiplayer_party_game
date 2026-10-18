"""Couch Guys - compact Suburbs depot (Blender 3.6 / 4.x / 5.x)

REBUILD BASED ON THE REFERENCE IMAGE: a compact rectangular warehouse, low flat
charcoal roof, pale timber sides, white-brick front, and TWO front roller doors.

- Left front door: bought-truck dispatch. Trucks actually originate offstage in
  the REAR service tunnel, move along a clear internal lane, then depart front.
- Right front door: player's main gate to Suburbs with inside/outside buttons.
- Rear RIGHT: sight-blocked backstage couch staging room. Couch prefab is placed
  where players cannot see it, then fed through a covered conveyor chute.
- Frog manager and player spawn are in the accessible front/right showroom.
- Roof is in its own collection so it can be hidden to inspect the interior.
- Two separately keyframed sectional doors. Only 12 slats each; export-friendly.

Run from Blender's Scripting workspace with Run Script. Everything is replaced
in the current Blender scene. Press Home and switch to Material Preview.

One Blender unit is one metre. Z is up. Main exterior faces negative Y.
Generated markers (Empties) are suggestions; Unity must implement the logic.

To export: set DO_EXPORT_FBX = True near the top and re-run. This exports
static depot + individual animated door rigs (3 FBX files); no giant full-scene
animation bake. Requires saving .blend first or sets export folder to home.
"""
import bpy
import math
import random
import os
import json
from mathutils import Vector

# ================================ SETTINGS =================================
RNG = random.Random(4480)
DO_EXPORT_FBX = False   # opt-in: separate static and animated doors
DRAW_REFERENCE_BASE = True
ADD_TREE_DETAILS = True
ADD_PREVIEW_LIGHTS = True
BEVEL_SEGMENTS = 2

W = 28.0
D = 23.0
H = 7.45
FRONT = -D / 2
REAR = D / 2
TRUCK_X = -7.2
TRUCK_W = 8.0
EXIT_X = 7.05
EXIT_W = 7.8
DOOR_HEIGHT = 5.35
FRONT_DOOR_Y = FRONT - .13
BACKSTAGE_Y = 4.75
CONV_X = 4.5
CONV_TOP = 1.24

# Exact, nonoverlapping Unity import ranges.
CLIPS = {
    'TruckDoor_Open': (1, 41),
    'TruckDoor_Close': (51, 91),
    'SuburbsExit_Open': (101, 141),
    'SuburbsExit_Close': (151, 191),
}
ANIM_STEP = 3  # 12 slats/door, modest animation sampling to reduce FBX stalls

# ================================ CLEANUP ==================================
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
for co in list(bpy.data.collections):
    if co.name.startswith('CGD_'):
        bpy.data.collections.remove(co)
scene = bpy.context.scene
scene.unit_settings.system = 'METRIC'
scene.frame_start = 1
scene.frame_end = 191
scene.render.resolution_x = 1600
scene.render.resolution_y = 1000
scene.render.resolution_percentage = 100
scene.world.color = (.44, .59, .79)


def collection(name):
    c = bpy.data.collections.new(name)
    scene.collection.children.link(c)
    return c

C_BASE = collection('CGD_01_Exterior_Ground')
C_WALL = collection('CGD_02_Shell_Exterior')
C_ROOF = collection('CGD_03_Hide_For_Interior_Roof')
C_INSIDE = collection('CGD_04_Interior_Frog_Showroom')
C_COUCH = collection('CGD_05_Hidden_Couch_Staging_And_Conveyor')
C_TRUCK = collection('CGD_06_Rear_Truck_Receiving_And_Dispatch')
C_TRUCKDOOR = collection('CGD_07_Truck_Front_Roller_Animated')
C_EXITDOOR = collection('CGD_08_Suburbs_Exit_Roller_Animated')
C_MARKERS = collection('CGD_09_Unity_Spawn_And_Interaction_Empties')
C_DECOR = collection('CGD_10_Exterior_Details')
C_PREVIEW = collection('CGD_11_Preview_Only_No_Export')

# ================================= MATERIALS ===============================
def material(name, color, rough=.57, metal=0.0, emission=None):
    mat = bpy.data.materials.get('CGD_M_' + name)
    if mat is None:
        mat = bpy.data.materials.new('CGD_M_' + name)
    mat.diffuse_color = (*color, 1.0)
    mat.use_nodes = True
    bs = mat.node_tree.nodes.get('Principled BSDF')
    if bs:
        bs.inputs['Base Color'].default_value = (*color, 1.0)
        bs.inputs['Roughness'].default_value = rough
        bs.inputs['Metallic'].default_value = metal
        if emission:
            co = bs.inputs.get('Emission Color') or bs.inputs.get('Emission')
            if co: co.default_value = (*emission, 1.0)
            power = bs.inputs.get('Emission Strength')
            if power: power.default_value = 1.2
    return mat

M = {
    'brick': material('WarmIvoryBrick', (.83,.87,.88)),
    'mortar': material('BrickMortar', (.62,.69,.75)),
    'wood': material('PaleHoneyTimber', (.89,.74,.55)),
    'wood_trim': material('WoodEdge', (.63,.40,.25)),
    'white': material('WarmWhite', (.96,.94,.84)),
    'roof': material('DarkSlateRoof', (.11,.16,.21),.68),
    'roofcap': material('RoofMetalTrim', (.20,.27,.34),.4,.32),
    'teal': material('CouchGuysTeal', (.04,.69,.72),.38),
    'teal_dark': material('DarkTeal', (.035,.23,.30),.5),
    'blue': material('BlueGlow', (.13,.56,1.0),.27,emission=(.04,.38,.9)),
    'orange': material('CouchOrange', (.99,.34,.14)),
    'coral': material('WarmCoral', (1,.51,.31)),
    'yellow': material('SunnyYellow', (1,.73,.18)),
    'concrete': material('WarmConcrete', (.72,.75,.70),.83),
    'floor': material('PolishedWarehouseFloor', (.66,.75,.75),.81),
    'road': material('ToyRoadAsphalt', (.22,.28,.32),.92),
    'lines': material('IvoryRoadLines', (.96,.94,.75),.75),
    'metal': material('SatinMetal', (.58,.67,.72),.36,.55),
    'dark': material('DeepNavy', (.045,.092,.14),.57),
    'belt': material('ConveyorRubber', (.09,.14,.16),.93),
    'green': material('GrassGreen', (.34,.69,.34)),
    'leaves': material('TreeLime', (.43,.77,.38)),
    'leaves2': material('TreeGreen', (.15,.64,.42)),
    'glass': material('TintedGlass', (.18,.42,.52),.20,.12),
    'red': material('ControlRed', (.98,.18,.15),.31),
    'button_green': material('ControlGreen', (.28,.91,.36),.25,emission=(.05,.48,.1)),
    'woodbox': material('Cardboard', (.77,.52,.28)),
}

# ================================== MESH ===================================
def mesh_obj(name, vertices, faces, mat, coll):
    mesh = bpy.data.meshes.new(name+'_Mesh')
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    ob = bpy.data.objects.new(name, mesh)
    coll.objects.link(ob)
    if mat: ob.data.materials.append(mat)
    return ob


def block(name, xyz, dims, mat, coll, soft=0.0, rotate=None):
    x,y,z = dims
    v = [(i*x*.5,j*y*.5,k*z*.5) for i,j,k in
         [(-1,-1,-1),(-1,-1,1),(-1,1,-1),(-1,1,1),
          (1,-1,-1),(1,-1,1),(1,1,-1),(1,1,1)]]
    faces = [(0,4,6,2),(1,3,7,5),(0,1,5,4),
             (2,6,7,3),(0,2,3,1),(4,5,7,6)]
    ob = mesh_obj(name, v, faces, mat, coll)
    ob.location = xyz
    if rotate: ob.rotation_euler = rotate
    if soft:
        be = ob.modifiers.new('SubtleRoundedEdges', 'BEVEL')
        be.width = min(float(soft), min(dims)*.25)
        be.segments = BEVEL_SEGMENTS
    return ob


def span(name, x0,x1,y0,y1,z0,z1,mat,coll,soft=0.0):
    return block(name, ((x0+x1)/2,(y0+y1)/2,(z0+z1)/2),
                 (x1-x0,y1-y0,z1-z0),mat,coll,soft)


def cylinder(name, xyz, radius, depth, mat, coll, axis='Z', n=16):
    v=[]
    for z in (-depth/2,depth/2):
        for i in range(n):
            a=i*2*math.pi/n
            v.append((radius*math.cos(a),radius*math.sin(a),z))
    faces=[tuple(range(n-1,-1,-1)),tuple(range(n,2*n))]
    for i in range(n):
        j=(i+1)%n
        faces.append((i,j,n+j,n+i))
    ob=mesh_obj(name,v,faces,mat,coll)
    ob.location=xyz
    if axis=='Y': ob.rotation_euler[0]=math.pi/2
    if axis=='X': ob.rotation_euler[1]=math.pi/2
    return ob


def sphere(name, xyz, radius, mat, coll, rings=8, segments=12):
    # low-poly toy canopy, no operator calls or heavy subdivision modifiers
    v=[]
    for j in range(rings+1):
        latitude=math.pi*j/rings
        for k in range(segments):
            a=k*math.tau/segments
            v.append((radius*math.sin(latitude)*math.cos(a),
                      radius*math.sin(latitude)*math.sin(a),
                      radius*math.cos(latitude)))
    faces=[]
    for j in range(rings):
        for k in range(segments):
            n=j*segments+k
            m=j*segments+(k+1)%segments
            faces.append((n,m,m+segments,n+segments))
    ob=mesh_obj(name,v,faces,mat,coll)
    ob.location=xyz
    for polygon in ob.data.polygons: polygon.use_smooth=True
    return ob


def arrow(name,pos,purpose,side=0,**custom):
    ob = bpy.data.objects.new('CGD_MARK_'+name, None)
    C_MARKERS.objects.link(ob)
    ob.location = pos
    ob.rotation_euler[2] = side
    ob.empty_display_type = 'ARROWS'
    ob.empty_display_size = 1.0
    ob['cg_role'] = purpose
    for k,v in custom.items(): ob[k] = v
    return ob


def button_marker(name,pos,door_id,side):
    ob=bpy.data.objects.new('CGD_INTERACT_'+name,None)
    C_MARKERS.objects.link(ob)
    ob.location=pos
    ob.empty_display_type='SPHERE'
    ob.empty_display_size=.35
    ob['cg_role']='press_to_toggle_roller_door'
    ob['cg_door_id']=door_id
    ob['cg_side']=side
    ob['cg_max_interact_distance_m']=2.1
    ob['cg_multiplayer']='server_authoritative'
    return ob

# =========================== ROADS / FOUNDATION =============================
block('Foundation_Main',(0,0,-.20),(W+.7,D+.7,.40),M['concrete'],C_BASE,.14)
block('WarehouseInterior_WalkableFloor',(0,0,.015),(W-.6,D-.55,.06),M['floor'],C_BASE)
# Toy road border around front and rear of depot, inspired by supplied reference
if DRAW_REFERENCE_BASE:
    block('Front_ConcreteApproach',(0,-16,-.15),(29.9,9.1,.20),M['concrete'],C_BASE,.1)
    block('Outside_Street',(0,-22.4,-.24),(36.8,5.5,.16),M['road'],C_BASE,.15)
    block('Rear_ServiceRoad',(-7.2,17.2,-.17),(10.3,11.9,.18),M['road'],C_BASE,.07)
    for x in (TRUCK_X,EXIT_X):
        block('Garage_Apron_Lane',(x,-16,-.023),(7.55,9.0,.031),M['road'],C_BASE)
    for x in (-11.2,-3.2,3.15,10.95):
        block('Apron_WhiteEdge',(x,-16,.002),(.10,8.0,.015),M['lines'],C_BASE)
    for x in (TRUCK_X,EXIT_X):
        for y in (-14,-17,-19):
            block('ApproachCentreDash',(x,y,.002),(.10,.94,.015),M['yellow'],C_BASE)
    for x in (-13,-7,-1,5,11):
        block('Street_CenterLine',(x,-22.4,-.145),(2.65,.095,.016),M['lines'],C_BASE)

# ================================ BUILDING ==================================
# Facade: front has REAL openings, matching the two bay doors in reference.
left0,left1=TRUCK_X-TRUCK_W/2,TRUCK_X+TRUCK_W/2
right0,right1=EXIT_X-EXIT_W/2,EXIT_X+EXIT_W/2
assert -W/2<left0<left1<right0<right1<W/2
front_sections=[(-W/2,left0),(left1,right0),(right1,W/2)]
for idx,(a,b) in enumerate(front_sections):
    span('BrickFront_Solid_%02d'%idx,a,b,FRONT-.22,FRONT+.23,
         0,H,M['brick'],C_WALL,.055)
for a,b,tag in ((left0,left1,'BoughtTruck'),(right0,right1,'SuburbsExit')):
    span('Front_'+tag+'_RealLintel',a,b,FRONT-.22,FRONT+.23,
         DOOR_HEIGHT+.12,H,M['brick'],C_WALL,.035)
    block('Front_'+tag+'_UpperLintelTrim',((a+b)/2,FRONT-.33,DOOR_HEIGHT+.20),
          (b-a+.14,.24,.25),M['roofcap'],C_WALL,.05)
# Stylised brick seams drawn sparsely to avoid thousands of mesh objects.
# Only on solid segments and lintels; NOT across actual door apertures.
solid_seams=list(front_sections)+[(left0,left1),(right0,right1)]
# Merge all brick/mortar accent segments into ONE mesh object for FBX speed.
seam_vertices=[]
seam_faces=[]
def add_seam(x0,x1,y0,y1,z0,z1):
    v=[(x,y,z) for x,y,z in [(x0,y0,z0),(x0,y0,z1),
       (x0,y1,z0),(x0,y1,z1),(x1,y0,z0),(x1,y0,z1),
       (x1,y1,z0),(x1,y1,z1)]]
    base=len(seam_vertices)
    seam_vertices.extend(v)
    for f in ((0,4,6,2),(1,3,7,5),(0,1,5,4),
              (2,6,7,3),(0,2,3,1),(4,5,7,6)):
        seam_faces.append(tuple(base+q for q in f))

for si,(a,b) in enumerate(solid_seams):
    zstart=0 if si<3 else DOOR_HEIGHT+.20
    k=0
    z=zstart+.44
    while z<H-.15:
        add_seam(a+.07,b-.07,FRONT-.236,FRONT-.225,z,z+.028)
        # Staggered vertical mortar strokes on facing surface
        step=1.55
        x=a+(.63 if k%2 else 1.35)
        while x<b-.15:
            add_seam(x,x+.031,FRONT-.238,FRONT-.220,z-.34,z)
            x+=step
        z+=.64
        k+=1
mesh_obj('BrickFacade_MortarLines_COMBINED',seam_vertices,seam_faces,
         M['mortar'],C_DECOR)

# Side walls: pale timber like the supplied small-warehouse reference.
# The LEFT wall includes TWO dark rectangular window apertures.
for x,side in ((-W/2,'Left'),(W/2,'Right')):
    if side=='Left':
        gaps=[(-7.9,-3.6),(-1.7,2.6)]
    else:
        gaps=[(-7.9,-3.6),(1.1,5.4)]
    intervals=[(FRONT,gaps[0][0]),(gaps[0][1],gaps[1][0]),(gaps[1][1],REAR)]
    for i,(a,b) in enumerate(intervals):
        span(side+'_PaleWood_Solid_%d'%i,x-.21,x+.21,a,b,0,H,M['wood'],C_WALL,.05)
    for i,(a,b) in enumerate(gaps):
        span(side+'_BelowWindow_%d'%i,x-.21,x+.21,a,b,0,4.12,M['wood'],C_WALL)
        span(side+'_AboveWindow_%d'%i,x-.21,x+.21,a,b,5.46,H,M['wood'],C_WALL)
        block(side+'_WindowInset_%d'%i,(x,(a+b)/2,4.79),(.080,b-a-.12,1.18),
              M['glass'],C_WALL,.02)
        for yy in (a,b):
            block(side+'_WindowVerticalTrim',(x,yy,4.79),(.27,.12,1.50),
                  M['roofcap'],C_WALL,.025)
        for zz in (4.12,5.46):
            block(side+'_WindowHorizontalTrim',(x,(a+b)/2,zz),
                  (.28,b-a+.18,.14),M['roofcap'],C_WALL,.025)
    # Simple timber board seams: large individual planks NOT required.
    for yy in (FRONT+1.2,FRONT+5.6,FRONT+10.1,FRONT+14.6,REAR-1.4):
        block(side+'_WoodWall_VerticalSeam',(x+(.23 if x>0 else -.23),yy,3.64),
              (.02,.045,7.18),M['wood_trim'],C_DECOR)

# Rear: left contains a full-scale truck receiving opening, right is solid
# and hides the backstage couch room from the approach / exterior.
back_a=TRUCK_X-TRUCK_W/2+.2
back_b=TRUCK_X+TRUCK_W/2-.2
for i,(a,b) in enumerate([(-W/2,back_a),(back_b,W/2)]):
    span('RearWall_%d'%i,a,b,REAR-.22,REAR+.22,0,H,M['wood'],C_WALL,.04)
span('TruckArrival_RearWallHeader',back_a,back_b,REAR-.22,REAR+.22,
     DOOR_HEIGHT+.13,H,M['wood'],C_WALL)
# Rear receiving door stays OPEN; vehicles can be generated in an offstage bay.
block('RearTruckPortal_OverheadRollerHousing',(TRUCK_X,REAR+.04,DOOR_HEIGHT+.55),
      (TRUCK_W+.6,1.1,.76),M['roofcap'],C_TRUCK,.16)
for rx in (back_a-.14,back_b+.14):
    block('RearPortal_Bumper',(rx,REAR+.20,2.75),(.30,.47,5.5),
          M['teal_dark'],C_TRUCK,.075)
# Keep the rear arrival opening CLEAR for vehicles; fake darkness with
# overhead shading and side reveals, NOT a solid blocking black wall.
block('RearArrival_ShadowedCeiling',(TRUCK_X,REAR+5.65,5.47),
      (TRUCK_W-.55,.25,.43),M['dark'],C_TRUCK)
for side in (-1,1):
    block('RearArrival_ShadowSideReveal',
          (TRUCK_X+side*(TRUCK_W/2-.21),REAR+5.58,2.70),
          (.14,.20,5.36),M['dark'],C_TRUCK)
# Rear covered short receiving tunnel; side walls conceal actual truck arrival.
for x in (back_a-.18,back_b+.18):
    block('ArrivalServiceTunnel_SideWall',(x,REAR+3.1,2.75),
          (.35,6.6,5.55),M['wood'],C_TRUCK,.09)
block('ArrivalServiceTunnel_Canopy',(TRUCK_X,REAR+3.15,5.8),
      (TRUCK_W+.8,7.0,.33),M['roofcap'],C_TRUCK,.13)
block('ArrivalServiceTunnel_GuideLane',(TRUCK_X,REAR+3.1,.046),
      (TRUCK_W-.8,6.3,.055),M['road'],C_TRUCK)

# Roof is a removable ONE-PIECE flat, subtle-chamfered charcoal slab.
# No animated geometry or thousands of corrugations.
block('Roof_Main_Removable',(0,0,H+.25),(W+1.08,D+1.10,.49),
      M['roof'],C_ROOF,.16)
block('Roof_ParapetFront',(0,FRONT-.64,H+.35),(W+1.1,.18,.38),
      M['roofcap'],C_ROOF,.03)
block('Roof_ParapetBack',(0,REAR+.64,H+.35),(W+1.1,.18,.38),
      M['roofcap'],C_ROOF,.03)
for sx in (-W/2-.55,W/2+.55):
    block('Roof_SideEdging',(sx,0,H+.37),(.18,D+1.20,.37),
          M['roofcap'],C_ROOF,.03)
for x,y in [(-7,-5),(-6,5),(6,1),(6,6)]:
    block('Roof_SquareServiceVent',(x,y,H+.60),(.65,.78,.25),
          M['roofcap'],C_ROOF,.09)
# Subtle exposed timber exterior corner strips.
for x in (-W/2,W/2):
    for y in (FRONT,REAR):
        block('Exterior_TimberCornerPost',(x,y,H/2),(.30,.28,H),
              M['wood_trim'],C_WALL,.055)

# Building-facing overdoor trims, stylised awnings and couch EMBLEM ONLY.
for cx,ww,colour in ((TRUCK_X,TRUCK_W,M['orange']),
                     (EXIT_X,EXIT_W,M['teal'])):
    block('Front_Bay_Canopy',(cx,FRONT-.66,6.40),
          (ww+.78,1.42,.24),colour,C_DECOR,.095)
for cx in (TRUCK_X,EXIT_X):
    for dx in (-1.2,1.2):
        block('CanopySupportStem',(cx+dx,FRONT-.67,6.02),
              (.12,.12,.77),M['roofcap'],C_DECOR)
# Pictogram centered on central solid pier, easily removable; NO TEXT.
couch_emblem_x = (left1+right0)*.5
block('CouchLogo_Back',(couch_emblem_x,FRONT-.41,4.0),
      (2.0,.19,.70),M['orange'],C_DECOR,.13)
block('CouchLogo_Seat',(couch_emblem_x,FRONT-.66,3.52),
      (2.16,.53,.37),M['coral'],C_DECOR,.11)
for dx in (-1.04,1.04):
    block('CouchLogo_Arm',(couch_emblem_x+dx,FRONT-.62,3.76),
          (.30,.56,.58),M['orange'],C_DECOR,.08)
    block('CouchLogo_Foot',(couch_emblem_x+dx*.68,FRONT-.51,3.29),
          (.14,.19,.15),M['dark'],C_DECOR)

# ======================== TRUCK DISPATCH AND ARRIVAL ========================
# Width 8.0 m, clear x in [-11.2,-3.2] and clear y along the entire building.
# Front garage area is deliberately empty: do not put display couches here.
for x in (TRUCK_X-3.56, TRUCK_X+3.56):
    block('TruckRoute_FloorSafetyLine',(x,-.25,.056),(.10,D-1.9,.012),
          M['yellow'],C_TRUCK)
for y in (-8,0,7):
    block('TruckRoute_CentreDash',(TRUCK_X,y,.058),(.12,1.5,.012),
          M['yellow'],C_TRUCK)
for x in (TRUCK_X-3.76,TRUCK_X+3.76):
    for y in (-9,4):
        cylinder('DispatchSafetyBollard',(x,y,.65),.15,1.3,
                 M['yellow'],C_TRUCK)
# Create both rear/offstage marker and front staging marker.
arrow('Truck_RearOffstageArrival',(TRUCK_X,REAR+4.5,.12),
      'spawn_bought_truck_out_of_player_view', side=math.pi,
      movement='enter_rear_portal_then_follow_truck_path')
arrow('Truck_Path_01_InsideRear',(TRUCK_X,8.2,.12),
      'truck_arrival_waypoint',side=math.pi)
arrow('Truck_Path_02_Centre',(TRUCK_X,2.1,.12),
      'truck_arrival_waypoint',side=math.pi)
arrow('Truck_DispatchStaging',(TRUCK_X,-5.0,.12),
      'truck_purchase_final_staging_position',side=math.pi,
      cg_trigger_door='TruckGarageDoor')
arrow('Truck_Path_03_DriveOutside',(TRUCK_X,-17.9,.12),
      'truck_purchase_drive_out_destination',side=math.pi)
arrow('TruckDoor_SafetyVolume',(TRUCK_X,FRONT+.55,1.05),
      'check_no_truck_player_or_couch_in_doorway')

# ========================== BACKSTAGE COUCH ROOM ============================
# Rear-right room walled off at y=+4.75; front/right accessible showroom ends
# before it. Enclosed chute blocks lines of sight from the player's lower end.
# Side wall facing dispatch lane: x between 1.9 and 2.2.
span('Backstage_LeftOpaquePartition',1.45,1.73,BACKSTAGE_Y,REAR-.25,
     0,6.4,M['wood'],C_COUCH,.045)
# Main partition wall. ONLY the conveyor aperture x 2.7 .. 6.3 is open.
span('Backstage_FrontWall_Left',1.45,2.75,BACKSTAGE_Y-.16,BACKSTAGE_Y+.16,
     0,6.65,M['wood'],C_COUCH,.06)
span('Backstage_FrontWall_Right',6.45,W/2-.32,BACKSTAGE_Y-.16,BACKSTAGE_Y+.16,
     0,6.65,M['wood'],C_COUCH,.06)
span('Backstage_ConveyorWindow_Header',2.75,6.45,
     BACKSTAGE_Y-.16,BACKSTAGE_Y+.16,3.12,6.65,M['wood'],C_COUCH)
# A sloping-looking hood hides the origin without occluding conveyor pickup.
block('Conveyor_LowOpaqueCanopy',(CONV_X,2.95,3.08),
      (3.82,4.3,.23),M['teal_dark'],C_COUCH,.06)
for xx in (2.62,6.37):
    block('Conveyor_VisibilityBaffle',(xx,3.04,2.08),
          (.20,4.25,1.89),M['teal_dark'],C_COUCH,.055)
block('Conveyor_HoodEntranceSkirt',(CONV_X,.97,2.79),
      (3.8,.25,.43),M['teal'],C_COUCH,.045)
# Overlapping non-colliding hanging rubber strips conceal the rear loading room.
# They are a visual blocker only: in Unity disable MeshColliders on these
# meshes and optionally animate their swing when a couch comes through.
for curtain_i in range(9):
    curtain_x=3.0+curtain_i*.375
    strip=block('Conveyor_OcclusionCurtain_NO_COLLIDER_%02d'%curtain_i,
                (curtain_x,1.60,2.02),(.43,.075,1.86),
                M['teal_dark'] if curtain_i%2 else M['roofcap'],
                C_COUCH,.025)
    strip['cg_no_collider']=True
    strip['cg_optional_animation']='swing_outward_on_couch_passage'

# Separate, entirely concealed furniture placement floorpad, behind partition.
block('HiddenSofaPrefab_PlacementPad',(9.25,8.0,.09),
      (4.05,4.0,.16),M['yellow'],C_COUCH,.16)
for x in (7.35,11.15):
    for y in (6.35,9.65):
        cylinder('BackstageFloorPadCorner',(x,y,.12),.15,.03,M['dark'],C_COUCH)
arrow('Couch_OffstageHiddenSpawn',(9.25,8.0,.20),
      'instantiate_couch_offscreen_then_transfer_to_feed',
      cg_visibility='opaque_wall_between_spawn_and_players')
arrow('Couch_TransferIntoConveyor',(CONV_X,8.0,CONV_TOP+.40),
      'hidden_transfer_waypoint_before_conveyor')
# Shallow chute hides couch arrival until it moves into customer-facing area.
CONV_Y_MIN=-5.2
CONV_Y_MAX=8.8
CONV_LENGTH=CONV_Y_MAX-CONV_Y_MIN
CONV_Y_MID=(CONV_Y_MIN+CONV_Y_MAX)*.5
block('Conveyor_MainBody',(CONV_X,CONV_Y_MID,.76),
      (3.25,CONV_LENGTH+.5,.75),M['teal_dark'],C_COUCH,.085)
block('Conveyor_RubberBeltSurface',(CONV_X,CONV_Y_MID,CONV_TOP-.06),
      (2.91,CONV_LENGTH+.20,.20),M['belt'],C_COUCH,.05)
for x in (CONV_X-1.65, CONV_X+1.65):
    block('Conveyor_Guardrail',(x,CONV_Y_MID,1.49),
          (.18,CONV_LENGTH+.45,.43),M['yellow'],C_COUCH,.07)
for i in range(16):
    y=CONV_Y_MIN+.27+i*(CONV_LENGTH-.54)/15
    cylinder('Conveyor_VisibleRoller_%02d'%i,(CONV_X,y,1.29),
             .095,2.84,M['metal'],C_COUCH,axis='X',n=12)
for y in (-4.0,0.3,4.2,8.0):
    for x in (CONV_X-1.35,CONV_X+1.35):
        block('ConveyorSupport',(x,y,.39),(.24,.28,.75),M['roofcap'],C_COUCH,.045)
arrow('Couch_ConveyorFeed',(CONV_X,7.95,CONV_TOP+.38),
      'couch_fed_to_belt_inside_hidden_room')
for idx,y in enumerate((7.95,5.0,1.9,-1.65,-5.15)):
    arrow('Couch_ConveyorPoint_%d'%idx,(CONV_X,y,CONV_TOP+.35),
          'conveyor_movement_waypoint')
arrow('Couch_VisiblePickup',(CONV_X,-5.15,CONV_TOP+.38),
      'available_couch_pickup_location')
# Interior backstage crates and shelving, kept OFF truck path/conveyor.
for y in (6.3,9.2):
    block('BackstageShelf',(12.25,y,1.55),(1.5,2.3,3.0),
          M['roofcap'],C_COUCH,.09)
    block('BackstageCardboardBox',(12.25,y,.78),(1.05,1.15,1.02),
          M['woodbox'],C_COUCH,.09)

# =========================== PLAYER-FACING INTERIOR ========================
# Frog manager is in front/right of the inaccessible backstage room.
block('FrogCounter_Main',(10.1,1.62,1.03),
      (4.6,1.1,1.73),M['wood'],C_INSIDE,.13)
block('FrogCounter_Cap',(10.1,1.61,1.92),
      (4.85,1.39,.16),M['white'],C_INSIDE,.065)
block('FrogCounter_TealAccent',(10.1,1.02,1.06),
      (4.0,.07,.61),M['teal'],C_INSIDE,.04)
block('FrogNPC_StandingSpot',(9.7,-.48,.08),
      (2.45,2.4,.025),M['green'],C_INSIDE,.24)
arrow('DeliveryMan_Frog',(9.65,-.45,.13),'delivery_manager_npc_spawn',
      side=math.pi, cg_npc_prefab_hint='frog_01')
arrow('DeliveryMan_Interact',(9.3,-1.95,.13),
      'delivery_dialogue_interaction_position')
arrow('Player_InitialSpawn',(10.0,-7.40,.12),
      'suburbs_initial_player_spawn',side=0.0)
block('PlayerSpawnSmall_Decal',(10.0,-7.40,.067),
      (1.65,1.65,.018),M['blue'],C_INSIDE,.14)
# Keep lines of travel between frog, conveyor pickup, exit door.
for x,y in ((12.5,-8.4),(12.5,2.7)):
    cylinder('Warehouse_FloorAccentDisc',(x,y,.10),.44,.025,
             M['teal'],C_INSIDE)
# Display couch in showroom but not blocking primary circulation.
def showroom_sofa(cx,cy,scale=.78):
    block('Showroom_Sofa_Base',(cx,cy,.47*scale),
          (2.65*scale,1.24*scale,.73*scale),M['orange'],C_INSIDE,.15)
    block('Showroom_Sofa_Back',(cx,cy+.44*scale,1.06*scale),
          (2.62*scale,.40*scale,1.13*scale),M['orange'],C_INSIDE,.14)
    for side in (-1,1):
        block('Showroom_Sofa_Arm',(cx+side*1.2*scale,cy,.91*scale),
              (.28*scale,1.2*scale,.80*scale),M['coral'],C_INSIDE,.09)
showroom_sofa(11.1,-4.65)

# Visible interior framework / skylight glow (all static props).
for xx in (-13.0,.4,12.9):
    for yy in (-10.4,3.25):
        block('WarehouseCornerSupport',(xx,yy,3.35),
              (.28,.28,6.7),M['teal_dark'],C_INSIDE,.04)
for y in (-7.0,.3,7.25):
    block('WarehouseCeilingBeam',(0,y,H-.40),
          (W-1.2,.22,.31),M['roofcap'],C_INSIDE,.055)
for x,y in ((-7,-7),(-7,4),(5,-5),(10,-5),(8,2)):
    block('InteriorPendantLED',(x,y,H-1.15),
          (2.8,.65,.16),M['white'],C_INSIDE,.05)

# ==================== TWO SECTIONAL ROLLER DOOR ASSEMBLIES ===================
# Each slat follows a VERTICAL -> QUARTER ARC -> HORIZONTAL overhead rail.
# Do not use 100s of slats or Blender curve modifiers; this remains lightweight.
DOORS={}

def roller_door(tag,cx,width,collection_ref,accent):
    y=FRONT_DOOR_Y
    height=DOOR_HEIGHT
    radius=.73
    slat_count=12
    slat_h=height/slat_count
    # Overhead housings/track and pair of side jambs.
    for x in (cx-width/2-.12,cx+width/2+.12):
        block(tag+'_VerticalTrack',(x,y+.12,height/2),
              (.24,.35,height+.36),M['roofcap'],collection_ref,.045)
        block(tag+'_OverheadHorizontalRail',(x,y+height/2+.55,
              height+radius),(.19,height+1.1,.16),M['metal'],collection_ref,.035)
    block(tag+'_OverheadMotorHousing',(cx,y+radius*.53,height+radius+.12),
          (width+.5,1.65,.69),M['roofcap'],collection_ref,.13)
    block(tag+'_MotorAccentStripe',(cx,y-.34,height+radius+.17),
          (width-.45,.08,.13),accent,collection_ref,.035)
    for side in (-1,1):
        cylinder(tag+'_EndDriveMotor',(cx+side*(width/2+.18),
                 y+radius*.55,height+radius+.07),.31,.27,
                 accent,collection_ref,axis='X')
    block(tag+'_GroundSill',(cx,y,.055),(width+.26,.55,.11),
          M['roofcap'],collection_ref,.025)
    slats=[]
    for i in range(slat_count):
        z=.11+(i+.5)*slat_h
        obj=block(tag+'_Slat_%02d'%i,(cx,y,z),
                  (width-.17,.19,slat_h-.028),
                  accent if i==0 else (M['white'] if i%3==0 else M['metal']),
                  collection_ref,soft=0.0)
        obj['cg_door_id']=tag
        obj['cg_slat_index']=i
        obj['cg_animated']=True
        slats.append((obj,z))
    empty=arrow(tag+'_AnimationRoot',(cx,y,height/2),
                'animated_door_root_reference')
    empty['cg_door_id']=tag
    return dict(tag=tag,cx=cx,y=y,width=width,height=height,
                radius=radius,slats=slats,root=empty)

TRUCK_GATE=roller_door('TruckGarageDoor',TRUCK_X,TRUCK_W,
                        C_TRUCKDOOR,M['yellow'])
EXIT_GATE=roller_door('ExitRollerDoor',EXIT_X,EXIT_W,
                       C_EXITDOOR,M['teal'])
DOORS['truck']=TRUCK_GATE
DOORS['exit']=EXIT_GATE


def door_pose(door,how_open):
    h=door['height'];r=door['radius']; yy=door['y'];cx=door['cx']
    total_travel = h + r*math.pi/2 + .45
    advance = max(0,min(1,how_open))*total_travel
    for ob,z0 in door['slats']:
        s=z0+advance
        if s<=h:
            loc_y=yy; loc_z=s; ang=0
        elif s<h+r*math.pi/2:
            a=(s-h)/r
            loc_y=yy+r*(1-math.cos(a))
            loc_z=h+r*math.sin(a)
            ang=-a
        else:
            loc_y=yy+r+(s-h-r*math.pi/2)
            loc_z=h+r
            ang=-math.pi/2
        ob.location=(cx,loc_y,loc_z)
        ob.rotation_euler=(ang,0,0)


def insert_door_keys(door,frame,fraction):
    door_pose(door,fraction)
    for obj,_ in door['slats']:
        obj.keyframe_insert('location',frame=frame,group='RollerDoor')
        obj.keyframe_insert('rotation_euler',frame=frame,group='RollerDoor')


def smoothstep(t):
    t=max(0,min(1,t))
    return t*t*(3-2*t)


def animate(door,start,end,open_at_start,open_at_end):
    frs=sorted(set([start,end]+list(range(start,end+1,ANIM_STEP))))
    for frame in frs:
        u=smoothstep((frame-start)/float(end-start))
        insert_door_keys(door,frame,open_at_start*(1-u)+open_at_end*u)

animate(TRUCK_GATE,1,41,0,1)
insert_door_keys(TRUCK_GATE,51,1)
animate(TRUCK_GATE,51,91,1,0)
insert_door_keys(TRUCK_GATE,191,0)
insert_door_keys(EXIT_GATE,1,0)
insert_door_keys(EXIT_GATE,91,0)
animate(EXIT_GATE,101,141,0,1)
insert_door_keys(EXIT_GATE,151,1)
animate(EXIT_GATE,151,191,1,0)

# Exterior and interior buttons only on Suburbs exit (manual player control).
# Buttons sit on the wall at right of door, outside its clearance.
def door_button(name, x, y, face_sign):
    block(name+'_Housing',(x,y,1.56),(.67,.24,1.1),
          M['roofcap'],C_DECOR,.085)
    cylinder(name+'_PushToOpen',(x,y+face_sign*.17,1.69),
             .23,.15,M['button_green'],C_DECOR,axis='Y')
    cylinder(name+'_DoorStatusLamp',(x,y+face_sign*.17,1.22),
             .09,.13,M['red'],C_DECOR,axis='Y')
    button_marker(name,(x,y+face_sign*.64,1.65),
                  'ExitRollerDoor','OUTSIDE' if face_sign<0 else 'INSIDE')

door_button('ExitDoor_ExteriorButton',11.55,FRONT-.44,-1)
door_button('ExitDoor_InteriorButton',11.55,FRONT+.44,1)
arrow('ExitDoor_SafetyVolume',(EXIT_X,FRONT+.5,1.06),
      'prevent_closing_when_player_couch_vehicle_blocks_gate')
arrow('ExitDoor_SuburbsOutside',(EXIT_X,FRONT-6,.14),
      'depot_to_suburbs_navigation_waypoint')
arrow('ExitDoor_InteriorApproach',(EXIT_X,FRONT+3,.14),
      'inside_depot_exit_navigation_waypoint')

# ======================== EXTERIOR TOY-STYLE DETAILS ========================
# Reference has black roof and simple tidy lines; keep this tasteful & sparse.
for xx in (-13.1,13.1):
    block('FacadeCorner_Bollard',(xx,-14,.67),(.35,.35,1.34),
          M['teal'],C_DECOR,.1)
for x in (-13.2,13.2):
    for y in (-8.5,6):
        block('DepotWallExteriorLamp',(x+(-.17 if x<0 else .17),y,5.8),
              (.18,.78,.40),M['yellow'],C_DECOR,.08)

if ADD_TREE_DETAILS:
    for i,(x,y) in enumerate([(-17,-12),(16.8,-12),(-17,7),(16.8,8)]):
        cylinder('SmallPlanter_%d'%i,(x,y,.30),1.20,.60,
                 M['wood_trim'],C_DECOR)
        cylinder('RoundedTreeTrunk_%d'%i,(x,y,1.49),.23,2.4,
                 M['wood_trim'],C_DECOR)
        sphere('RoundedTreeCanopy_%d'%i,(x,y,3.07),1.43,
               M['leaves'] if i%2 else M['leaves2'],C_DECOR)
        for j in range(2):
            a=i*.7+j*2.5
            sphere('RoundedTree_CrownLobe_%d'%i,
                   (x+math.cos(a)*.65,y+math.sin(a)*.64,2.93),.69,
                   M['leaves'],C_DECOR)

# ========================== CAMERA / DEV METADATA ===========================
if ADD_PREVIEW_LIGHTS:
    camera_data=bpy.data.cameras.new('DepotReference_Angle')
    cam=bpy.data.objects.new('DepotReference_Angle',camera_data)
    C_PREVIEW.objects.link(cam)
    cam.location=(-37,-43,28)
    target=Vector((0,0,2.65))
    cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler()
    camera_data.type='ORTHO'
    camera_data.ortho_scale=45
    scene.camera=cam
    sun_data=bpy.data.lights.new('PreviewSun','SUN')
    sun_data.energy=2.1
    sun=bpy.data.objects.new('PreviewSun',sun_data)
    C_PREVIEW.objects.link(sun)
    sun.rotation_euler=(math.radians(30),math.radians(-23),math.radians(-28))
    for x,y in ((-7,-4),(6,-3),(7,3)):
        ld=bpy.data.lights.new('PreviewInteriorArea','AREA')
        ld.energy=750
        ld.shape='DISK'
        ld.size=8
        lamp=bpy.data.objects.new('PreviewInteriorArea',ld)
        C_PREVIEW.objects.link(lamp)
        lamp.location=(x,y,6.8)

scene['CGD_BUILD']='Suburbs_Depot_V2_Reference_Shape'
scene['CGD_BUILDING_FRONT']='NEGATIVE_Y'
scene['CGD_NOTE']='Door keys are on mesh slats, not a single door mesh.'
scene['CGD_ANIM_CLIPS']=json.dumps(CLIPS)
scene['CGD_NETWORK_NOTE']='Server-authoritative Unity door and truck spawn logic required'
scene['CGD_BACKSTAGE_NOTE']='Backstage sofa pad is invisible behind physical walls.'
scene.frame_set(1)

# ======================= OPTIONAL FAST FBX EXPORTS ==========================
def select_only(objects):
    bpy.ops.object.select_all(action='DESELECT')
    for obj in objects:
        if obj and obj.type in {'MESH','EMPTY'}:
            obj.select_set(True)
    if objects:
        bpy.context.view_layer.objects.active=next(
            (o for o in objects if o and o.type in {'MESH','EMPTY'}),None)


def export_one(filepath,objects,include_animations):
    objs=[o for o in objects if o.type in {'MESH','EMPTY'}]
    select_only(objs)
    bpy.ops.export_scene.fbx(
        filepath=filepath,
        use_selection=True,
        object_types={'MESH','EMPTY'},
        use_mesh_modifiers=True,
        add_leaf_bones=False,
        axis_forward='-Z', axis_up='Y',
        bake_anim=include_animations,
        bake_anim_use_all_bones=False,
        bake_anim_use_nla_strips=False,
        bake_anim_use_all_actions=False,
        bake_anim_step=1.0,
        bake_anim_simplify_factor=0.0 if include_animations else 1.0,
    )


def export_separately():
    if bpy.data.filepath:
        folder=os.path.join(os.path.dirname(bpy.data.filepath),'CouchGuys_DepotV2_Exports')
    else:
        folder=os.path.join(os.path.expanduser('~'),'CouchGuys_DepotV2_Exports')
    os.makedirs(folder,exist_ok=True)
    static_collections=[C_BASE,C_WALL,C_ROOF,C_INSIDE,C_COUCH,
                        C_TRUCK,C_MARKERS,C_DECOR]
    static_objects=[ob for col in static_collections for ob in col.objects]
    export_one(os.path.join(folder,'CGD_DepotV2_Static.fbx'),static_objects,False)
    export_one(os.path.join(folder,'CGD_TruckDoor_Animated.fbx'),
               list(C_TRUCKDOOR.objects),True)
    export_one(os.path.join(folder,'CGD_ExitDoor_Animated.fbx'),
               list(C_EXITDOOR.objects),True)
    # Engine-agnostic helper containing exact named markers/locs/animation ranges.
    all_markers=[ob for ob in C_MARKERS.objects if ob.type=='EMPTY']
    with open(os.path.join(folder,'CGD_DepotV2_UnityMarkers.json'),'w') as handle:
        json.dump({'units':'metres','front':'-Y','animations':CLIPS,
                   'markers':{o.name:{'role':o.get('cg_role',''),
                                      'location':list(o.location),
                                      'rotation_euler':list(o.rotation_euler)}
                              for o in all_markers}},handle,indent=2)
    print('FBX exported to:',folder)

if DO_EXPORT_FBX:
    export_separately()

# Deselect (save memory/UI churn after build). Point at the frog marker.
select_only([])
frog=bpy.data.objects.get('CGD_MARK_DeliveryMan_Frog')
if frog:
    frog.select_set(True)
    bpy.context.view_layer.objects.active=frog

print('\n'+'='*68)
print('COUCH GUYS DEPOT V2 BUILT')
print('REFERENCE SHAPE: rectangular, charcoal flat roof, pale timber sides')
print('FRONT DOORS: truck dispatch on LEFT, Suburbs exit on RIGHT')
print('REAR TRUCK ARRIVAL: CGD_MARK_Truck_RearOffstageArrival')
print('HIDDEN COUCH SPAWN: CGD_MARK_Couch_OffstageHiddenSpawn')
print('VISIBLE COUCH PICKUP: CGD_MARK_Couch_VisiblePickup')
print('FROG SPAWN: CGD_MARK_DeliveryMan_Frog')
print('TO SEE INSIDE: hide CGD_03_Hide_For_Interior_Roof')
print('FOUR ANIMATION CLIPS:',CLIPS)
print('DO_EXPORT_FBX:',DO_EXPORT_FBX)
print('='*68+'\n')
