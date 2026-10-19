"""
COUCH GUYS  |  PROCEDURAL CARTOON SUBURBAN TREE  v1.0
---------------------------------------------------------
Run in Blender's Scripting workspace (Blender 3.6 / 4.x).
Generates a new, differently shaped and coloured stylised tree every run.

AIM: soft Fortnite/Pixar-style silhouette, rounded scalloped canopy,
organic branch splits, rooted curved trunk and hand-placed foliage details;
no textures, geometry nodes, add-ons or external files required.

UNITY: An Empty at ground level is the prefab root, containing just ONE
mesh with a few colour materials. No hidden preview models are created.
Select the generated root + mesh (selected automatically) and export FBX.

IMPORTANT PERFORMANCE NOTE: Trees placed in large numbers should use
Unity LODGroups, GPU instancing where appropriate, and distance culling.
Use QUALITY='LOW' for lots of background suburban trees.
"""

import bpy
import math
import random
from mathutils import Vector

# ============================================================
# USER SETTINGS -- EDIT THESE
# ============================================================
TREE_COUNT = 1                # 1 creates a Unity-ready individual tree
SEED = None                   # None = NEW RANDOM TREE on EVERY RUN; integer = reproducible
QUALITY = "MEDIUM"            # LOW / MEDIUM / HIGH (polygon budget)
ALLOW_FLOWERING = False       # True adds occasional soft-pink ornamental trees
EXPORT_FBX = False            # True automatically writes FBX(s) beside the .blend
EXPORT_DIRECTORY = "//"       # // means folder containing the saved .blend
TREE_SPACING = 11.0           # Used only when TREE_COUNT > 1
GENERATED_COLLECTION = "CG | Procedural Trees (regenerated)"

# Materials are simple flat base colours designed for Unity Built-in/URP.
# Lighting and AO will add depth in-game; none are baked into the model.
PALETTES = [
    ("Leafy Lime",     (0.34, 0.64, 0.24), (0.22, 0.47, 0.20), (0.47, 0.75, 0.30)),
    ("Emerald",        (0.19, 0.58, 0.35), (0.12, 0.43, 0.28), (0.29, 0.71, 0.44)),
    ("Meadow Green",   (0.39, 0.66, 0.29), (0.28, 0.49, 0.23), (0.54, 0.76, 0.39)),
    ("Fresh Mint",     (0.22, 0.61, 0.49), (0.14, 0.46, 0.39), (0.39, 0.74, 0.57)),
    ("Olive Spring",   (0.48, 0.65, 0.23), (0.31, 0.47, 0.17), (0.60, 0.76, 0.32)),
]
FLOWERING_PALETTE = ("Pink Blossom", (0.91, 0.51, 0.63), (0.72, 0.34, 0.49), (1.00, 0.69, 0.74))

SILHOUETTES = [
    # name           crown X  Y     Z     branch count
    ("Cloud Round",    1.95, 1.86, 1.46, 6),
    ("Wide Oak",       2.38, 2.17, 1.26, 7),
    ("Tall Pear",      1.51, 1.45, 1.88, 5),
    ("Lopsided Park",  2.08, 1.82, 1.42, 6),
    ("Soft Umbrella",  2.33, 2.09, 1.29, 7),
]

DETAIL = {
    "LOW":    {"longitude": 10, "latitude": 7,  "lobes": 0.64, "leaves": 0.0,  "bark": 3, "wood_sides": 8},
    "MEDIUM": {"longitude": 14, "latitude": 9,  "lobes": 1.00, "leaves": 1.0,  "bark": 9, "wood_sides": 11},
    "HIGH":   {"longitude": 18, "latitude": 11, "lobes": 1.28, "leaves": 1.6,  "bark": 13,"wood_sides": 13},
}

# ============================================================
# GEOMETRY / MATERIAL HELPERS
# ============================================================


def clamp(a, low, high):
    return max(low, min(high, a))


def vec(x, y, z):
    return Vector((float(x), float(y), float(z)))


