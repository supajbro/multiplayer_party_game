"""Run with Blender -b CouchGuy_Redesigned.blend --python this_file -- <review number>."""
import math
from pathlib import Path
import sys

import bpy
import numpy as np
from mathutils import Matrix, Vector

stage = int(sys.argv[sys.argv.index("--") + 1])
export_only = "--export-only" in sys.argv
renders_only = "--renders-only" in sys.argv
preview_all_actions = "--preview-actions" in sys.argv
project = Path(__file__).resolve().parents[2]
source = project / "ArtSource" / "HoverDelivery"
review = project / "Temp" / "HoverDelivery" / f"review{stage}"
review.mkdir(parents=True, exist_ok=True)
scene = bpy.context.scene
rig = bpy.data.objects["CouchGuy_Rig"]
root = rig.parent
root.name = "HoverDelivery"
preview_weapons = {
    name: bpy.data.objects.get("Weapon_"+name)
    for name in ("Pistol","AssaultRifle","Shotgun")
} if stage >= 4 else {}
weapon_objects = {item for weapon in preview_weapons.values() if weapon
                  for item in [weapon, *weapon.children_recursive]}
rig.animation_data.action = None
for track in list(rig.animation_data.nla_tracks):
    rig.animation_data.nla_tracks.remove(track)
rig.matrix_basis.identity()
for bone in rig.pose.bones:
    bone.matrix_basis.identity()
for obj in list(scene.objects):
    if obj not in (rig, root) and obj not in weapon_objects:
        bpy.data.objects.remove(obj, do_unlink=True)
for obj in weapon_objects:
    obj.hide_render = True
for action in bpy.data.actions:
    action.use_fake_user = True
bpy.context.view_layer.update()

def material(name, colour, roughness, metal=0, emission=0):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    shader = mat.node_tree.nodes.get("Principled BSDF")
    shader.inputs["Base Color"].default_value = (*colour, 1)
    shader.inputs["Roughness"].default_value = roughness
    shader.inputs["Metallic"].default_value = metal
    shader.inputs["Coat Weight"].default_value = .32 if name == "Porcelain" else .15
    shader.inputs["Emission Color"].default_value = (*colour, 1)
    shader.inputs["Emission Strength"].default_value = emission
    mat.diffuse_color = (*colour, 1)
    return mat

white = material("Porcelain", (.94, .91, 1), .26, .08)
trim = material("Ceramic Trim", (.65, .67, .81), .31, .23)
dark = material("Graphite", (.038, .047, .078), .32, .40)
rubber = material("Joint Seals", (.011, .016, .026), .47, .15)
blue = material("Anodised Blue", (.025, .19, .38), .30, .55)
light = material("Cyan Light", (.001, .64, .88), .24, .05, 1.55 if stage >= 2 else 2.8)
if stage >= 4:
    emitter = light.node_tree.nodes.get("Principled BSDF")
    emitter.inputs["Emission Color"].default_value = (0,.48,.72,1)
    emitter.inputs["Emission Strength"].default_value = 2.65
glass = material("Display Glass", (.007, .015, .024), .28, .25)
parts = {}

def mesh(name, vertices, faces, mat):
    data = bpy.data.meshes.new(name)
    data.from_pydata(vertices, [], faces)
    data.update()
    obj = bpy.data.objects.new(name, data)
    scene.collection.objects.link(obj)
    assign(obj, mat)
    return obj

def assign(obj, mat):
    obj.data.materials.append(mat)
    for polygon in obj.data.polygons:
        polygon.use_smooth = True

def build_surface(name, rings, mat, mapping=None, segments=64, caps=True):
    vertices = []
    for z, rx, ry, power in rings:
        for i in range(segments):
            angle = 2 * math.pi * i / segments
            def component(value, radius):
                return radius * math.copysign(abs(value) ** power, value)
            xy = component(math.cos(angle), rx), component(math.sin(angle), ry)
            vertices.append(mapping(*xy, z) if mapping else (*xy, z))
    faces = []
    for j in range(len(rings)-1):
        a, b = j * segments, (j + 1) * segments
        for i in range(segments):
            n = (i + 1) % segments
            faces.append((a+i, a+n, b+n, b+i))
    if caps:
        faces += [tuple(range(segments-1, -1, -1)),
                  tuple(range(len(vertices)-segments, len(vertices)))]
    return mesh(name, vertices, faces, mat)

