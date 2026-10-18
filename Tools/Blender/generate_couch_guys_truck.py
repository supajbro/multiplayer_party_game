"""
COUCH GUYS — STYLISED COUCH DELIVERY TRUCK
Blender 3.6+ / 4.x, Python 3. Runs in Blender's Scripting workspace.

Usage:
    1. Open Blender -> Scripting -> Open this .py -> Run Script.
    2. Scrub frames 1-141 to see the rear roller shutter open and close.
    3. Place logo.png beside this script (included in the ZIP download).
    4. A .blend and a Unity-ready .fbx are saved beside this script.

MODEL CONVENTIONS
    +X = vehicle FRONT, -X = REAR, +Z = up. Units = metres.
    Only CG_DeliveryTruck exists: no render studio, preview floor, camera, or lights.
    Side panels use the supplied Couch Guys PNG logo; no separate lettering
    or additional text objects are generated.

ROLLER DOOR — ACTIVE BLENDER ACTION + THREE FBX EXPORTS:
    Blender: The door armature is ACTIVE when this script finishes. The Action
    Editor shows RollerDoor_FullCycle by default. Scrub 1–61 to open, 81–141
    to close. The Action dropdown also contains RollerDoor_Open and
    RollerDoor_Close as separate reusable actions.

    Unity: Main FBX exports three named actions (FullCycle, Open, Close).
    Two additional single-clip FBXs are also exported with the SAME exact
    rig and geometry, one baked clip in each. These are reliable
    fallbacks if your Unity/Blender FBX importer hides extra takes.

    Opening and closing actions deliberately use local frames 1–61.
    The main FullCycle uses timeline frames 1–141. The shutter uses a real
    armature with 15 weighted bones, not animated rigid mesh fragments.

    Seat markers and the cargo loading marker are EMPTY transforms. Runtime
    seats, interaction, loading/physics, networking and vehicle controller
    remain Unity-side systems; this asset is visual geometry + marker transforms.
"""

import bpy
import math
import os
import shutil
from pathlib import Path
from mathutils import Vector, Quaternion

# -----------------------------------------------------------------------------
# ADJUSTABLE SETTINGS
# -----------------------------------------------------------------------------
SEED = 27                      # Reserved for procedural variants.
EXPORT_FBX = True
SAVE_BLEND = True
# No studio, ground, preview objects, lighting, or scene camera are created.
ENABLE_LOADING_RAMP = False    # Deploy a removable display ramp behind the truck.
OUTPUT_DIRECTORY = None       # None => folder containing this script or current .blend.
LOGO_FILENAME = "logo.png"    # Supplied transparent Couch Guys logo, kept beside the script.

RED = (0.81, 0.225, 0.22, 1.0)           # warm courier red
RED_LIGHT = (0.94, 0.35, 0.30, 1.0)
RED_DARK = (0.48, 0.125, 0.13, 1.0)

WHEEL_FRONT_X = 2.57
WHEEL_REAR_X = -2.77
WHEEL_Z = 0.59
REAR_X = -3.84
BOX_FRONT_X = 0.65
BOX_REAR_X = -3.75
BOX_CENTER_X = (BOX_FRONT_X + BOX_REAR_X) * .5
BOX_LENGTH = BOX_FRONT_X - BOX_REAR_X
BOX_SIDE_Y = 1.175

DOOR_BOTTOM_Z = 1.24
DOOR_SLAT_COUNT = 15
DOOR_SLAT_HEIGHT = 0.145
DOOR_REAR_X = -3.87
DOOR_CURVE_START_Z = 3.48
DOOR_CURVE_RADIUS = .27
DOOR_WIDTH = 2.175

OPEN_BEGIN, OPEN_END = 1, 61
CLOSE_BEGIN, CLOSE_END = 81, 141
FPS = 30

# -----------------------------------------------------------------------------
# SCENE AND OBJECT UTILS
# -----------------------------------------------------------------------------
# Clear *all* old generated objects, including hidden preview objects.
# Selecting and deleting visible objects is insufficient if a previous studio
# collection has been hidden from the view layer.
for old_object in list(bpy.data.objects):
    bpy.data.objects.remove(old_object, do_unlink=True)
for old_collection in list(bpy.data.collections):
    bpy.data.collections.remove(old_collection, do_unlink=True)
for old_action in list(bpy.data.actions):
    bpy.data.actions.remove(old_action, do_unlink=True)
# Drop unused preview data blocks from earlier script versions too.
for data_collection in (bpy.data.cameras,bpy.data.lights):
    for data in list(data_collection):
        if data.users == 0:
            data_collection.remove(data)

scene = bpy.context.scene
scene.render.fps = FPS
scene.frame_start, scene.frame_end = OPEN_BEGIN, CLOSE_END
scene.unit_settings.system = 'METRIC'
scene.unit_settings.scale_length = 1.0

truck_collection = bpy.data.collections.new('CG_DeliveryTruck')
scene.collection.children.link(truck_collection)
# After removing old collections, explicitly make the new truck collection
# active so mesh primitives have a valid Blender view-layer context.
truck_layer=bpy.context.view_layer.layer_collection.children.get(truck_collection.name)
if truck_layer is not None:
    bpy.context.view_layer.active_layer_collection=truck_layer

root = bpy.data.objects.new('CG_DeliveryTruck_ROOT', None)
root.empty_display_type = 'CUBE'
root.empty_display_size = 0.4
truck_collection.objects.link(root)


def link_in(obj, collection=truck_collection, parent=root):
    # Blender primitives are initially in the active scene collection.
    for col in list(obj.users_collection):
        col.objects.unlink(obj)
    collection.objects.link(obj)
    if parent is not None:
        obj.parent = parent
    return obj


