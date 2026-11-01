"""Explicit authoring only. Run in Blender; exports metre-scale FBX and editable source."""
import bpy
import math
import os
import json
import bmesh
from mathutils import Vector

ROOT = r'C:\Users\Jaden\Documents\MyUnityProjects\multiplayer_party_game'
OUT = os.path.join(ROOT, 'Assets/_Project/Models/Environment/WaterFountain')
os.makedirs(OUT, exist_ok=True)
previous=bpy.data.scenes.get('water_fountain_authoring')
if previous:
    for o in list(previous.objects): bpy.data.objects.remove(o,do_unlink=True)
    bpy.data.scenes.remove(previous)
for m in list(bpy.data.materials):
    if m.name.startswith('Fountain_') and m.users==0: bpy.data.materials.remove(m)
scene = bpy.data.scenes.new('water_fountain_authoring')
bpy.context.window.scene = scene
materials = []
for name, color, metal, rough in [
    ('Fountain_Stone', (.67,.55,.45,1),0,.42),
    ('Fountain_Blue', (.015,.30,.76,1),.18,.27),
    ('Fountain_Inset', (.20,.23,.29,1),0,.58),
    ('Fountain_Bolts', (.62,.68,.73,1),.65,.24),
    ('Fountain_Water', (.005,.65,.85,1),.12,.18)]:
    m=bpy.data.materials.new(name); m.diffuse_color=color; m.use_nodes=True
    bs=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
    bs.inputs['Base Color'].default_value=color
    bs.inputs['Metallic'].default_value=metal; bs.inputs['Roughness'].default_value=rough
    materials.append(m)

parts=[]; lod=0
def finish(o,name,mat,bevel=0):
    o.name=name; o.data.materials.append(materials[mat]); parts.append(o)
    bpy.context.view_layer.objects.active=o; o.select_set(True)
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    if bevel:
        b=o.modifiers.new('Soft sculpted edges','BEVEL'); b.width=bevel; b.segments=[3,2,1][lod]
        bpy.ops.object.modifier_apply(modifier=b.name)
        for p in o.data.polygons: p.use_smooth=True
        n=o.modifiers.new('Weighted face normals','WEIGHTED_NORMAL'); n.keep_sharp=True; n.weight=50
        bpy.ops.object.modifier_apply(modifier=n.name)
    o.select_set(False)
    return o
def box(name,loc,size,mat=0,b=.04):
    bpy.ops.mesh.primitive_cube_add(size=1,location=loc); o=bpy.context.object; o.dimensions=size
    return finish(o,name,mat,b)
def prism(name,points,depth,mat,bevel=.025):
    # points in x/z, front along -Y; useful for recesses and tapered decorative frames
    verts=[(x,y,z) for y in (-depth/2,depth/2) for x,z in points]; n=len(points)
    faces=[tuple(reversed(range(n))),tuple(range(n,2*n))]
    faces += [(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
    mesh=bpy.data.meshes.new(name); mesh.from_pydata(verts,[],faces); mesh.update()
    o=bpy.data.objects.new(name,mesh); scene.collection.objects.link(o)
    return finish(o,name,mat,bevel)
def rotate_side(o,side,y=-2.5):
    o.location.y=y; o.rotation_euler.z=side*math.pi/2
    o.location.x=-y*math.sin(side*math.pi/2); o.location.y=y*math.cos(side*math.pi/2)
def bolt(loc,side=0,r=.075):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=12 if lod==0 else 8,ring_count=6 if lod==0 else 4,location=loc)
    o=bpy.context.object; o.scale=(r,r*.43,r); o.rotation_euler.z=side*math.pi/2
    return finish(o,'Rounded steel fastener',3,0)
def taper(name,z0,z1,w0,w1,mat=0,b=.04):
    v=[(x*w,y*w,z) for z,w in [(z0,w0/2),(z1,w1/2)] for x,y in [(-1,-1),(1,-1),(1,1),(-1,1)]]
    mesh=bpy.data.meshes.new(name); mesh.from_pydata(v,[],[(3,2,1,0),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)])
    o=bpy.data.objects.new(name,mesh); scene.collection.objects.link(o); return finish(o,name,mat,b)
