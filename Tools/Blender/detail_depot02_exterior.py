"""Explicit exterior-only authoring. Existing depot meshes and animation are fingerprinted."""
import bpy, math, random, json, hashlib
from pathlib import Path
from mathutils import Vector
ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT/'Assets/_Project/Models/depot_02/depot_02.blend'
bpy.ops.wm.open_mainfile(filepath=str(SOURCE), load_ui=False)
s = bpy.context.scene
def fingerprint(o):
    data = [list(v.co) for v in o.data.vertices] if o.type == 'MESH' else []
    return hashlib.sha256(repr((data, [tuple(p.vertices) for p in o.data.polygons] if o.type=='MESH' else [], [list(r) for r in o.matrix_world])).encode()).hexdigest()
original = {o.name:fingerprint(o) for o in s.objects if not o.name.startswith('D02_EXT_')}
for o in list(s.objects):
    if o.name.startswith('D02_EXT_'): bpy.data.objects.remove(o, do_unlink=True)
col = bpy.data.collections.get('Depot02_ExteriorDetails') or bpy.data.collections.new('Depot02_ExteriorDetails')
if col.name not in s.collection.children: s.collection.children.link(col)
palette = {m.name[4:]:m for m in bpy.data.materials if m.name.startswith('D02_')}
green = bpy.data.materials.get('D02_ExteriorGreen') or bpy.data.materials.new('D02_ExteriorGreen')
green.use_nodes=True
bs=next(n for n in green.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
bs.inputs['Base Color'].default_value=(.04,.9,.17,1)
bs.inputs['Emission Color'].default_value=(.04,.9,.17,1);bs.inputs['Emission Strength'].default_value=3
palette['ExteriorGreen']=green
groups={}
def own(o, group, mat):
    o.name='D02_EXT_'+group
    for c in list(o.users_collection): c.objects.unlink(o)
    col.objects.link(o);o.data.materials.append(palette[mat]);groups.setdefault(group,[]).append(o)
    return o
def box(g,p,d,m='Steel',b=.035):
    bpy.ops.mesh.primitive_cube_add(size=1, location=p);o=own(bpy.context.object,g,m);o.dimensions=d
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    if b:
        mod=o.modifiers.new('Edge chamfer','BEVEL');mod.width=min(b,min(d)*.25);mod.segments=2
    return o
def pipe(g,pts,r=.065,m='Silver'):
    cu=bpy.data.curves.new('Exterior conduit','CURVE');cu.dimensions='3D';cu.bevel_depth=r;cu.bevel_resolution=2
    sp=cu.splines.new('POLY');sp.points.add(len(pts)-1)
    for p,co in zip(sp.points,pts):p.co=(*co,1)
    o=bpy.data.objects.new('pipe',cu);col.objects.link(o);return own(o,g,m)
def cylinder(g,p,r,h,m='Silver',axis='Z'):
    bpy.ops.mesh.primitive_cylinder_add(vertices=12,radius=r,depth=h,location=p);o=own(bpy.context.object,g,m)
    if axis=='Y':o.rotation_euler[0]=math.pi/2
    return o
# Deep roof fascia, with continuous warm light and matching returns.
for g,z,d,m in [('RoofFascia',8.22,(29.2,.7,.38),'Steel'),('RoofFascia',7.94,(29.5,.85,.16),'Silver'),('RoofLighting',8.15,(29.15,.08,.075),'Light')]:
    box(g,(0,-10.46 if m!='Light' else -10.855,z),d,m)
for x in (-14.25,14.25):
    box('RoofReturns',(x,0,8.25),(.45,20.5,.35))
    box('RoofReturns',(x+(-.23 if x<0 else .23),0,8.2),(.055,20.5,.065),'Light')
# Front pilasters remain wholly outside the existing wall.
for x in (-13.65,-3.7,13.65):
    box('FacadePiers',(x,-10.33,3.92),(.42,.28,7.8))
    for z in (.35,7.65):box('FacadePiers',(x,-10.39,z),(.67,.4,.3))
    box('PierLights',(x,-10.5,3.6),(.17,.065,.95),'Rubber')
    box('PierLights',(x,-10.545,3.6),(.07,.035,.76),'Light')
# Exterior plinth panels, seams and exposed fixings on solid facade areas.
for a,b in [(-13.4,-11.15),(-3.4,2.85),(6.15,13.4)]:
    box('Plinth',((a+b)/2,-10.23,.64),(b-a,.08,1.25))
    for z in (1.35,4.7):box('PanelSeams',((a+b)/2,-10.205,z),(b-a,.025,.035),'Silver',0)
    x=a+.18
    while x<b:
        box('PanelSeams',(x,-10.205,4),(.025,.024,5.1),'Silver',0)
        for z in (1.52,4.5,6.75):cylinder('FacadeFasteners',(x+.1,-10.235,z),.035,.025,'Silver','Y')
        x+=2.25
# Entrance awning, angled braces, door-status lamps. Opening stays clear.
box('EntranceCanopy',(4.5,-10.95,3.97),(4.35,1.65,.22))
box('EntranceCanopy',(4.5,-11.76,4.04),(4.5,.12,.32))
box('EntranceLights',(4.5,-11.65,3.84),(3.9,.12,.065),'ExteriorGreen')
for x in (2.7,6.3):
    pipe('CanopyBraces',[(x,-10.25,4.65),(x,-11.55,4.12)],.065,'Steel')
for x in (2.55,6.45,-11.45,-3.55):
    box('DoorStatus',(x,-10.45,2.6),(.24,.2,.48),'Rubber')
    box('DoorStatus',(x,-10.565,2.6),(.13,.04,.31),'ExteriorGreen')
for x in (-11.7,-3.3,2.8,6.2):
    cylinder('SafetyBollards',(x,-11.35,.07),.18,.12,'Steel')
    cylinder('SafetyBollards',(x,-11.35,.54),.105,.94,'Yellow')
    cylinder('SafetyBollards',(x,-11.35,.86),.108,.1,'Silver')
# Downward wall fixtures placed below the roof edge, clear of the original brand.
for x in (-12.5,-7.5,-1.7,8.5,12.3):
    box('WallLamps',(x,-10.57,7.55),(.7,.5,.19),'Rubber')
    box('WallLamps',(x,-10.69,7.435),(.52,.28,.045),'Light')
# Framed louvres on two blank wall panels.
for x,z,w,h in [(-.4,3.5,1.8,1),(11.8,3.8,.9,.85)]:
    box('WallVents',(x,-10.28,z),(w+.16,.13,h+.16),'Silver')
    box('WallVents',(x,-10.36,z),(w,.07,h),'Rubber')
    for i in range(8):box('WallVents',(x,-10.425,z-h*.42+i*h*.12),(w-.09,.08,.04),'Silver',.008)
# Surface utility conduits, clamps and service boxes.
for x in (-12.65,12.8):
    for dx in (-.1,.1):
        pipe('UtilityPipes',[(x+dx,-10.36,7.8),(x+dx,-10.36,2),(x+dx+.16,-10.4,1.75)],.045)
    for z in (2.4,4.1,5.8,7.3):box('UtilityPipes',(x,-10.39,z),(.43,.17,.08))
    box('ServiceBoxes',(x,-10.53,1.65),(.62,.35,.8),'Silver')
    box('ServiceBoxes',(x,-10.725,1.65),(.46,.055,.62))
    for dx,z in [(-.12,1.83),(.12,1.83),(-.12,1.6)]:box('ServiceIndicators',(x+dx,-10.77,z),(.075,.035,.075),'Light')
# Roof HVAC sits over opaque roof, leaving skylights clear.
for x in (-11,11):
    box('RooftopHVAC',(x,-6.5,8.38),(2.6,2.2,.18))
    box('RooftopHVAC',(x,-6.5,9),(2.3,1.8,1.12),'Ivory')
    box('RooftopHVAC',(x,-7.43,9),(1.8,.08,.78),'Rubber')
    for i in range(9):box('RooftopHVAC',(x,-7.49,8.67+i*.08),(1.7,.035,.025),'Silver',0)
    for dx in (-.55,0,.55):box('RooftopHVAC',(x+dx,-7.51,9),(.027,.03,.7),'Silver',0)
    side=-1 if x<0 else 1
    for y in (-6.85,-6.15):
        pipe('RoofDucts',[(x+side*1.05,y,9),(x+side*1.65,y,9),(x+side*1.95,y,8.75),(x+side*1.95,y,8.4)],.13)
        for xx in (x+side*1.2,x+side*1.65):
            box('RoofDucts',(xx,y,9),(.06,.33,.33),'Steel')
# Box planters use deterministic, faceted shrubs; no plants overlap entrances.
random.seed(205)
for x,w in [(-.45,3.2),(9.4,3.5)]:
    box('Planters',(x,-11.05,.35),(w,1,.7),'Ivory')
    box('Planters',(x,-11.05,.72),(w+.07,1.07,.12),'Silver')
    box('Planters',(x,-11.05,.79),(w-.2,.8,.04),'Rubber')
    for dx in (-w*.32,w*.32):box('PlanterLights',(x+dx,-11.57,.36),(.4,.045,.075),'Light')
    for i in range(9):
        p=(x+random.uniform(-w*.42,w*.42),-11.05+random.uniform(-.26,.26),.95+random.uniform(0,.18))
        bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1,radius=random.uniform(.3,.48),location=p)
        own(bpy.context.object,'Shrubs','LeavesLight' if i%3 else 'Leaves')
