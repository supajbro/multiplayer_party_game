"""Couch Guys - Procedural Cartoon Car Generator (Blender 3.6+/4.x).

Creates a different stylised suburban vehicle every run, following the house
script's seeded, self-contained, non-destructive generation approach.

Run in Blender > Scripting > Run Script, or:
    blender --background --python generate_cartoon_car.py

Conventions:
* One Blender unit = one metre; Z is up and the vehicle faces Blender -Y.
* Root/origin is at ground level, in the centre of the vehicle.
* Export as FBX with Forward=-Z, Up=Y for a Unity +Z facing vehicle.
* Four named wheel pivots are provided for Unity vehicle controllers.
* No Blender-specific textures, physics or external add-ons required.
* Re-running deletes ONLY the previous GeneratedCar hierarchy.

This generates a distinct, softly sculpted vehicle, NOT a primitive-box stack.
The body uses multi-section contours, actual cut-out wheel arches, a tapered
windscreen/greenhouse, custom rounded tyres and layered details.
"""

import bpy
import math
import random
import time
from mathutils import Vector


# ============================================================================
# USER SETTINGS
# ============================================================================
SEED = None                    # None = different vehicle every run
CAR_TYPE = "RANDOM"            # RANDOM, HATCHBACK, SEDAN, SUV, UTE, VAN, MICRO
DETAIL_LEVEL = "HIGH"         # HIGH or LOW (for distant traffic variants)
ENABLE_WHEEL_ARCH_CUTS = True  # Boolean-cut true arches into the body
GENERATE_EXTRAS = True         # Roof rails, stripes, bumper guards etc.

CAR_TYPES = ("HATCHBACK", "SEDAN", "SUV", "UTE", "VAN", "MICRO")
# Approximate real-world paint, slightly lifted/saturated for Couch Guys.
PAINTS = (
    (0.72, 0.85, 0.73, 1),  # mint/sage
    (0.38, 0.66, 0.70, 1),  # dusty turquoise
    (0.97, 0.70, 0.40, 1),  # mustard ochre
    (0.84, 0.46, 0.39, 1),  # warm coral
    (0.47, 0.61, 0.79, 1),  # powder blue
    (0.93, 0.87, 0.72, 1),  # cream
    (0.57, 0.70, 0.54, 1),  # olive sage
    (0.65, 0.48, 0.64, 1),  # subdued purple
    (0.80, 0.36, 0.33, 1),  # dusty red
    (0.82, 0.78, 0.68, 1),  # warm grey
    (0.35, 0.46, 0.56, 1),  # navy blue
)


# ============================================================================
# HELPERS (HOUSE GENERATOR PATTERN)
# ============================================================================
def erase_previous():
    old = bpy.data.objects.get("GeneratedCar")
    if old:
        def erase_tree(o):
            for child in list(o.children):
                erase_tree(child)
            bpy.data.objects.remove(o, do_unlink=True)
        erase_tree(old)


def empty(name, parent=None, location=(0, 0, 0), display=0.15):
    o = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(o)
    o.empty_display_type = "PLAIN_AXES"
    o.empty_display_size = display
    o.parent = parent
    o.location = location
    return o


def material(name, colour, roughness=0.65, metallic=0.0, emission=0.0):
    full_name = "Car_" + name
    m = bpy.data.materials.get(full_name) or bpy.data.materials.new(full_name)
    m.use_nodes = True
    m.diffuse_color = colour
    principled = m.node_tree.nodes.get("Principled BSDF")
    if principled:
        principled.inputs["Base Color"].default_value = colour
        principled.inputs["Roughness"].default_value = roughness
        principled.inputs["Metallic"].default_value = metallic
        col = principled.inputs.get("Emission Color") or principled.inputs.get("Emission")
        strength = principled.inputs.get("Emission Strength")
        if col is not None:
            col.default_value = colour
        if strength is not None:
            strength.default_value = emission
    return m


def lighten(col, factor):
    return tuple(max(0.0, min(1.0, c * factor)) for c in col[:3]) + (1.0,)