def box(name, p, size, bevel, mat, rotation=None, segments=4):
    bpy.ops.mesh.primitive_cube_add(size=1, location=p)
    obj = bpy.context.object
    obj.name = name
    obj.dimensions = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if rotation is not None:
        obj.rotation_euler = rotation
    assign(obj, mat)
    edge = obj.modifiers.new("Rounded edges", "BEVEL")
    edge.width = bevel
    edge.segments = segments
    normal = obj.modifiers.new("Panel normals", "WEIGHTED_NORMAL")
    normal.keep_sharp = True
    return obj

def sphere(name, p, radii, mat, segments=20, rings=12):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segments, ring_count=rings, location=p)
    obj = bpy.context.object
    obj.name = name
    obj.scale = radii
    assign(obj, mat)
    return obj

def capsule(name, a, b, radius, mat, ellipticity=1):
    a, b = Vector(a), Vector(b)
    obj = sphere(name, (a+b)/2, (radius, radius*ellipticity, (b-a).length/2+radius*.45), mat)
    obj.rotation_euler = (b-a).to_track_quat("Z", "Y").to_euler()
    return obj

def collar(name, p, radii, mat, side=0):
    start, end, radius = radii
    edge = min((end-start)*.28, radius*.065)
    profile = [(start, radius-edge, radius-edge, 1), (start+edge, radius, radius, 1),
               (end-edge, radius, radius, 1), (end, radius-edge, radius-edge, 1)]
    if side:
        mapping = (lambda x,y,z: (side*(p[0]+z), p[1]+x, p[2]+side*y))
    else:
        mapping = (lambda x,y,z: (p[0]+x,p[1]+y,p[2]+z))
    return build_surface(name, profile, mat, mapping, 36)

def torus(name, p, radius, width, mat, side=False):
    bpy.ops.mesh.primitive_torus_add(major_segments=48, minor_segments=10, location=p,
                                    major_radius=radius, minor_radius=width)
    obj = bpy.context.object
    obj.name = name
    if side:
        obj.rotation_euler.y = math.pi/2
    assign(obj, mat)
    return obj

def attach(name, bone, objects):
    # Join by rigid attachment, so the Unity prefab can share the original bone rig
    # while the FBX also works as an independently skinned model.
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        for mod in list(obj.modifiers):
            bpy.ops.object.modifier_apply(modifier=mod.name)
    if len(objects) > 1:
        bpy.ops.object.join()
    obj = bpy.context.object
    obj.name = name
    obj.data.name = name
    obj.parent = root
    group = obj.vertex_groups.new(name=bone)
    group.add(list(range(len(obj.data.vertices))), 1, "REPLACE")
    deform = obj.modifiers.new("Rigid segment", "ARMATURE")
    deform.object = rig
    obj["attachment"] = bone
    parts[name] = obj
    return obj

head_z = 1.92 if stage == 0 else 1.945
head_w = .69 if stage == 0 else .728
head_h = .438 if stage == 0 else .475
def helmet_mapping(x, z, depth):
    return x, -depth, head_z+z

helmet = build_surface("Helmet", [
    (-.49,.45,head_h*.70,.58),(-.47,.55,head_h*.82,.56),
    (-.42,.635,head_h*.92,.52),(-.31,head_w*.97,head_h*.99,.52),
    (-.16,head_w,head_h,.52),(.09,head_w*.996,head_h*.99,.55),
    (.26,head_w*.93,head_h*.93,.59),(.40,head_w*.75,head_h*.77,.63),
    (.455,head_w*.47,head_h*.49,.70),(.462,.005,.005,1)], white, helmet_mapping)
visor_setback = .041 if stage >= 2 else 0
bezel = build_surface("Visor gasket", [(d-visor_setback,w,h,p) for d,w,h,p in [
    (.347,.602,.365,.55),(.425,.616,.372,.53),(.461,.600,.356,.53),
    (.481,.568,.331,.54),(.483,.555,.318,.54)]], rubber, helmet_mapping)