def make_material(name, color, metallic=0, roughness=.6, alpha=1., emission=0):
    m = bpy.data.materials.new(name)
    m.diffuse_color = color
    m.use_nodes = True
    p = m.node_tree.nodes.get('Principled BSDF')
    if p:
        p.inputs['Base Color'].default_value = color
        p.inputs['Metallic'].default_value = metallic
        p.inputs['Roughness'].default_value = roughness
        p.inputs['Alpha'].default_value = alpha
        if emission:
            # Blender 4 changes the name of the emission input.
            if p.inputs.get('Emission Color'):
                p.inputs['Emission Color'].default_value = color
            elif p.inputs.get('Emission'):
                p.inputs['Emission'].default_value = color
            if p.inputs.get('Emission Strength'):
                p.inputs['Emission Strength'].default_value = emission
    if alpha < 1:
        if hasattr(m, 'surface_render_method'):
            try:
                m.surface_render_method = 'BLENDED'
            except (TypeError, ValueError):
                pass
        if hasattr(m, 'blend_method'):
            try:
                m.blend_method = 'BLEND'
            except (TypeError, ValueError):
                pass
        m.use_nodes = True
        m.diffuse_color = (color[0], color[1], color[2], alpha)
        if p:
            p.inputs['Alpha'].default_value = alpha
        if hasattr(m, 'show_transparent_back'):
            m.show_transparent_back = False
    return m


body = make_material('Paint_Red_Satin', RED, metallic=.2, roughness=.38)
body_light = make_material('Paint_WarmRed_Highlights', RED_LIGHT, metallic=.15, roughness=.42)
body_dark = make_material('Paint_DeepRed', RED_DARK, metallic=.1)
metal = make_material('Metal_Satin_Silver', (.67, .72, .74, 1), metallic=.75, roughness=.31)
metal_dark = make_material('Metal_Charcoal', (.17, .19, .21, 1), metallic=.7, roughness=.39)
black = make_material('Rubber_Charcoal', (.055, .06, .065, 1), roughness=.93)
rubber_soft = make_material('Rubber_Light', (.11, .12, .13, 1), roughness=.90)
glass = make_material('Glass_ClearBlue_Transparent', (.32, .62, .76, 1), metallic=0.0, roughness=.10, alpha=.24)
glass_edge = make_material('Glass_Outline', (.065, .14, .18, 1), metallic=.12, roughness=.29)
interior = make_material('Interior_Dark', (.15, .16, .17, 1), roughness=.85)
seat_color = make_material('Seat_Upholstery_WarmGray', (.37, .40, .41, 1), roughness=.9)
seat_accent = make_material('Seat_Accent', (.77, .38, .29, 1), roughness=.84)
wood = make_material('Cargo_Floor_WarmWood', (.49, .33, .22, 1), roughness=.8)
wood_alt = make_material('Cargo_Floor_Alternate', (.42, .29, .20, 1), roughness=.8)
white_lamp = make_material('Lamp_WarmWhite', (1.0, .94, .70, 1), emission=.9)
red_lamp = make_material('Lamp_Red', (1.0, .10, .08, 1), emission=.6)
amber_lamp = make_material('Lamp_Amber', (1., .59, .11, 1.), emission=.5)


def bevel(obj, amount=.03, segments=3):
    if amount > 0:
        m = obj.modifiers.new('RoundedEdges', 'BEVEL')
        m.width, m.segments = amount, segments
        if hasattr(m, 'affect'):
            m.affect = 'EDGES'
        # Weighted normals reduce the faceted-box look.
        try:
            n = obj.modifiers.new('SmoothBevelNormals', 'WEIGHTED_NORMAL')
            n.keep_sharp = True
        except Exception:
            pass
    return obj


def cube(name, loc, dims, material, radius=.0, collection=truck_collection, parent=root):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc)
    o = bpy.context.object
    o.name = name
    o.dimensions = dims
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if material:
        o.data.materials.append(material)
    link_in(o, collection, parent)
    bevel(o, radius)
    return o


def cylinder(name, loc, radius, length, material, sides=32, rot=(0,0,0), bevel_amount=0.0):
    bpy.ops.mesh.primitive_cylinder_add(vertices=sides, radius=radius, depth=length,
                                        location=loc, rotation=rot)
    o = bpy.context.object
    o.name = name
    if material:
        o.data.materials.append(material)
    link_in(o)
    bevel(o, bevel_amount, 2)
    return o


def sphere(name, loc, dims, material, segments=16, rings=8):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segments, ring_count=rings,
                                        radius=1, location=loc)
    o = bpy.context.object
    o.name = name
    o.scale = dims
    if material:
        o.data.materials.append(material)
    link_in(o)
    for p in o.data.polygons:
        p.use_smooth = True
    return o


def torus(name, loc, major_radius, minor_radius, material, rotation=(0,0,0)):
    bpy.ops.mesh.primitive_torus_add(major_segments=40, minor_segments=12,
                                    location=loc, rotation=rotation,
                                    major_radius=major_radius,
                                    minor_radius=minor_radius)
    o = bpy.context.object
    o.name = name
    if material:
        o.data.materials.append(material)
    link_in(o)
    for p in o.data.polygons:
        p.use_smooth = True
    return o


def polygon(name, verts, material, thickness=0.0):
    mesh = bpy.data.meshes.new(name + '_mesh')
    mesh.from_pydata(verts, [], [tuple(range(len(verts)))])
    mesh.update()
    o = bpy.data.objects.new(name, mesh)
    truck_collection.objects.link(o)
    o.parent = root
    mesh.materials.append(material)
    if thickness:
        m = o.modifiers.new('PanelThickness', 'SOLIDIFY')
        m.thickness = thickness
        m.offset = 0
    return o


def line(name, coords, material, thickness=.018, resolution=3):
    crv = bpy.data.curves.new(name + '_curve', type='CURVE')
    crv.dimensions = '3D'
    crv.resolution_u = 16
    crv.bevel_depth, crv.bevel_resolution = thickness, resolution
    sp = crv.splines.new('POLY')
    sp.points.add(len(coords)-1)
    for p,co in zip(sp.points, coords):
        p.co = (co[0],co[1],co[2],1)
    obj = bpy.data.objects.new(name,crv)
    truck_collection.objects.link(obj)
    obj.parent = root
    obj.data.materials.append(material)
    # FBX does not reliably support Curve objects: convert all decorative lines
    # into ordinary meshes so their geometry arrives intact in Unity.
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.convert(target='MESH')
    return bpy.context.object