def materials_for(rng):
    base = rng.choice(PAINTS)
    base = tuple(max(0.0, min(1.0, c + rng.uniform(-.035, .035))) for c in base[:3]) + (1.0,)
    return {
        "paint": material("Paint", base, .36, .10),
        "highlight": material("PaintHighlight", lighten(base, 1.12), .40, .08),
        "shade": material("PaintShade", lighten(base, .76), .42, .07),
        "dark": material("RubberTrim", (.068, .079, .084, 1), .83),
        "tyre": material("Tyres", (.038, .045, .051, 1), .95),
        "tread": material("Tread", (.073, .080, .085, 1), .98),
        "hub": material("Wheels", (.66, .69, .68, 1), .43, .43),
        "hub_shadow": material("WheelShadow", (.22, .26, .28, 1), .66, .20),
        "chrome": material("Chrome", (.80, .80, .74, 1), .31, .48),
        "glass": material("Windows", (.22, .43, .48, 1), .18, .04),
        "glass_light": material("WindowReflection", (.46, .68, .70, 1), .20, .0),
        "glass_dark": material("WindowSeal", (.075, .105, .12, 1), .56),
        "headlamp": material("Headlights", (1.0, .94, .75, 1), .19, 0, .3),
        "tail": material("TailLights", (.85, .17, .13, 1), .28, 0, .12),
        "indicator": material("Indicators", (1.0, .61, .18, 1), .27),
        "plate": material("NumberPlate", (.95, .92, .78, 1), .50),
        "plate_text": material("PlateMarks", (.13, .17, .18, 1), .70),
        "interior": material("Interior", (.28, .30, .27, 1), .87),
        "stripe": material("SideAccent", rng.choice(((.95, .91, .77, 1),
                        (.22, .30, .30, 1), (.91, .67, .36, 1))), .64),
    }


def mesh_obj(name, verts, faces, mat, parent, smooth=False, bevel=0.0):
    mesh = bpy.data.meshes.new("Car_" + name)
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    mesh.materials.append(mat)
    o = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(o)
    o.parent = parent
    if bevel > 0:
        bevel_mod = o.modifiers.new("SoftCartoonEdges", 'BEVEL')
        bevel_mod.width = bevel
        bevel_mod.segments = 3 if DETAIL_LEVEL == "HIGH" else 2
        bevel_mod.limit_method = "ANGLE"
        bevel_mod.angle_limit = math.radians(28)
        norm = o.modifiers.new("WeightedNormals", "WEIGHTED_NORMAL")
        norm.keep_sharp = True
        norm.weight = 45
    if smooth:
        for poly in mesh.polygons:
            poly.use_smooth = True
    return o


def box(name, loc, dimensions, mat, parent, bevel=.04):
    x, y, z = loc
    a, b, c = [v * 0.5 for v in dimensions]
    v = [(x-a,y-b,z-c),(x+a,y-b,z-c),(x+a,y+b,z-c),(x-a,y+b,z-c),
         (x-a,y-b,z+c),(x+a,y-b,z+c),(x+a,y+b,z+c),(x-a,y+b,z+c)]
    faces = [(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)]
    return mesh_obj(name, v, faces, mat, parent, bevel=min(bevel, min(dimensions)*.22))


def quad(name, points, mat, parent, depth=0):
    # For gently curved 3D sheet surfaces, triangulation not required.
    return mesh_obj(name, points, [(0,1,2,3)], mat, parent)


def line(name, points, mat, parent, thickness=.018, cyclic=False):
    """Create exportable tubular moulding/door seams as actual meshes."""
    points = [Vector(p) for p in points]
    if len(points) < 2:
        return None
    verts, faces = [], []
    count = 6 if DETAIL_LEVEL == "LOW" else 8
    for k, p in enumerate(points):
        ahead = points[min(k+1,len(points)-1)]
        behind = points[max(0,k-1)]
        tangent = (ahead-behind).normalized()
        arbitrary = Vector((0,0,1)) if abs(tangent.z)<.85 else Vector((1,0,0))
        side = tangent.cross(arbitrary).normalized()
        other = tangent.cross(side).normalized()
        for j in range(count):
            angle = math.tau*j/count
            q = p + thickness*(side*math.cos(angle)+other*math.sin(angle))
            verts.append(tuple(q))
    loops = len(points) if cyclic else len(points)-1
    for k in range(loops):
        k2 = (k+1)%len(points)
        for j in range(count):
            j2 = (j+1)%count
            faces.append((k*count+j,k*count+j2,k2*count+j2,k2*count+j))
    if not cyclic:
        faces.append(tuple(reversed(range(count))))
        faces.append(tuple((len(points)-1)*count+j for j in range(count)))
    return mesh_obj(name, verts, faces, mat, parent, smooth=True)