class MeshBuilder:
    """Combine ALL components into one mesh with material slots."""
    def __init__(self):
        self.verts = []
        self.faces = []
        self.mats = []
        self.smooth = []

    def vertex(self, p):
        self.verts.append((p.x, p.y, p.z))
        return len(self.verts) - 1

    def face(self, ids, material, smooth=True):
        if len(set(ids)) < 3:
            return
        self.faces.append(tuple(ids))
        self.mats.append(material)
        self.smooth.append(smooth)

    def triangle_outward(self, a, b, c, n, material):
        pa, pb, pc = (Vector(self.verts[i]) for i in (a, b, c))
        if (pb - pa).cross(pc - pa).dot(n) < 0:
            b, c = c, b
        self.face((a, b, c), material, smooth=False)

    def build(self, name, collection, parent, materials):
        mesh = bpy.data.meshes.new(name + "_Mesh")
        mesh.from_pydata(self.verts, [], self.faces)
        mesh.materials.clear()
        for mat in materials:
            mesh.materials.append(mat)
        mesh.update(calc_edges=True)
        obj = bpy.data.objects.new(name, mesh)
        collection.objects.link(obj)
        obj.parent = parent
        for poly, midx, soft in zip(mesh.polygons, self.mats, self.smooth):
            poly.material_index = midx
            poly.use_smooth = soft
        return obj


def material(name, rgb, roughness=0.86):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    m.diffuse_color = (rgb[0], rgb[1], rgb[2], 1.0)
    m.use_backface_culling = False
    nodes = m.node_tree.nodes
    principled = nodes.get("Principled BSDF")
    if principled is not None:
        principled.inputs["Base Color"].default_value = (*rgb, 1.0)
        principled.inputs["Roughness"].default_value = roughness
        if "Metallic" in principled.inputs:
            principled.inputs["Metallic"].default_value = 0.0
    return m


def append_tube(builder, nodes, radii, mat_idx, sides, twist=0.0):
    """A curved tapered tube. Each point has its own tangent/frame."""
    if len(nodes) < 2:
        return
    points = [Vector(n) for n in nodes]
    rings = []
    previous_x = None
    for i, p in enumerate(points):
        before = points[max(0, i-1)]
        after = points[min(len(points)-1, i+1)]
        axis = (after - before).normalized()
        if axis.length_squared < 0.001:
            axis = vec(0, 0, 1)
        # Parallel-transport the frame so curved branches do not abruptly twist.
        xaxis = None
        if previous_x is not None:
            projected = previous_x - axis * previous_x.dot(axis)
            if projected.length_squared > 0.0001:
                xaxis = projected.normalized()
        if xaxis is None:
            guide = vec(0, 1, 0) if abs(axis.z) > 0.86 else vec(0, 0, 1)
            xaxis = guide.cross(axis).normalized()
        yaxis = axis.cross(xaxis).normalized()
        previous_x = xaxis
        ring = []
        for s in range(sides):
            a = 2 * math.pi * s / sides + twist
            offset = (xaxis * math.cos(a) + yaxis * math.sin(a)) * max(0.003, radii[i])
            ring.append(builder.vertex(p + offset))
        rings.append(ring)
    for i in range(len(rings)-1):
        for s in range(sides):
            t = (s + 1) % sides
            # In local tube coordinates, this winds outward.
            builder.face((rings[i][s], rings[i][t], rings[i+1][t], rings[i+1][s]), mat_idx)
    builder.face(tuple(reversed(rings[0])), mat_idx, smooth=False)
    builder.face(tuple(rings[-1]), mat_idx, smooth=False)


def canopy_point(center, dimensions, theta, phi, phase):
    """Smooth, deliberately sculpted 'marshmallow' lobe, not a rigid sphere."""
    st, ct = math.sin(theta), math.cos(theta)
    cp, sp = math.cos(phi), math.sin(phi)
    # Low-amplitude periodic warps that keep lobes plush, not jagged.
    wobble = (1.0
              + 0.045 * math.sin(3.0 * phi + phase) * st ** 2
              + 0.035 * math.cos(5.0 * phi - phase * 0.7) * math.sin(2.0 * theta)
              + 0.028 * math.sin(4.0 * theta + phase * 1.3) * st)
    return center + vec(
        dimensions.x * st * cp * wobble,
        dimensions.y * st * sp * wobble,
        dimensions.z * ct * wobble,
    )


