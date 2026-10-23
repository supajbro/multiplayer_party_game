"""Build House 02 with the existing cartoon house mesh utilities and House 01 palette.

blender --background --python Tools/Blender/generate_easy_delivery_house.py
Front is Blender -Y (Unity +Z). Unity builds four combined mesh LODs on import.
"""
import bpy
import math
import runpy
from pathlib import Path

PROJECT = Path(__file__).resolve().parents[2]
OUTPUT = PROJECT / 'Assets/_Project/Models/Houses'
utils = runpy.run_path(str(Path(__file__).with_name('generate_cartoon_house.py')))
box = utils['create_box']
mesh = utils['create_mesh']
empty = utils['make_empty']

# Background build, never edits the reference file or the user's open Blender scene.
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.fbx(filepath=str(OUTPUT / 'CouchGuys_House_01.fbx'))
palette = {m.name: m for m in bpy.data.materials}
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
print('Reference palette:', sorted(palette))

def material(fragment):
    return next(m for n, m in palette.items() if fragment.lower() in n.lower())

wall = material('Siding_WarmGrey')
trim = material('Trim')
roof = material('Roof_DarkBrownGrey')
concrete = material('Concrete')
door = material('Door_WarmWood')
garage = material('Garage_OffWhite')
glass = material('Window')
dark = material('Hardware')
leaf = material('Leaf_Green')
glow = material('Lantern_Glow')
siding_shadow = material('Siding_Shadow')
stone = material('Stone_LightGrey')
shutter = material('Shutter_BlueGrey')
door_inset = material('Door_Inset')
root = empty('CouchGuys_House_02')
root['generator'] = 'Couch Guys Easy Delivery House v1'
root['seed'] = 2002
root['floors'] = 1
root['forward_axis'] = '-Y'
root['has_garage'] = True

def cube(name, xyz, size, mat, bevel=0.025):
    return box(name, xyz, size, mat, root, bevel)

cube('Foundation_Main', (0, 0, .12), (10.6, 6, .24), concrete)
cube('Structure_Main', (-1.7, 0, 1.59), (7.2, 5.9, 2.7), wall)
cube('Structure_Garage', (3.6, 0, 1.49), (3.4, 5.9, 2.5), wall)

def gable(name, cx, width, depth, eave, peak):
    x0, x1 = cx-width/2, cx+width/2
    y0, y1 = -depth/2, depth/2
    verts = [(x0,y0,eave),(x1,y0,eave),(cx,y0,peak),
             (x0,y1,eave),(x1,y1,eave),(cx,y1,peak)]
    faces = [(0,2,1),(3,4,5),(0,1,4,3),(0,3,5,2),(1,2,5,4)]
    return mesh(name, verts, [tuple(reversed(f)) for f in faces], roof, root, .025)

gable('Roof_Main', -1.7, 7.8, 6.5, 2.94, 4.12)
gable('Roof_Garage', 3.6, 3.85, 6.5, 2.74, 3.35)
# Plain inexpensive sheet roofing, with sparse raised standing seams.
for side in (-1, 1):
    for i in range(1, 8):
        y = -3.25 + i*.81
        r = cube('Detail_RoofSeam_%s_%02d' % (side,i),
                 (-1.7+side*1.95,y,3.55), (4.08,.025,.035), roof, .006)
        r.rotation_euler.y = side*math.atan2(1.18,3.9)
cube('Roof_Fascia_Main', (-1.7,-3.27,2.94), (7.8,.12,.16), trim)
cube('Roof_Fascia_Garage', (3.6,-3.27,2.74), (3.85,.12,.16), trim)
cube('FrontDoor_Frame', (-.45,-3.01,1.35), (1.25,.16,2.25), trim)
cube('FrontDoor_Plain', (-.45,-3.11,1.31), (1.03,.08,2.12), door)
cube('Detail_DoorHandle', (-.08,-3.18,1.22), (.08,.07,.16), dark,.008)
cube('Porch_Slab', (-.45,-3.48,.12), (1.85,1.05,.24), concrete)
cube('Entrance_Awning', (-.45,-3.43,2.58), (1.9,1.1,.13), roof)
cube('GarageDoor_Frame', (3.6,-3.02,1.35), (2.95,.16,2.35), trim)
cube('GarageDoor_Plain', (3.6,-3.12,1.31), (2.65,.08,2.16), garage)
for i in range(1,7):
    cube('Detail_GarageSlat_%02d'%i,(3.6,-3.17,.26+i*.30),(2.6,.025,.025),concrete,.004)
