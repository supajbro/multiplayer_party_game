"""Explicit interior detailing; retain exterior, existing props and moving-door geometry."""
import bpy, math, json, hashlib, ast
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[2]
SOURCE=ROOT/'Assets/_Project/Models/depot_02/depot_02.blend'
bpy.ops.wm.open_mainfile(filepath=str(SOURCE),load_ui=False)
s=bpy.context.scene
helpers=ast.parse((ROOT/'Tools/Blender/detail_depot02_exterior.py').read_text().replace('D02_EXT_','D02_INT_'))
exec(compile(ast.Module(body=[n for n in helpers.body if isinstance(n,ast.FunctionDef) and n.name in {'fingerprint','own','box','pipe','cylinder'}],type_ignores=[]),'interior_helpers','exec'))
original={o.name:fingerprint(o) for o in s.objects if o.name!='D02_BrandSign' and not o.name.startswith('D02_INT_')}
for o in list(s.objects):
    if o.name=='D02_BrandSign' or o.name.startswith('D02_INT_'):bpy.data.objects.remove(o,do_unlink=True)
col=bpy.data.collections.get('Depot02_InteriorDetails') or bpy.data.collections.new('Depot02_InteriorDetails')
if col.name not in s.collection.children:s.collection.children.link(col)
palette={m.name[4:]:m for m in bpy.data.materials if m.name.startswith('D02_')}
groups={}
# Recessed-looking panel assemblies replace the logo above the staff entry.
for x,w in [(1.1,1.1),(4.5,4.35),(7.9,1.1)]:
    box('UpperWallPanels',(x,-9.715,6.25),(w,.12,1.85),'Silver')
    box('UpperWallPanels',(x,-9.635,6.25),(w-.12,.065,1.71),'Ivory')
    for dx in (-w/2+.15,w/2-.15):
        for z in (5.51,6.99):cylinder('PanelFasteners',(x+dx,-9.58,z),.035,.025,'Steel','Y')
box('EntranceVent',(4.5,-9.56,6.28),(3.72,.13,1.23),'Steel')
box('EntranceVent',(4.5,-9.475,6.28),(3.48,.06,1.02),'Rubber')
for i in range(10):box('EntranceVent',(4.5,-9.42,5.86+i*.094),(3.36,.08,.042),'Silver',.01)
for x in (3.43,4.5,5.57):box('EntranceVent',(x,-9.36,6.28),(.035,.045,.96),'Steel',.006)
for x in (2.06,6.94):
    pipe('EntranceConduits',[(x,-9.57,7.4),(x,-9.57,5.65),(x+.2,-9.57,5.45)],.05)
    for z in (5.9,6.6,7.2):box('ConduitClamps',(x,-9.55,z),(.21,.15,.065),'Steel')
# Cable tray across the upper front wall; clear of both signboards.
box('FrontCableTray',(0,-9.49,7.46),(26.8,.39,.12),'Steel')
for y in (-9.68,-9.3):box('FrontCableTray',(0,y,7.57),(26.8,.045,.18),'Silver')
for x in range(-13,14):box('FrontCableTray',(x,-9.49,7.55),(.045,.36,.04),'Silver',0)
for y,z in [(-9.48,7.57),(-9.4,7.6)]:pipe('TrayCables',[(-13,y,z),(13,y,z)],.035,'Rubber')
# Thin wall seams and bolts break up blank panels without covering existing graphics.
for a,b in [(-13.35,-11.5),(-3.2,2.5),(6.65,13.35)]:
    x=a
    while x<b:
        for lo,hi in [(1.45,3.7),(5.25,7.15)]:
            # Keep the new logo-replacement panels legible.
            if hi>5 and 0<x<8.6:continue
            box('FrontPanelSeams',(x,-9.785,(lo+hi)/2),(.032,.035,hi-lo),'Silver',0)
            for z in (lo+.13,hi-.13):cylinder('PanelFasteners',(x+.1,-9.747,z),.03,.025,'Steel','Y')
        x+=1.85