def empty(name, loc, radius=.23):
    o = bpy.data.objects.new(name, None)
    truck_collection.objects.link(o)
    o.parent = root
    o.location = loc
    o.empty_display_type = 'ARROWS'
    o.empty_display_size = radius
    return o

# -----------------------------------------------------------------------------
# UNDERBODY, CARGO HOLD AND SUSPENSION
# -----------------------------------------------------------------------------
cube('Chassis_Longitudinal_Left', (-.40, -.74, .89), (7.35,.16,.22), metal_dark, .045)
cube('Chassis_Longitudinal_Right', (-.40, .74, .89), (7.35,.16,.22), metal_dark, .045)
for i,x in enumerate((-3.13,-2.10,-.95,.30,1.52,2.70)):
    cube('Chassis_Crossmember_%02d'%i, (x,0,.88), (.11,1.64,.18),metal_dark,.02)
cube('Fuel_Tank', (.57, -.93,.66), (1.10,.40,.43), metal, .1)
cube('Battery_Box', (-1.45,.92,.73), (.74,.31,.35),metal_dark,.05)
cube('Rear_Safety_UnderrunBar', (-3.89,0,.64),(.17,2.38,.16),metal,.035)

# Open interior is made from separate floor, walls and roof. NOT a sealed cube.
cube('Cargo_Floor_Structural', (BOX_CENTER_X,0,1.14), (BOX_LENGTH,2.30,.18),metal_dark,.027)
cube('Cargo_InsideFloor', (BOX_CENTER_X,0,1.243), (BOX_LENGTH-.1,2.17,.034),wood,.012)
for n in range(6):
    yy = -.88+n*.352
    cube('Cargo_Floor_Plank_%02d'%n,(BOX_CENTER_X,yy,1.265),
         (BOX_LENGTH-.16,.334,.012), wood if n%2==0 else wood_alt,.004)

for side, sign in [('Right',-1),('Left',1)]:
    yy=sign*BOX_SIDE_Y
    cube('Cargo_%s_OuterPanel'%side,(BOX_CENTER_X,yy,2.58),
         (BOX_LENGTH,.13,2.52),body,.045)
    # Interior reinforcement ribs, largely subtle at the opening.
    for j,x in enumerate((-3.40,-2.35,-1.25,-.12)):
        cube('Cargo_%s_InnerRib_%d'%(side,j),
             (x,sign*1.063,2.45),(.050,.042,2.35),metal,.014)

cube('Cargo_Bulkhead_Front',(.65,0,2.55),(.12,2.40,2.69),body_light,.035)
cube('Cargo_Roof', (BOX_CENTER_X,0,3.92), (BOX_LENGTH+.13,2.51,.19),body,.08)
cube('Cargo_Roof_InteriorLiner',(BOX_CENTER_X,0,3.797),
     (BOX_LENGTH-.12,2.27,.022),metal_dark,.01)

# Outline trim and braces make the cartoon vehicle read at longer distances.
for sign,side in ((-1,'R'),(1,'L')):
    yy=sign*1.252
    cube('Cargo_%s_TopTrim'%side,(BOX_CENTER_X,yy,3.89),
         (BOX_LENGTH+.16,.065,.10),body_light,.03)
    cube('Cargo_%s_BottomTrim'%side,(BOX_CENTER_X,yy,1.33),
         (BOX_LENGTH,.065,.08),body_dark,.018)
    cube('Cargo_%s_FrontVerticalTrim'%side,(.64,yy,2.59),
         (.12,.065,2.58),body_light,.035)
    cube('Cargo_%s_RearVerticalTrim'%side,(-3.75,yy,2.59),
         (.10,.070,2.58),metal,.028)
    cube('Cargo_%s_SideMarker'%side,(-2.15,sign*1.277,1.43),
         (.28,.035,.075),amber_lamp,.025)

for x in (-2.76,-.18):
    cube('Ceiling_InteriorLamp_%s'%str(x).replace('.','_'),
         (x,0,3.757),(.52,.22,.027),white_lamp,.013)

# Rear bumper, sill, and a reinforced door frame.
cube('Rear_Bumper',(-4.02,0,.87),(.24,2.58,.17),metal,.065)
cube('Rear_Threshold',(-3.91,0,1.20),(.25,2.32,.09),metal,.02)
for side, sign in [('Right',-1),('Left',1)]:
    cube('Rear_%s_FramePost'%side,(-3.85,sign*1.173,2.44),
         (.18,.11,2.73),metal,.036)
cube('Rear_DoorTopHeader',(-3.84,0,3.62),(.18,2.46,.21),metal,.035)

# -----------------------------------------------------------------------------
# CAB AND FOUR SEATS — 2 front seats plus 2 rear seats
# -----------------------------------------------------------------------------
# Hollow cab construction: don't use a solid cube here, as players and seats
# must fit INSIDE and be visible through actual transparent window openings.
cube('Cab_InnerFloor',(2.02,0,1.078),(3.04,2.16,.20),body_dark,.09)
cube('Cab_RearFirewall',(.57,0,1.96),(.13,2.19,1.89),body_light,.06)
cube('Cab_FrontLowerShell',(3.45,0,1.54),(.21,2.13,1.17),body_light,.075)
for sign,side in ((-1,'Right'),(1,'Left')):
    cube('Cab_%s_LowerSideShell'%side,(2.04,sign*1.025,1.63),
         (2.95,.16,1.23),body_light,.083)
    cube('Cab_%s_TopWindowRail'%side,(2.02,sign*1.076,2.841),
         (2.79,.13,.12),body,.042)
    cube('Cab_%s_FrontPillar'%side,(3.27,sign*1.062,2.46),
         (.16,.15,.80),body,.050)