cube('Detail_GarageHandle',(3.6,-3.2,.75),(.4,.05,.07),dark,.008)

def window(name, x, y, z, side=False):
    frame=cube('Window_Frame_'+name,(x,y,z),(1.65,.16,1.25),trim)
    pane=cube('Window_Glass_'+name,(x,y-.10,z),(1.4,.055,1.01),glass)
    bar=cube('Detail_WindowMullion_'+name,(x,y-.15,z),(.07,.05,1.03),trim,.008)
    if side:
        for o in (frame,pane,bar):
            dx,dy=o.location.x-x,o.location.y-y
            o.location.x=x+dy
            o.location.y=y-dx
            o.rotation_euler.z=-math.pi/2

window('Front',-3.4,-3.02,1.65)
window('Left',-5.32,1.0,1.65,True)
window('Rear',-2,3.18,1.65)

# Match House 01's layered construction: weatherboards, a masonry skirt and
# substantial joinery. These are decorative meshes, never additional colliders.
for section, cx, width, height in (('Main',-1.7,7.2,2.7),('Garage',3.6,3.4,2.5)):
    for row in range(11):
        z = .70 + row*.205
        if z > height+.10:
            continue
        for face, y in (('Front',-2.978),('Rear',2.978)):
            cube('Detail_Siding_%s_%s_%02d'%(section,face,row),
                 (cx,y,z),(width,.075,.195),wall,.009)
    for x in (cx-width/2+.07,cx+width/2-.07):
        board_height=height-.06
        cube('CornerBoard_%s_%s'%(section,x),(x,-3.035,.29+board_height/2),
             (.14,.15,board_height),trim,.018)
    cube('Wall_Skirt_'+section,(cx,-3.005,.44),(width,.15,.40),stone,.018)
    cube('Wall_SkirtCap_'+section,(cx,-3.055,.68),(width,.20,.085),trim,.012)
    # A few staggered mortar joints give the skirt scale without dense brickwork.
    for row in range(2):
        for i in range(int(width/.85)):
            x=cx-width/2+.45+i*.85+(row%2)*.3
            if x < cx+width/2-.1:
                cube('Detail_Mortar_%s_%s_%s'%(section,row,i),
                     (x,-3.087,.34+row*.18),(.018,.012,.15),concrete,.002)

for side,x in (('Left',-5.328),('Right',5.328)):
    for row in range(11):
        if side == 'Right' and row == 10:
            continue
        cube('Detail_Siding_%s_%02d'%(side,row),(x,0,.70+row*.205),
             (.075,5.9,.195),wall,.009)
    cube('Wall_Skirt_'+side,(x,0,.44),(.15,5.9,.40),stone,.018)
    cube('Wall_SkirtCap_'+side,(x,0,.68),(.20,5.9,.085),trim,.012)
    for y in (-2.88,2.88):
        board_height=2.64 if side == 'Left' else 2.44
        cube('CornerBoard_%s_%s'%(side,y),(x,y,.29+board_height/2),
             (.15,.14,board_height),trim,.018)

# Front window: deep sill, lintel and modest painted louvred shutters.
cube('Window_Sill_Front',(-3.4,-3.18,.99),(1.90,.37,.13),trim,.022)
cube('Window_Lintel_Front',(-3.4,-3.115,2.34),(1.85,.23,.12),trim,.018)
for side in (-1,1):
    x=-3.4+side*1.04
    cube('Window_Shutter_Front_%s'%side,(x,-3.105,1.65),(.31,.13,1.24),shutter,.018)
    for row in range(7):
        cube('Detail_ShutterLouvre_%s_%02d'%(side,row),(x,-3.19,1.17+row*.16),
             (.27,.065,.065),shutter,.009)