# Blank-wall utility stations, with inset doors, grilles and indicator lamps.
for x,z,w in [(10.6,2.85,1.15),(-2.1,2.65,.86)]:
    box('ServiceCabinets',(x,-9.61,z),(w,.27,1.37),'Silver')
    box('ServiceCabinets',(x,-9.445,z),(w-.12,.08,1.22),'Steel')
    box('CabinetHandles',(x+w*.32,-9.38,z),(.055,.065,.27),'Rubber')
    for i in range(4):box('CabinetGrilles',(x,-9.385,z-.4+i*.08),(w*.6,.035,.028),'Rubber',0)
    for dx,m in [(-.21,'Light'),(0,'ExteriorGreen')]:
        box('CabinetIndicators',(x+dx,-9.38,z+.4),(.085,.035,.085),m)
    pipe('WallConduits',[(x-.32,-9.64,z+.69),(x-.32,-9.64,3.7),(x-.6,-9.64,3.94)],.045)
    pipe('WallConduits',[(x+.24,-9.64,z-.69),(x+.24,-9.64,1.03),(x+.42,-9.64,.86)],.045)
    for zz in (1.4,3.55):box('ConduitClamps',(x+.24,-9.6,zz),(.2,.13,.065),'Steel')
# Upper corner air returns and wall fixtures.
for x in (-12.5,11.2):
    box('CornerAirReturns',(x,-9.65,5.8),(1.3,.2,1.0),'Silver')
    box('CornerAirReturns',(x,-9.52,5.8),(1.13,.07,.83),'Rubber')
    for i in range(7):box('CornerAirReturns',(x,-9.46,5.45+i*.11),(1.06,.065,.035),'Silver',.006)
for x in (-12.5,-1.2,9.3,12.6):
    box('WallTaskLights',(x,-9.45,7.05),(.68,.38,.16),'Steel')
    box('WallTaskLights',(x,-9.29,6.96),(.5,.16,.045),'Light')
# Side-wall dado panels and rails sit flush to the perimeter, below the windows.
for side in (-1,1):
    x=side*13.73
    for a,b in ([(-9.3,-.65),(4.2,9.3)] if side==1 else [(-9.3,9.3)]):
        for z in (.38,1.38):box('SideWallRails',(x,(a+b)/2,z),(.1,b-a,.1),'Steel')
        y=a+.3
        while y<b:
            box('SidePanelSeams',(side*13.77,y,2.55),(.035,.03,1.9),'Silver',0)
            for z in (1.75,3.35):
                o=cylinder('SidePanelBolts',(side*13.72,y+.1,z),.034,.035,'Silver')
                o.rotation_euler[1]=math.pi/2
            y+=2.2
# Consolidate only the new assemblies.
for name,parts in groups.items():
    bpy.ops.object.select_all(action='DESELECT')
    for o in parts:
        o.select_set(True);bpy.context.view_layer.objects.active=o
        if o.type=='CURVE':bpy.ops.object.convert(target='MESH')
        for mod in list(o.modifiers):bpy.ops.object.modifier_apply(modifier=mod.name)
        o.select_set(False)
    for o in parts:o.select_set(True)
    bpy.context.view_layer.objects.active=parts[0]
    if len(parts)>1:bpy.ops.object.join()
    parts[0].name='D02_INT_'+name
    bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
assert all(bpy.data.objects.get(n) and fingerprint(bpy.data.objects[n])==h for n,h in original.items())
out=[]
for o in col.objects:
    me=o.data;me.calc_loop_triangles()
    out.append(dict(name=o.name,vertices=[dict(x=-v.co.x,y=v.co.z,z=-v.co.y) for v in me.vertices],materials=[m.name for m in me.materials],submeshes=[dict(indices=[v for t in me.loop_triangles if t.material_index==i for v in reversed(t.vertices)]) for i in range(len(me.materials))]))
(ROOT/'ArtSource/Depot02/depot_02_interior_meshes.json').write_text(json.dumps(dict(meshes=out)))
(ROOT/'ArtSource/Depot02/depot_02_interior_validation.json').write_text(json.dumps(dict(unchangedObjects=len(original),removedObjects=['D02_BrandSign'],assemblies=len(out),triangles=sum(len(sm['indices'])//3 for o in out for sm in o['submeshes'])),indent=2))
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE))
c=s.camera;c.location=(8,5.8,4.7);c.rotation_euler=(Vector((1,-9.5,4.1))-c.location).to_track_quat('-Z','Y').to_euler();c.data.type='PERSP';c.data.lens=20
s.render.resolution_x=1500;s.render.resolution_y=900;s.render.resolution_percentage=100;s.cycles.samples=32
s.render.filepath=str(ROOT/'ArtSource/Depot02/depot_02_interior_detailed.png');bpy.ops.render.render(write_still=True)
print('INTERIOR_COMPLETE',len(out))
