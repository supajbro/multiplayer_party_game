
"""
Couch Guys - Procedural Cartoon Suburban House Generator
========================================================

Generates randomized, stylized suburban houses inspired by the
Couch Guys neighbourhood concept art.

Features:
- One-storey and two-storey houses
- Cream, pastel, and painted stucco walls
- Gabled and hipped roofs with visible roof tiles
- Optional attached garages
- Covered front porches with pitched entrance roofs
- Front doors, stairs, pillars, and exterior lights
- Framed windows with warm illuminated glass
- Optional shutters and chimneys
- Low-poly bushes and flower beds
- Optional driveways
- Deterministic random seeds
- Organized Blender object hierarchy
- Unity-compatible proportions and orientation

The front of the house faces Blender -Y.
One Blender unit is approximately one metre.

Running again removes ONLY the existing GeneratedHouse hierarchy.

Usage:
    Blender > Scripting > Open this file > Run Script

Command line:
    blender --background --python generate_cartoon_house.py
"""

import bpy
import math
import random
import time


# ============================================================
# GENERATOR CONFIGURATION
# ============================================================

# None generates a new variation every time.
# Set an integer to reproduce the same house.
SEED = None

# House proportions, in metres.
HOUSE_WIDTH = (8.0, 11.5)
HOUSE_DEPTH = (6.0, 8.5)
FLOOR_HEIGHT = (2.7, 3.1)

TWO_STOREY_CHANCE = 0.28
GARAGE_CHANCE = 0.75
SECONDARY_SECTION_CHANCE = 0.25
PORCH_CHANCE = 0.95
CHIMNEY_CHANCE = 0.35

# Main roofs favour the style shown in the reference.
ROOF_TYPES = (
    "HIP",
    "HIP",
    "HIP",
    "GABLE",
    "GABLE",
)

ROOF_HEIGHT = (1.35, 2.2)
ROOF_OVERHANG = (0.40, 0.65)

# Visual detail.
GENERATE_ROOF_TILES = True
GENERATE_SHUTTERS = True
GENERATE_LANDSCAPING = True
GENERATE_FLOWERS = True
GENERATE_DRIVEWAY = False

# Disable decorative details for lower-poly output.
GENERATE_ROOF_TRIM = True
GENERATE_WINDOW_SILLS = True

# More tiles increase roof detail.
ROOF_TILE_ROWS = 7
ROOF_TILE_COLUMNS = 15

# 0 disables rounded geometry.
BEVEL_WIDTH = 0.035

# Windows are visually emissive.
# Actual Unity lighting should be configured separately.
WINDOW_EMISSION = 0.45

# Number of decorative shrub clusters.
BUSH_COUNT = (4, 8)

# ============================================================
# COLOUR PALETTES
# ============================================================

# Palette is biased towards warm suburban colours rather than
# very saturated toy-house colours.

COLOUR_SCHEMES = (
    {
        "wall": (0.88, 0.83, 0.72, 1),
        "roof": (0.46, 0.27, 0.21, 1),
        "trim": (0.96, 0.94, 0.86, 1),
        "door": (0.51, 0.24, 0.13, 1),
    },
    {
        "wall": (0.91, 0.86, 0.75, 1),
        "roof": (0.53, 0.32, 0.25, 1),
        "trim": (0.98, 0.95, 0.87, 1),
        "door": (0.42, 0.27, 0.17, 1),
    },
    {
        "wall": (0.77, 0.83, 0.77, 1),
        "roof": (0.36, 0.37, 0.36, 1),
        "trim": (0.94, 0.94, 0.86, 1),
        "door": (0.25, 0.37, 0.35, 1),
    },
    {
        "wall": (0.79, 0.83, 0.87, 1),
        "roof": (0.33, 0.34, 0.39, 1),
        "trim": (0.94, 0.93, 0.89, 1),
        "door": (0.25, 0.36, 0.48, 1),
    },
    {
        "wall": (0.91, 0.80, 0.70, 1),
        "roof": (0.53, 0.29, 0.22, 1),
        "trim": (0.98, 0.93, 0.83, 1),
        "door": (0.43, 0.22, 0.17, 1),
    },
    {
        "wall": (0.82, 0.82, 0.75, 1),
        "roof": (0.39, 0.31, 0.29, 1),
        "trim": (0.97, 0.94, 0.87, 1),
        "door": (0.34, 0.35, 0.28, 1),
    },
)


# ============================================================
# OBJECT AND MATERIAL UTILITIES
# ============================================================

def delete_generated_house():
    """Remove only the previously generated house."""

    root = bpy.data.objects.get("GeneratedHouse")

    if root is None:
        return

    def remove_recursive(obj):
        for child in list(obj.children):
            remove_recursive(child)

        bpy.data.objects.remove(obj, do_unlink=True)

    remove_recursive(root)


def make_empty(name, parent=None):

    obj = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(obj)

    obj.empty_display_type = "PLAIN_AXES"
    obj.empty_display_size = 0.25

    if parent is not None:
        obj.parent = parent

    return obj


def make_material(
    name,
    colour,
    roughness=0.8,
    metallic=0.0,
    emission=0.0
):
    """Create or update a reusable Blender material."""

    mat = bpy.data.materials.get(name)

    if mat is None:
        mat = bpy.data.materials.new(name)

    mat.use_nodes = True
    mat.diffuse_color = colour

    nodes = mat.node_tree.nodes
    bsdf = nodes.get("Principled BSDF")

    if bsdf is not None:

        bsdf.inputs["Base Color"].default_value = colour
        bsdf.inputs["Roughness"].default_value = roughness
        bsdf.inputs["Metallic"].default_value = metallic

        emission_colour = (
            bsdf.inputs.get("Emission Color")
            or bsdf.inputs.get("Emission")
        )

        emission_strength = bsdf.inputs.get(
            "Emission Strength"
        )

        if emission_colour is not None:
            emission_colour.default_value = colour

        if emission_strength is not None:
            emission_strength.default_value = emission

    return mat


def vary_colour(colour, rng, amount=0.025):

    return tuple(
        max(0.0, min(1.0, c + rng.uniform(-amount, amount)))
        for c in colour[:3]
    ) + (1.0,)


