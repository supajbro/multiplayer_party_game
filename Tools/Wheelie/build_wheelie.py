"""Explicit, offline Blender authoring tool. Never runs on Unity import."""
import bpy, math, os, json
from mathutils import Vector
from math import sin, cos, pi
ROOT=os.path.abspath(os.path.join(os.path.dirname(__file__),'../..'))
OUT=os.path.join(ROOT,'Assets/_Project/Models/Wheelie')
SOURCE=os.path.join(ROOT,'ArtSource/Wheelie')
os.makedirs(OUT,exist_ok=True); os.makedirs(SOURCE,exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
scene=bpy.context.scene
scene.unit_settings.system='METRIC'; scene.unit_settings.scale_length=1
parts=[]; bone_defs=[]
def mat(name,color,rough=.28,metal=.0,emission=0):
 m=bpy.data.materials.new(name); m.diffuse_color=(*color,1); m.use_nodes=True
 p=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
 p.inputs['Base Color'].default_value=(*color,1); p.inputs['Roughness'].default_value=rough; p.inputs['Metallic'].default_value=metal
 p.inputs['Emission Color'].default_value=(*color,1); p.inputs['Emission Strength'].default_value=emission
 return m
white=mat('Wheelie_Armour',(.92,.95,1),.24,.12)
dark=mat('Wheelie_Mechanics',(.035,.045,.065),.31,.38)
rubber=mat('Wheelie_Tyre',(.016,.021,.029),.7)
cyan=mat('Wheelie_Cyan',(.015,.72,1),.22,.25,2.2)
black=mat('Wheelie_Screen',(.001,.002,.004),.19,.08)
def finish(o,name,material,bone,bevel=0):
 o.name=name; o.data.materials.append(material)
 if bevel:
  mod=o.modifiers.new('Manufactured edge radii','BEVEL'); mod.width=bevel; mod.segments=2 if bevel>.005 else 1
  bpy.context.view_layer.objects.active=o; bpy.ops.object.modifier_apply(modifier=mod.name)
 for f in o.data.polygons:f.use_smooth=True
 if bevel:
  mod=o.modifiers.new('Area weighted normals','WEIGHTED_NORMAL'); mod.keep_sharp=True; mod.weight=40
  bpy.ops.object.modifier_apply(modifier=mod.name)
 g=o.vertex_groups.new(name=bone); g.add(list(range(len(o.data.vertices))),1,'REPLACE')
 parts.append(o); return o
def mesh(name,verts,faces,material,bone,bevel=0):
 d=bpy.data.meshes.new(name); d.from_pydata(verts,[],faces); d.update()
 o=bpy.data.objects.new(name,d); scene.collection.objects.link(o)
 # Recalculate winding consistently on closed shells.
 bpy.context.view_layer.objects.active=o; o.select_set(True)
 bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT'); bpy.ops.mesh.normals_make_consistent(inside=False); bpy.ops.object.mode_set(mode='OBJECT'); o.select_set(False)
 return finish(o,name,material,bone,bevel)
def box(name,loc,size,material,bone,bevel=.01):
 bpy.ops.mesh.primitive_cube_add(size=1,location=loc); o=bpy.context.object; o.dimensions=size
 bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
 return finish(o,name,material,bone,bevel)
def cyl(name,a,b,r,material,bone,verts=32):
 a,b=Vector(a),Vector(b); v=b-a
 bpy.ops.mesh.primitive_cylinder_add(vertices=verts,radius=r,depth=v.length,location=(a+b)/2)
 o=bpy.context.object; o.rotation_euler=v.to_track_quat('Z','Y').to_euler()
 bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
 return finish(o,name,material,bone,.003)
def rod(name,a,b,r,material,bone):return cyl(name,a,b,r,material,bone,16)
def ring(name,x,y,z,r,thick,material,bone):
 bpy.ops.mesh.primitive_torus_add(major_segments=48,minor_segments=8,location=(x,y,z),major_radius=r,minor_radius=thick,rotation=(0,pi/2,0))
 return finish(bpy.context.object,name,material,bone)
def bone(name,a,b,parent=None):bone_defs.append((name,a,b,parent))
def contour(w,h,n=40,p=4):
 return [(w/2*math.copysign(abs(cos(2*pi*i/n))**(2/p),cos(2*pi*i/n)),h/2*math.copysign(abs(sin(2*pi*i/n))**(2/p),sin(2*pi*i/n))) for i in range(n)]
def loft(name,loops,material,bone,cap=True):
 n=len(loops[0]); v=[p for loop in loops for p in loop]; f=[]
 for j in range(len(loops)-1):
  for i in range(n):f.append((j*n+i,j*n+(i+1)%n,(j+1)*n+(i+1)%n,(j+1)*n+i))
 if cap:f.extend([tuple(range(n-1,-1,-1)),tuple((len(loops)-1)*n+i for i in range(n))])
 if name=='FaceDisplay':f.append(tuple((len(loops)-1)*n+i for i in range(n)))
 return mesh(name,v,f,material,bone)
def plate(name,outline,y,depth,material,bone,bevel=.012):
 n=len(outline); v=[(x,yy,z) for yy in (y-depth/2,y+depth/2) for x,z in outline]
 f=[tuple(range(n-1,-1,-1)),tuple(range(n,2*n))]+[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
 return mesh(name,v,f,material,bone,bevel)
bone('Root',(0,0,0),(0,0,.12))
bone('Chassis',(0,0,.225),(0,0,.51),'Root')
bone('Wheel',(0,0,.225),(.12,0,.225),'Chassis')
bone('Waist',(0,0,.51),(0,0,.62),'Chassis')
bone('Torso',(0,0,.62),(0,0,.825),'Waist')
bone('Neck',(0,0,.825),(0,0,.87),'Torso')
bone('Head',(0,0,.87),(0,0,1.08),'Neck')
# Continuous tyre with recessed chevron channels, not floating tread decals.
profile=[(-.103,.166),(-.105,.19),(-.096,.212),(-.075,.223),(-.04,.225),(0,.225),(.04,.225),(.075,.223),(.096,.212),(.105,.19),(.103,.166)]
loops=[]
for k in range(32):
 for phase,depth in [(0,.0035),(.10,0),(.87,0),(.98,.0035)]:
  loop=[]
  for x,r in profile:
   t=2*pi*(k+phase+abs(x)/.105*.35)/32
   rr=r-depth; loop.append((x,-rr*sin(t),.225+rr*cos(t)))
  loops.append(loop)
# close angle and connect manually because profile is open and circle is closed
v=[p for l in loops for p in l]; f=[]; n=len(profile); m=len(loops)
for j in range(m):
 for i in range(n-1):f.append((j*n+i,((j+1)%m)*n+i,((j+1)%m)*n+i+1,j*n+i+1))
mesh('Chevron radial tyre',v,f,rubber,'Wheel')
for s in [-1,1]:
 cyl('Wheel sidewall',(s*.087,0,.225),(s*.108,0,.225),.184,rubber,'Wheel',48)
 cyl('Rim backing',(s*.106,0,.225),(s*.118,0,.225),.154,dark,'Wheel',48)
 ring('Outer luminous rim',s*.12,0,.225,.142,.012,cyan,'Wheel')
 cyl('Dish hub',(s*.118,0,.225),(s*.13,0,.225),.118,dark,'Wheel',48)
 ring('Inner luminous rim',s*.133,0,.225,.081,.006,cyan,'Wheel')
 cyl('Axle cap',(s*.129,0,.225),(s*.137,0,.225),.067,dark,'Wheel')
 for k in range(6):
  t=2*pi*k/6; y=.111*sin(t); z=.225+.111*cos(t)
  cyl('Hub fastener',(s*.129,y,z),(s*.133,y,z),.005,white,'Wheel',12)
# A swept, bevelled arch hugs the wheel and is rigid to Chassis.
cross=[(-.139,.237),(-.153,.246),(-.153,.269),(-.133,.283),(.133,.283),(.153,.269),(.153,.246),(.139,.237)]
loops=[]
for j in range(33):
 t=math.radians(-76+152*j/32)
 loops.append([(x,-r*sin(t),.225+r*cos(t)) for x,r in cross])
loft('Swept wheel armour',loops,white,'Chassis')
for s in [-1,1]:
 rod('Fork stay',(s*.143,0,.225),(s*.143,.015,.445),.023,dark,'Chassis')
 cyl('Suspension pivot',(s*.14,0,.445),(s*.166,0,.445),.042,dark,'Chassis')
 ring('Suspension LED',s*.17,0,.445,.029,.005,cyan,'Chassis')
plate('Front fender brow',[(-.111,.493),(.111,.493),(.125,.41),(.09,.383),(-.09,.383),(-.125,.41)],-.188,.045,white,'Chassis')
box('Fender inset',(0,-.215,.467),(.094,.012,.035),dark,'Chassis',.012)
box('Fender status light',(0,-.223,.467),(.072,.008,.018),cyan,'Chassis',.008)
rear=box('Rear fender inset',(0,.239,.389),(.095,.018,.039),dark,'Chassis',.011); rear.rotation_euler.x=.6
rear=box('Rear fender light',(0,.249,.396),(.072,.009,.02),cyan,'Chassis',.008); rear.rotation_euler.x=.6
# Segmented spine and clean chest, without any fabric.
cyl('Waist spindle',(0,0,.503),(0,0,.625),.043,dark,'Waist')
for z in [.528,.562,.597]:box('Waist vertebra',(0,0,z),(.108,.098,.025),dark,'Waist',.007)
box('Pelvis collar',(0,0,.511),(.145,.125,.03),dark,'Chassis',.012)
outline=[(-.165,.807),(-.129,.833),(.129,.833),(.165,.807),(.139,.704),(.071,.648),(-.071,.648),(-.139,.704)]
plate('Chest inner chassis',outline,0,.183,dark,'Torso',.025)
outline=[(-.151,.808),(-.102,.828),(.102,.828),(.151,.808),(.119,.715),(.055,.668),(-.055,.668),(-.119,.715)]
plate('Sculpted chest armour',outline,-.058,.13,white,'Torso',.018)
plate('Central sternum',[(-.066,.797),(.066,.797),(.057,.725),(0,.691),(-.057,.725)],-.134,.018,white,'Torso',.008)
box('Chest power recess',(0,-.148,.783),(.088,.01,.024),dark,'Torso',.009)
box('Chest power bar',(0,-.155,.783),(.064,.007,.009),cyan,'Torso',.004)
plate('Rear service plate',[(-.111,.795),(.111,.795),(.095,.709),(-.095,.709)],.105,.025,white,'Torso',.012)
for z in [.738,.754,.770]:box('Rear cooling louvre',(0,.122,z),(.099,.007,.005),dark,'Torso',.002)
cyl('Neck swivel',(0,0,.825),(0,0,.881),.053,dark,'Neck')
cyl('Neck bearing',(0,0,.839),(0,0,.852),.063,white,'Neck')
# TV shell: purpose-built rounded rectangular loft, open at front.
loops=[]
for y,w,h,zc in [(-.165,.382,.233,1.004),(-.145,.423,.272,1.004),(-.11,.447,.286,1.004),(.085,.447,.286,1.004),(.132,.418,.26,1.004),(.153,.35,.218,1.004),(.16,.26,.16,1.004)]:
 loops.append([(x,y,z+zc) for x,z in contour(w,h)])
head=loft('Rounded TV casing',loops,white,'Head',False)
bpy.context.view_layer.objects.active=head
mod=head.modifiers.new('Continuous curved casing','SUBSURF');mod.levels=1
bpy.ops.object.modifier_apply(modifier=mod.name)
plate('Rear TV panel',[(x,z+1.004) for x,z in contour(.285,.173)],.156,.016,white,'Head',.004)
loops=[]
for y,w,h in [(-.166,.383,.234),(-.174,.369,.222),(-.177,.35,.202)]:loops.append([(x,y,z+1.004) for x,z in contour(w,h)])
loft('Screen gasket',loops,dark,'Head',False)
# Concentric quad rings create a softly convex, expression-free screen.
loops=[]
for scale in [1,.75,.5,.25,.025]:
 loops.append([(x,-.179-.009*(1-scale*scale),z+1.004) for x,z in contour(.35*scale,.202*scale)])
screen=loft('FaceDisplay',loops,black,'Head',False)
uv=screen.data.uv_layers.new(name='UVMap')
unit=screen.data.uv_layers.new(name='Screen01')
for poly in screen.data.polygons:
 for li in poly.loop_indices:
  co=screen.data.vertices[screen.data.loops[li].vertex_index].co
  u=co.x/.35+.5; vv=(co.z-1.004)/.202+.5
  uv.data[li].uv=(.38040865+vv*.23918271,.7526041+(1-u)*.24479163)
  unit.data[li].uv=(u,vv)
screen.data.uv_layers.active_index=0
box('Rear head status bezel',(0,.174,.999),(.108,.015,.037),dark,'Head',.012)
box('Rear head status LED',(0,.184,.999),(.083,.008,.02),cyan,'Head',.008)
box('Crown cap',(0,.02,1.151),(.211,.14,.033),dark,'Head',.014)
for s in [-1,1]:
 cyl('Ear root',(s*.212,.015,1.0),(s*.24,.015,1.0),.09,dark,'Head')
 cyl('Ear armour',(s*.237,.015,1.0),(s*.254,.015,1.0),.078,white,'Head')
 cyl('Ear inset',(s*.251,.015,1.0),(s*.26,.015,1.0),.063,dark,'Head')
 ring('Ear light ring',s*.263,.015,1.0,.055,.006,cyan,'Head')
 cyl('Ear centre',(s*.26,.015,1.0),(s*.267,.015,1.0),.044,dark,'Head')
# Single angled antenna, as in the turnaround.
rod('Antenna hinge',(.18,.035,1.105),(.215,.035,1.145),.019,dark,'Head')
ant=box('Signal fin',(.244,.035,1.193),(.047,.041,.132),dark,'Head',.009); ant.rotation_euler[1]=.58
ant=box('Signal strip',(.244,.010,1.193),(.022,.009,.104),cyan,'Head',.004); ant.rotation_euler[1]=.58
# Mechanical arms, tapered armour, segmented fingers and attachment bones.
for s,label in [(-1,'L'),(1,'R')]:
 shoulder=(s*.17,0,.784); elbow=(s*.235,-.005,.672); wrist=(s*.281,-.033,.526); hand=(s*.285,-.038,.493)
 bsh='Shoulder.'+label; bu='UpperArm.'+label; bf='Forearm.'+label; bh='Hand.'+label
 bone(bsh,shoulder,(s*.195,0,.765),'Torso'); bone(bu,shoulder,elbow,bsh); bone(bf,elbow,wrist,bu); bone('Wrist.'+label,wrist,hand,bf); bone(bh,hand,(s*.287,-.045,.46),'Wrist.'+label)
 cyl('Shoulder bearing', (s*.149,0,.785),(s*.196,0,.785),.052,dark,bsh)
 cyl('Shoulder cap',(s*.19,0,.785),(s*.21,0,.785),.046,white,bsh)
 rod('Upper arm piston',shoulder,elbow,.026,dark,bu)
 rod('Upper arm sleeve',(s*.195,0,.749),(s*.219,-.003,.704),.033,white,bu)
 cyl('Elbow axle',(s*.209,-.005,.672),(s*.26,-.005,.672),.033,dark,bf)
 cyl('Elbow cap',(s*.258,-.005,.672),(s*.264,-.005,.672),.021,white,bf)
 # Elliptical cross sections form a tapered, rounded gauntlet.
 a=Vector(elbow); b=Vector(wrist); d=(b-a).normalized(); vx=Vector((0,1,0)); vy=d.cross(vx).normalized()
 loops=[]
 for t,r in [(.12,.032),(.21,.043),(.76,.049),(.91,.039)]:
  c=a.lerp(b,t); loops.append([tuple(c+vx*cos(i*2*pi/16)*r*.84+vy*sin(i*2*pi/16)*r) for i in range(16)])
 loft('Forearm shell.'+label,loops,white,bf)
 led=box('Forearm LED well',(s*.268,-.071,.59),(.025,.012,.047),dark,bf,.008); led.rotation_euler[1]=s*.27
 led=box('Forearm LED',(s*.268,-.079,.594),(.014,.006,.027),cyan,bf,.005); led.rotation_euler[1]=s*.27
 rod('Wrist ball',wrist,hand,.025,dark,'Wrist.'+label)
 palm=box('Palm.'+label,(s*.285,-.038,.48),(.069,.05,.048),dark,bh,.014)
 box('Knuckle armour.'+label,(s*.285,-.008,.481),(.057,.012,.033),white,bh,.008)
 for i in range(3):
  x=s*(.264+i*.022); start=(x,-.04,.462); mid=(x,-.052,.429); end=(x,-.071,.415)
  bn='Finger%d.01.%s'%(i+1,label); bn2='Finger%d.02.%s'%(i+1,label)
  bone(bn,start,mid,bh); bone(bn2,mid,end,bn)
  rod('Finger proximal',start,mid,.009,dark,bn); rod('Finger tip',mid,end,.0085,dark,bn2)
 start=(s*.247,-.035,.49); mid=(s*.234,-.058,.463); end=(s*.244,-.08,.451)
 bone('Thumb.01.'+label,start,mid,bh);bone('Thumb.02.'+label,mid,end,'Thumb.01.'+label)
 rod('Thumb proximal',start,mid,.012,dark,'Thumb.01.'+label);rod('Thumb tip',mid,end,.01,dark,'Thumb.02.'+label)
 bone('WeaponSocket.'+label,(s*.285,-.065,.455),(s*.285,-.115,.455),bh)
# Rigid skinning avoids rubbery armour and consolidates to five material draws.
arm=bpy.data.armatures.new('Wheelie_Skeleton'); rig=bpy.data.objects.new('Wheelie',arm); scene.collection.objects.link(rig)
bpy.context.view_layer.objects.active=rig; rig.select_set(True)
bpy.ops.object.mode_set(mode='EDIT')
for name,a,b,parent in bone_defs:
 eb=arm.edit_bones.new(name); eb.head=a; eb.tail=b
 if parent:eb.parent=arm.edit_bones[parent]
bpy.ops.object.mode_set(mode='OBJECT'); rig.show_in_front=True
for o in parts:o.select_set(False)
bodyparts=[o for o in parts if o!=screen]
for o in bodyparts:o.select_set(True)
bpy.context.view_layer.objects.active=bodyparts[0]; rig.select_set(False)
bpy.ops.object.join(); body=bpy.context.object; body.name='Wheelie_Body'
for o in [body,screen]:
 bpy.context.view_layer.objects.active=o
 bpy.context.scene.cursor.location=(0,0,0); bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
 o.parent=rig; mod=o.modifiers.new('Wheelie rigid skin','ARMATURE'); mod.object=rig
 # Export-ready UVs for the non-textured solid-colour armour.
 if o==body:
  bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT'); bpy.ops.uv.smart_project(island_margin=.015); bpy.ops.object.mode_set(mode='OBJECT')
rig['Design']='Wheelie / Couch Guys / Model 03; no fabric, logos or baked face'
rig['Wheel axle']='Wheel bone head at (0, 0, 0.225)m; local Y is axle / Blender global X'
rig['Face UV']='UVMap matches existing RobotFaceDisplaySettings rotated island; Screen01 is optional 0..1'
rig['Weapon attachment']='WeaponSocket.L and WeaponSocket.R; bone local +Y points forward in grip'
rig['Scale']='Metres; ground origin; Blender front -Y, FBX Unity forward +Z / up +Y'
# Player FBX uses centimetre transforms and a 50-scale prefab root. Keep Wheelie
# at 1.14m including antenna, below the approximately 1.2m player shell.
for o in [body,screen]:
 for vertex in o.data.vertices:vertex.co*=.9
bpy.context.view_layer.objects.active=rig
bpy.ops.object.mode_set(mode='EDIT')
for eb in arm.edit_bones:eb.head*=.9;eb.tail*=.9
bpy.ops.object.mode_set(mode='OBJECT')
rig['Wheel axle']='Wheel head (0,0,0.2025)m; rotate local Y; housing weights are Chassis'
# Studio objects are excluded from the selected FBX export.
studio=bpy.data.collections.new('STUDIO - excluded from FBX');scene.collection.children.link(studio)
def to_studio(o):
 for c in list(o.users_collection):c.objects.unlink(o)
 studio.objects.link(o)
floor=mat('Studio only',(.075,.092,.13),.58)
bpy.ops.mesh.primitive_plane_add(size=200); ground=bpy.context.object; ground.name='Studio floor'; ground.data.materials.append(floor);ground.location.z=-.002;to_studio(ground)
def aim(o,pt):o.rotation_euler=(Vector(pt)-o.location).to_track_quat('-Z','Y').to_euler()
for name,loc,power,size,color in [('Key',(-2,-3,4),350,3,(.82,.9,1)),('Fill',(2,-1,2),180,2,(.65,.84,1)),('Rim',(1,2,3),420,2,(.8,.87,1))]:
 d=bpy.data.lights.new(name,'AREA');d.energy=power;d.shape='DISK';d.size=size;d.color=color
 o=bpy.data.objects.new(name,d);studio.objects.link(o);o.location=loc;aim(o,(0,0,.65))
world=bpy.data.worlds.new('Wheelie studio');world.use_nodes=True
next(n for n in world.node_tree.nodes if n.type=='BACKGROUND').inputs[0].default_value=(.12,.15,.22,1);scene.world=world
d=bpy.data.cameras.new('Review camera');camera=bpy.data.objects.new('Review camera',d);studio.objects.link(camera);scene.camera=camera;d.type='ORTHO';d.ortho_scale=1.48
try:scene.render.engine='CYCLES'
except TypeError:pass
scene.cycles.samples=32;scene.cycles.use_denoising=True
scene.render.resolution_x=900;scene.render.resolution_y=1000;scene.render.resolution_percentage=100
scene.view_settings.view_transform='AgX'
scene.render.image_settings.file_format='PNG'
for o in bpy.context.selected_objects:o.select_set(False)
for o in [rig,body,screen]:o.select_set(True)
bpy.context.view_layer.objects.active=rig
camera.location=(1.7,-2.7,1.35);aim(camera,(0,0,.62))
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(SOURCE,'Wheelie.blend'))
bpy.ops.export_scene.fbx(filepath=os.path.join(OUT,'Wheelie.fbx'),use_selection=True,object_types={'MESH','ARMATURE'},apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',axis_forward='-Z',axis_up='Y',add_leaf_bones=False,bake_anim=False,use_armature_deform_only=False,path_mode='AUTO')
report={'vertices':sum(len(o.data.vertices) for o in [body,screen]),'triangles':sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in [body,screen]),'bones':len(arm.bones),'materials':[m.name for m in body.data.materials]+[black.name],'root':list(rig.location),'axle':list(arm.bones['Wheel'].head_local),'unweighted':sum(1 for o in [body,screen] for v in o.data.vertices if not v.groups),'face_uv': 'UVMap existing rotated island; Screen01 normalized'}
with open(os.path.join(SOURCE,'validation.json'),'w') as f:json.dump(report,f,indent=2)
print('WHEELIE_REPORT',json.dumps(report))
for name,loc in [('front',(0,-3,.67)),('side',(3,0,.67)),('rear',(0,3,.67)),('three_quarter',(1.8,-2.8,1.35))]:
 camera.location=loc;aim(camera,(0,0,.62));scene.render.filepath=os.path.join(SOURCE,'Wheelie_'+name+'.png');bpy.ops.render.render(write_still=True)