cube('Cab_Roof',(1.94,0,2.91),(3.0,2.28,.23),body,.105)
cube('Cab_FrontNose',(3.52,0,1.48),(.24,2.10,.92),body,.10)
cube('Cab_RearStep',(1.10,0,1.015),(1.09,2.34,.18),body_dark,.06)
# Front angled windshield glass and its slightly darker perimeter.
def windscreen(name,x_offset,lower_z,upper_z,halfw,material):
    return polygon(name,[(3.525+x_offset,-halfw,lower_z),
                         (3.525+x_offset, halfw,lower_z),
                         (3.31+x_offset,  halfw,upper_z),
                         (3.31+x_offset, -halfw,upper_z)],material,.014)
# IMPORTANT: DO NOT use an opaque full-sized windshield backing face: that
# turns the glass solid black in Blender and Unity regardless of glass alpha.
windscreen('Windshield_TransparentGlass', .046, 2.075, 2.81, .969, glass)
# Rubber perimeter is a frame around the opening, leaving the centre clear.
windframe=[(3.548,-1.015,2.02),(3.548,1.015,2.02),
           (3.345,1.015,2.85),(3.345,-1.015,2.85),
           (3.548,-1.015,2.02)]
line('Windshield_RubberSurround_FRAME', windframe, glass_edge, .033)
# Central windscreen pillar and two wipers, intentionally without text.
line('Windscreen_CentreSeam',[(3.58,0,2.075),(3.37,0,2.81)],metal_dark,.010)
for sign in [-1,1]:
    line('Wiper_%s'%sign,[(3.584,.08*sign,2.105),
                          (3.562,.78*sign,2.18)],metal_dark,.020)

# Side windows / panels / seams / door handles, mirrored.
for sign,side in ((-1,'Right'),(1,'Left')):
    outer=sign*1.115
    window_y=sign*1.122
    # Four passenger doors have distinct panel/handle/window features.
    for ci,(cx,win_width) in enumerate(((1.28,1.055),(2.63,1.025))):
        label='Rear' if ci==0 else 'Front'
        cube('Cab_%s_%s_DoorSurface'%(side,label),
             (cx,outer,1.625),(1.19,.031,1.23),body,.035)
        # Open sightline! Four thin gasket rails, not a solid black window panel.
        for direction in (-1,1):
            cube('Cab_%s_%s_WindowVerticalFrame_%s'%(side,label,direction),
                 (cx+direction*(win_width*.5-.015),window_y,2.506),
                 (.035,.042,.60),glass_edge,.009)
            cube('Cab_%s_%s_WindowHorizontalFrame_%s'%(side,label,direction),
                 (cx,window_y,2.506+direction*.29),
                 (win_width,.042,.036),glass_edge,.009)
        cube('Cab_%s_%s_TransparentWindow'%(side,label),
             (cx,window_y+sign*.016,2.506),
             (win_width-.075,.012,.50),glass,.005)
        cube('Cab_%s_%s_DoorHandle'%(side,label),
             (cx-.28,outer+sign*.044,1.955),(.25,.055,.07),metal,.023)
        cube('Cab_%s_%s_DoorStep'%(side,label),
             (cx,sign*1.17,1.06),(1.08,.25,.085),metal_dark,.026)
    for j,door_edge_x in enumerate((.62,1.955,3.35)):
        line('Cab_%s_DoorGap_%d'%(side,j),
             [(door_edge_x,sign*1.133,1.10),(door_edge_x,sign*1.133,2.19)],
             body_dark,.012)
    cube('Cab_%s_WindowCentrePillar'%side,
         (1.94,sign*1.151,2.51),(.055,.042,.61),body,.018)
    cube('Cab_%s_MirrorArm'%side,(3.11,sign*1.23,2.225),(.18,.35,.045),metal_dark,.02)
    cube('Cab_%s_MirrorHousing'%side,(3.19,sign*1.405,2.25),
         (.30,.15,.34),body,.065)
    cube('Cab_%s_MirrorGlass'%side,(3.21,sign*1.494,2.25),
         (.23,.02,.27),metal,.029)

# Seat meshes stay independent so they can be hidden / animated / replaced.
cube('Cab_Interior_Floor',(1.92,0,1.24),(2.42,1.96,.10),interior,.03)
cube('Cab_Dashboard',(3.21,0,1.78),(.30,1.94,.29),interior,.055)
for row,x in (('Front',2.48),('Rear',1.19)):
    for side,y in (('Right',-.525),('Left',.525)):
        base_name='Seat_%s_%s'%(row,side)
        cube(base_name+'_Cushion',(x,y,1.54),(.65,.69,.19),seat_color,.075)
        cube(base_name+'_Backrest',(x-.27,y,1.85),(.19,.68,.69),seat_color,.07)
        cube(base_name+'_Headrest',(x-.28,y,2.27),(.18,.39,.23),seat_accent,.08)
        cube(base_name+'_Pedestal',(x,y,1.38),(.28,.27,.24),metal_dark,.04)
        cube(base_name+'_FrontStitch',(x+.28,y,1.633),(.05,.51,.026),seat_accent,.01)
        # Markers are small transforms, not visible mesh. Facing direction: +X.
        empty('SeatAnchor_%s_%s'%(row,side), (x-.04,y,1.57),.19)

# Steering wheel on the Australian right-hand driver's side.
torus('Steering_Wheel', (2.995,-.525,1.99),.198,.029,metal_dark,(0,math.pi/2,0))
cylinder('Steering_Wheel_Hub',(3.015,-.525,1.99),.071,.07,metal_dark,
         rot=(0,math.pi/2,0))
line('Steering_Wheel_Spoke_A',[(3.026,-.525,1.99),(3.026,-.525,2.165)],metal_dark,.021)
line('Steering_Wheel_Spoke_B',[(3.026,-.525,1.99),(3.026,-.69,1.91)],metal_dark,.021)
line('Steering_Wheel_Spoke_C',[(3.026,-.525,1.99),(3.026,-.36,1.91)],metal_dark,.021)

# Front fascia / bumper / lights, never any text or registration numbers.
cube('Front_Grille_Surround',(3.663,0,1.57),(.065,1.03,.40),metal,.07)
cube('Front_Grille_Centre',(3.704,0,1.57),(.017,.90,.29),metal_dark,.032)
for j in range(3):
    cube('Grille_Slat_%d'%j,(3.716,0,1.47+j*.092),(.020,.85,.018),metal,.005)