def create_materials(rng):

    scheme = dict(rng.choice(COLOUR_SCHEMES))

    wall = vary_colour(scheme["wall"], rng)
    roof = vary_colour(scheme["roof"], rng)

    def shade(col, factor):

        return tuple(
            min(1.0, max(0.0, channel * factor))
            for channel in col[:3]
        ) + (1.0,)

    return {
        "wall": make_material(
            "House_Stucco",
            wall,
        ),

        "roof": make_material(
            "House_TerracottaRoof",
            roof,
            roughness=0.93,
        ),

        "roof_light": make_material(
            "House_RoofTileLight",
            shade(roof, 1.12),
            roughness=0.93,
        ),

        "roof_dark": make_material(
            "House_RoofTileDark",
            shade(roof, 0.90),
            roughness=0.93,
        ),

        "roof_edge": make_material(
            "House_RoofEdge",
            shade(roof, 0.76),
        ),

        "trim": make_material(
            "House_WhiteTrim",
            scheme["trim"],
        ),

        "door": make_material(
            "House_FrontDoor",
            scheme["door"],
        ),

        "glass": make_material(
            "House_WindowGlass",
            (0.44, 0.68, 0.78, 1),
            roughness=0.25,
        ),

        "warm_glass": make_material(
            "House_WarmWindowGlass",
            (1.0, 0.76, 0.36, 1),
            roughness=0.3,
            emission=WINDOW_EMISSION,
        ),

        "light": make_material(
            "House_ExteriorLight",
            (1.0, 0.84, 0.49, 1),
            emission=0.9,
        ),

        "dark": make_material(
            "House_DarkDetails",
            (0.18, 0.19, 0.19, 1),
        ),

        "foundation": make_material(
            "House_StoneFoundation",
            (0.62, 0.61, 0.55, 1),
        ),

        "stone": make_material(
            "House_PorchStone",
            (0.74, 0.73, 0.68, 1),
        ),

        "garage": make_material(
            "House_GarageDoor",
            (0.90, 0.89, 0.84, 1),
        ),

        "wood": make_material(
            "House_WoodAccent",
            (0.45, 0.30, 0.20, 1),
        ),

        "shutter": make_material(
            "House_WindowShutter",
            shade(scheme["door"], 0.88),
        ),

        "grass": make_material(
            "House_GardenGrass",
            (0.35, 0.58, 0.23, 1),
        ),

        "bush": make_material(
            "House_BushGreen",
            (0.27, 0.49, 0.20, 1),
        ),

        "bush_light": make_material(
            "House_BushLight",
            (0.42, 0.63, 0.29, 1),
        ),

        "bush_dark": make_material(
            "House_BushDark",
            (0.20, 0.39, 0.16, 1),
        ),

        "flower_pink": make_material(
            "House_FlowerPink",
            (0.97, 0.42, 0.56, 1),
        ),

        "flower_yellow": make_material(
            "House_FlowerYellow",
            (1.0, 0.79, 0.27, 1),
        ),

        "flower_white": make_material(
            "House_FlowerWhite",
            (0.98, 0.96, 0.86, 1),
        ),

        "soil": make_material(
            "House_GardenSoil",
            (0.32, 0.25, 0.18, 1),
        ),

        "concrete": make_material(
            "House_Driveway",
            (0.66, 0.65, 0.61, 1),
        ),
    }


# ============================================================
# GEOMETRY UTILITIES
# ============================================================

def create_mesh(
    name,
    vertices,
    faces,
    material,
    parent,
    bevel=0.0,
    extra_materials=None,
    face_material_indices=None,
):
    """Create a mesh without relying on Blender edit-mode operators."""

    if not vertices or not faces:
        return None

    centre = tuple(
        (
            min(v[i] for v in vertices)
            + max(v[i] for v in vertices)
        ) * 0.5
        for i in range(3)
    )

    local_vertices = [
        (
            v[0] - centre[0],
            v[1] - centre[1],
            v[2] - centre[2],
        )
        for v in vertices
    ]

    mesh = bpy.data.meshes.new("House_" + name)

    mesh.from_pydata(local_vertices, [], faces)
    mesh.update()

    mesh.materials.append(material)

    if extra_materials:
        for mat in extra_materials:
            mesh.materials.append(mat)

    if face_material_indices:
        for polygon, index in zip(
            mesh.polygons,
            face_material_indices,
        ):
            polygon.material_index = index

    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)

    obj.location = centre
    obj.parent = parent

    if bevel > 0.0:
        modifier = obj.modifiers.new(
            "SoftCartoonEdges",
            "BEVEL",
        )

        modifier.width = bevel
        modifier.segments = 1
        modifier.limit_method = "ANGLE"

        normal_modifier = obj.modifiers.new(
            "WeightedNormals",
            "WEIGHTED_NORMAL",
        )

        normal_modifier.keep_sharp = True

    return obj


def create_box(
    name,
    location,
    dimensions,
    material,
    parent,
    bevel=BEVEL_WIDTH,
):

    x, y, z = location

    hx = dimensions[0] * 0.5
    hy = dimensions[1] * 0.5
    hz = dimensions[2] * 0.5

    verts = [
        (x - hx, y - hy, z - hz),
        (x + hx, y - hy, z - hz),
        (x + hx, y + hy, z - hz),
        (x - hx, y + hy, z - hz),

        (x - hx, y - hy, z + hz),
        (x + hx, y - hy, z + hz),
        (x + hx, y + hy, z + hz),
        (x - hx, y + hy, z + hz),
    ]

    faces = [
        (0, 3, 2, 1),
        (4, 5, 6, 7),
        (0, 1, 5, 4),
        (1, 2, 6, 5),
        (2, 3, 7, 6),
        (3, 0, 4, 7),
    ]

    safe_bevel = min(
        bevel,
        min(dimensions) * 0.2,
    )

    return create_mesh(
        name,
        verts,
        faces,
        material,
        parent,
        bevel=max(0.0, safe_bevel),
    )


def create_icosphere(
    name,
    location,
    scale,
    material,
    parent,
    subdivisions=1,
):

    bpy.ops.mesh.primitive_ico_sphere_add(
        subdivisions=subdivisions,
        radius=1.0,
        location=location,
    )

    obj = bpy.context.object

    obj.name = name
    obj.data.name = "House_" + name
    obj.scale = scale

    obj.data.materials.append(material)
    obj.parent = parent

    return obj