def batch_lines(name, paths, mat, parent, thickness=.018, count=6):
    """One shared mesh for multiple disconnected tubes (fewer Unity draw calls)."""
    verts, faces = [], []
    for points in paths:
        points = [Vector(p) for p in points]
        if len(points) < 2:
            continue
        base = len(verts)
        for k, p in enumerate(points):
            tangent = (points[min(k + 1, len(points) - 1)] -
                       points[max(0, k - 1)]).normalized()
            arbitrary = Vector((0, 0, 1)) if abs(tangent.z) < .85 else Vector((1, 0, 0))
            side = tangent.cross(arbitrary).normalized()
            other = tangent.cross(side).normalized()
            for j in range(count):
                a = math.tau*j/count
                q = p + thickness*(side*math.cos(a)+other*math.sin(a))
                verts.append(tuple(q))
        faces.append(tuple(base + j for j in reversed(range(count))))
        for k in range(len(points) - 1):
            for j in range(count):
                nxt = (j + 1) % count
                faces.append((base+k*count+j, base+k*count+nxt,
                              base+(k+1)*count+nxt, base+(k+1)*count+j))
        faces.append(tuple(base+(len(points)-1)*count+j for j in range(count)))
    return mesh_obj(name, verts, faces, mat, parent, smooth=True) if verts else None


def cylinder_x(name, centre, radius, depth, mat, parent, sides=24):
    # Cylinder axis along local X, appropriate for wheel hubs and wheel bolts.
    x,y,z=centre
    verts=[]
    for xx in (x-depth*.5,x+depth*.5):
        for i in range(sides):
            t=math.tau*i/sides
            verts.append((xx,y+math.cos(t)*radius,z+math.sin(t)*radius))
    faces=[tuple(reversed(range(sides)))]
    for i in range(sides):
        j=(i+1)%sides
        faces.append((i,j,sides+j,sides+i))
    faces.append(tuple(sides+i for i in range(sides)))
    return mesh_obj(name,verts,faces,mat,parent,smooth=True)


# ============================================================================
# BODY STYLE + SCULPTED HULL
# ============================================================================
def choose_layout(rng, forced):
    body_type = rng.choice(("HATCHBACK", "HATCHBACK", "SEDAN", "SUV", "UTE", "VAN", "MICRO")) if forced=="RANDOM" else forced
    if body_type not in CAR_TYPES:
        raise ValueError("CAR_TYPE must be RANDOM or one of " + str(CAR_TYPES))
    specs = {
        # length, width, shoulderZ, roofZ, wheelRadius, wheelbaseFraction,
        # cabinFrontBase, cabinFrontRoof, cabinRearRoof, cabinRearBase
        "MICRO":     (3.15,1.65,1.06,1.80,.39,.56,-.58,-.28,.70,1.19),
        "HATCHBACK": (3.90,1.77,1.14,1.92,.43,.62,-.70,-.30,.96,1.64),
        "SEDAN":     (4.58,1.84,1.12,1.84,.43,.66,-.82,-.34,.77,1.22),
        "SUV":       (4.62,1.98,1.29,2.24,.49,.65,-1.00,-.62,1.26,1.80),
        "UTE":       (4.76,1.93,1.32,2.17,.48,.66,-1.03,-.63,.30,.70),
        "VAN":       (4.60,1.96,1.34,2.40,.47,.68,-1.72,-1.35,1.42,1.92),
    }
    L,W,shoulder,roof,radius,wheelbase,cf,ct,rt,cr = specs[body_type]
    scale=rng.uniform(.96,1.06)
    L*=scale
    W*=rng.uniform(.97,1.055)
    radius*=scale
    return dict(kind=body_type, length=L, width=W, shoulder=shoulder*scale,
                roof=roof*scale, radius=radius, front_axle=-L*wheelbase/2,
                rear_axle=L*wheelbase/2, cf=cf*scale, ct=ct*scale,
                rt=rt*scale, cr=cr*scale, half_width=W/2,
                wheel_z=radius+.07*scale, scale=scale,
                contrast=rng.random()<.4, trim_type=rng.choice(('COLOURED','BLACK','CHROME')),
                whitewall=rng.random()<.16, num_spokes=rng.choice((5,5,6,7)),
                stripe=rng.random()<.42, head_style=rng.choice(('ROUND','WIDE')),
                roof_extra=rng.choice(('NONE','NONE','NONE','RACK','RAILS','BEACON')),
                foglamps=rng.random()<.35)


def body_ring(hw, low_z, top_z, nose_factor, y):
    w=hw*nose_factor
    # Cross-section with distinct curved corners and broad rounded shoulder.
    outline=[(-.77,low_z+.01),(-.93,low_z+.052),(-1,low_z+.17),
             (-1,top_z-.20),(-.97,top_z-.075),(-.86,top_z-.004),
             (-.65,top_z+.025),(.65,top_z+.025),(.86,top_z-.004),
             (.97,top_z-.075),(1,top_z-.20),(1,low_z+.17),
             (.93,low_z+.052),(.77,low_z+.01)]
    return [(x*w,y,z) for x,z in outline]