head_parts = [helmet, bezel]

def screen():
    # The UV layout uses the whole 0–1 screen rectangle, as expected by RobotFaceDisplay.
    verts = [(0,-.525+visor_setback,head_z)]
    rows, count = 6, 64
    width, height = .553,.315
    for level in range(1, rows+1):
        r = level/rows
        for i in range(count):
            t = 2*math.pi*i/count
            x = r*width*math.copysign(abs(math.cos(t))**.55, math.cos(t))
            z = r*height*math.copysign(abs(math.sin(t))**.55, math.sin(t))
            verts.append((x,-.525+visor_setback+.039*r*r,head_z+z))
    faces = [(0,1+i,1+(i+1)%count) for i in range(count)]
    for row in range(rows-1):
        a, b = 1+row*count, 1+(row+1)*count
        for i in range(count):
            n = (i+1)%count
            faces.append((a+i,b+i,b+n,a+n))
    obj = mesh("Display Glass", verts, faces, glass)
    uv = obj.data.uv_layers.new(name="Display UV")
    for loop in obj.data.loops:
        x, _, z = verts[loop.vertex_index]
        uv.data[loop.index].uv = (x/width*.5+.5, (z-head_z)/height*.5+.5)
    return obj

for side in (-1,1):
    p = (.665, .007, head_z-.005)
    head_parts += [
        collar("Ear bearing",p,(0,.105,.266),rubber,side),
        collar("Ear housing",p,(.068,.181,.232),dark,side),
        collar("Ear diffuser inset",p,(.171,.202,.195),blue,side),
        torus("Ear light", (side*.874,p[1],p[2]),.164,.021 if stage<2 else .026,light,True),
        collar("Ear centre",p,(.187,.219,.130),dark,side)
    ]
    base = Vector((side*.756,.025,head_z+.179))
    tip = Vector((side*(.908 if stage==0 else 1.012),.053,2.648 if stage==0 else 2.665))
    vector = tip-base
    antenna_rot = vector.to_track_quat("Z","Y").to_euler()
    mid = (base+tip)/2
    fin_width = .201 if stage >= 2 else .128
    head_parts += [
        capsule("Antenna hinge",base-Vector((0,0,.015)),base+vector*.28,.066,rubber),
        box("Antenna fin",mid+vector*.081,(fin_width,.101 if stage >= 2 else .085,vector.length*.73),.025,dark,antenna_rot,3),
        box("Antenna trim",mid+vector*.13+Vector((0,-.050,0)),(fin_width*.79,.025,vector.length*.59),.011,blue,antenna_rot,3),
        box("Antenna light",mid+vector*.15+Vector((side*.024,-.065,0)),(fin_width*.41,.020,vector.length*.49),.009,light,antenna_rot,3)
    ]
if stage >= 2:
    head_parts.append(box("Recessed top service seam",(0,.095,head_z+.467),(.34,.24,.022),.010,white if stage >= 3 else trim))
attach("DeliveryHelmet", "Head", head_parts)
attach("DeliveryFace", "Head", [screen()])

body_width = .507 if stage == 0 else .531
torso = build_surface("Chest housing", [
    (.879,.269,.196,.55),(.903,.344,.234,.55),(.977,.448,.272,.56),
    (1.145,body_width,.313,.54),(1.244,body_width*.98,.304,.51),
    (1.319,.418,.244,.51),(1.345,.335,.204,.55)], white,
    lambda x,y,z:(x,y+.016,z))
breast = build_surface("Flush chest panel",[
    (.250,.298,.141,.43),(.294,.336,.159,.50),(.315,.318,.139,.52),
    (.322 if stage >= 2 else .330,.274,.119,.55)], white,
    lambda x,z,y: (x*(1+.59*z),-y,1.135+z))
attach("DeliveryChest", "Torso", [torso, breast,
    collar("Neck base",(0,0,1.352),(-.028,.132,.146),rubber),
    collar("Neck bearing",(0,0,1.424),(0,.075,.166),dark),
    collar("Lumbar upper flange",(0,.026,.875),(-.043,.036,.219),dark),
    collar("Lumbar cylinder",(0,.026,.748),(-.10,.115,.152),rubber),
    collar("Lumbar lower flange",(0,.026,.638),(-.030,.048,.212),dark),
    collar("Collar lip",(0,.026,.613),(-.017,.010,.236),trim),
])