def create_cylinder(
    name,
    location,
    radius,
    depth,
    material,
    parent,
    vertices=12,
):

    bpy.ops.mesh.primitive_cylinder_add(
        vertices=vertices,
        radius=radius,
        depth=depth,
        location=location,
    )

    obj = bpy.context.object

    obj.name = name
    obj.data.name = "House_" + name
    obj.data.materials.append(material)
    obj.parent = parent

    return obj


# ============================================================
# HOUSE LAYOUT
# ============================================================

def choose_layout(rng):

    floors = (
        2 if rng.random() < TWO_STOREY_CHANCE else 1
    )

    width = rng.uniform(*HOUSE_WIDTH)
    depth = rng.uniform(*HOUSE_DEPTH)

    floor_height = rng.uniform(*FLOOR_HEIGHT)

    garage = rng.random() < GARAGE_CHANCE
    garage_side = rng.choice((-1, 1))

    secondary = (
        rng.random() < SECONDARY_SECTION_CHANCE
    )

    secondary_side = (
        -garage_side
        if garage
        else rng.choice((-1, 1))
    )

    # Door is kept near the centre to allow two attractive
    # front window groups.
    door_x = rng.uniform(-0.55, 0.55)

    return {
        "width": width,
        "depth": depth,
        "floors": floors,
        "floor_height": floor_height,
        "body_height": floors * floor_height,

        "roof_type": rng.choice(ROOF_TYPES),
        "roof_height": rng.uniform(*ROOF_HEIGHT),
        "roof_overhang": rng.uniform(
            *ROOF_OVERHANG
        ),

        "door_x": door_x,

        "has_garage": garage,
        "garage_side": garage_side,

        "has_secondary": secondary,
        "secondary_side": secondary_side,

        "has_porch": (
            rng.random() < PORCH_CHANCE
        ),

        "has_chimney": (
            rng.random() < CHIMNEY_CHANCE
        ),
    }


# ============================================================
# ROOF GENERATION
# ============================================================

def roof_coordinate(u, v, centre, direction):
    """
    Convert roof-local coordinates into world XY.

    X direction: roof ridge runs along world X.
    Y direction: roof ridge runs along world Y.
    """

    if direction == "X":
        return (
            centre[0] + u,
            centre[1] + v,
        )

    return (
        centre[0] - v,
        centre[1] + u,
    )


def create_roof_piece(
    name,
    centre,
    width,
    depth,
    base_z,
    height,
    roof_type,
    direction,
    materials,
    parent,
    rng,
    tiles=True,
):

    if direction == "X":
        ridge_length = width
        slope_span = depth
    else:
        ridge_length = depth
        slope_span = width

    half_u = ridge_length * 0.5
    half_v = slope_span * 0.5

    low_z = base_z - 0.10

    def point(u, v, z):
        x, y = roof_coordinate(
            u, v, centre, direction
        )
        return (x, y, z)

    verts = [
        point(-half_u, -half_v, low_z),
        point(half_u, -half_v, low_z),
        point(half_u, half_v, low_z),
        point(-half_u, half_v, low_z),
    ]

    if roof_type == "HIP":

        ridge_half = half_u * 0.50

        verts.extend([
            point(
                -ridge_half,
                0,
                base_z + height,
            ),
            point(
                ridge_half,
                0,
                base_z + height,
            ),
        ])

        faces = [
            (0, 1, 5, 4),
            (3, 4, 5, 2),
            (0, 4, 3),
            (1, 2, 5),
            (0, 3, 2, 1),
        ]

    else:

        ridge_half = half_u

        verts.extend([
            point(
                -half_u,
                0,
                base_z + height,
            ),
            point(
                half_u,
                0,
                base_z + height,
            ),
        ])

        faces = [
            (0, 1, 5, 4),
            (3, 4, 5, 2),
            (0, 4, 3),
            (1, 2, 5),
            (0, 3, 2, 1),
        ]

    roof = create_mesh(
        name,
        verts,
        faces,
        materials["roof"],
        parent,
        bevel=0.025,
    )

    if GENERATE_ROOF_TILES and tiles:

        create_roof_tiles(
            name,
            centre,
            ridge_length,
            slope_span,
            base_z,
            height,
            roof_type,
            direction,
            ridge_half,
            materials,
            parent,
            rng,
        )

    if GENERATE_ROOF_TRIM:

        create_roof_fascia(
            name,
            centre,
            ridge_length,
            slope_span,
            low_z,
            direction,
            materials,
            parent,
        )

    return roof


def create_roof_tiles(
    name,
    centre,
    ridge_length,
    slope_span,
    base_z,
    height,
    roof_type,
    direction,
    ridge_half,
    materials,
    parent,
    rng,
):
    """
    Create roof tile surfaces as one batched mesh.

    Tiles follow the roof slope instead of being flat blocks.
    Hipped roofs taper towards the ridge.
    """

    half_u = ridge_length * 0.5
    half_v = slope_span * 0.5

    rows = max(2, ROOF_TILE_ROWS)

    columns = max(
        4,
        round(
            ROOF_TILE_COLUMNS
            * ridge_length / 10.0
        ),
    )

    verts = []
    faces = []
    material_indices = []

    def roof_half_width(v):

        if roof_type != "HIP":
            return half_u

        t = abs(v) / half_v

        return (
            ridge_half
            + (half_u - ridge_half) * t
        )

    def roof_z(v):

        t = abs(v) / half_v

        return (
            base_z
            + height * (1.0 - t)
            + 0.018
        )

    def add_tile(u0, u1, v0, v1, mat_index):

        points = [
            (u0[0], v0, roof_z(v0)),
            (u0[1], v0, roof_z(v0)),
            (u1[1], v1, roof_z(v1)),
            (u1[0], v1, roof_z(v1)),
        ]

        start = len(verts)

        for u, v, z in points:
            x, y = roof_coordinate(
                u,
                v,
                centre,
                direction,
            )

            verts.append((x, y, z))

        faces.append((
            start,
            start + 1,
            start + 2,
            start + 3,
        ))

        material_indices.append(mat_index)

    # Two large slopes.
    for side in (-1, 1):

        for row in range(rows):

            t0 = row / rows
            t1 = (row + 1) / rows

            # Leave small gaps between courses so the
            # tile pattern remains visible.
            t0 += 0.007
            t1 -= 0.007

            if side == -1:
                v0 = -half_v + t0 * half_v
                v1 = -half_v + t1 * half_v
            else:
                v0 = t0 * half_v
                v1 = t1 * half_v

            width0 = roof_half_width(v0)
            width1 = roof_half_width(v1)

            for col in range(columns):

                c0 = col / columns + 0.003
                c1 = (col + 1) / columns - 0.003

                if c1 <= c0:
                    continue

                bottom_u = (
                    -width0 + 2 * width0 * c0,
                    -width0 + 2 * width0 * c1,
                )

                top_u = (
                    -width1 + 2 * width1 * c0,
                    -width1 + 2 * width1 * c1,
                )

                tile_material = rng.choices(
                    [0, 1, 2],
                    weights=[75, 12, 13],
                    k=1,
                )[0]

                add_tile(
                    bottom_u,
                    top_u,
                    v0,
                    v1,
                    tile_material,
                )

    create_mesh(
        name + "_RoofTiles",
        verts,
        faces,
        materials["roof"],
        parent,
        extra_materials=[
            materials["roof_light"],
            materials["roof_dark"],
        ],
        face_material_indices=material_indices,
    )


