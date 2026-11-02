"""Explicit offline authoring: blender --background --python Tools/Blender/generate_medium_house_02.py.
Front is -Y; units, garage and modular access match measured medium_house_01.
Three independently authored LODs, joined by material; never edits the reference house.
"""
import bpy, math, random, runpy, json
from pathlib import Path
from mathutils import Vector

P = Path(__file__).resolve().parents[2]
OUT = P/'Assets/_Project/Models/Houses/MediumHouses'
OUT.mkdir(exist_ok=True)
U = runpy.run_path(str(Path(__file__).with_name('generate_cartoon_house.py')))
bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete(use_global=False)
palette = {}
def mat(name, rgb):
    m = bpy.data.materials.new('MH02_'+name); m.diffuse_color=(*rgb,1); m.use_nodes=True
    bsdf=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
    bsdf.inputs['Base Color'].default_value=(*rgb,1); bsdf.inputs['Roughness'].default_value=.78
    palette[name]=m
    return m
cream=mat('Cream',(.83,.75,.62)); stone=mat('Limestone',(.60,.55,.46))
stone2=mat('LimestoneLight',(.74,.68,.57)); timber=mat('Timber',(.48,.215,.065))
dark=mat('Charcoal',(.075,.085,.105)); glass=mat('Glass',(.18,.29,.34))
concrete=mat('Concrete',(.52,.50,.46)); garage=mat('Garage',(.70,.68,.63))
leaf=mat('Leaf',(.17,.38,.07)); glow=mat('WarmLight',(1,.63,.19))
root=U['make_empty']('medium_house_02')
root['forward_axis']='-Y';root['reference_bounds']='17.6954 x 15.0403 x 8.2604 metres including access modules'
lod_roots=[]; totals=[]
def build(level):
    rng=random.Random(202)
    parent=U['make_empty']('LOD'+str(level),root); lod_roots.append(parent)
    def box(name, xyz, size, material, bevel=.025):
        coarse_bevel = name in {'LeftWing','StoneTower','MainRear','EntranceRear','GarageBody','Upper','UpperTimber','PorchSlab','PorchCanopy','PorchSoffit','PorchPost','DoorFrame','FrontDoor','GarageFrame','GarageDoor','EntryStep','RearPatio','GardenBed'}
        return U['create_box'](name,xyz,size,material,parent,bevel if level<2 or coarse_bevel else 0)
    def roof(name,x0,x1,y0,y1,z0,z1,thick=.21,material=dark):
        # A true solid skillion, falling left to right as in the concept.
        verts=[(x0,y0,z0),(x1,y0,z1),(x1,y1,z1),(x0,y1,z0),
               (x0,y0,z0-thick),(x1,y0,z1-thick),(x1,y1,z1-thick),(x0,y1,z0-thick)]
        return U['create_mesh'](name,verts,[(0,1,2,3),(7,6,5,4),(4,5,1,0),(5,6,2,1),(6,7,3,2),(7,4,0,3)],material,parent)
    # Recess the entry rather than covering the door with a continuous front wall.
    box('LeftWing',(-6.15,.05,1.62),(3.55,5.9,3.24),cream)
    box('StoneTower',(-2.7,-.05,3.4),(3.4,6.1,6.8),stone)
    box('MainRear',(.4,1.0,1.65),(8,4.1,3.3),cream)
    box('EntranceRear',(1.6,-2.3,1.75),(4.8,1.5,3.5),cream)
    box('GarageBody',(6.35,.05,1.62),(4.45,5.9,3.24),cream)
    box('Upper',(1.5,.05,4.9),(5.8,5.6,3.0),cream)
    box('UpperTimber',(1.5,-2.79,4.95),(5.8,.12,2.9),timber)
    roof('UpperWallCap',-1.4,4.4,-2.85,2.85,7.40,6.60,1.1,timber)
    roof('StoneWallCap',-4.4,-1,-3.10,3.0,7.875,6.89,1.1,stone)
    roof('UpperSkillion',-1.65,4.85,-3.25,3.25,7.65,6.75)
    roof('TowerSkillion',-4.62,-.85,-3.55,3.3,8.15,7.06)
    roof('LeftSkillion',-8.5,-4.28,-3.4,3.5,3.55,4.10)
    roof('GarageSkillion',4.15,9.19536686,-3.4,3.5,4.0,3.55)
    if level<2:
        roof('TowerSoffit',-4.58,-.9,-3.49,3.25,7.93,6.85,.07,cream)
        roof('UpperSoffit',-1.6,4.8,-3.19,3.2,7.43,6.53,.07,cream)
        for row in range(17 if level==0 else 9):
            z=3.54+row*(.195 if level==0 else .39)
            if z<6.4: box('TimberBoard',(1.5,-2.88,z),(5.77,.07,.17 if level==0 else .35),timber,.008)
        # Staggered limestone blocks stop at the tall glazing opening.
        for row in range(23 if level==0 else 12):
            z=(.17 if level==0 else .29)+row*(.30 if level==0 else .58)
            for a,b in [(-4.4,-3.43),(-1.97,-1.0)]:
                for col in range(2 if level==0 else 1):
                    w=(b-a)/(2 if level==0 else 1)
                    box('LimestoneBlock',(a+(col+.5)*w,-3.14,z),(w-.025,.16,.275 if level==0 else .55),stone2 if rng.random()>.4 else stone,.018)
        for row in range(2 if level==0 else 1):
            for col in range(7 if level==0 else 4):
                box('StoneHeader',(-4.16+col*(.46 if level==0 else .8),-3.14,6.45+row*.31),(.43 if level==0 else .76,.16,.28),stone2,.018)
    def window(name,x,y,z,w,h,side=0):
        items=[]
        items.append(box(name+'Frame',(x,y,z),(w,.16,h),dark))
        items.append(box(name+'Glass',(x,y-.095,z),(w-.14,.055,h-.14),glass,.008))
        if level<2:
            for i in range(1,3 if w>2.4 else 2):
                xx=x-w/2+w*i/(3 if w>2.4 else 2)
                items.append(box(name+'Mullion',(xx,y-.135,z),(.065,.06,h-.1),dark,.006))
            if h>3:
                for zz in (1.7,3.3,4.9): items.append(box(name+'Transom',(x,y-.14,zz),(w-.1,.07,.065),dark,.006))
            if level==0:
                for i in range(4): items.append(box(name+'Blind',(x,y-.14,z+h*.3-i*.10),(w-.16,.04,.025),concrete,.002))
                items.append(box(name+'Sill',(x,y-.14,z-h/2),(w+.16,.30,.10),concrete))
        if side:
            for o in items:
                dx,dy=o.location.x-x,o.location.y-y
                o.location.x=x+math.cos(side)*dx-math.sin(side)*dy
                o.location.y=y+math.sin(side)*dx+math.cos(side)*dy
                o.rotation_euler.z=side
    window('TallFeature',-2.7,-3.25,3.45,1.55,5.65)
    window('LeftFront',-6.15,-2.98,1.75,2.8,1.95)
    window('UpperFront',1.5,-3.0,5.35,3.7,1.65)
    for x in (-2.7,1.8): window('RearUpper',x,2.91,5.25,1.75,1.5,math.pi)
    for x in (-2.6,2.5): window('RearSliding',x,3.09,1.65,3.1,2.65,math.pi)
    window('LeftSide',-7.96,.6,1.8,1.6,1.5,-math.pi/2)
    window('RightSide',8.62,.7,1.8,1.6,1.5,math.pi/2)
    window('UpperLeftSide',-4.43,.6,5.3,1.6,1.5,-math.pi/2)
    window('UpperRightSide',4.43,.6,5.2,1.6,1.5,math.pi/2)
    box('PorchSlab',(1.55,-3.9,.35),(4.35,2.1,.46),concrete)
    box('PorchCanopy',(1.55,-3.77,3.65),(4.8,2.45,.24),dark)
    box('PorchSoffit',(1.55,-3.77,3.49),(4.65,2.3,.10),cream)
    for x in (-.55,3.65): box('PorchPost',(x,-4.57,2.02),(.23,.26,2.88),timber)
    box('DoorFrame',(1.55,-3.11,1.88),(1.65,.16,2.69),dark)
    box('FrontDoor',(1.55,-3.23,1.85),(1.41,.12,2.5),timber)
    for z in (1.0,1.55,2.1,2.65): box('DoorGlass',(1.62,-3.305,z),(.88,.025,.21),glass,.006)
    if level<2: box('DoorHandle',(1.01,-3.35,1.8),(.055,.08,.52),dark,.007)
    box('GarageFrame',(6.35,-2.97,1.62),(4.05,.18,2.95),dark)
    box('GarageDoor',(6.35,-3.09,1.57),(3.82,.12,2.72),garage)
    for x in (5.05,5.92,6.79,7.66): box('GarageGlass',(x,-3.166,2.61),(.72,.025,.26),glass,.004)
    if level<2:
        for z in (.65,1.13,1.61,2.09): box('GarageJoint',(6.35,-3.16,z),(3.77,.025,.023),concrete,.003)
        for x in (.15,2.95,8.6):
            box('WallLightHousing',(x,-3.22,2.26),(.17,.18,.46),dark)
            box('WallLightGlow',(x,-3.325,2.26),(.115,.03,.32),glow,.005)
        box('PorchCeilingLight',(1.55,-3.9,3.41),(.48,.38,.06),glow)
    for i in range(3): box('EntryStep',(1.55,-5.0-i*.33,.42-i*.13),(2.65,.4,.24),concrete)
    box('RearPatio',(0,4.45,.11),(8.4,2.78,.22),concrete)
    box('GardenBed',(-5.9,-3.79,.25),(4.9,1.15,.5),stone)
    for i in range(7 if level<2 else 4):
        x=-7.95+i*(.68 if level<2 else 1.36)
        bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=2 if level==0 else 1,radius=1,location=(x,-3.8,.85))
        o=bpy.context.object; o.name='GardenShrub';o.scale=(.48,.45,.5);o.parent=parent;o.data.materials.append(leaf)
        if level==0:
            for j in range(4):
                o=box('PlantLeaf',(x+(j-1.5)*.12,-3.9,1.12),(.12,.10,.74),leaf,.015)
                o.rotation_euler.y=(j-1.5)*.3
    if level<2:
        for x in (-.18,3.2):
            box('EntryPlanter',(x,-3.9,.93),(.5,.52,.7),cream)
            bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=2 if level==0 else 1,radius=.36,location=(x,-3.9,1.45))
            o=bpy.context.object;o.name='Topiary';o.parent=parent;o.data.materials.append(leaf)
    # Apply bevel once, combine by material, then triangulate. No hidden high-detail copies.
    parts=[o for o in parent.children_recursive if o.type=='MESH']
    for o in parts:
        bpy.context.view_layer.objects.active=o
        for mod in list(o.modifiers): bpy.ops.object.modifier_apply(modifier=mod.name)
    groups=[[o for o in parts if o.data.materials[0]==m] for m in palette.values()]
    for group in groups:
        if not group: continue
        bpy.ops.object.select_all(action='DESELECT')
        for o in group: o.select_set(True)
        bpy.context.view_layer.objects.active=group[0];bpy.ops.object.join()
        o=bpy.context.object;o.name=f'MH02_LOD{level}_{o.data.materials[0].name}'
        mod=o.modifiers.new('Triangles','TRIANGULATE');bpy.ops.object.modifier_apply(modifier=mod.name)
    totals.append(sum(len(o.data.polygons) for o in parent.children_recursive if o.type=='MESH'))