pack = [box("Backpack mount",(0,.288,1.125),(.634,.216,.456),.102,rubber),
        box("Backpack housing",(0,.378,1.101),(.592,.238,.412),.083,dark),
        box("Backpack rear cover",(0,.494,1.073),(.395,.073,.279),.033,dark),
        box("Status recess",(0,.535,1.106),(.306,.026,.112),.012,rubber),
        box("Cyan status bar",(0,.553,1.106),(.237,.019,.051),.009,light)]
if stage >= 2:
    for side in (-1,1):
        pack += [box("Side isolator",(side*.298,.358,1.12),(.067,.201,.303),.03,trim)]
        for z in (.984,1.029):
            pack += [box("Vent recess",(side*.222,.483,z),(.081,.025,.016),.007,rubber)]
attach("DeliveryBackpack", "Torso", pack)

radius = .637 if stage == 0 else .682
def base_shape(name, profile, mat):
    return build_surface(name, [(z,r,r*.94,1) for z,r in profile],mat,
                         lambda x,y,z:(x,y+.035,z))
attach("DeliveryHover", "Root", [
    base_shape("Porcelain hover fairing",[(.396,radius*.95),(.414,radius),
        (.441,radius*.999),(.484,radius*.951),(.55,radius*.82),(.62,radius*.65),
        (.644,radius*.53),(.651,.04)],white),
    base_shape("Fairing gasket",[(.352,radius*.894),(.376,radius*.944),
        (.407,radius*.969),(.412,radius*.965)],rubber),
    base_shape("Wide plasma ring",[(.302,radius*.802),(.321,radius*.875),
        (.354,radius*.904),(.369,radius*.912)],light),
    base_shape("Nozzle casing",[(.194,.31),(.219,.428),(.251,.490),(.304,.535),
        (.319,radius*.825),(.335,radius*.82)],blue if stage>=2 else dark),
    base_shape("Central plasma lens",[(.147,.018),(.154,.146),(.176,.249),
        (.203,.301),(.230,.309)],light),
])

for side, sign in (("L",-1),("R",1)):
    upper, lower, hand = [rig.data.bones[n+"_"+side] for n in ("UpperArm","LowerArm","Hand")]
    shoulder, elbow, wrist = [bone.head_local for bone in (upper,lower,hand)]
    u, v = (elbow-shoulder).normalized(), (wrist-elbow).normalized()
    if stage >= 3:
        # Taper the cuff in forearm coordinates to leave space at both moving joints.
        basis = v.to_track_quat("Z","Y")
        forearm = build_surface("Forearm porcelain",[
            (.073,.070,.066,.87),(.098,.109,.094,.80),(.131,.127,.109,.80),
            (.216,.128,.110,.80),(.248,.111,.099,.82),(.269,.088,.079,.88),
        ],white,lambda x,y,z:tuple(elbow+basis@Vector((x,y,z))),32)
    else:
        forearm = capsule("Forearm porcelain",elbow+v*(.106 if stage==0 else .079),wrist-v*.038,
                          .138 if stage==0 else .144 if stage==1 else .133,white,.96)
    attach("DeliveryShoulder_"+side,upper.name,[
        sphere("Shoulder actuator",shoulder,(.178,.174,.177),rubber),
        capsule("Upper link",shoulder+u*.083,elbow-u*.014,.091,dark),
        capsule("Shoulder porcelain",shoulder + u*.016+Vector((sign*.03,-.100 if stage>=4 else -.075 if stage>=2 else 0,.055)),
                shoulder+u*(.175 if stage<2 else .153)+Vector((sign*.03,-.079 if stage>=2 else -.055 if stage else 0,.075)),
                .145 if stage==0 else .154 if stage==1 else .160 if stage>=4 else .147,white,1.13 if stage>=4 else .93),
    ])
    attach("DeliveryForearm_"+side,lower.name,[
        sphere("Elbow bellows",elbow,(.111,.112,.109),dark),
        forearm,
        capsule("Forearm light recess",elbow+v*.108+Vector((sign*.105,-.032,.049)),
                wrist-v*.086+Vector((sign*.105,-.032,.049)),.034,dark),
        capsule("Forearm indicator",elbow+v*.116+Vector((sign*.125,-.038,.059)),
                wrist-v*.092+Vector((sign*.125,-.038,.059)),.020,light),
    ])
    forward = (hand.tail_local-wrist).normalized()
    across = forward.cross(Vector((0,0,1))).normalized()
    grip_center = wrist + forward*.109
    palm = [
        sphere("Wrist ball",wrist,(.093,.091,.093),rubber),
        capsule("Palm",wrist+forward*.056,wrist+forward*.144,
                .124 if stage==0 else .106,dark,.82),
        capsule("Opposing thumb",grip_center+across*sign*.095+Vector((0,0,.028)),
                grip_center+across*sign*.138+forward*.05-Vector((0,0,.038)),.039,dark)
    ]
    for i in range(3):
        off = across*((i-1)*(.067 if stage<3 else .060))
        knuckle = wrist+forward*(.162 if stage<3 else .151)+off
        palm.append(capsule("Finger link",knuckle+Vector((0,0,.022)),
                            knuckle+forward*.048-Vector((0,0,.012)),.032,dark))
        palm.append(capsule("Curled finger tip",knuckle+forward*.050-Vector((0,0,.022)),
                            knuckle+forward*(.033 if stage<3 else .022)-Vector((0,0,.062)),.029,rubber))
    attach("DeliveryHand_"+side,hand.name,palm)