def create_roof_fascia(
    name,
    centre,
    ridge_length,
    slope_span,
    low_z,
    direction,
    materials,
    parent,
):

    half_v = slope_span * 0.5

    for side, label in (
        (-1, "Front"),
        (1, "Back"),
    ):

        u = 0.0
        v = side * half_v

        x, y = roof_coordinate(
            u,
            v,
            centre,
            direction,
        )

        if direction == "X":
            dims = (
                ridge_length,
                0.12,
                0.16,
            )
        else:
            dims = (
                0.12,
                ridge_length,
                0.16,
            )

        create_box(
            name + "_Fascia_" + label,
            (x, y, low_z + 0.04),
            dims,
            materials["roof_edge"],
            parent,
            bevel=0.025,
        )


# ============================================================
# MAIN HOUSE STRUCTURE
# ============================================================

def create_house_body(
    layout,
    materials,
    groups,
    rng,
):

    w = layout["width"]
    d = layout["depth"]
    h = layout["body_height"]

    create_box(
        "MainHouseBody",
        (0, 0, h * 0.5),
        (w, d, h),
        materials["wall"],
        groups["Structure"],
        bevel=0.09,
    )

    create_box(
        "Foundation",
        (0, 0, 0.15),
        (w + 0.15, d + 0.15, 0.30),
        materials["foundation"],
        groups["Structure"],
        bevel=0.025,
    )

    # Thin horizontal architectural accent.
    create_box(
        "FrontCornice",
        (
            0,
            -d * 0.5 - 0.055,
            h - 0.20,
        ),
        (
            w + 0.06,
            0.12,
            0.13,
        ),
        materials["trim"],
        groups["Structure"],
        bevel=0.02,
    )

    if layout["floors"] == 2:

        create_box(
            "FloorSeparationTrim",
            (
                0,
                -d * 0.5 - 0.045,
                layout["floor_height"],
            ),
            (
                w + 0.05,
                0.09,
                0.13,
            ),
            materials["trim"],
            groups["Structure"],
            bevel=0.015,
        )


def create_secondary_section(
    layout,
    materials,
    groups,
    rng,
):

    if not layout["has_secondary"]:
        return

    side = layout["secondary_side"]

    w = rng.uniform(2.6, 3.5)
    d = rng.uniform(4.5, 6.3)
    h = layout["floor_height"] * rng.uniform(
        0.91, 1.02
    )

    x = side * (
        layout["width"] * 0.5
        + w * 0.38
    )

    y = rng.uniform(0.2, 0.7)

    create_box(
        "SecondaryHouseBody",
        (x, y, h * 0.5),
        (w, d, h),
        materials["wall"],
        groups["Structure"],
        bevel=0.08,
    )

    create_roof_piece(
        "SecondaryRoof",
        (x, y),
        w + 0.65,
        d + 0.65,
        h,
        rng.uniform(0.85, 1.35),
        rng.choice(("GABLE", "HIP")),
        "Y",
        materials,
        groups["Roof"],
        rng,
    )

    layout["secondary_data"] = {
        "x": x,
        "y": y,
        "width": w,
        "depth": d,
        "height": h,
    }


def create_main_roof(
    layout,
    materials,
    groups,
    rng,
):

    overhang = layout["roof_overhang"]

    create_roof_piece(
        "MainRoof",
        (0, 0),
        layout["width"] + 2 * overhang,
        layout["depth"] + 2 * overhang,
        layout["body_height"],
        layout["roof_height"],
        layout["roof_type"],
        "X",
        materials,
        groups["Roof"],
        rng,
    )


# ============================================================
# WINDOWS
# ============================================================