def body_top_at(layout, y):
    L=layout['length']; front=-L*.5; back=L*.5
    # Tip curves slope down slightly, a long bonnet on sedans, a stubby van nose.
    edge=min((y-front)/(.33*L),(back-y)/(.30*L),1)
    return layout['shoulder']-.10*max(0.0,1.0-edge)


def make_body(layout, mats, parent):
    L=layout['length']; half=L*.5; hw=layout['half_width']
    # More longitudinal stations create a real rounded car outline, not a cube.
    offsets=[0,.035,.085,.17,.29,.42,.57,.71,.84,.93,.974,1]
    sections=[]
    for t in offsets:
        y=-half+L*t
        taper=.86+.14*min(1,t/.14,(1-t)/.12)
        ztop=body_top_at(layout,y)
        sections.append(body_ring(hw,.36*layout['scale'],ztop,taper,y))
    verts=[p for sec in sections for p in sec]
    n=len(sections[0]); faces=[tuple(reversed(range(n)))]
    for k in range(len(sections)-1):
        for j in range(n):
            next_j=(j+1)%n
            faces.append((k*n+j,k*n+next_j,(k+1)*n+next_j,(k+1)*n+j))
    faces.append(tuple((len(sections)-1)*n+j for j in range(n)))
    body=mesh_obj('Body_SculptedShell',verts,faces,mats['paint'],parent,smooth=True)
    if ENABLE_WHEEL_ARCH_CUTS:
        for axle_name,y in [('Front',layout['front_axle']),('Rear',layout['rear_axle'])]:
            # A pair of arch tunnels, leaving the normal car roof untouched.
            # The cut is applied now, so Unity gets ordinary mesh geometry.
            r=layout['radius']*1.115
            bpy.ops.mesh.primitive_cylinder_add(vertices=32 if DETAIL_LEVEL=='HIGH' else 20,
                radius=r,depth=layout['width']+1.2,
                location=(0,y,layout['wheel_z']),rotation=(0,math.pi/2,0))
            cutter=bpy.context.object
            cutter.name='TemporaryWheelArchCutter_'+axle_name
            bpy.context.view_layer.objects.active=body
            bpy.ops.object.select_all(action='DESELECT')
            body.select_set(True)
            mod=body.modifiers.new('WheelArch_'+axle_name,'BOOLEAN')
            mod.operation='DIFFERENCE'
            mod.solver='EXACT'
            mod.object=cutter
            try:
                bpy.ops.object.modifier_apply(modifier=mod.name)
            except RuntimeError as exc:
                print('WARNING: could not apply wheel arch:',exc)
                body.modifiers.remove(mod)
            bpy.data.objects.remove(cutter,do_unlink=True)
            # Rounded outline at both exterior sides makes the arch intentional.
            for side,label in ((-1,'L'),(1,'R')):
                path=[]
                for step in range(17 if DETAIL_LEVEL=='HIGH' else 11):
                    a=math.pi*(step/(16 if DETAIL_LEVEL=='HIGH' else 10))
                    path.append((side*(hw+.014), y+r*math.cos(a),
                                 layout['wheel_z']+r*math.sin(a)))
                line('ArchMoulding_'+axle_name+'_'+label,path,mats['shade'],parent,.026)
    edge_soften=body.modifiers.new('SoftSculptedPanelEdges','BEVEL')
    edge_soften.width=.020*layout['scale']
    edge_soften.segments=2
    edge_soften.limit_method='ANGLE'
    edge_soften.angle_limit=math.radians(42)
    norm=body.modifiers.new('BodyWeightedNormals','WEIGHTED_NORMAL')
    norm.keep_sharp=True
    return body


# ============================================================================
# PASSENGER CABIN: GLASS, PILLARS, ROOF
# ============================================================================
def lerp(a,b,t):
    return tuple((1-t)*u+t*v for u,v in zip(a,b))