def append_lobe(builder, center, dimensions, phase, mat_idx, detail):
    lon, lat = detail["longitude"], detail["latitude"]
    top = builder.vertex(canopy_point(center, dimensions, 0, 0, phase))
    rings = []
    for j in range(1, lat):
        th = math.pi * j / lat
        ring = []
        for k in range(lon):
            ph = 2 * math.pi * k / lon
            ring.append(builder.vertex(canopy_point(center, dimensions, th, ph, phase)))
        rings.append(ring)
    bottom = builder.vertex(canopy_point(center, dimensions, math.pi, 0, phase))
    for k in range(lon):
        builder.face((top, rings[0][k], rings[0][(k+1)%lon]), mat_idx)
    for j in range(len(rings)-1):
        upper, lower = rings[j], rings[j+1]
        for k in range(lon):
            t = (k+1) % lon
            builder.face((upper[k], lower[k], lower[t], upper[t]), mat_idx)
    for k in range(lon):
        builder.face((rings[-1][k], bottom, rings[-1][(k+1)%lon]), mat_idx)


def append_leaf_mark(builder, lobe, rng, mat_idx):
    """A tiny embossed, 4-facet leaf accent hugging a canopy surface."""
    center, dims, phase = lobe
    theta = rng.uniform(0.30, 2.55)
    phi = rng.uniform(0, 2 * math.pi)
    pos = canopy_point(center, dims, theta, phi, phase)
    n = vec(math.sin(theta)*math.cos(phi)/dims.x,
            math.sin(theta)*math.sin(phi)/dims.y,
            math.cos(theta)/dims.z).normalized()
    right = vec(-math.sin(phi), math.cos(phi), 0).normalized()
    up = right.cross(n).normalized()
    rotation = rng.uniform(0, 2 * math.pi)
    axis = right*math.cos(rotation) + up*math.sin(rotation)
    wing = n.cross(axis).normalized()
    length = rng.uniform(0.11, 0.25)
    width = length * rng.uniform(0.36, 0.62)
    base = pos + n*0.035
    v0 = builder.vertex(base - axis * length * .55)
    v1 = builder.vertex(base + wing * width * .52)
    v2 = builder.vertex(base + axis * length * .55)
    v3 = builder.vertex(base - wing * width * .52)
    peak = builder.vertex(base + n * 0.07)
    for a, b in ((v0, v1), (v1, v2), (v2, v3), (v3, v0)):
        builder.triangle_outward(a, b, peak, n, mat_idx)


def root_position_at_height(height, trunk_height, lean, wobble):
    t = clamp(height / trunk_height, 0, 1.4)
    return vec(
        lean.x*t + 0.05*math.sin(t*math.pi*1.5 + wobble)*t,
        lean.y*t + 0.045*math.sin(t*math.pi*1.7 - wobble)*t,
        height,
    )


def trunk_radius_at(height, trunk_height, scale=1.0):
    t = clamp(height / trunk_height, 0, 1.4)
    flare = 0.13 * math.exp(-height / 0.23)
    return scale * (0.315 * (1 - 0.48 * t) + flare)


def bark_ribbon(builder, rng, trunk_h, lean, wobble, branch_mat, scale):
    """Organic dark bark grain that lies directly on the curved trunk."""
    z_start = rng.uniform(0.22, trunk_h * 0.74)
    length = rng.uniform(0.36, 1.15)
    z_end = min(trunk_h + 0.1, z_start+length)
    angle = rng.uniform(0, 2*math.pi)
    half_width = rng.uniform(0.011, 0.024)*scale
    left, right = [], []
    for j in range(6):
        t = j/5
        z = z_start + (z_end-z_start)*t
        a = angle + 0.15 * math.sin(t*math.pi*1.5 + angle)
        c = root_position_at_height(z, trunk_h, lean, wobble)
        radius = trunk_radius_at(z, trunk_h, scale) + 0.010
        u = vec(math.cos(a), math.sin(a), 0)
        tangent = vec(-math.sin(a), math.cos(a), 0)
        taper = 0.16 + 0.84*math.sin(math.pi*t)**0.45
        left.append(builder.vertex(c + u*radius - tangent*half_width*taper))
        right.append(builder.vertex(c + u*radius + tangent*half_width*taper))
    for j in range(5):
        builder.face((left[j], right[j], right[j+1], left[j+1]), branch_mat, smooth=False)