def create_window(
    name,
    location,
    width,
    height,
    facing,
    materials,
    parent,
    warm=True,
    shutters=False,
):
    """
    Generate a cartoon window with oversized trim,
    warm-lit glass, mullions, and a sill.

    facing:
        FRONT = lies along X, faces -Y
        SIDE  = lies along Y, faces +/-X
    """

    x, y, z = location

    frame = 0.105
    thickness = 0.10

    glass_mat = (
        materials["warm_glass"]
        if warm
        else materials["glass"]
    )

    def add_part(suffix, u, v, normal, du, dv, mat):

        if facing == "FRONT":

            loc = (
                x + u,
                y + normal,
                z + v,
            )

            dims = (
                du,
                thickness,
                dv,
            )

        else:

            sign = 1 if x >= 0 else -1

            loc = (
                x + sign * normal,
                y + u,
                z + v,
            )

            dims = (
                thickness,
                du,
                dv,
            )

        return create_box(
            name + "_" + suffix,
            loc,
            dims,
            mat,
            parent,
            bevel=0.015,
        )

    # Glass panel.
    add_part(
        "Glass",
        0, 0, 0,
        width,
        height,
        glass_mat,
    )

    # Thick outer frame.
    add_part(
        "TopFrame",
        0,
        height * 0.5,
        -0.045,
        width + frame * 2,
        frame,
        materials["trim"],
    )

    add_part(
        "BottomFrame",
        0,
        -height * 0.5,
        -0.045,
        width + frame * 2,
        frame,
        materials["trim"],
    )

    for label, side in (
        ("Left", -1),
        ("Right", 1),
    ):

        add_part(
            label + "Frame",
            side * width * 0.5,
            0,
            -0.045,
            frame,
            height,
            materials["trim"],
        )

    # Vertical divider.
    add_part(
        "VerticalMullion",
        0,
        0,
        -0.065,
        0.065,
        height,
        materials["trim"],
    )

    # Horizontal divider.
    add_part(
        "HorizontalMullion",
        0,
        0.08,
        -0.065,
        width,
        0.065,
        materials["trim"],
    )

    if GENERATE_WINDOW_SILLS:

        add_part(
            "WindowSill",
            0,
            -height * 0.5 - 0.075,
            -0.13,
            width + 0.28,
            0.13,
            materials["stone"],
        )

    if shutters and GENERATE_SHUTTERS:

        shutter_width = 0.20

        for label, side in (
            ("Left", -1),
            ("Right", 1),
        ):

            add_part(
                "Shutter_" + label,
                side * (
                    width * 0.5 + 0.19
                ),
                0,
                -0.03,
                shutter_width,
                height * 0.91,
                materials["shutter"],
            )

            # Two simple decorative shutter slats.
            for i in range(2):

                add_part(
                    "ShutterSlat_"
                    + label
                    + str(i),
                    side * (
                        width * 0.5 + 0.19
                    ),
                    (i - 0.5) * height * 0.30,
                    -0.09,
                    shutter_width * 0.82,
                    0.035,
                    materials["trim"],
                )


def create_house_windows(
    layout,
    materials,
    groups,
    rng,
):

    w = layout["width"]
    d = layout["depth"]

    front_y = -d * 0.5 - 0.075

    floor_h = layout["floor_height"]

    # Symmetrical ground-floor windows match
    # the concept's suburban home silhouettes.
    positions = [
        -w * 0.32,
        w * 0.32,
    ]

    for i, x in enumerate(positions):

        create_window(
            "GroundFloorWindow_" + str(i),
            (
                x,
                front_y,
                floor_h * 0.57,
            ),
            rng.uniform(1.15, 1.55),
            rng.uniform(1.1, 1.4),
            "FRONT",
            materials,
            groups["Windows"],
            warm=rng.random() < 0.85,
            shutters=rng.random() < 0.35,
        )

    # Upper-level windows.
    if layout["floors"] == 2:

        positions = [
            -w * 0.31,
            0.0,
            w * 0.31,
        ]

        for i, x in enumerate(positions):

            create_window(
                "UpperFloorWindow_" + str(i),
                (
                    x,
                    front_y,
                    floor_h * 1.60,
                ),
                rng.uniform(1.0, 1.35),
                rng.uniform(1.0, 1.35),
                "FRONT",
                materials,
                groups["Windows"],
                warm=rng.random() < 0.75,
                shutters=rng.random() < 0.30,
            )

    # Side windows.
    for side_name, side in (
        ("Left", -1),
        ("Right", 1),
    ):

        if (
            layout["has_garage"]
            and side == layout["garage_side"]
        ):
            continue

        if (
            layout["has_secondary"]
            and side == layout["secondary_side"]
        ):
            continue

        for floor in range(layout["floors"]):

            if rng.random() > 0.85:
                continue

            create_window(
                "SideWindow_"
                + side_name
                + "_"
                + str(floor),
                (
                    side * (w * 0.5 + 0.075),
                    rng.uniform(-d * 0.15, d * 0.15),
                    floor_h * (floor + 0.58),
                ),
                rng.uniform(0.95, 1.25),
                rng.uniform(1.0, 1.30),
                "SIDE",
                materials,
                groups["Windows"],
                warm=rng.random() < 0.75,
            )

    # Secondary section window.
    section = layout.get("secondary_data")

    if section:

        create_window(
            "SecondaryFrontWindow",
            (
                section["x"],
                section["y"]
                - section["depth"] * 0.5
                - 0.075,
                section["height"] * 0.57,
            ),
            1.15,
            1.15,
            "FRONT",
            materials,
            groups["Windows"],
            warm=True,
        )


# ============================================================
# FRONT DOOR
# ============================================================

def create_front_door(
    layout,
    materials,
    groups,
    rng,
):

    x = layout["door_x"]
    front = -layout["depth"] * 0.5

    door_width = rng.uniform(1.05, 1.25)
    door_height = rng.uniform(2.15, 2.40)

    door_y = front - 0.105
    door_z = door_height * 0.5 + 0.13

    create_box(
        "FrontDoor",
        (
            x,
            door_y,
            door_z,
        ),
        (
            door_width,
            0.14,
            door_height,
        ),
        materials["door"],
        groups["Doors"],
        bevel=0.045,
    )

    frame = 0.12

    create_box(
        "DoorTopFrame",
        (
            x,
            door_y - 0.035,
            door_z + door_height * 0.5,
        ),
        (
            door_width + frame * 2,
            0.17,
            frame,
        ),
        materials["trim"],
        groups["Doors"],
    )

    for side in (-1, 1):

        create_box(
            "DoorSideFrame_" + str(side),
            (
                x + side * door_width * 0.5,
                door_y - 0.035,
                door_z,
            ),
            (
                frame,
                0.17,
                door_height,
            ),
            materials["trim"],
            groups["Doors"],
        )

    # Raised door panels.
    for index, panel_z in enumerate(
        (
            door_z - door_height * 0.22,
            door_z + door_height * 0.18,
        )
    ):

        create_box(
            "DoorPanel_" + str(index),
            (
                x,
                door_y - 0.09,
                panel_z,
            ),
            (
                door_width * 0.66,
                0.045,
                door_height * 0.27,
            ),
            materials["wood"],
            groups["Doors"],
            bevel=0.02,
        )

    # Small door handle.
    create_box(
        "DoorHandle",
        (
            x + door_width * 0.33,
            door_y - 0.14,
            door_z,
        ),
        (
            0.065,
            0.09,
            0.16,
        ),
        materials["dark"],
        groups["Doors"],
        bevel=0.02,
    )