def studio():
    scene.world = bpy.data.worlds.new("Periwinkle studio")
    scene.world.use_nodes = True
    scene.world.node_tree.nodes["Background"].inputs[0].default_value = (.40,.47,.72,1)
    scene.world.node_tree.nodes["Background"].inputs[1].default_value = .37
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 12
    scene.cycles.use_denoising = True
    scene.render.threads_mode = "FIXED"
    scene.render.threads = 8
    scene.view_settings.view_transform = "AgX"
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = False
    scene.use_nodes = False
    for name,p,energy,size,colour in [
        ("Key",(-3,-4,6),500,4.6,(.87,.92,1)),
        ("Fill",(3,-2,3.3),165,3,(.63,.79,1)),
        ("Rim",(2,3,4.7),590,3,(1,.82,.91)),
        ("Top",(-2,1,5),240,2.8,(1,1,1)),
        ("Lower reflector",(0,-2,-2),110,3,(.66,.76,1))]:
        lamp = bpy.data.lights.new(name,"AREA")
        lamp.energy, lamp.shape, lamp.size, lamp.color = energy,"DISK",size,colour
        obj = bpy.data.objects.new(name,lamp)
        scene.collection.objects.link(obj)
        obj.location = p
        obj.rotation_euler = (Vector((0,0,1.35))-obj.location).to_track_quat("-Z","Y").to_euler()
    data = bpy.data.cameras.new("Turntable")
    data.type = "ORTHO"
    data.ortho_scale = 3.12
    camera = bpy.data.objects.new("Turntable",data)
    scene.collection.objects.link(camera)
    scene.camera = camera
    return camera

camera = studio()
target = Vector((0,0,1.38))
views = {
    "01_Front": (0,-7,0), "02_Rear": (0,7,0), "03_Left": (-7,0,0),
    "04_Right": (7,0,0), "05_FrontThreeQuarter": (3.7,-6,1.45),
    "06_BackThreeQuarter": (-3.7,6,1.45), "07_Top": (0,-.008,7),
    "08_Underside": (0,-2,-7),
}

def render(name, offset, size, folder=review):
    camera.location = target+Vector(offset)
    camera.rotation_euler = (target-camera.location).to_track_quat("-Z","Y").to_euler()
    scene.render.resolution_x = scene.render.resolution_y = size
    scene.render.resolution_percentage = 100
    scene.render.filepath = str(folder / (name+".png"))
    bpy.ops.render.render(write_still=True)
    return Path(scene.render.filepath)