cube('Front_Bumper',(3.66,0,1.03),(.25,2.38,.20),metal,.06)
for sign,side in ((-1,'R'),(1,'L')):
    cube('Headlight_%s'%side,(3.682,sign*.85,1.65),(.052,.36,.22),white_lamp,.060)
    cube('Indicator_%s'%side,(3.679,sign*1.015,1.43),(.055,.16,.094),amber_lamp,.025)
    cube('TailLight_%s'%side,(-3.886,sign*1.025,1.27),(.090,.30,.35),red_lamp,.035)
    cube('Tail_Reversing_%s'%side,(-3.945,sign*1.025,1.18),(.024,.16,.09),white_lamp,.018)

# -----------------------------------------------------------------------------
# FOUR CARTOON WHEELS: rounded tires, silver rims, hub bolts, curved guards
# -----------------------------------------------------------------------------
for ax,(front_back,x) in enumerate((('Front',WHEEL_FRONT_X),('Rear',WHEEL_REAR_X))):
    cylinder('%s_Axle'%front_back,(x,0,WHEEL_Z),.17,2.27,metal_dark,
             sides=24,rot=(math.pi/2,0,0))
    for sign,side in ((-1,'Right'),(1,'Left')):
        y=sign*1.178
        cylinder('Tire_%s_%s'%(front_back,side),(x,y,WHEEL_Z),.52,.265,black,
                 sides=40,rot=(math.pi/2,0,0),bevel_amount=.07)
        torus('Tire_RoundedSidewall_%s_%s'%(front_back,side),
              (x,y,WHEEL_Z),.465,.105,black,(math.pi/2,0,0))
        yface=sign*1.335
        cylinder('Wheel_Rim_%s_%s'%(front_back,side),(x,yface,WHEEL_Z),
                 .315,.045,metal,sides=40,rot=(math.pi/2,0,0),bevel_amount=.012)
        torus('Rim_DarkInset_%s_%s'%(front_back,side),(x,yface+sign*.031,WHEEL_Z),
              .242,.022,metal_dark,(math.pi/2,0,0))
        cylinder('Hub_%s_%s'%(front_back,side),
                 (x,yface+sign*.03,WHEEL_Z),.105,.053,metal_dark,sides=24,
                 rot=(math.pi/2,0,0),bevel_amount=.008)
        for k in range(6):
            a=2*math.pi*k/6
            sphere('Bolt_%s_%s_%d'%(front_back,side,k),
                   (x+math.cos(a)*.183,yface+sign*.054,WHEEL_Z+math.sin(a)*.183),
                   (.028,.014,.028),metal,12,6)
        # Wheel-arch visual trim (open bottom) along the truck's side.
        points=[]
        for i in range(17):
            angle=math.pi*i/16
            points.append((x-.645*math.cos(angle),sign*1.344,
                           WHEEL_Z+.625*math.sin(angle)))
        line('Fender_Arch_%s_%s'%(front_back,side),points,body_dark,.033)

# -----------------------------------------------------------------------------
# THE USER'S ACTUAL COUCH GUYS LOGO: transparent, image-textured side decals.
# No generated couch icon or extra text. UV orientation accounts for the fact
# that the two side panels face opposite directions.
# -----------------------------------------------------------------------------
def find_logo_file():
    candidates=[]
    try:
        candidates.append(Path(__file__).resolve().parent/LOGO_FILENAME)
    except (NameError, OSError):
        pass
    if bpy.data.filepath:
        candidates.append(Path(bpy.data.filepath).resolve().parent/LOGO_FILENAME)
    candidates.append(Path(bpy.path.abspath('//')).resolve()/LOGO_FILENAME)
    if OUTPUT_DIRECTORY:
        candidates.append(Path(os.path.expanduser(OUTPUT_DIRECTORY)).resolve()/LOGO_FILENAME)
    for loc in candidates:
        if loc.is_file():
            return loc
    raise FileNotFoundError('Couch Guys logo not found. Put logo.png beside this Python script. Looked in: '+str(candidates))


logo_source = find_logo_file()
logo_img = bpy.data.images.load(str(logo_source), check_existing=True)
logo_img.alpha_mode = 'STRAIGHT'

logo_material = bpy.data.materials.new('CouchGuys_OriginalLogo_TransparentPNG')
logo_material.use_nodes = True
logo_material.diffuse_color = (1, 1, 1, 1)
logo_bsdf = logo_material.node_tree.nodes.get('Principled BSDF')
logo_bsdf.inputs['Metallic'].default_value = 0.0
logo_bsdf.inputs['Roughness'].default_value = .55
logo_texture = logo_material.node_tree.nodes.new('ShaderNodeTexImage')
logo_texture.name = 'Original_CouchGuys_Logo'
logo_texture.image = logo_img
logo_texture.interpolation = 'Linear'
logo_material.node_tree.links.new(logo_texture.outputs['Color'], logo_bsdf.inputs['Base Color'])
logo_material.node_tree.links.new(logo_texture.outputs['Alpha'], logo_bsdf.inputs['Alpha'])
# A slightly illuminated painted logo keeps its colours vivid in shaded views.
if logo_bsdf.inputs.get('Emission Color'):
    logo_material.node_tree.links.new(logo_texture.outputs['Color'], logo_bsdf.inputs['Emission Color'])
elif logo_bsdf.inputs.get('Emission'):
    logo_material.node_tree.links.new(logo_texture.outputs['Color'], logo_bsdf.inputs['Emission'])
if logo_bsdf.inputs.get('Emission Strength'):
    logo_bsdf.inputs['Emission Strength'].default_value = .12
if hasattr(logo_material, 'surface_render_method'):
    try:
        logo_material.surface_render_method = 'BLENDED'
    except (TypeError, ValueError):
        pass
if hasattr(logo_material, 'blend_method'):
    try:
        logo_material.blend_method = 'BLEND'
    except (TypeError, ValueError):
        pass