# ============================================================
# FRONT PORCH AND STAIRS
# ============================================================

def create_porch(
    layout,
    materials,
    groups,
    rng,
):

    x = layout["door_x"]
    front = -layout["depth"] * 0.5

    width = rng.uniform(2.75, 3.55)
    depth = rng.uniform(1.35, 1.85)

    centre_y = front - depth * 0.51

    deck_height = 0.38

    # Raised entrance platform.
    create_box(
        "PorchFoundation",
        (
            x,
            centre_y,
            deck_height * 0.5,
        ),
        (
            width,
            depth,
            deck_height,
        ),
        materials["stone"],
        groups["Porch"],
        bevel=0.045,
    )

    create_box(
        "PorchDeckTop",
        (
            x,
            centre_y,
            deck_height + 0.045,
        ),
        (
            width + 0.10,
            depth + 0.10,
            0.09,
        ),
        materials["foundation"],
        groups["Porch"],
        bevel=0.025,
    )

    # Three wide stone entrance steps.
    stair_width = width * 0.66

    for i in range(3):

        step_height = 0.12 * (i + 1)

        step_y = (
            centre_y
            - depth * 0.5
            - 0.68
            + i * 0.27
        )

        create_box(
            "PorchStep_" + str(i),
            (
                x,
                step_y,
                step_height * 0.5,
            ),
            (
                stair_width + (2 - i) * 0.15,
                0.48,
                step_height,
            ),
            materials["stone"],
            groups["Porch"],
            bevel=0.035,
        )

    if not layout["has_porch"]:
        return

    roof_base = rng.uniform(2.73, 2.95)
    roof_rise = rng.uniform(0.62, 0.95)

    # The gable ridge runs front to back.
    # This creates the little triangular entry roof
    # seen in the reference illustration.
    create_roof_piece(
        "PorchGableRoof",
        (
            x,
            centre_y,
        ),
        width + 0.42,
        depth + 0.50,
        roof_base,
        roof_rise,
        "GABLE",
        "Y",
        materials,
        groups["Porch"],
        rng,
    )

    # Two columns supporting the entrance roof.
    column_height = roof_base - deck_height

    for label, side in (
        ("Left", -1),
        ("Right", 1),
    ):

        column_x = x + side * width * 0.40

        column_y = (
            centre_y
            - depth * 0.5
            + 0.18
        )

        create_box(
            "PorchColumn_" + label,
            (
                column_x,
                column_y,
                deck_height + column_height * 0.5,
            ),
            (
                0.19,
                0.19,
                column_height,
            ),
            materials["trim"],
            groups["Porch"],
            bevel=0.035,
        )

        # Chunky column base.
        create_box(
            "PorchColumnBase_" + label,
            (
                column_x,
                column_y,
                deck_height + 0.13,
            ),
            (
                0.30,
                0.30,
                0.26,
            ),
            materials["stone"],
            groups["Porch"],
            bevel=0.035,
        )

        # Decorative capital.
        create_box(
            "PorchColumnCapital_" + label,
            (
                column_x,
                column_y,
                roof_base - 0.09,
            ),
            (
                0.31,
                0.31,
                0.18,
            ),
            materials["trim"],
            groups["Porch"],
            bevel=0.02,
        )

    # Low side railings.
    for label, side in (
        ("Left", -1),
        ("Right", 1),
    ):

        rail_x = x + side * width * 0.46

        create_box(
            "PorchRailing_" + label,
            (
                rail_x,
                centre_y + 0.18,
                deck_height + 0.47,
            ),
            (
                0.10,
                depth * 0.58,
                0.10,
            ),
            materials["trim"],
            groups["Porch"],
            bevel=0.018,
        )


# ============================================================
# GARAGE
# ============================================================

def create_garage(
    layout,
    materials,
    groups,
    rng,
):

    if not layout["has_garage"]:
        return

    side = layout["garage_side"]

    gw = rng.uniform(3.3, 4.35)
    gd = rng.uniform(5.2, 6.7)
    gh = rng.uniform(2.65, 3.0)

    gx = side * (
        layout["width"] * 0.5
        + gw * 0.45
    )

    # Garage front roughly aligns with the house front.
    front = -layout["depth"] * 0.5

    gy = front + gd * 0.5 + 0.12

    create_box(
        "GarageBody",
        (
            gx,
            gy,
            gh * 0.5,
        ),
        (
            gw,
            gd,
            gh,
        ),
        materials["wall"],
        groups["Garage"],
        bevel=0.08,
    )

    create_box(
        "GarageFoundation",
        (
            gx,
            gy,
            0.13,
        ),
        (
            gw + 0.10,
            gd + 0.10,
            0.26,
        ),
        materials["foundation"],
        groups["Garage"],
        bevel=0.025,
    )

    # Front-facing gable matches suburban garages.
    create_roof_piece(
        "GarageRoof",
        (
            gx,
            gy,
        ),
        gw + 0.72,
        gd + 0.72,
        gh,
        rng.uniform(0.95, 1.35),
        "GABLE",
        "Y",
        materials,
        groups["Garage"],
        rng,
    )

    door_width = gw * 0.79
    door_height = min(
        gh - 0.27,
        rng.uniform(2.12, 2.40),
    )

    garage_front = gy - gd * 0.5 - 0.085

    create_box(
        "GarageDoor",
        (
            gx,
            garage_front,
            door_height * 0.5 + 0.09,
        ),
        (
            door_width,
            0.14,
            door_height,
        ),
        materials["garage"],
        groups["Garage"],
        bevel=0.035,
    )

    # Panelled garage door.
    for row in range(1, 5):

        z = (
            0.09
            + door_height * row / 5
        )

        create_box(
            "GarageHorizontalPanel_" + str(row),
            (
                gx,
                garage_front - 0.085,
                z,
            ),
            (
                door_width * 0.96,
                0.025,
                0.035,
            ),
            materials["trim"],
            groups["Garage"],
            bevel=0.006,
        )

    for col in (-1, 0, 1):

        x = gx + col * door_width * 0.25

        create_box(
            "GarageVerticalPanel_" + str(col),
            (
                x,
                garage_front - 0.088,
                door_height * 0.5 + 0.09,
            ),
            (
                0.035,
                0.025,
                door_height * 0.93,
            ),
            materials["trim"],
            groups["Garage"],
            bevel=0.005,
        )

    # Garage perimeter trim.
    frame_width = 0.12

    create_box(
        "GarageDoorTopTrim",
        (
            gx,
            garage_front - 0.04,
            door_height + 0.09,
        ),
        (
            door_width + 0.22,
            0.15,
            frame_width,
        ),
        materials["trim"],
        groups["Garage"],
    )

    for s in (-1, 1):

        create_box(
            "GarageDoorSideTrim_" + str(s),
            (
                gx + s * door_width * 0.5,
                garage_front - 0.04,
                door_height * 0.5 + 0.09,
            ),
            (
                0.12,
                0.15,
                door_height,
            ),
            materials["trim"],
            groups["Garage"],
        )

    # Small vent or detail in the front gable.
    create_box(
        "GarageGableVent",
        (
            gx,
            garage_front + 0.05,
            gh + 0.30,
        ),
        (
            0.50,
            0.085,
            0.22,
        ),
        materials["dark"],
        groups["Garage"],
        bevel=0.035,
    )

    for i in range(3):

        create_box(
            "GarageVentSlat_" + str(i),
            (
                gx,
                garage_front - 0.003,
                gh + 0.23 + i * 0.065,
            ),
            (
                0.40,
                0.025,
                0.018,
            ),
            materials["trim"],
            groups["Garage"],
            bevel=0.004,
        )

    layout["garage_data"] = {
        "x": gx,
        "y": gy,
        "width": gw,
        "depth": gd,
        "front": garage_front,
    }


