"""Explicit Blender authoring/export; run blender --background --python this_file.py.
Metres, Z-up authoring, -Y front. Centre pivots; Unity FBX -Z forward/Y up.
"""
import bpy, bmesh, math, json
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Assets/_Project/Models/Healing'; SOURCE=ROOT/'Tools/Blender/SourceModels/Healing'
PREVIEW=ROOT/'Tools/Blender/Previews/Healing'
bpy.context.preferences.filepaths.file_preview_type='NONE'
bpy.context.preferences.filepaths.save_version=0
for p in (OUT,SOURCE,PREVIEW): p.mkdir(parents=True,exist_ok=True)

def mat(name,color,metal=0,rough=.3):
 m=bpy.data.materials.new(name);m.diffuse_color=(*color,1);m.use_nodes=True
 p=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
 p.inputs['Base Color'].default_value=(*color,1);p.inputs['Metallic'].default_value=metal;p.inputs['Roughness'].default_value=rough
 return m
blue=mat('Heal_TapeBlue',(.045,.018,.72),.12,.23)
card=mat('Heal_Cardboard',(.52,.38,.23),0,.72)
cardedge=mat('Heal_CardboardEdge',(.72,.59,.39),0,.65)
green=mat('Heal_SprayGreen',(.025,.5,.07),.12,.3)
silver=mat('Heal_Silver',(.65,.72,.75),.8,.25)
white=mat('Heal_White',(.94,.97,1),0,.28)
dark=mat('Heal_NozzleDark',(.025,.04,.035),.15,.4)
lime=mat('Heal_OilLime',(.22,.65,.003),0,.26)
panel=mat('Heal_OilPanel',(.008,.055,.012),0,.45)
rim=mat('Heal_OilRim',(.13,.4,.012),0,.32)

def bevel(obj,width=.008,segments=3):
 bpy.context.view_layer.objects.active=obj
 mod=obj.modifiers.new('Soft manufactured edges','BEVEL');mod.width=width;mod.segments=segments
 bpy.ops.object.modifier_apply(modifier=mod.name)
 for p in obj.data.polygons:p.use_smooth=True
 mod=obj.modifiers.new('Weighted corner normals','WEIGHTED_NORMAL');mod.keep_sharp=True
 bpy.ops.object.modifier_apply(modifier=mod.name)
 return obj

def cube(name,loc,scale,material,rounding=.008):
 bpy.ops.mesh.primitive_cube_add(size=1,location=loc);o=bpy.context.object;o.name=name;o.dimensions=scale
 bpy.ops.object.transform_apply(location=False,rotation=False,scale=True);o.data.materials.append(material)
 if rounding:bevel(o,rounding)
 return o

def cyl(name,loc,radius,depth,material,vertices=40,rounding=.003):
 bpy.ops.mesh.primitive_cylinder_add(vertices=vertices,radius=radius,depth=depth,location=loc)
 o=bpy.context.object;o.name=name;o.data.materials.append(material);bevel(o,rounding,2);return o

def lathe(name,profile,material,segments=64):
 verts=[(r*math.cos(i*2*math.pi/segments),r*math.sin(i*2*math.pi/segments),z) for r,z in profile for i in range(segments)]
 faces=[]
 for j in range(len(profile)):
  k=(j+1)%len(profile)
  for i in range(segments):n=(i+1)%segments;faces.append((j*segments+i,j*segments+n,k*segments+n,k*segments+i))
 mesh=bpy.data.meshes.new(name);mesh.from_pydata(verts,[],faces);mesh.update()
 o=bpy.data.objects.new(name,mesh);bpy.context.collection.objects.link(o);o.data.materials.append(material)
 for p in mesh.polygons:p.use_smooth=True
 return o

def beam(name,a,b,r,material):
 mid=(Vector(a)+Vector(b))/2;o=cyl(name,mid,r,(Vector(b)-Vector(a)).length,material,24,.002)
 o.rotation_euler=(Vector(b)-Vector(a)).to_track_quat('Z','Y').to_euler();return o

def extruded_ring(name,outer,inner,depth,material):
 # Matching perimeter loops form a solid handle with a true open centre.
 n=len(outer);verts=[]
 for y in (-depth/2,depth/2):
  for loop in (outer,inner):verts.extend((x,y,z) for x,z in loop)
 faces=[]
 for i in range(n):
  j=(i+1)%n
  faces.extend([(i,j,n+j,n+i),(2*n+i,3*n+i,3*n+j,2*n+j),
                (i,2*n+i,2*n+j,j),(n+i,n+j,3*n+j,3*n+i)])
 mesh=bpy.data.meshes.new(name);mesh.from_pydata(verts,[],faces);mesh.update()
 o=bpy.data.objects.new(name,mesh);bpy.context.collection.objects.link(o);o.data.materials.append(material);bevel(o,.008,3);return o

def start(name):
 bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
 bpy.context.scene.name=name;bpy.context.scene.unit_settings.system='METRIC';bpy.context.scene.unit_settings.scale_length=1