def make_cabin(layout,mats,groups,rng):
    hw=layout['half_width']; baseZ=layout['shoulder']-.06*layout['scale']
    roofZ=layout['roof']; roofW=hw*.80
    frontLow=layout['cf']; frontHigh=layout['ct']
    backHigh=layout['rt']; backLow=layout['cr']
    # x +/- each, glazing begins above the body; large generous rounded cabin.
    FL=(-hw*.88,frontLow,baseZ); FR=(hw*.88,frontLow,baseZ)
    FTL=(-roofW,frontHigh,roofZ); FTR=(roofW,frontHigh,roofZ)
    RTL=(-roofW,backHigh,roofZ); RTR=(roofW,backHigh,roofZ)
    RL=(-hw*.88,backLow,baseZ); RR=(hw*.88,backLow,baseZ)
    # Windshield and rear window: two planar trapezoids, no stretched rectangles.
    quad('FrontWindscreen',(FL,FR,FTR,FTL),mats['glass'],groups['Glass'])
    quad('RearWindow',(RR,RL,RTL,RTR),mats['glass'],groups['Glass'])
    # Hand-shaped roof with slight crown in width and fore-aft; avoids flat toy top.
    crown=.042*layout['scale']; roofV=[FTL,FTR,(-roofW*.64,(frontHigh+backHigh)*.5,roofZ+crown),
          (roofW*.64,(frontHigh+backHigh)*.5,roofZ+crown),RTL,RTR]
    mesh_obj('Roof_CrownedPanel',roofV,[(0,1,3,2),(2,3,5,4)],
             mats['highlight'] if layout['contrast'] else mats['paint'],
             groups['Bodywork'],bevel=.05)
    for side,sign in [('L',-1),('R',1)]:
        frontBottom=(sign*hw*.88,frontLow,baseZ)
        frontTop=(sign*roofW,frontHigh,roofZ)
        backTop=(sign*roofW,backHigh,roofZ)
        backBottom=(sign*hw*.88,backLow,baseZ)
        # Dark surrounding window gasket below inset glazing.
        quad('WindowBacking_'+side,(frontBottom,frontTop,backTop,backBottom),
             mats['glass_dark'],groups['Glass'])
        div=.50 if layout['kind'] not in ('VAN','UTE') else (.46 if layout['kind']=='VAN' else .57)
        # Split into two true front/rear side windows, leaving B-pillar between.
        midTop=lerp(frontTop,backTop,div)
        midBot=lerp(frontBottom,backBottom,div)
        panels=[(frontBottom,frontTop,midTop,midBot),
                (midBot,midTop,backTop,backBottom)]
        for i,(a,b,c,d) in enumerate(panels):
            center=tuple(sum(p[k] for p in (a,b,c,d))/4 for k in range(3))
            glass=[]
            for pt in (a,b,c,d):
                p=lerp(pt,center,.095 if i==0 else .10)
                glass.append((p[0]+sign*.018,p[1],p[2]))
            quad('SideWindow_'+side+'_'+str(i),glass,mats['glass'],groups['Glass'])
            # Single asymmetrical high-gloss glint makes windows read as glass.
            if i==0 and DETAIL_LEVEL=='HIGH':
                p1=lerp(glass[1],glass[2],.20)
                p2=lerp(glass[0],glass[3],.23)
                line('GlassShine_'+side,[p1,p2],mats['glass_light'],groups['Glass'],.012)
        line('WindowOutline_'+side,[frontBottom,frontTop,backTop,backBottom],
             mats['glass_dark'],groups['Trim'],.045)
        line('Pillar_B_'+side,[midBot,midTop],mats['paint'],groups['Trim'],.057)
        line('WindowLowerChrome_'+side,[frontBottom,backBottom],
             mats['chrome'] if layout['trim_type']=='CHROME' else mats['shade'],
             groups['Trim'],.019)
    for name,path in [
        ('FrontWindscreenBorder',[FL,FR,FTR,FTL,FL]),
        ('RearWindowBorder',[RL,RR,RTR,RTL,RL])]:
        line(name,path,mats['glass_dark'],groups['Trim'],.046)
    # Wipers angled across the front glass, resting near bonnet.
    for side,sign in [('L',-1),('R',1)]:
        a=lerp(FL,FR,.5+sign*.19)
        b=lerp(FTL,FTR,.5+sign*.07)
        w0=lerp(a,b,.075); w1=lerp(a,b,.17)
        line('Wiper_'+side,[w0,w1],mats['dark'],groups['Trim'],.021)
    return (frontLow,backLow,baseZ)


# ============================================================================
# WHEELS, HUBS AND ARCHES
# ============================================================================
def make_tyre(name, radius, width, mat, parent, facets):
    # Fully rounded tyre profile rather than a primitive cylinder.
    tyre_profile=[(-.52,.66),(-.51,.83),(-.43,.96),(-.28,1.0),(.28,1.0),
                  (.43,.96),(.51,.83),(.52,.66)]
    verts=[]
    for ax,rad in tyre_profile:
        for i in range(facets):
            a=math.tau*i/facets
            verts.append((width*ax,radius*rad*math.cos(a),radius*rad*math.sin(a)))
    faces=[]
    for k in range(len(tyre_profile)):
        nk=(k+1)%len(tyre_profile)
        for i in range(facets):
            j=(i+1)%facets
            faces.append((k*facets+i,k*facets+j,nk*facets+j,nk*facets+i))
    return mesh_obj(name,verts,faces,mat,parent,smooth=True)