def clean_generated():
    old = bpy.data.collections.get(GENERATED_COLLECTION)
    if old:
        for obj in list(old.objects):
            bpy.data.objects.remove(obj, do_unlink=True)
        bpy.data.collections.remove(old)
    # Generated materials from previous runs are disposed of only if unused.
    for mat in list(bpy.data.materials):
        if mat.name.startswith("CGTree_Auto_") and mat.users == 0:
            bpy.data.materials.remove(mat)


def make_collection():
    c = bpy.data.collections.new(GENERATED_COLLECTION)
    bpy.context.scene.collection.children.link(c)
    return c


def create_tree(collection, seed, number, detail):
    rng = random.Random(seed)
    shape_name, cx, cy, cz, branches_n = rng.choice(SILHOUETTES)
    is_flowering = ALLOW_FLOWERING and rng.random() < 0.14
    p_name, mid, dark, bright = FLOWERING_PALETTE if is_flowering else rng.choice(PALETTES)
    # Trunk and crown dimensions vary beyond the chosen shape family.
    size = rng.uniform(0.88, 1.13)
    crown_x = cx*size*rng.uniform(0.96, 1.06)
    crown_y = cy*size*rng.uniform(0.95, 1.05)
    crown_z = cz*size*rng.uniform(0.97, 1.06)
    trunk_height = rng.uniform(2.68, 3.22)*size
    crown_center_z = trunk_height + crown_z * rng.uniform(0.72, 0.90)
    wind_angle = rng.uniform(0, 2*math.pi)
    lean = vec(math.cos(wind_angle), math.sin(wind_angle), 0)*rng.uniform(0.04, 0.30)*size
    if shape_name == "Lopsided Park":
        lean *= 1.45
    trunk_wobble = rng.uniform(-math.pi, math.pi)
    crown_shift = vec(lean.x*1.8, lean.y*1.8, 0)
    crown_centre = crown_shift + vec(0, 0, crown_center_z)
    crown_scale = vec(crown_x, crown_y, crown_z)

    name = f"CG_Tree_{number:02d}_{seed:08X}"
    root = bpy.data.objects.new(name, None)
    collection.objects.link(root)
    root.empty_display_type = 'PLAIN_AXES'
    root.empty_display_size = .35
    root.location = ((number - 1)*TREE_SPACING, 0, 0)
    root["CouchGuys_Seed"] = int(seed)
    root["CouchGuys_Silhouette"] = shape_name
    root["CouchGuys_Palette"] = p_name
    root["CouchGuys_Quality"] = QUALITY
    root["CouchGuys_Units"] = "1 Blender unit = 1 metre; pivot = trunk base"

    tag = f"CGTree_Auto_{seed:08X}_{number:02d}"
    bark_mid = rng.choice([(0.48, 0.27, 0.15), (0.43, 0.24, 0.14),
                           (0.51, 0.30, 0.17), (0.47, 0.31, 0.19)])
    bark_shadow = tuple(c * 0.73 for c in bark_mid)
    materials = [
        material(f"{tag}_01_Bark", bark_mid),
        material(f"{tag}_02_BarkGrooves", bark_shadow),
        material(f"{tag}_03_FoliageShadow", dark),
        material(f"{tag}_04_Foliage", mid),
        material(f"{tag}_05_FoliageSun", bright),
        material(f"{tag}_06_LeafDetails", tuple(min(1.0, c*0.79 + 0.13) for c in bright)),
    ]
    WOOD, GROOVES, DARK, MID, LIGHT, FLECK = range(6)
    b = MeshBuilder()

    # 1) WIDE BUTTRESS ROOTS: anchor the model naturally to the ground.
    root_count = rng.randint(4, 6)
    for i in range(root_count):
        a = (2*math.pi/root_count)*i + rng.uniform(-.14, .14)
        radial = vec(math.cos(a), math.sin(a), 0)
        reach = rng.uniform(.56, .86)*size
        path = [root_position_at_height(.50*size, trunk_height, lean, trunk_wobble) + radial*.20*size,
                root_position_at_height(.23*size, trunk_height, lean, trunk_wobble) + radial*.37*size,
                radial*reach + vec(0,0,.055*size)]
        radii = [.17*size, .115*size, .018*size]
        append_tube(b, path, radii, WOOD, detail["wood_sides"])

    # 2) SLIGHTLY WOBBLY, TAPERING MAIN TRUNK (not a square pole).
    steps = 11
    heights = [(i / (steps - 1)) * (trunk_height + 0.32*size) for i in range(steps)]
    trunk_pts = [root_position_at_height(h, trunk_height, lean, trunk_wobble) for h in heights]
    trunk_radii = [trunk_radius_at(h, trunk_height, size) for h in heights]
    append_tube(b, trunk_pts, trunk_radii, WOOD, detail["wood_sides"], twist=rng.random()*.4)

    # 3) BRANCHES radiating towards the underside and outside of the canopy.
    for i in range(branches_n):
        a = 2*math.pi*(i+rng.uniform(-0.16, 0.16))/branches_n + wind_angle*.38
        direction = vec(math.cos(a), math.sin(a), 0)
        origin_h = trunk_height*rng.uniform(.66, .91)
        base = root_position_at_height(origin_h, trunk_height, lean, trunk_wobble)
        dest = (crown_centre
                + vec(crown_x*direction.x*rng.uniform(.52, .83),
                      crown_y*direction.y*rng.uniform(.52, .83),
                      crown_z*rng.uniform(-.24, .52)))
        inward = base + (dest-base)*.36 + vec(0, 0, .05*size)
        elbow = base + (dest-base)*.70 + vec(0, 0, .15*size)
        thickness = rng.uniform(.105, .155)*size
        append_tube(b, [base, inward, elbow, dest],
                    [thickness*1.16, thickness*.81, thickness*.49, .015*size],
                    WOOD, max(7, detail["wood_sides"]-2), twist=a)

    # 4) SCULPTED CLOUD CANOPY: one dark under-mass + overlapping varied lobes.
    lobes = []
    def add(center, dimensions, colour):
        phase = rng.uniform(-math.pi, math.pi)
        dims = Vector(dimensions)
        append_lobe(b, Vector(center), dims, phase, colour, detail)
        lobes.append((Vector(center), dims, phase))

    add(crown_centre - vec(0, 0, crown_z*.06),
        (crown_x*.79, crown_y*.78, crown_z*.76), DARK)

    primary_count = max(6, round(rng.randint(11, 14)*detail["lobes"]))
    offset = rng.random()*math.pi*2
    for i in range(primary_count):
        a = offset + (2*math.pi*i/primary_count) + rng.uniform(-.17,.17)
        t = (i % 4)/3
        centre = crown_centre + vec(
            crown_x*math.cos(a)*rng.uniform(.48,.67),
            crown_y*math.sin(a)*rng.uniform(.46,.66),
            crown_z*(rng.uniform(-.34,.23) + (t-.5)*.16))
        dimensions = (crown_x*rng.uniform(.40,.56),
                      crown_y*rng.uniform(.41,.56),
                      crown_z*rng.uniform(.46,.65))
        choice = rng.choices([DARK, MID, LIGHT], weights=[1, 7, 3], k=1)[0]
        add(centre, dimensions, choice)

    # Edge scallops break up the outline, but remain attached to the core.
    edge_count = max(5, round(rng.randint(8, 12)*detail["lobes"]))
    for i in range(edge_count):
        a = 2*math.pi*(i+rng.uniform(-.3,.3))/edge_count + offset
        z = rng.uniform(-.51,.50)
        location = crown_centre + vec(
            crown_x*.70*math.cos(a), crown_y*.69*math.sin(a), crown_z*z)
        dims = (crown_x*rng.uniform(.24,.37),
                crown_y*rng.uniform(.24,.37),
                crown_z*rng.uniform(.30,.47))
        add(location, dims, rng.choice([MID, MID, LIGHT]))

    # Some small high crown puffs and a few hanging lobes prevent a perfect ball.
    for _ in range(max(2, round(3*detail["lobes"]))):
        a = rng.uniform(0, 2*math.pi)
        r = rng.uniform(.0,.50)
        add(crown_centre + vec(crown_x*r*math.cos(a),
                               crown_y*r*math.sin(a), crown_z*rng.uniform(.56,.79)),
            (crown_x*rng.uniform(.25,.40), crown_y*rng.uniform(.25,.40),
             crown_z*rng.uniform(.29,.39)), LIGHT)
    for _ in range(max(2, round(3*detail["lobes"]))):
        a = rng.uniform(0, 2*math.pi)
        r = rng.uniform(.12,.72)
        add(crown_centre + vec(crown_x*r*math.cos(a),
                               crown_y*r*math.sin(a), -crown_z*rng.uniform(.53,.74)),
            (crown_x*rng.uniform(.23,.34), crown_y*rng.uniform(.23,.34),
             crown_z*rng.uniform(.27,.39)), rng.choice([DARK, MID]))

    # 5) DELIBERATELY SUBTLE LEAF SHAPES. Not billboards or transparency.
    if detail["leaves"] > 0:
        accent_count = int((44 + edge_count*3) * detail["leaves"])
        accents = lobes[1:]  # prefer visible outer foliage, skip dark inner core
        for _ in range(accent_count):
            append_leaf_mark(b, rng.choice(accents), rng, FLECK)

    # 6) STRIATED BARK: shallow colour-filled grooves; still one mesh.
    for _ in range(detail["bark"]):
        bark_ribbon(b, rng, trunk_height, lean, trunk_wobble, GROOVES, size)

    mesh_obj = b.build(name + "_Geometry", collection, root, materials)
    mesh_obj["CouchGuys_MeshPurpose"] = "Combined wood, branches, foliage, leaves; Unity-ready"
    mesh_obj["CouchGuys_Generated"] = True
    triangle_count = sum(max(1, len(face)-2) for face in b.faces)
    root["CouchGuys_Triangles"] = triangle_count
    root["CouchGuys_HeightMetres_Approx"] = round(crown_center_z + crown_z*1.2, 2)
    print(f"[Couch Guys] {name} | {shape_name} | {p_name} | "
          f"{triangle_count:,} triangles | seed={seed}")
    return root, mesh_obj