cube('Window_Sill_Left',(-5.47,1.0,.99),(.37,1.9,.13),trim,.022)

# Simple recessed door panels and a small wall lantern, as on the reference.
for z,h in ((.80,.62),(1.68,.84)):
    cube('Detail_DoorPanelFrame_%s'%z,(-.45,-3.163,z),(.78,.035,h),door_inset,.018)
    cube('Detail_DoorPanel_%s'%z,(-.45,-3.188,z),(.65,.025,h-.13),door,.015)
for x in (-1.17,.27):
    cube('Detail_AwningBracket_%s'%x,(x,-3.22,2.40),(.10,.45,.21),trim,.015)
cube('Detail_LanternBackplate',(.53,-3.075,2.03),(.22,.08,.43),dark,.018)
cube('Detail_LanternGlass',(.53,-3.24,2.05),(.19,.20,.29),glow,.018)
for x in (.405,.655):
    cube('Detail_LanternFrame_%s'%x,(x,-3.25,2.05),(.035,.25,.36),dark,.006)
for z in (1.86,2.24):
    cube('Detail_LanternCap_%s'%z,(.53,-3.25,z),(.32,.31,.065),dark,.012)

# Gable trim and a restrained vent break up the otherwise blank roof face.
for side in (-1,1):
    r=cube('Gable_Trim_%s'%side,(-1.7+side*1.95,-3.285,3.53),
           (4.08,.14,.12),trim,.015)
    r.rotation_euler.y=side*math.atan2(1.18,3.9)
cube('Detail_GableVent_Frame',(-1.7,-3.285,3.43),(.65,.09,.34),trim,.018)
for row in range(3):
    cube('Detail_GableVent_%02d'%row,(-1.7,-3.345,3.32+row*.095),
         (.48,.035,.038),siding_shadow,.005)

# Same module names and ordering as House 01: 00 is nearest the building.
for i in range(4):
    cube('Driveway_Slab_%02d'%i,(3.6,-4.45-i*1.1,.07),(2.95,1.04,.14),concrete,.015)
cube('Garage_Apron',(3.6,-3.5,.07),(2.95,1.0,.14),concrete,.015)
for i in range(5):
    cube('Front_Path_Step_%02d'%i,(-.45,-4.25-i*.65,.07),(1.45,.60,.14),concrete,.012)
cube('PathLight_Post_00',(-1.4,-4.25,.33),(.10,.10,.66),dark,.01)
cube('PathLight_Glow_00',(-1.4,-4.25,.69),(.19,.19,.16),glow,.015)
cube('PathLight_Cap_00',(-1.4,-4.25,.80),(.25,.25,.07),dark,.01)
for i,x in enumerate((-4.5,-2.25)):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1,radius=.46,location=(x,-3.5,.42))
    shrub=bpy.context.object
    shrub.name='Detail_Shrub_%02d'%i
    shrub.scale=(1,.7,.8)
    shrub.data.materials.append(leaf)
    shrub.parent=root

# Apply bevels once, keeping Unity colliders simple boxes on named structural meshes.
for obj in list(root.children_recursive):
    if obj.type == 'MESH':
        bpy.context.view_layer.objects.active=obj
        for modifier in list(obj.modifiers):
            bpy.ops.object.modifier_apply(modifier=modifier.name)
bpy.ops.object.select_all(action='DESELECT')
root.select_set(True)
for obj in root.children_recursive:
    obj.select_set(True)
bpy.context.view_layer.objects.active=root
bpy.context.preferences.filepaths.save_version = 0
bpy.ops.wm.save_as_mainfile(filepath=str(OUTPUT/'CouchGuys_House_02.blend'))
bpy.ops.export_scene.fbx(filepath=str(OUTPUT/'CouchGuys_House_02.fbx'),use_selection=True,
    object_types={'MESH','EMPTY'},apply_unit_scale=True,axis_forward='-Z',axis_up='Y',
    use_mesh_modifiers=True,bake_anim=False,add_leaf_bones=False)
print('HOUSE02_BUILD_OK',len(root.children_recursive),'objects',
      sum(len(o.data.polygons) for o in root.children_recursive if o.type=='MESH'),'polygons')