for level in range(3): build(level)
bpy.ops.object.select_all(action='DESELECT');root.select_set(True)
for o in root.children_recursive:o.select_set(True)
bpy.context.view_layer.objects.active=root
bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'medium_house_02.blend'))
bpy.ops.export_scene.fbx(filepath=str(OUT/'medium_house_02.fbx'),use_selection=True,object_types={'MESH','EMPTY'},apply_unit_scale=True,axis_forward='-Z',axis_up='Y',use_mesh_modifiers=True,bake_anim=False,add_leaf_bones=False)
print('LOD_TRIANGLES',totals)
# Workbench review sheet. Only LOD0 is shown; saved source retains all levels.
for parent in lod_roots[1:]:
    for o in parent.children_recursive:o.hide_render=True
scene=bpy.context.scene;scene.render.engine='BLENDER_WORKBENCH'
scene.display.shading.light='STUDIO';scene.display.shading.color_type='MATERIAL'
scene.display.shading.show_shadows=True;scene.display.shading.show_cavity=True
scene.display.shading.cavity_type='BOTH';scene.display.shading.background_type='WORLD'
scene.world.color=(.12,.16,.22)
bpy.ops.object.camera_add(location=(18,-30,14));cam=bpy.context.object
cam.rotation_euler=(Vector((0,0,3))-cam.location).to_track_quat('-Z','Y').to_euler()
cam.data.type='ORTHO';cam.data.ortho_scale=24;scene.camera=cam
scene.render.resolution_x=1200;scene.render.resolution_y=850;scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG'
scene.render.filepath=str(P/'Tools/Blender/Previews/medium_house_02_blender.png')
bpy.ops.render.render(write_still=True)
