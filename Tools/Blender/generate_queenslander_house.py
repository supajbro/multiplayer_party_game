"""Build the additive Queenslander variant. Explicit offline authoring; never edits small_house_01.

blender --background --python Tools/Blender/generate_queenslander_house.py
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
bpy.ops.import_scene.fbx(filepath=str(OUTPUT / 'MediumHouses/medium_house_01.fbx'))
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
roof = roof.copy()
roof.name = 'Queenslander_SlateRoof'
roof.diffuse_color = (.16, .23, .30, 1)
if roof.use_nodes:
    next(n for n in roof.node_tree.nodes if n.type == 'BSDF_PRINCIPLED').inputs['Base Color'].default_value = (.16,.23,.30,1)
door = shutter
door_inset = door
stone = concrete
garage = trim
root = empty('small_house_02')
root['generator'] = 'Couch Guys Queenslander Easy House v1'
root['seed'] = 2003
root['floors'] = 1
root['forward_axis'] = '-Y'
root['has_garage'] = True

def cube(name, xyz, size, mat, bevel=0.025):
    return box(name, xyz, size, mat, root, bevel)

cube('Foundation_Main', (0, 0, .12), (10.6, 6, .24), concrete)
cube('Structure_Main', (-1.7, 0, 1.59), (7.2, 5.9, 2.7), wall)
cube('Structure_Garage', (3.6, 0, 1.49), (3.4, 5.9, 2.5), wall)

def hip(name, cx, cy, width, depth, eave, peak):
    x0,x1=cx-width/2,cx+width/2
    y0,y1=cy-depth/2,cy+depth/2
    v=[(x0,y0,eave),(x1,y0,eave),(x1,y1,eave),(x0,y1,eave),
       (cx,y0+width*.36,peak),(cx,y1-width*.36,peak)]
    return mesh(name,v,[(0,1,4),(1,2,5,4),(2,3,5),(3,0,4,5),(3,2,1,0)],roof,root,.015)

hip('Roof_Main',-1.7,-.55,7.8,7.9,2.94,4.12)
hip('Roof_Garage',3.6,0,3.85,6.5,2.74,3.35)
cube('Roof_Fascia_Main',(-1.7,-4.52,2.94),(7.8,.13,.18),trim)
cube('Roof_Fascia_Garage',(3.6,-3.27,2.74),(3.85,.13,.16),trim)
# Sparse roof seams are removed first by the existing combined-LOD pipeline.
for i in range(5):
    y=-1.5+i*.5
    for side in (-1,1):
        r=cube('Detail_RoofSeam_%s_%s'%(side,i),
               (-1.7+side*1.95,y,3.54),(4.08,.024,.03),roof,.003)
        r.rotation_euler.y=side*math.atan2(1.18,3.9)
cube('FrontDoor_Frame', (-.45,-3.01,1.35), (1.25,.16,2.25), trim)
cube('FrontDoor_Plain', (-.45,-3.11,1.31), (1.03,.08,2.12), door)
cube('Detail_DoorHandle', (-.08,-3.18,1.22), (.08,.07,.16), dark,.008)
cube('Porch_Slab', (-1.7,-3.65,.12), (7.2,1.4,.24), concrete)

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

# Broad verandah, paired fretwork brackets, open entry and spaced balusters.
cube('Verandah_Beam',(-1.7,-4.25,2.73),(7.2,.17,.23),trim)
for i,x in enumerate((-5.12,-2.65,.95)):
    cube('Structure_VerandahPost_%s'%i,(x,-4.23,1.47),(.16,.18,2.46),trim,.015)
    cube('Verandah_PostBase_%s'%i,(x,-4.23,.36),(.25,.26,.24),trim,.015)
    cube('Verandah_PostCapital_%s'%i,(x,-4.23,2.52),(.29,.28,.18),trim,.015)
    for side in (-1,1):
        r=cube('Detail_FretBracket_%s_%s'%(i,side),(x+side*.22,-4.23,2.41),(.61,.1,.09),trim,.008)
        r.rotation_euler.y=-side*.78
for name,cx,width in (('Left',-3.15,3.8),('Right',.73,.44)):
    cube('Structure_VerandahRail_'+name,(cx,-4.23,.95),(width,.14,.11),trim,.012)
    cube('Verandah_RailBase_'+name,(cx,-4.23,.36),(width,.1,.09),trim,.008)
    for i in range(max(2,int(width/.23))):
        x=cx-width/2+.10+i*(width-.20)/max(1,int(width/.23)-1)
        cube('Detail_Baluster_%s_%s'%(name,i),(x,-4.23,.65),(.065,.075,.54),trim,.006)
for i in range(28):
    cube('Detail_FretFringe_%02d'%i,(-5.03+i*.214,-4.25,2.61),(.06,.07,.18),trim,.004)
cube('Window_Sill_Front',(-3.4,-3.18,.99),(1.9,.37,.13),trim,.02)
cube('Window_Lintel_Front',(-3.4,-3.115,2.34),(1.85,.23,.12),trim,.015)
anchor=empty('FrontDoorAnchor',root)
anchor.location=(-.45,-4.72,.16)

# Simple recessed door panels and a small wall lantern, as on the reference.
for z,h in ((.80,.62),(1.68,.84)):
    cube('Detail_DoorPanelFrame_%s'%z,(-.45,-3.163,z),(.78,.035,h),door_inset,.018)
    cube('Detail_DoorPanel_%s'%z,(-.45,-3.188,z),(.65,.025,h-.13),door,.015)

cube('Detail_LanternBackplate',(.53,-3.075,2.03),(.22,.08,.43),dark,.018)
cube('Detail_LanternGlass',(.53,-3.24,2.05),(.19,.20,.29),glow,.018)
for x in (.405,.655):
    cube('Detail_LanternFrame_%s'%x,(x,-3.25,2.05),(.035,.25,.36),dark,.006)
for z in (1.86,2.24):
    cube('Detail_LanternCap_%s'%z,(.53,-3.25,z),(.32,.31,.065),dark,.012)

# Same module names and ordering as House 01: 00 is nearest the building.
for i in range(4):
    cube('Driveway_Slab_%02d'%i,(3.6,-4.45-i*1.1,.07),(2.95,1.04,.14),concrete,.015)
cube('Garage_Apron',(3.6,-3.5,.07),(2.95,1.0,.14),concrete,.015)
for i in range(5):
    cube('Front_Path_Step_%02d'%i,(-.45,-4.85-i*.65,.07),(1.45,.60,.14),concrete,.012)
cube('PathLight_Post_00',(-1.4,-4.25,.33),(.10,.10,.66),dark,.01)
cube('PathLight_Glow_00',(-1.4,-4.25,.69),(.19,.19,.16),glow,.015)
cube('PathLight_Cap_00',(-1.4,-4.25,.80),(.25,.25,.07),dark,.01)
for i,x in enumerate((-4.5,-2.25)):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1,radius=.46,location=(x,-4.65,.42))
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
bpy.ops.wm.save_as_mainfile(filepath=str(OUTPUT/'SmallHouses/small_house_02.blend'))
bpy.ops.export_scene.fbx(filepath=str(OUTPUT/'SmallHouses/small_house_02.fbx'),use_selection=True,
    object_types={'MESH','EMPTY'},apply_unit_scale=True,axis_forward='-Z',axis_up='Y',
    use_mesh_modifiers=True,bake_anim=False,add_leaf_bones=False)
print('HOUSE02_BUILD_OK',len(root.children_recursive),'objects',
      sum(len(o.data.polygons) for o in root.children_recursive if o.type=='MESH'),'polygons')