# ============================================================
# CHIMNEY
# ============================================================

def create_chimney(
    layout,
    materials,
    groups,
    rng,
):

    if not layout["has_chimney"]:
        return

    x = rng.uniform(
        -layout["width"] * 0.27,
        layout["width"] * 0.27,
    )

    y = rng.uniform(
        -layout["depth"] * 0.10,
        layout["depth"] * 0.10,
    )

    height = rng.uniform(1.2, 1.7)

    base_z = (
        layout["body_height"]
        + layout["roof_height"] * 0.45
    )

    create_box(
        "ChimneyBody",
        (
            x,
            y,
            base_z + height * 0.5,
        ),
        (
            0.66,
            0.75,
            height,
        ),
        materials["wall"],
        groups["Details"],
        bevel=0.045,
    )

    create_box(
        "ChimneyCap",
        (
            x,
            y,
            base_z + height,
        ),
        (
            0.88,
            0.96,
            0.17,
        ),
        materials["stone"],
        groups["Details"],
        bevel=0.035,
    )

    create_box(
        "ChimneyOpening",
        (
            x,
            y,
            base_z + height + 0.09,
        ),
        (
            0.44,
            0.50,
            0.025,
        ),
        materials["dark"],
        groups["Details"],
        bevel=0.0,
    )


# ============================================================
# EXTERIOR LIGHTS
# ============================================================

def create_exterior_lights(
    layout,
    materials,
    groups,
    rng,
):

    front = -layout["depth"] * 0.5
    door_x = layout["door_x"]

    for side in (-1, 1):

        x = door_x + side * 0.93

        # Dark wall-mounted base.
        create_box(
            "EntranceLightBase_" + str(side),
            (
                x,
                front - 0.12,
                1.96,
            ),
            (
                0.23,
                0.10,
                0.36,
            ),
            materials["dark"],
            groups["Details"],
            bevel=0.035,
        )

        # Warm illuminated central glass.
        create_box(
            "EntranceLightGlow_" + str(side),
            (
                x,
                front - 0.19,
                1.96,
            ),
            (
                0.16,
                0.09,
                0.25,
            ),
            materials["light"],
            groups["Details"],
            bevel=0.025,
        )

        create_box(
            "EntranceLightCap_" + str(side),
            (
                x,
                front - 0.17,
                2.16,
            ),
            (
                0.27,
                0.18,
                0.08,
            ),
            materials["dark"],
            groups["Details"],
            bevel=0.018,
        )


# ============================================================
# GARDEN / LANDSCAPING
# ============================================================

def create_bush(
    name,
    location,
    radius,
    materials,
    parent,
    rng,
):

    x, y, z = location

    green = rng.choice((
        materials["bush"],
        materials["bush_light"],
        materials["bush_dark"],
    ))

    create_icosphere(
        name + "_Main",
        (
            x,
            y,
            z + radius * 0.75,
        ),
        (
            radius,
            radius * 0.77,
            radius * 0.85,
        ),
        green,
        parent,
    )

    # Smaller offset leaves make the shrub less spherical.
    for i in range(2):

        angle = rng.uniform(0, math.tau)

        dx = math.cos(angle) * radius * 0.42
        dy = math.sin(angle) * radius * 0.40

        create_icosphere(
            name + "_Leaves_" + str(i),
            (
                x + dx,
                y + dy,
                z + radius * 0.9,
            ),
            (
                radius * 0.57,
                radius * 0.55,
                radius * 0.62,
            ),
            rng.choice((
                materials["bush"],
                materials["bush_light"],
            )),
            parent,
        )


def create_flower_patch(
    name,
    location,
    materials,
    parent,
    rng,
):

    x, y, z = location

    colours = (
        materials["flower_pink"],
        materials["flower_yellow"],
        materials["flower_white"],
    )

    for i in range(rng.randint(3, 6)):

        px = x + rng.uniform(-0.30, 0.30)
        py = y + rng.uniform(-0.25, 0.25)

        stem_height = rng.uniform(0.18, 0.28)

        create_cylinder(
            name + "_Stem_" + str(i),
            (
                px,
                py,
                z + stem_height * 0.5,
            ),
            0.025,
            stem_height,
            materials["bush_dark"],
            parent,
            vertices=6,
        )

        create_icosphere(
            name + "_Flower_" + str(i),
            (
                px,
                py,
                z + stem_height,
            ),
            (
                0.095,
                0.095,
                0.075,
            ),
            rng.choice(colours),
            parent,
        )