def make_wheels(layout,mats,parent,rng):
    hw=layout['half_width']; R=layout['radius']
    tire_width=.31*layout['scale'] if layout['kind'] not in ('SUV','UTE') else .37*layout['scale']
    for axle,y in (('F',layout['front_axle']),('R',layout['rear_axle'])):
        for side,sign in (('L',-1),('R',1)):
            pivot=empty('Wheel_'+axle+side,parent,(sign*(hw+.018),y,layout['wheel_z']),.10)
            pivot['unity_wheel']=True
            pivot['axle']='Front' if axle=='F' else 'Rear'
            pivot['steerable']=axle=='F'
            pivot['radius_metres']=R
            make_tyre('Tyre_'+axle+side,R,tire_width,mats['tyre'],pivot,
                      32 if DETAIL_LEVEL=='HIGH' else 20)
            outer=sign*(tire_width*.54+.006)
            cylinder_x('WheelRim_'+axle+side,(outer,0,0),R*.63,.027,mats['hub_shadow'],pivot,24)
            cylinder_x('WheelDish_'+axle+side,(outer+sign*.016,0,0),R*.54,.018,mats['hub'],pivot,24)
            cylinder_x('CentreCap_'+axle+side,(outer+sign*.035,0,0),R*.18,.035,
                       mats['chrome'],pivot,16)
            spokeN=layout['num_spokes']
            spokes=[]
            for k in range(spokeN):
                a=math.tau*k/spokeN
                r0=R*.25; r1=R*.48
                spokes.append([(outer+sign*.031,r0*math.cos(a),r0*math.sin(a)),
                               (outer+sign*.032,r1*math.cos(a),r1*math.sin(a))])
            batch_lines('WheelSpokes_'+axle+side,spokes,mats['hub_shadow'],pivot,.042)
            if layout['whitewall']:
                # Ring around tyre sidewall, a nice occasional retro variant.
                pts=[(outer+sign*.008,R*.77*math.cos(math.tau*i/32),
                      R*.77*math.sin(math.tau*i/32)) for i in range(32)]
                line('Whitewall_'+axle+side,pts,mats['plate'],pivot,.035,True)
            if DETAIL_LEVEL=='HIGH':
                # Batch all tread ribs into one mesh per tyre for traffic performance.
                treads=[]
                for k in range(18):
                    a=math.tau*k/18
                    treads.append([(-tire_width*.22,R*1.004*math.cos(a),R*1.004*math.sin(a)),
                                   (tire_width*.22,R*1.004*math.cos(a),R*1.004*math.sin(a))])
                batch_lines('TyreTread_'+axle+side,treads,mats['tread'],pivot,.015)