if hasattr(logo_material, 'show_transparent_back'):
    logo_material.show_transparent_back = False


def image_decal(name, sign, centre_x=-1.52, centre_z=2.64,
                width=3.02, height=1.96):
    xlo, xhi = centre_x-width*.5, centre_x+width*.5
    zlo, zhi = centre_z-height*.5, centre_z+height*.5
    y = sign*1.252  # Cargo side panel outer surface is ~ +/-1.240
    verts = [(xlo,y,zlo), (xhi,y,zlo), (xhi,y,zhi), (xlo,y,zhi)]
    # On -Y the viewer's image-right is +X. On +Y image-right is -X.
    if sign == -1:
        faces = [(0,1,2,3)]
        uvs = [(0,0),(1,0),(1,1),(0,1)]
    else:
        faces = [(3,2,1,0)]
        uvs = [(1,1),(0,1),(0,0),(1,0)]
    mesh = bpy.data.meshes.new(name+'_mesh')
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    mesh.materials.append(logo_material)
    uv_layer=mesh.uv_layers.new(name='LogoUV')
    for loop_idx, uv in zip(mesh.polygons[0].loop_indices, uvs):
        uv_layer.data[loop_idx].uv=uv
    obj=bpy.data.objects.new(name,mesh)
    truck_collection.objects.link(obj)
    obj.parent=root
    return obj


image_decal('CouchGuys_Logo_Decal_Right', -1)
image_decal('CouchGuys_Logo_Decal_Left', 1)

# Pack the supplied image within the .blend file so it travels with the asset.
# It is ALSO kept as a standalone PNG beside the FBX for Unity materials.
try:
    logo_img.pack()
except (RuntimeError, ValueError):
    pass

# -----------------------------------------------------------------------------
# REAR SHUTTER / ROLL-UP DOOR: each slat follows a physical 90° curved track.
# -----------------------------------------------------------------------------

def shutter_path(distance):
    """Arc-length to (x,z,rotY). Tangent is vertical, then rounds, then horizontal."""
    vertical = DOOR_CURVE_START_Z - DOOR_BOTTOM_Z
    quarter_arc = DOOR_CURVE_RADIUS * math.pi*.5
    if distance <= vertical:
        return DOOR_REAR_X, DOOR_BOTTOM_Z+distance, 0.0
    if distance <= vertical+quarter_arc:
        a=(distance-vertical)/DOOR_CURVE_RADIUS
        theta=math.pi-a
        x=DOOR_REAR_X+DOOR_CURVE_RADIUS+DOOR_CURVE_RADIUS*math.cos(theta)
        z=DOOR_CURVE_START_Z+DOOR_CURVE_RADIUS*math.sin(theta)
        return x,z,a
    overhead=distance-vertical-quarter_arc
    return DOOR_REAR_X+DOOR_CURVE_RADIUS+overhead, DOOR_CURVE_START_Z+DOOR_CURVE_RADIUS, math.pi/2


def ease(t):
    t=max(0.0,min(1.0,t))
    return t*t*(3-2*t)


# Blender/Unity-compatible animation uses ONE armature with two NLA actions.
# Every door slat has 100% rigid influence from a single bone. This prevents
# multiple unrelated mesh actions being lost during FBX export/import.

# Side guide track meshes follow the same vertical/curve/horizontal journey.
for sign, side in ((-1,'Right'),(1,'Left')):
    pts=[]
    path_max=(DOOR_CURVE_START_Z-DOOR_BOTTOM_Z)+DOOR_CURVE_RADIUS*math.pi/2+2.63
    for i in range(56):
        x,z,_=shutter_path(i*path_max/55)
        pts.append((x,sign*1.119,z))
    line('RollerDoor_%s_RunningTrack'%side,pts,metal_dark,.040)
    cube('RollerDoor_%s_LowerStop'%side,
         (DOOR_REAR_X,sign*1.12,1.21),(.13,.12,.09),metal,.02)

# A real armature is the most dependable way to produce reusable FBX clips.
# All armature data is local to the truck root (+X front / +Z up).
arm_data=bpy.data.armatures.new('RollerDoor_ArmatureData')
armature=bpy.data.objects.new('RollerDoor_Rig', arm_data)
truck_collection.objects.link(armature)
armature.parent=root
armature.show_in_front=False
arm_data.display_type='STICK'

bpy.ops.object.select_all(action='DESELECT')
armature.select_set(True)
bpy.context.view_layer.objects.active=armature
bpy.ops.object.mode_set(mode='EDIT')
for i in range(DOOR_SLAT_COUNT):
    base_z=DOOR_BOTTOM_Z+(i+.5)*DOOR_SLAT_HEIGHT
    bone=arm_data.edit_bones.new('RollerDoor_Bone_%02d'%i)
    bone.head=(DOOR_REAR_X,0,base_z)
    bone.tail=(DOOR_REAR_X,0,base_z+.12)
    bone.use_deform=True
bpy.ops.object.mode_set(mode='OBJECT')


def rigid_skin_to_bone(obj, bone_name):
    """Bake the mesh's rest placement into vertices, then skin it rigidly.

    Keeping vertex positions in armature (truck) coordinates avoids any
    ambiguity from object parenting and makes each bone turn around its slat.
    """
    bpy.context.view_layer.update()
    original_world=obj.matrix_world.copy()
    obj.parent=None
    obj.matrix_world=original_world
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active=obj
    bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
    # The truck root is at world origin, but keep model hierarchy in Unity.
    obj.parent=root
    vertex_group=obj.vertex_groups.new(name=bone_name)
    vertex_group.add(list(range(len(obj.data.vertices))), 1.0, 'REPLACE')
    skin=obj.modifiers.new('Animated_RollerDoor_Skin', 'ARMATURE')
    skin.object=armature
    skin.use_deform_preserve_volume=False
    return obj