# Consolidate by editable exterior assembly, applying only new modifiers.
for name,parts in groups.items():
    bpy.ops.object.select_all(action='DESELECT')
    for o in parts:
        o.select_set(True);bpy.context.view_layer.objects.active=o
        if o.type=='CURVE':bpy.ops.object.convert(target='MESH')
        for mod in list(o.modifiers):bpy.ops.object.modifier_apply(modifier=mod.name)
        o.select_set(False)
    for o in parts:o.select_set(True)
    bpy.context.view_layer.objects.active=parts[0];bpy.ops.object.join();parts[0].name='D02_EXT_'+name
    bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
assert all(bpy.data.objects.get(n) and fingerprint(bpy.data.objects[n])==h for n,h in original.items()), 'Existing geometry changed'
out=[]
for o in col.objects:
    me=o.data;me.calc_loop_triangles()
    out.append(dict(name=o.name,vertices=[dict(x=-v.co.x,y=v.co.z,z=-v.co.y) for v in me.vertices],materials=[m.name for m in me.materials],submeshes=[dict(indices=[v for t in me.loop_triangles if t.material_index==i for v in reversed(t.vertices)]) for i in range(len(me.materials))]))
(ROOT/'ArtSource/Depot02/depot_02_exterior_meshes.json').write_text(json.dumps(dict(meshes=out)))
(ROOT/'ArtSource/Depot02/depot_02_exterior_validation.json').write_text(json.dumps(dict(unchangedOriginalObjects=len(original),assemblies=len(out),triangles=sum(len(sm['indices'])//3 for o in out for sm in o['submeshes']),originalFingerprints=original),indent=2))
s.render.image_settings.file_format='PNG'
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE))
c=s.camera;c.location=(19,-43,18);c.rotation_euler=(Vector((0,-2,4))-c.location).to_track_quat('-Z','Y').to_euler();c.data.type='ORTHO';c.data.ortho_scale=38
s.render.resolution_x=1500;s.render.resolution_y=950;s.render.resolution_percentage=100;s.cycles.samples=24
s.render.filepath=str(ROOT/'ArtSource/Depot02/depot_02_exterior.png');bpy.ops.render.render(write_still=True)
c.location=(0,-42,11);c.rotation_euler=(Vector((0,-9,4))-c.location).to_track_quat('-Z','Y').to_euler();c.data.ortho_scale=32
s.render.filepath=str(ROOT/'ArtSource/Depot02/depot_02_front.png');bpy.ops.render.render(write_still=True)
print('EXTERIOR_COMPLETE',len(out))