def export_tree_fbx(root, mesh_obj):
    import os
    directory = bpy.path.abspath(EXPORT_DIRECTORY)
    os.makedirs(directory, exist_ok=True)
    for ob in bpy.context.selected_objects:
        ob.select_set(False)
    root.select_set(True)
    mesh_obj.select_set(True)
    bpy.context.view_layer.objects.active = root
    path = os.path.join(directory, root.name + ".fbx")
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=True,
        object_types={'EMPTY', 'MESH'},
        axis_forward='-Z',
        axis_up='Y',
        apply_unit_scale=True,
        add_leaf_bones=False,
        bake_anim=False,
        path_mode='AUTO',
    )
    print("[Couch Guys] Exported " + path)


def main():
    quality = QUALITY.upper()
    if quality not in DETAIL:
        raise ValueError("QUALITY must be LOW, MEDIUM or HIGH")
    if TREE_COUNT < 1:
        raise ValueError("TREE_COUNT must be 1 or greater")
    clean_generated()
    group = make_collection()
    base_seed = random.SystemRandom().randint(1, 2**31-1) if SEED is None else int(SEED)
    results = []
    for i in range(TREE_COUNT):
        seed = (base_seed + i * 15485863) & 0x7FFFFFFF
        root, geo = create_tree(group, seed, i+1, DETAIL[quality])
        results.append((root, geo))
        if EXPORT_FBX:
            export_tree_fbx(root, geo)

    for ob in bpy.context.selected_objects:
        ob.select_set(False)
    for root, geo in results:
        root.select_set(True)
        geo.select_set(True)
    bpy.context.view_layer.objects.active = results[-1][0]
    print("[Couch Guys] New tree(s) ready. Run script again for NEW variations."
          " Set SEED to a number to regenerate an exact favourite.")


if __name__ == "__main__":
    main()