counts=[]
for lod in range(3):
    parts=[]
    # Segmented foundation; seams remain visible at medium detail.
    n=3 if lod<2 else 1
    for row in range(n):
        for col in range(n):
            w=5.5/n
            box('Foundation stone',(-2.75+w*(col+.5),-2.75+w*(row+.5),.14),(w-.014,w-.014,.28),0,.065 if lod<2 else .025)
    box('Thin blue foundation reveal',(0,0,.315),(5.39,5.39,.075),1,.065)
    box('Basin floor',(0,0,.43),(5.18,5.18,.18),2,.05)
    for side in range(4):
        # Short corner stones plus a long middle panel section.
        for i,(x,w) in enumerate([(-1.92,1.03),(0,2.79),(1.92,1.03)] if lod<2 else [(0,4.85)]):
            wall=box('Basin masonry',(x,-2.43,.71),(w,.48,.72),0,0)
            if lod<2 and i==1:
                cutter=prism('Panel cutter',[(-1.21,.49),(-1.32,.65),(-1.20,.86),(1.20,.86),(1.32,.65),(1.21,.49)],.12,2,0)
                cutter.location.y=-2.66
                bpy.context.view_layer.objects.active=wall
                mod=wall.modifiers.new('Recessed geometric panel','BOOLEAN'); mod.object=cutter
                bpy.ops.object.modifier_apply(modifier=mod.name)
                parts.remove(cutter); bpy.data.objects.remove(cutter,do_unlink=True)
                p=prism('Recess shadow',[(-1.16,.51),(-1.26,.65),(-1.16,.83),(1.16,.83),(1.26,.65),(1.16,.51)],.012,2,.012)
                rotate_side(p,side,-2.613)
                if side in (0,2):
                    plate=prism('Blue inset plaque',[(-.50,.51),(-.62,.66),(-.50,.85),(.50,.85),(.62,.66),(.50,.51)],.045,1,.03)
                    rotate_side(plate,side,-2.637)
                    if lod==0:
                        for bx in [-.43,.43]:
                            a=side*math.pi/2; bolt((bx*math.cos(a)+2.679*math.sin(a),bx*math.sin(a)-2.679*math.cos(a),.68),side,.065)
            # Apply bevel after cutting the recess.
            bpy.context.view_layer.objects.active=wall
            b=wall.modifiers.new('Rounded stone arris','BEVEL'); b.width=.045; b.segments=[3,2,1][lod]
            bpy.ops.object.modifier_apply(modifier=b.name)
            for poly in wall.data.polygons: poly.use_smooth=True
            normal=wall.modifiers.new('Stable masonry normals','WEIGHTED_NORMAL'); normal.keep_sharp=True
            bpy.ops.object.modifier_apply(modifier=normal.name)
            wall.rotation_euler.z=side*math.pi/2
            wall.location=Vector((x,-2.43,.71)); a=side*math.pi/2
            wall.location.x=x*math.cos(a)+2.43*math.sin(a); wall.location.y=x*math.sin(a)-2.43*math.cos(a)
        # Segmented coping blocks on top, a slightly wider lip.
        for k in range(5 if lod<2 else 1):
            ncap=5 if lod<2 else 1; x=-2.38+(k+.5)*4.76/ncap
            o=box('Coping cap',(0,0,1.04),(4.76/ncap-.012,.54,.16),0,.04)
            a=side*math.pi/2; o.location.x=x*math.cos(a)+2.43*math.sin(a); o.location.y=x*math.sin(a)-2.43*math.cos(a); o.rotation_euler.z=a
    for x in [-1,1]:
        for y in [-1,1]:
            # Three overlapping bevelled plates create a continuous corner guard.
            box('Corner guard crown',(x*2.39,y*2.39,1.125),(.61,.61,.16),1,.09)
            box('Corner guard face X',(x*2.665,y*2.39,.87),(.16,.60,.54),1,.08)
            box('Corner guard face Y',(x*2.39,y*2.665,.87),(.60,.16,.54),1,.08)
            if lod<2:
                bolt((x*2.39,y*2.765,.85),0,.085)
                bolt((x*2.765,y*2.39,.85),1,.085)
                b=bolt((x*2.39,y*2.39,1.228),0,.065); b.rotation_euler.x=math.pi/2
    box('Pedestal plinth',(0,0,.985),(1.69,1.69,.30),0,.10)
    box('Pedestal lower moulding',(0,0,1.15),(1.49,1.49,.12),0,.045)
    taper('Tapered central pedestal',1.17,2.0,1.40,.99,0,.055)
    if lod<2:
        for side in range(4):
            # The panel slopes follow the taper. A dark border reads as a recessed socket.
            for name,pts,depth,mat,y in [
                ('Panel socket',[(-.48,1.28),(.48,1.28),(.32,1.85),(-.32,1.85)],.025,2,-.645),
                ('Blue panel frame',[(-.43,1.32),(.43,1.32),(.29,1.81),(-.29,1.81)],.025,1,-.662),
                ('Inset stone face',[(-.32,1.38),(.32,1.38),(.22,1.74),(-.22,1.74)],.027,0,-.676)]:
                o=prism(name,pts,depth,mat,.015)
                # Shear in depth so all three layers stay parallel to the tapered face.
                for v in o.data.vertices: v.co.y += (v.co.z-1.565)*.247
                rotate_side(o,side,y)
    box('Upper bowl base',(0,0,2.025),(1.51,1.51,.16),0,.065)
    for side in range(4):
        for name,z,w,d,h,mat,b in [('Bowl blue band',2.155,1.49,.20,.18,1,.045),('Upper bowl rim',2.31,1.56,.25,.18,0,.065)]:
            a=side*math.pi/2; o=box(name,(0,0,z),(w,d,h),mat,b)
            o.location.x=.66*math.sin(a); o.location.y=-.66*math.cos(a); o.rotation_euler.z=a
        if lod<2:
            for x in [-.48,.48]:
                a=side*math.pi/2; bolt((x*math.cos(a)+.781*math.sin(a),x*math.sin(a)-.781*math.cos(a),2.15),side,.052)
    box('Upper bowl interior',(0,0,2.14),(1.13,1.13,.06),2,.05)
    # Join per LOD, keep material submeshes, shared slots, ground pivot and metre units.
    bpy.ops.object.select_all(action='DESELECT')
    for o in parts:o.select_set(True)
    bpy.context.view_layer.objects.active=parts[0]; bpy.ops.object.join(); merged=bpy.context.object
    merged.name='water_fountain_LOD'+str(lod)
    scene.cursor.location=(0,0,0); bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
    bm=bmesh.new(); bm.from_mesh(merged.data)
    bmesh.ops.dissolve_degenerate(bm,dist=.000001,edges=list(bm.edges))
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    bmesh.ops.triangulate(bm,faces=list(bm.faces))
    bm.to_mesh(merged.data); bm.free()
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(island_margin=.015); bpy.ops.object.mode_set(mode='OBJECT')
    merged.data.calc_loop_triangles(); counts.append(len(merged.data.loop_triangles))
    merged.hide_set(lod!=0)