def create_landscaping(
    layout,
    materials,
    groups,
    rng,
):

    if not GENERATE_LANDSCAPING:
        return

    width = layout["width"]
    front = -layout["depth"] * 0.5

    # Shallow decorative garden beds near the house.
    for side in (-1, 1):

        x = side * width * 0.33

        create_box(
            "GardenBed_" + str(side),
            (
                x,
                front - 0.50,
                0.035,
            ),
            (
                width * 0.26,
                0.75,
                0.07,
            ),
            materials["soil"],
            groups["Garden"],
            bevel=0.025,
        )

    # Place bushes primarily near the facade.
    # Avoid the centre doorway and garage entrance.
    bush_count = rng.randint(*BUSH_COUNT)

    for i in range(bush_count):

        side = -1 if i % 2 == 0 else 1

        x = side * rng.uniform(
            width * 0.21,
            width * 0.46,
        )

        y = front - rng.uniform(0.28, 0.75)

        radius = rng.uniform(0.28, 0.48)

        create_bush(
            "FrontBush_" + str(i),
            (
                x,
                y,
                0,
            ),
            radius,
            materials,
            groups["Garden"],
            rng,
        )

    if GENERATE_FLOWERS:

        for i in range(5):

            side = -1 if i % 2 == 0 else 1

            x = side * rng.uniform(
                width * 0.24,
                width * 0.45,
            )

            y = front - rng.uniform(0.55, 0.85)

            create_flower_patch(
                "FlowerPatch_" + str(i),
                (
                    x,
                    y,
                    0.04,
                ),
                materials,
                groups["Garden"],
                rng,
            )

    # Small planters beside the entrance.
    door_x = layout["door_x"]

    for side in (-1, 1):

        px = door_x + side * 1.60

        py = front - 1.15

        create_box(
            "EntrancePlanter_" + str(side),
            (
                px,
                py,
                0.22,
            ),
            (
                0.52,
                0.52,
                0.44,
            ),
            materials["stone"],
            groups["Garden"],
            bevel=0.055,
        )

        create_bush(
            "EntrancePlant_" + str(side),
            (
                px,
                py,
                0.44,
            ),
            0.36,
            materials,
            groups["Garden"],
            rng,
        )


# ============================================================
# OPTIONAL DRIVEWAY
# ============================================================

def create_driveway(
    layout,
    materials,
    groups,
    rng,
):

    if not GENERATE_DRIVEWAY:
        return

    garage = layout.get("garage_data")

    if garage is None:
        return

    length = rng.uniform(4.5, 7.5)

    front = garage["front"]

    create_box(
        "GarageDriveway",
        (
            garage["x"],
            front - length * 0.5,
            -0.035,
        ),
        (
            garage["width"] * 0.87,
            length,
            0.07,
        ),
        materials["concrete"],
        groups["Garden"],
        bevel=0.025,
    )

    # Driveway expansion seams.
    for i in range(1, 4):

        y = front - length * i / 4

        create_box(
            "DrivewaySeam_" + str(i),
            (
                garage["x"],
                y,
                0.003,
            ),
            (
                garage["width"] * 0.84,
                0.025,
                0.008,
            ),
            materials["foundation"],
            groups["Garden"],
            bevel=0.0,
        )


# ============================================================
# HOUSE GENERATION
# ============================================================

def generate_house(seed=SEED):

    # Remove only the previous generated house.
    delete_generated_house()

    if seed is None:
        seed = random.SystemRandom().randrange(
            1,
            2 ** 31,
        )

    rng = random.Random(seed)

    materials = create_materials(rng)
    layout = choose_layout(rng)

    # Main hierarchy.
    root = make_empty("GeneratedHouse")

    group_names = (
        "Structure",
        "Roof",
        "Windows",
        "Doors",
        "Garage",
        "Porch",
        "Details",
        "Garden",
    )

    groups = {
        name: make_empty(name, root)
        for name in group_names
    }

    # Primary structure.
    create_house_body(
        layout,
        materials,
        groups,
        rng,
    )

    # Secondary architectural extension.
    create_secondary_section(
        layout,
        materials,
        groups,
        rng,
    )

    # Main pitched roof.
    create_main_roof(
        layout,
        materials,
        groups,
        rng,
    )

    # Windows and door.
    create_house_windows(
        layout,
        materials,
        groups,
        rng,
    )

    create_front_door(
        layout,
        materials,
        groups,
        rng,
    )

    # Entryway.
    create_porch(
        layout,
        materials,
        groups,
        rng,
    )

    # Garage.
    create_garage(
        layout,
        materials,
        groups,
        rng,
    )

    # Architectural details.
    create_chimney(
        layout,
        materials,
        groups,
        rng,
    )

    create_exterior_lights(
        layout,
        materials,
        groups,
        rng,
    )

    # Landscaping.
    create_landscaping(
        layout,
        materials,
        groups,
        rng,
    )

    create_driveway(
        layout,
        materials,
        groups,
        rng,
    )

    # Metadata for Unity and debugging.
    root["generator"] = (
        "Couch Guys Cartoon House v2"
    )

    root["seed"] = seed
    root["forward_axis"] = "-Y"
    root["unity_scale_meters"] = 1.0

    root["floors"] = layout["floors"]
    root["roof_type"] = layout["roof_type"]

    root["has_garage"] = layout["has_garage"]
    root["has_porch"] = layout["has_porch"]

    root["generated_utc"] = time.strftime(
        "%Y-%m-%dT%H:%M:%SZ",
        time.gmtime(),
    )

    # Select the generated root.
    bpy.ops.object.select_all(action="DESELECT")

    root.select_set(True)
    bpy.context.view_layer.objects.active = root

    print("")
    print("=" * 55)
    print("COUCH GUYS HOUSE GENERATED")
    print("=" * 55)

    print("Seed:", seed)
    print("Floors:", layout["floors"])
    print("Roof:", layout["roof_type"])
    print("Garage:", layout["has_garage"])
    print("Porch:", layout["has_porch"])

    print(
        "Dimensions:",
        round(layout["width"], 2),
        "x",
        round(layout["depth"], 2),
    )

    print("=" * 55)
    print("")

    return root


# ============================================================
# ENTRY POINT
# ============================================================

if __name__ == "__main__":
    generate_house()