# ============================================================================
# CAR SURFACE DETAILS
# ============================================================================
def make_details(layout,mats,groups,rng):
    L=layout['length']; hw=layout['half_width']; S=layout['scale']
    front=-L*.5; back=L*.5; shoulder=layout['shoulder']
    # Rounded bumpers below panels, with deliberately chunky playful scale.
    box('FrontBumper',(0,front-.055,.52*S),(layout['width']*.91,.20*S,.25*S),
        mats['shade'] if layout['trim_type']=='COLOURED' else mats['dark'],groups['Trim'],.085*S)
    box('RearBumper',(0,back+.045,.52*S),(layout['width']*.90,.20*S,.24*S),
        mats['shade'] if layout['trim_type']=='COLOURED' else mats['dark'],groups['Trim'],.080*S)
    grille_y=front-.050
    box('FrontGrille',(0,grille_y,shoulder-.37*S),
        (layout['width']*.39,.060*S,.20*S),mats['glass_dark'],groups['Details'],.070*S)
    if DETAIL_LEVEL=='HIGH':
        for j in (-1,0,1):
            box('GrilleSlat_'+str(j),(0,grille_y-.039,shoulder-.37*S+j*.053*S),
                (layout['width']*.32,.020,.018*S),mats['hub'],groups['Details'],.007)
    # Pronounced soft lamps with inset surrounds, separated from grille.
    for side,sign in [('L',-1),('R',1)]:
        x=sign*hw*.70
        lampW=.36*S if layout['head_style']=='ROUND' else .48*S
        box('HeadlampSurround_'+side,(x,front-.035,shoulder-.20*S),
            (lampW+.075,.10*S,.27*S),mats['shade'],groups['Details'],.07*S)
        box('Headlamp_'+side,(x,front-.087,shoulder-.20*S),
            (lampW,.07*S,.21*S),mats['headlamp'],groups['Details'],.078*S)
        box('FrontIndicator_'+side,(sign*hw*.93,front+.025,shoulder-.25*S),
            (.15*S,.13*S,.12*S),mats['indicator'],groups['Details'],.048*S)
        box('TailLamp_'+side,(sign*hw*.78,back+.037,shoulder-.18*S),
            (.33*S,.11*S,.30*S),mats['tail'],groups['Details'],.060*S)
        box('TailIndicator_'+side,(sign*hw*.78,back+.097,shoulder-.33*S),
            (.26*S,.036*S,.076*S),mats['indicator'],groups['Details'],.028*S)
        # Door handles, with separate doors unless car is a micro or single-cab ute.
        sideX=sign*(hw+.025)
        doors=1 if layout['kind'] in ('MICRO','UTE') else 2
        start=layout['cf']+.10*S; end=layout['cr']-.05*S
        for i in range(doors):
            t=(i+.55)/doors
            dy=start+(end-start)*t
            box('DoorHandle_'+side+'_'+str(i),(sideX+sign*.015,dy,shoulder-.21*S),
                (.08*S,.22*S,.055*S),mats['chrome'] if layout['trim_type']=='CHROME' else mats['shade'],
                groups['Details'],.022*S)
            # Sloping door gap lines, deliberately low contrast.
            edge=start+(end-start)*(i+1)/doors
            line('DoorSeam_'+side+'_'+str(i),[(sideX,edge,shoulder-.055*S),
                 (sideX,edge+.05*S,.73*S),(sideX,edge+.02*S,.48*S)],mats['shade'],
                 groups['Trim'],.009*S)
        # Door-mounted mirrors; integrated painted fairing + dark reflective face.
        mirrorY=layout['cf']+.26*S
        box('MirrorHousing_'+side,(sign*(hw+.125*S),mirrorY,shoulder+.095*S),
            (.22*S,.25*S,.16*S),mats['paint'],groups['Details'],.07*S)
        box('MirrorGlass_'+side,(sign*(hw+.24*S),mirrorY+.02*S,shoulder+.095*S),
            (.025*S,.16*S,.10*S),mats['glass'],groups['Details'],.020*S)
        # Narrow wheel side-protection trim along doors (not on arches).
        if layout['stripe']:
            a=layout['front_axle']+layout['radius']*1.3
            b=layout['rear_axle']-layout['radius']*1.3
            if a<b:
                line('DoorSideStripe_'+side,[(sign*(hw+.01),a,.77*S),
                     (sign*(hw+.01),b,.77*S)],mats['stripe'],groups['Decor'],.043*S)
        if layout['foglamps']:
            box('FogLamp_'+side,(sign*hw*.77,front-.162,.51*S),
                (.18*S,.052*S,.12*S),mats['headlamp'],groups['Details'],.045*S)
    # Front / rear Australian style plates, with geometric pseudo-character marks.
    for name,yy in [('Front',front-.17*S),('Rear',back+.17*S)]:
        box(name+'NumberPlate',(0,yy,.55*S),(.45*S,.027*S,.15*S),
            mats['plate'],groups['Details'],.013*S)
        for j in range(4):
            box(name+'PlateGlyph'+str(j),((j-1.5)*.083*S,yy+(-.018 if name=='Front' else .018)*S,.55*S),
                (.035*S,.013*S,.055*S),mats['plate_text'],groups['Details'],.004)
    # Bonnet ridge/pressed metal character lines (sitting just over body).
    if DETAIL_LEVEL=='HIGH' and layout['kind']!='VAN':
        for side,sign in [('L',-1),('R',1)]:
            y0=front+.23*S; y1=layout['cf']-.13*S
            if y1>y0:
                line('BonnetCrease_'+side,[(sign*hw*.43,y0,body_top_at(layout,y0)+.032),
                     (sign*hw*.49,y1,body_top_at(layout,y1)+.035)],mats['highlight'],
                     groups['Details'],.012*S)
    # Roof extras: only plausible for each car type.
    roofExtra=layout['roof_extra']
    roofZ=layout['roof']+.035*S
    if GENERATE_EXTRAS and roofExtra in ('RACK','RAILS') and layout['kind'] in ('SUV','VAN','UTE'):
        for sign,label in [(-1,'L'),(1,'R')]:
            line('RoofRail_'+label,[(sign*hw*.61,layout['ct']+.15*S,roofZ+.11*S),
                 (sign*hw*.61,layout['rt']-.12*S,roofZ+.11*S)],mats['chrome'],
                 groups['Decor'],.038*S)
        if roofExtra=='RACK':
            for i in (0,1):
                y=layout['ct']+(layout['rt']-layout['ct'])*(.28+.44*i)
                line('RoofCrossbar_'+str(i),[(-hw*.61,y,roofZ+.13*S),
                     (hw*.61,y,roofZ+.13*S)],mats['dark'],groups['Decor'],.042*S)
    elif GENERATE_EXTRAS and roofExtra=='BEACON' and layout['kind'] not in ('UTE','MICRO'):
        box('CartoonRoofBeacon',(0,(layout['ct']+layout['rt'])*.5,roofZ+.10*S),
            (.42*S,.28*S,.15*S),mats['indicator'],groups['Decor'],.065*S)
    # Ute rear tray: open dark inset with surrounding thick painted side rails.
    if layout['kind']=='UTE':
        tray_front=layout['cr']+.10*S
        tray_back=back-.17*S
        traylen=max(.30,tray_back-tray_front)
        trayY=(tray_front+tray_back)*.5
        box('UteBedInlay',(0,trayY,shoulder+.024*S),
            (hw*1.60,traylen,.06*S),mats['dark'],groups['Details'],.018*S)
        for side,sign in [('L',-1),('R',1)]:
            box('UteBedSideRail_'+side,(sign*hw*.82,trayY,shoulder+.085*S),
                (.095*S,traylen,.15*S),mats['highlight'],groups['Details'],.023*S)
        box('UteTailgateLip',(0,back-.14*S,shoulder+.08*S),
            (hw*1.70,.11*S,.16*S),mats['highlight'],groups['Details'],.025*S)
    # Van sliding door line, SUV distinctive cladding, cheeky sedan trunk lip.
    if layout['kind']=='VAN':
        for sign,label in [(-1,'L'),(1,'R')]:
            line('SlidingDoorRunner_'+label,
                 [(sign*(hw+.025),-.02*S,shoulder-.12*S),
                  (sign*(hw+.025),back-.44*S,shoulder-.12*S)],
                 mats['shade'],groups['Trim'],.016*S)
    if layout['kind']=='SUV':
        for sign,label in [(-1,'L'),(1,'R')]:
            y0=layout['front_axle']+layout['radius']*1.20
            y1=layout['rear_axle']-layout['radius']*1.20
            if y0<y1:
                line('ProtectiveDoorCladding_'+label,[(sign*(hw+.017),y0,.57*S),
                     (sign*(hw+.017),y1,.57*S)],mats['dark'],groups['Trim'],.060*S)
    if layout['kind']=='SEDAN' and rng.random()<.4:
        box('TrunkLip',(0,back-.18*S,shoulder+.01*S),
            (hw*1.43,.14*S,.09*S),mats['highlight'],groups['Decor'],.035*S)