slats=[]
for i in range(DOOR_SLAT_COUNT):
    base_z=DOOR_BOTTOM_Z+(i+.5)*DOOR_SLAT_HEIGHT
    bone_name='RollerDoor_Bone_%02d'%i
    slat=cube('RollerDoor_Slat_%02d'%i,
              (DOOR_REAR_X,0,base_z),
              (.061,DOOR_WIDTH,DOOR_SLAT_HEIGHT-.004),metal,.017)
    # Rib is a separate mesh sharing the exact same rigid bone. That keeps the
    # visual detail fixed to its slat through the entire curved animation.
    slat_detail=cube('RollerDoor_Slat_%02d_DecorativeRib'%i,
                     (DOOR_REAR_X-.034,0,base_z+.035),
                     (.020,DOOR_WIDTH-.078,.018),metal_dark,.006)
    rigid_skin_to_bone(slat,bone_name)
    rigid_skin_to_bone(slat_detail,bone_name)
    slats.append(slat)


def set_rig_pose(progress):
    """Set all 15 armature bones along the roller-door's curved track."""
    progress=ease(progress)
    vertical=DOOR_CURVE_START_Z-DOOR_BOTTOM_Z
    quarter_arc=DOOR_CURVE_RADIUS*math.pi*.5
    displacement=vertical+quarter_arc+.25-(DOOR_SLAT_HEIGHT*.5)
    for i in range(DOOR_SLAT_COUNT):
        base_z=DOOR_BOTTOM_Z+(i+.5)*DOOR_SLAT_HEIGHT
        path_dist=(i+.5)*DOOR_SLAT_HEIGHT+progress*displacement
        x,z,turn_y=shutter_path(path_dist)
        pose_bone=armature.pose.bones['RollerDoor_Bone_%02d'%i]
        rest_orientation=pose_bone.bone.matrix_local.to_quaternion()
        # Blender's pose transforms are bone-LOCAL, so transform the target
        # delta/rotation out of world-aligned armature space into bone axes.
        delta=Vector((x-DOOR_REAR_X,0,z-base_z))
        pose_bone.rotation_mode='QUATERNION'
        pose_bone.location=rest_orientation.inverted() @ delta
        world_turn=Quaternion((0,1,0),turn_y)
        pose_bone.rotation_quaternion=(rest_orientation.inverted()
                                       @ world_turn @ rest_orientation)


# A direct active action, not a stack of NLA strips, is crucial here:
# the previous version cleared animation_data.action after writing the strips,
# so Blender's Action Editor could appear EMPTY and Unity often showed no clip.
# These actions are constructed by keyframe_insert() itself. Blender therefore
# creates/binds the correct Action SLOT automatically in Blender 4.4+, while
# also retaining compatibility with Blender 3.6–4.3 (which lack Action slots).


def create_door_action(name, last_frame, progress_at_frame):
    armature.animation_data_create()
    armature.animation_data.action = None
    for frame in range(1, last_frame+1):
        set_rig_pose(progress_at_frame(frame))
        for pose_bone in armature.pose.bones:
            pose_bone.keyframe_insert(data_path='location', frame=frame,
                                      group=pose_bone.name)
            pose_bone.keyframe_insert(data_path='rotation_quaternion', frame=frame,
                                      group=pose_bone.name)
    action=armature.animation_data.action
    if action is None:
        raise RuntimeError('Unable to create Blender Action for '+name)
    action.name = name
    action.use_fake_user = True
    # The generated action automatically owns an armature-compatible slot.
    # After unassigning, that slot must be restored when the action is reused.
    slot=getattr(armature.animation_data, 'action_slot', None)
    armature.animation_data.action=None
    return action,slot


def open_progress(frame):
    return (frame-1)/60.0


def close_progress(frame):
    return 1.0-(frame-1)/60.0


def cycle_progress(frame):
    if frame<=OPEN_END:
        return (frame-OPEN_BEGIN)/float(OPEN_END-OPEN_BEGIN)
    if frame<CLOSE_BEGIN:
        return 1.0
    return 1.0-(frame-CLOSE_BEGIN)/float(CLOSE_END-CLOSE_BEGIN)


open_action,open_slot=create_door_action('RollerDoor_Open',61,open_progress)
close_action,close_slot=create_door_action('RollerDoor_Close',61,close_progress)
cycle_action,cycle_slot=create_door_action('RollerDoor_FullCycle',141,cycle_progress)


def activate_door_action(action,slot=None):
    anim=armature.animation_data
    anim.action=action
    if slot is not None and hasattr(anim,'action_slot'):
        anim.action_slot=slot
    anim.use_nla=False  # No NLA can silently override an active action.


# Make the complete open/pause/close animation visible BY DEFAULT in Blender.
activate_door_action(cycle_action,cycle_slot)

for name,fr in [('OPEN_START',OPEN_BEGIN),('OPEN_END',OPEN_END),
                ('CLOSE_START',CLOSE_BEGIN),('CLOSE_END',CLOSE_END)]:
    scene.timeline_markers.new(name,frame=fr)


# Verify the *evaluated armature* moves with the action actually active.
def sample_rear_bone_position(frame):
    scene.frame_set(frame)
    bpy.context.view_layer.update()
    return armature.pose.bones['RollerDoor_Bone_00'].matrix.translation.copy()


def assert_action_moves(action,slot,first,last,expected_same=False):
    activate_door_action(action,slot)
    p0=sample_rear_bone_position(first)
    p1=sample_rear_bone_position(last)
    diff=(p1-p0).length
    if (diff<.5 and not expected_same) or (diff>.025 and expected_same):
        raise RuntimeError('Animation validation failed for %s: '
                           'bone movement=%0.4f m'%(action.name,diff))
    print('Validated Action %s: %.3f m bone movement'%(action.name,diff))


assert_action_moves(open_action,open_slot,1,61)
assert_action_moves(close_action,close_slot,1,61)
assert_action_moves(cycle_action,cycle_slot,1,61)
closed=sample_rear_bone_position(1)
reclosed=sample_rear_bone_position(141)
if (closed-reclosed).length>.025:
    raise RuntimeError('FullCycle does not finish closed!')
activate_door_action(cycle_action,cycle_slot)
scene.frame_set(1)