# Separate water surface with a central opening, 16x16 grid for vertex undulation.
verts=[]; faces=[]; n=24
for j in range(n+1):
    for i in range(n+1):verts.append((-2.18+4.36*i/n,-2.18+4.36*j/n,.925))
for j in range(n):
    for i in range(n):
        x=-2.18+4.36*(i+.5)/n; y=-2.18+4.36*(j+.5)/n
        if abs(x)<.75 and abs(y)<.75:continue
        a=j*(n+1)+i; faces.append((a,a+1,a+n+2,a+n+1))
mesh=bpy.data.meshes.new('Basin water grid'); mesh.from_pydata(verts,[],faces); mesh.update()
o=bpy.data.objects.new('BasinWater',mesh); scene.collection.objects.link(o); o.data.materials.append(materials[4])
bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active=o
bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.uv.smart_project(); bpy.ops.object.mode_set(mode='OBJECT')
box('UpperBowlWater',(0,0,2.245),(1.08,1.08,.015),4,.03)
# All LODs are exported, irrespective of viewport visibility. FBX maps Z up to Unity Y up.
bpy.ops.object.select_all(action='DESELECT')
for o in scene.objects:o.hide_set(False); o.select_set(True)
scene.unit_settings.system='METRIC'; scene.unit_settings.scale_length=1
bpy.ops.export_scene.fbx(filepath=os.path.join(OUT,'water_fountain.fbx'),use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,apply_scale_options='FBX_SCALE_ALL',bake_space_transform=True,bake_anim=False,add_leaf_bones=False)
for o in scene.objects:o.hide_set('LOD1' in o.name or 'LOD2' in o.name)
# Save source outside Assets so Unity does not invoke Blender automatically on import.
source=os.path.join(ROOT,'Tools/Blender/SourceModels/water_fountain.blend')
bpy.ops.wm.save_as_mainfile(filepath=source)
print(json.dumps({'triangles':counts,'fbx':OUT,'source':source}))