# ============================================================================
# ENTRY POINT
# ============================================================================
def generate_car(seed=SEED, car_type=CAR_TYPE):
    """Generate a new car; pass a fixed seed for repeatable Unity variants."""
    if car_type not in CAR_TYPES and car_type!='RANDOM':
        raise ValueError('Invalid car type '+repr(car_type))
    erase_previous()
    actual_seed = seed if seed is not None else random.SystemRandom().randrange(1,2**31)
    rng = random.Random(actual_seed)
    layout=choose_layout(rng,car_type)
    mats=materials_for(rng)
    root=empty('GeneratedCar',display=.27)
    groups={g:empty(g,root) for g in ('Bodywork','Glass','Trim','Details','Wheels','Decor')}
    make_body(layout,mats,groups['Bodywork'])
    make_cabin(layout,mats,groups,rng)
    make_wheels(layout,mats,groups['Wheels'],rng)
    make_details(layout,mats,groups,rng)

    # Runtime wiring guidance (no rigidbodies generated; Unity adds physics).
    root['generator']='Couch Guys Cartoon Traffic Car v1'
    root['seed']=actual_seed
    root['car_type']=layout['kind']
    root['forward_axis']='-Y'
    root['unity_scale_meters']=1.0
    root['length_metres']=round(layout['length'],3)
    root['width_metres']=round(layout['width'],3)
    root['wheel_radius_metres']=round(layout['radius'],3)
    root['front_axle_y']=round(layout['front_axle'],3)
    root['rear_axle_y']=round(layout['rear_axle'],3)
    root['wheel_pivots']='Wheel_FL,Wheel_FR,Wheel_RL,Wheel_RR'
    root['unity_notes']='Use one Rigidbody and simple compound colliders on root; animate/steer wheel pivots independently.'
    root['generated_utc']=time.strftime('%Y-%m-%dT%H:%M:%SZ',time.gmtime())
    bpy.ops.object.select_all(action='DESELECT')
    root.select_set(True)
    bpy.context.view_layer.objects.active=root
    print('\n=== COUCH GUYS CAR GENERATED ===')
    print('Seed:',actual_seed,'| Body:',layout['kind'],'| Dimensions:',
          round(layout['length'],2),'x',round(layout['width'],2))
    print('Four named wheels: Wheel_FL/FR/RL/RR; front = Blender -Y')
    return root


if __name__=='__main__':
    generate_car()