def sheet(paths, path, columns, rows):
    images = [bpy.data.images.load(str(p), check_existing=False) for p in paths]
    size = images[0].size[0]
    pixels = np.ones((rows*size,columns*size,4), dtype=np.float32)
    for i,img in enumerate(images):
        x, y = (i%columns)*size,(rows-1-i//columns)*size
        data = np.empty(size*size*4,dtype=np.float32)
        img.pixels.foreach_get(data)
        pixels[y:y+size,x:x+size] = data.reshape(size,size,4)
        bpy.data.images.remove(img)
    result = bpy.data.images.new(path.stem,width=columns*size,height=rows*size,alpha=False)
    result.pixels.foreach_set(pixels.ravel())
    result.filepath_raw = str(path)
    result.file_format = "PNG"
    result.save()
    bpy.data.images.remove(result)

def scenic_pose():
    for side in ("L","R"):
        for prefix, target_direction in (("UpperArm",( .52 if side=="R" else -.52,-.06,-.85)),
                                         ("LowerArm",(.35 if side=="R" else -.35,-.42,-.83)),
                                         ("Hand",(.11 if side=="R" else -.11,-.58,-.81))):
            bone = rig.pose.bones[prefix+"_"+side]
            direction = bone.matrix.to_3x3() @ Vector((0,1,0))
            rot = direction.rotation_difference(Vector(target_direction))
            orientation = rot.to_matrix().to_4x4() @ bone.matrix
            orientation.translation = bone.head
            bone.matrix = orientation
            bpy.context.view_layer.update()

def import_native_actions(path):
    if not path.is_file():
        return
    rest = {}
    actions = {}
    for line in path.read_text(encoding="utf-8-sig").splitlines():
        tag, *fields = line.split()
        if tag == "action":
            frames = actions.setdefault(" ".join(fields), {})
            continue
        name = fields[-17]
        data = list(map(float, fields[-16:]))
        matrix = Matrix(tuple(tuple(data[i:i+4]) for i in range(0,16,4)))
        if tag == "rest":
            rest[name] = matrix
        else:
            frames.setdefault(int(fields[0]), {})[name] = matrix
    # Four non-coplanar joints recover the FBX handedness, scale and offset.
    markers = ["Root", "Head", "UpperArm_L", "Hand_R"]
    def points(locations):
        return Matrix([(*locations[name],1) for name in markers]).transposed()
    unity = points({name: m.translation for name,m in rest.items()})
    blender = points({b.name: b.head_local for b in rig.data.bones})
    to_unity = unity @ blender.inverted()
    to_blender = to_unity.inverted()
    for name, frames in actions.items():
        action = bpy.data.actions.new(name)
        action.use_fake_user = True
        action.slots.new("OBJECT", rig.name)
        rig.animation_data.action = action
        rig.animation_data.action_slot = action.slots[0]
        prior = {}
        for frame, samples in sorted(frames.items()):
            wanted = {bone.name: to_blender @ samples[bone.name] @ to_unity @ bone.bone.matrix_local
                      for bone in rig.pose.bones}
            for bone in rig.pose.bones:
                parent = bone.parent
                rest_matrix = bone.bone.matrix_local
                desired = wanted[bone.name]
                if parent is not None:
                    local_rest = parent.bone.matrix_local.inverted() @ rest_matrix
                    basis = local_rest.inverted() @ wanted[parent.name].inverted() @ desired
                else:
                    basis = rest_matrix.inverted() @ desired
                location, rotation, scale = basis.decompose()
                euler = rotation.to_euler("XYZ", prior[bone.name]) if bone.name in prior else rotation.to_euler("XYZ")
                prior[bone.name] = euler
                bone.rotation_mode = "XYZ"
                bone.location = location
                bone.rotation_euler = euler
                bone.scale = scale
                for channel in ("location","rotation_euler","scale"):
                    bone.keyframe_insert(data_path=channel, frame=frame, group=bone.name)
        rig.animation_data.action = None
        print("NATIVE ACTION", name, len(frames))
    for bone in rig.pose.bones:
        bone.matrix_basis.identity()

native_path = project / "Logs" / "HoverDelivery" / "NativeActions.txt"
if stage >= 4:
    import_native_actions(native_path)

scenic_pose()
size = 330 if stage < 3 else 480
if not export_only:
    paths = [render(name, offset, size) for name,offset in views.items()]
    sheet(paths, review / "Turnaround.png", 4, 2)

if stage >= 2 and not export_only:
    # Sample representative action poses with the unchanged skeleton, for clearance review.
    poses = [("Idle",1),("AssaultRifle_Raise",30),("Carry_Idle",14),("Pickup",16),
             ("Punch_Right",8),("Death",36),("Celebrate",22),("Shotgun_Reload",34)]
    pose_paths = []
    for action_name, frame in poses:
        for bone in rig.pose.bones:
            bone.matrix_basis.identity()
        rig.animation_data.action = bpy.data.actions[action_name]
        scene.frame_set(frame)
        for name, weapon in preview_weapons.items():
            if weapon:
                for obj in [weapon, *weapon.children_recursive]:
                    obj.hide_render = not action_name.startswith(name)
        if action_name in ("Death","AssaultRifle_Raise"):
            target = Vector((-.28,0,1.16))
            camera.data.ortho_scale = 3.8
        pose_paths.append(render(action_name, views["05_FrontThreeQuarter"], 320))
        target = Vector((0,0,1.38))
        camera.data.ortho_scale = 3.12
    sheet(pose_paths, review / "PoseReview.png", 4, 2)

if preview_all_actions:
    checked = []
    camera.data.ortho_scale = 4.2
    target = Vector((0,0,1.27))
    scene.cycles.samples = 6
    actions = sorted(bpy.data.actions, key=lambda action: action.name)
    for index, action in enumerate(actions):
        for bone in rig.pose.bones:
            bone.matrix_basis.identity()
        rig.animation_data.action = action
        start, finish = action.frame_range
        for j, fraction in enumerate((.28,.67)):
            scene.frame_set(round(start + (finish-start)*fraction))
            checked.append(render(f"Action{index+1:02}_{j+1}_{action.name}", views["05_FrontThreeQuarter"], 158))
    for i in range(0,len(checked),24):
        page = checked[i:i+24]
        sheet(page, review/f"Actions_{1+i//24:02}.png", 6, math.ceil(len(page)/6))
    (review/"ActionOrder.txt").write_text("\n".join(action.name for action in actions)+"\n", encoding="utf-8")
    camera.data.ortho_scale = 3.12
    target = Vector((0,0,1.38))

if stage >= 3 and not renders_only:
    rig.animation_data.action = None
    for bone in rig.pose.bones:
        bone.matrix_basis.identity()
    scene.frame_set(1)
    bpy.context.view_layer.update()
    for obj in weapon_objects:
        bpy.data.objects.remove(obj, do_unlink=True)
    bpy.ops.object.select_all(action="DESELECT")
    for obj in [root, rig, *parts.values()]:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = root
    output = project / "Assets" / "_Project" / "Models" / "HoverDelivery"
    output.mkdir(parents=True,exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=str(output/"HoverDelivery.fbx"),use_selection=True,
        object_types={"ARMATURE","MESH","EMPTY"},axis_forward="-Z",axis_up="Y",
        add_leaf_bones=False,bake_anim=True,bake_anim_use_all_actions=True,
        bake_anim_use_nla_strips=False,bake_anim_simplify_factor=0,
        bake_anim_force_startend_keying=True,use_custom_props=True)
    scenic_pose()
    camera.location = target+Vector(views["05_FrontThreeQuarter"])
    camera.rotation_euler = (target-camera.location).to_track_quat("-Z","Y").to_euler()
    scene.render.filepath = "//HoverDelivery.png"
    bpy.context.preferences.filepaths.save_version = 0
    # Save with original armature rest pose and actions available in the Action Editor.
    for bone in rig.pose.bones:
        bone.matrix_basis.identity()
    bpy.context.view_layer.update()
    bpy.ops.wm.save_as_mainfile(filepath=str(source/"HoverDelivery.blend"))
    for name, obj in parts.items():
        print("PART",name,obj["attachment"],len(obj.data.polygons))