def finish(name):
 meshes=[o for o in bpy.context.scene.objects if o.type=='MESH']
 for o in meshes:
  bpy.context.view_layer.objects.active=o;o.select_set(True)
  bpy.ops.object.transform_apply(location=False,rotation=True,scale=True)
 # Keep one origin at the grip/centre for easy ground and hand placement.
 bpy.ops.object.select_all(action='DESELECT')
 for o in meshes:o.select_set(True)
 bpy.context.scene.cursor.location=(0,0,0);bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
 bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/(name+'.blend')))
 bpy.ops.export_scene.fbx(filepath=str(OUT/(name+'.fbx')),use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',
  bake_anim=False,add_leaf_bones=False,use_mesh_modifiers=True,path_mode='AUTO')
 tris=sum(len(p.vertices)-2 for o in meshes for p in o.data.polygons)
 print('HEALING_EXPORT',name,'triangles',tris,'meshes',len(meshes),flush=True)
 # Small studio preview, separate from source/export.
 scene=bpy.context.scene
 scene.render.engine='CYCLES';scene.cycles.samples=32
 scene.render.resolution_x=700;scene.render.resolution_y=700;scene.render.resolution_percentage=100
 scene.world.color=(.22,.22,.22)
 bpy.ops.object.camera_add(location=(.75,-1.2,.8));camera=bpy.context.object
 camera.rotation_euler=(Vector((0,0,.02))-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.type='ORTHO';camera.data.ortho_scale=.68;scene.camera=camera
 for loc,power,size in [((.1,-1,1.4),35,1),((-.8,-.1,.6),20,.7),((.5,.7,.9),40,.7)]:
  bpy.ops.object.light_add(type='AREA',location=loc);light=bpy.context.object;light.data.energy=power;light.data.shape='DISK';light.data.size=size
  light.rotation_euler=(-light.location).to_track_quat('-Z','Y').to_euler()
 scene.render.image_settings.file_format='PNG';scene.render.filepath=str(PREVIEW/(name+'.png'));scene.render.film_transparent=True
 bpy.ops.render.render(write_still=True)

start('EmergencyDuctTape')
# Rounded rectangular tape cross-section around a genuinely hollow cardboard tube.
lathe('BlueTapeRoll',[(.103,-.079),(.184,-.079),(.197,-.071),(.2,-.061),(.2,.061),(.197,.071),(.184,.079),(.103,.079)],blue)
lathe('CardboardInnerCore',[(.102,-.08),(.102,.08),(.091,.08),(.089,.073),(.089,-.073),(.091,-.08)],card)
for z in (-.0795,.0795):lathe('CardboardRim',[(.09,z-.0007),(.103,z-.0007),(.103,z+.0007),(.09,z+.0007)],cardedge)
finish('EmergencyDuctTape')

start('ProbablyFineRepairSpray')
cyl('GreenCan',(0,0,-.035),.105,.33,green,48,.012)
for z in (-.203,.136):cyl('RolledMetalRim',(0,0,z),.108,.012,silver,48,.003)
lathe('SilverShoulder',[(0,.142),(.102,.142),(.101,.157),(.086,.181),(.064,.199),(.043,.204),(0,.204)],silver,48)
cyl('ValveSocket',(0,0,.207),.041,.014,dark,32)
cube('WhiteSprayNozzle',(0,-.009,.232),(.064,.063,.043),white,.009)
nozzle=cyl('NozzleAperture',(0,-.042,.232),.008,.002,dark,20,.001);nozzle.rotation_euler=(math.pi/2,0,0)
# Thick white medical cross hugging the cylindrical front, no textures.
verts=[];faces=[]
# Small curved quads keep the cross flush with the can without non-planar ngons.
xs=[-.063,-.042,-.022,0,.022,.042,.063];zs=[-.067,-.022,.022,.067]
for x0,x1 in zip(xs,xs[1:]):
 for z0,z1 in zip(zs,zs[1:]):
  if abs((x0+x1)/2)>.0221 and abs((z0+z1)/2)>.0221:continue
  k=len(verts)
  for x,z in ((x0,z0),(x1,z0),(x1,z1),(x0,z1)):
   verts.append((x,-math.sqrt(.108*.108-x*x),z-.03))
  faces.append((k,k+1,k+2,k+3))
mesh=bpy.data.meshes.new('Cross');mesh.from_pydata(verts,[],faces);mesh.update();o=bpy.data.objects.new('WhiteMedicalCross',mesh);bpy.context.collection.objects.link(o);o.data.materials.append(white)
bm=bmesh.new();bm.from_mesh(mesh);bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.000001);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(mesh);bm.free()
solid=o.modifiers.new('Raised cross thickness','SOLIDIFY');solid.thickness=.001
bpy.context.view_layer.objects.active=o;bpy.ops.object.modifier_apply(modifier=solid.name)
finish('ProbablyFineRepairSpray')

start('PremiumRobotOil')
cube('LimePlasticBody',(0,0,-.025),(.285,.115,.34),lime,.018)
# Border rises above the inset dark front panel.
frame=extruded_ring('RecessedPanelBorder',[(-.121,-.171),(.121,-.171),(.121,.115),(-.121,.115)],
 [(-.106,-.156),(.106,-.156),(.106,.10),(-.106,.10)],.014,rim)
frame.location.y=-.063
cube('DarkGreenInsetPanel',(0,-.062,-.028),(.215,.003,.258),panel,.01)
# Slanted upper silhouette, open integrated handle above the rectangular body.
extruded_ring('IntegratedHandle',[(-.055,.126),(.142,.126),(.142,.257),(-.025,.231)],
 [(-.02,.156),(.108,.156),(.108,.218),(.001,.202)],.089,lime)
# Dark screw collar and long silver angled pouring neck to match the reference.
beam('SpoutCollar',(-.11,0,.127),(-.15,0,.185),.032,rim)
beam('SilverSpoutBase',(-.15,0,.185),(-.19,0,.23),.018,silver)
beam('AngledPouringSpout',(-.19,0,.23),(-.29,0,.265),.012,silver)
beam('SpoutTip',(-.29,0,.265),(-.325,0,.268),.009,silver)
finish('PremiumRobotOil')