# Rear opening, loading and occupancy markers — gameplay systems use these.
empty('Cargo_LoadPoint_Rear',(-4.23,0,1.32),.30)
empty('Cargo_CouchAnchor_Front',(-1.22,0,1.34),.32)
empty('Cargo_CouchAnchor_Rear',(-2.75,0,1.34),.32)
empty('Vehicle_Exit_Right',(1.25,-1.65,.95),.20)
empty('Vehicle_Exit_Left',(1.25,1.65,.95),.20)
empty('RollerDoor_InteractionPoint',(-4.36,-.8,1.65),.19)
empty('Camera_ChaseMount',(-6.20,0,3.38),.24)
empty('Vehicle_CentreOfMass',(-.10,0,1.05),.15)

# Optional static ramp for screenshots or a Unity interactive ramp prefab.
# This is a separate mesh, not part of the automatic door animation.
if ENABLE_LOADING_RAMP:
    ramp=cube('OPTIONAL_LoadingRamp_Deployed',(-4.75,0,.76),
              (1.69,2.16,.075),metal,.03)
    ramp.rotation_euler[1]=-.29
    for j in range(9):
        rib=cube('OPTIONAL_Ramp_GripStrip_%02d'%j,
                 (-5.42+j*.15,0,.0),(.022,1.99,.012),metal_dark,.003,
                 parent=ramp)
        rib.location=( -.72+j*.18,0,.047 )

# -----------------------------------------------------------------------------
# NO STUDIO OBJECTS: this .blend intentionally contains just the actual truck,
# its armature and gameplay anchor empties. Never generate preview geometry.
# -----------------------------------------------------------------------------

# -----------------------------------------------------------------------------
# OUTPUTS — full-cycle .blend; full-cycle+Open+Close FBX takes;
# plus standalone Open and Close FBXs for reliable Unity clip discovery.
# -----------------------------------------------------------------------------

# Select only the actual vehicle/rig; never export lights, cameras or previews.
def select_truck_for_export():
    bpy.ops.object.select_all(action='DESELECT')
    for o in truck_collection.objects:
        o.select_set(True)
    bpy.context.view_layer.objects.active=armature


select_truck_for_export()
activate_door_action(cycle_action,cycle_slot)
scene.frame_start,scene.frame_end=1,141
scene.frame_set(1)

# Blender Animation workspace: show the actual action curves, not an empty
# NLA/Dope Sheet context. Select the rig at the end instead of the root empty.
for screen in bpy.data.screens:
    if screen.name == 'Animation':
        for area in screen.areas:
            if area.type == 'DOPESHEET_EDITOR':
                try:
                    area.spaces.active.mode='ACTION'
                except Exception:
                    pass

if OUTPUT_DIRECTORY:
    out=Path(os.path.expanduser(OUTPUT_DIRECTORY)).resolve()
else:
    try:
        out=Path(__file__).resolve().parent
    except (NameError, OSError):
        out=Path(bpy.path.abspath('//')).resolve()
out.mkdir(parents=True,exist_ok=True)
logo_dest=out/LOGO_FILENAME
if logo_dest.resolve()!=logo_source.resolve():
    shutil.copy2(str(logo_source),str(logo_dest))
blend_path=out/'CouchGuys_DeliveryTruck.blend'
fbx_path=out/'CouchGuys_DeliveryTruck.fbx'

if SAVE_BLEND:
    bpy.ops.wm.save_as_mainfile(filepath=str(blend_path))
    print('Saved Blender file with ACTIVE action:',blend_path)


def export_fbx(filepath,all_actions):
    select_truck_for_export()
    bpy.ops.export_scene.fbx(
        filepath=str(filepath),
        use_selection=True,
        object_types={'MESH','EMPTY','ARMATURE'},
        use_mesh_modifiers=True,
        bake_anim=True,
        bake_anim_use_all_actions=all_actions,
        bake_anim_use_nla_strips=False,
        bake_anim_force_startend_keying=True,
        bake_anim_step=1.0,
        bake_anim_simplify_factor=0.0,
        axis_forward='-Z',
        axis_up='Y',
        apply_unit_scale=True,
        add_leaf_bones=False,
        path_mode='COPY',
        embed_textures=True,
    )
    if not filepath.is_file() or filepath.stat().st_size<1000:
        raise RuntimeError('FBX missing or empty after export: '+str(filepath))
    print('Exported:',filepath, '(%d bytes)'%filepath.stat().st_size)


if EXPORT_FBX:
    # Contains the active FullCycle and all compatible rig Actions.
    activate_door_action(cycle_action,cycle_slot)
    scene.frame_start,scene.frame_end=1,141
    scene.frame_set(1)
    export_fbx(fbx_path, all_actions=True)

    # Guaranteed, separate single-action clips. These use the SAME bone names.
    # Even importers that ignore all-actions can recognise these FBX takes.
    for action,slot,suffix in (
            (open_action,open_slot,'RollerDoor_Open'),
            (close_action,close_slot,'RollerDoor_Close')):
        activate_door_action(action,slot)
        scene.frame_start,scene.frame_end=1,61
        scene.frame_set(1)
        export_fbx(out/('CouchGuys_DeliveryTruck_'+suffix+'.fbx'),all_actions=False)

# Restore the actual truck Blender scene and the active FullCycle action.
activate_door_action(cycle_action,cycle_slot)
scene.frame_start,scene.frame_end=1,141
scene.frame_set(1)
select_truck_for_export()
# Keep only the armature selected in the viewport, so Action Editor ALWAYS
# has a clear, selected animated object immediately after script completion.
for o in truck_collection.objects:
    if o!=armature:
        o.select_set(False)
armature.select_set(True)
bpy.context.view_layer.objects.active=armature

print('DONE: Truck has 3 Blender Actions: RollerDoor_FullCycle, Open, Close.')
print('Blender: select RollerDoor_Rig, Action Editor, scrub frames 1..141.')
print('Unity: import main FBX or separate Open/Close FBXs as clip sources.')
print('No studio floor, cameras, lights or preview meshes are present.')
print('Four seat markers, cargo load markers and the original Couch Guys logo are retained.')
