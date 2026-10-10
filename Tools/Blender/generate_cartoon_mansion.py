"""
COUCH GUYS - PROCEDURAL CARTOON MANSION GENERATOR
================================================

Standalone mansion generator inspired by generate_cartoon_house.py.
Reuses its warm pastel palette, chunky bevels, windows, roof tiles,
landscaping geometry and seeded random design.

Features:
  - Large 2-3 storey houses, broad entrance and two attached wings
  - Always has a two/three-car garage and a DRIVEWAY leading to a road edge
  - Randomised rooflines, colours, chimneys, balconies, dormers, columns
  - Optional circular forecourt with a central garden island
  - Editable reproducible seed and optional FBX export
  - Organized GeneratedMansion hierarchy and Unity placement metadata

Units: metres. Front faces Blender -Y. Ground at Z=0.
Driveway road end is an approach point, NOT a road mesh. Match terrain road
height in Unity. Driveway is a separate mesh group for terrain adaptation.

Run: Blender > Scripting > Open file > Run Script.
Or: blender --background --python generate_cartoon_mansion.py
Running again deletes ONLY the previous GeneratedMansion hierarchy.
"""

import bpy
import math
import random
import time

# ------------------------------------------------------------
# SETTINGS
# ------------------------------------------------------------
SEED = None                # None = a different mansion each run
MANSION_STYLE = "RANDOM"   # RANDOM, ESTATE, CLASSIC, TUSCAN, STORYBOOK
MANSION_WIDTH = (15.5, 19.5)     # Main body only, in metres
MANSION_DEPTH = (10.5, 14.0)
FLOOR_HEIGHT = (2.95, 3.35)
THREE_STOREY_CHANCE = 0.17
GARAGE_BAYS = (2, 3)             # random between 2 and 3
DRIVEWAY_LENGTH = (9.0, 13.0)  # From garage front towards street
CIRCULAR_DRIVE_CHANCE = 0.30
BALCONY_CHANCE = 0.55
DORMER_CHANCE = 0.45
CHIMNEY_CHANCE = 0.60
GENERATE_ROOF_TILES = True
GENERATE_ROOF_TRIM = True
GENERATE_WINDOW_SILLS = True
GENERATE_SHUTTERS = True
GENERATE_LANDSCAPING = True
GENERATE_FLOWERS = True
ROOF_TILE_ROWS = 7
ROOF_TILE_COLUMNS = 15
BEVEL_WIDTH = 0.045
WINDOW_EMISSION = 0.45
EXPORT_FBX = False
FBX_PATH = "//CouchGuys_Mansion.fbx"

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

# ------------------------------------------------------------
# SHARED HOUSE GENERATOR GEOMETRY
# ------------------------------------------------------------

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
            "Mansion_Stucco",
            wall,
        ),

        "roof": make_material(
            "Mansion_TerracottaRoof",
            roof,
            roughness=0.93,
        ),

        "roof_light": make_material(
            "Mansion_RoofTileLight",
            shade(roof, 1.12),
            roughness=0.93,
        ),

        "roof_dark": make_material(
            "Mansion_RoofTileDark",
            shade(roof, 0.90),
            roughness=0.93,
        ),

        "roof_edge": make_material(
            "Mansion_RoofEdge",
            shade(roof, 0.76),
        ),

        "trim": make_material(
            "Mansion_WhiteTrim",
            scheme["trim"],
        ),

        "door": make_material(
            "Mansion_FrontDoor",
            scheme["door"],
        ),

        "glass": make_material(
            "Mansion_WindowGlass",
            (0.44, 0.68, 0.78, 1),
            roughness=0.25,
        ),

        "warm_glass": make_material(
            "Mansion_WarmWindowGlass",
            (1.0, 0.76, 0.36, 1),
            roughness=0.3,
            emission=WINDOW_EMISSION,
        ),

        "light": make_material(
            "Mansion_ExteriorLight",
            (1.0, 0.84, 0.49, 1),
            emission=0.9,
        ),

        "dark": make_material(
            "Mansion_DarkDetails",
            (0.18, 0.19, 0.19, 1),
        ),

        "foundation": make_material(
            "Mansion_StoneFoundation",
            (0.62, 0.61, 0.55, 1),
        ),

        "stone": make_material(
            "Mansion_PorchStone",
            (0.74, 0.73, 0.68, 1),
        ),

        "garage": make_material(
            "Mansion_GarageDoor",
            (0.90, 0.89, 0.84, 1),
        ),

        "wood": make_material(
            "Mansion_WoodAccent",
            (0.45, 0.30, 0.20, 1),
        ),

        "shutter": make_material(
            "Mansion_WindowShutter",
            shade(scheme["door"], 0.88),
        ),

        "grass": make_material(
            "Mansion_GardenGrass",
            (0.35, 0.58, 0.23, 1),
        ),

        "bush": make_material(
            "Mansion_BushGreen",
            (0.27, 0.49, 0.20, 1),
        ),

        "bush_light": make_material(
            "Mansion_BushLight",
            (0.42, 0.63, 0.29, 1),
        ),

        "bush_dark": make_material(
            "Mansion_BushDark",
            (0.20, 0.39, 0.16, 1),
        ),

        "flower_pink": make_material(
            "Mansion_FlowerPink",
            (0.97, 0.42, 0.56, 1),
        ),

        "flower_yellow": make_material(
            "Mansion_FlowerYellow",
            (1.0, 0.79, 0.27, 1),
        ),

        "flower_white": make_material(
            "Mansion_FlowerWhite",
            (0.98, 0.96, 0.86, 1),
        ),

        "soil": make_material(
            "Mansion_GardenSoil",
            (0.32, 0.25, 0.18, 1),
        ),

        "concrete": make_material(
            "Mansion_Driveway",
            (0.66, 0.65, 0.61, 1),
        ),
    }


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

    mesh = bpy.data.meshes.new("Mansion_" + name)

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
    obj.data.name = "Mansion_" + name
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
    obj.data.name = "Mansion_" + name
    obj.data.materials.append(material)
    obj.parent = parent

    return obj


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


# ------------------------------------------------------------
# MANSION LAYOUT
# ------------------------------------------------------------

def delete_generated_mansion():
    """Remove our previous mansion without touching the scene or house."""
    root = bpy.data.objects.get("GeneratedMansion")
    if root is None:
        return

    def remove_tree(obj):
        for child in list(obj.children):
            remove_tree(child)
        bpy.data.objects.remove(obj, do_unlink=True)

    remove_tree(root)


def choose_mansion_layout(rng):
    style = MANSION_STYLE.upper()
    if style == "RANDOM":
        style = rng.choice(("ESTATE", "CLASSIC", "TUSCAN", "STORYBOOK"))
    if style not in ("ESTATE", "CLASSIC", "TUSCAN", "STORYBOOK"):
        raise ValueError("Unknown MANSION_STYLE: " + MANSION_STYLE)

    width = rng.uniform(*MANSION_WIDTH)
    depth = rng.uniform(*MANSION_DEPTH)
    floor_h = rng.uniform(*FLOOR_HEIGHT)
    floors = 3 if rng.random() < THREE_STOREY_CHANCE else 2
    front = -depth / 2
    garage_bays = rng.randint(*GARAGE_BAYS)
    garage_side = rng.choice((-1, 1))
    other_side = -garage_side
    garage_width = max(6.4, garage_bays * rng.uniform(2.75, 2.95) + 0.8)
    garage_depth = rng.uniform(8.0, 10.5)
    other_width = rng.uniform(5.2, 7.3)
    other_depth = rng.uniform(7.0, 9.4)
    wings = {}
    for side, is_garage, wing_w, wing_d in (
        (garage_side, True, garage_width, garage_depth),
        (other_side, False, other_width, other_depth),
    ):
        projection = rng.uniform(0.45, 1.2) if not is_garage else rng.uniform(0.85, 1.8)
        overlap = rng.uniform(0.60, 1.05)
        wing_front = front - projection
        wing_x = side * (width / 2 + wing_w / 2 - overlap)
        wing_y = wing_front + wing_d / 2
        wing_floors = 2 if (floors == 3 or rng.random() < 0.75) else 1
        wings["garage" if is_garage else "residence"] = {
            "side": side, "x": wing_x, "y": wing_y,
            "width": wing_w, "depth": wing_d,
            "front": wing_front, "floors": wing_floors,
            "height": wing_floors * floor_h,
        }

    garage = wings["garage"]
    driveway_length = rng.uniform(*DRIVEWAY_LENGTH)
    road_y = garage["front"] - driveway_length
    balcony = rng.random() < BALCONY_CHANCE
    circular = rng.random() < CIRCULAR_DRIVE_CHANCE
    # Road stays in front of the full forecourt (when one is generated).
    if circular:
        road_y = min(road_y, front - 11.0)
    # Storybook mansions favour prominent gables, while classic estates
    # and Tuscan mansions have broad hipped rooflines.
    roof_main = (
        "GABLE" if style == "STORYBOOK" else
        "HIP" if style in ("CLASSIC", "TUSCAN") else
        rng.choice(("HIP", "HIP", "GABLE"))
    )
    return {
        "style": style,
        "width": width, "depth": depth, "front": front,
        "floors": floors, "floor_height": floor_h,
        "height": floor_h * floors,
        "wings": wings, "garage_bays": garage_bays,
        "garage_side": garage_side,
        "roof_main": roof_main,
        "roof_height": rng.uniform(2.0, 3.05),
        "balcony": balcony,
        "circular_drive": circular,
        "driveway_road_y": road_y,
        "driveway_road_x": garage["x"],
        "dormers": rng.random() < DORMER_CHANCE,
        "chimneys": rng.random() < CHIMNEY_CHANCE,
    }


def design_materials(layout, rng):
    # Style-specific subsets of the exact pastel schemes in the house script.
    # The source palettes remain unchanged for the next generation.
    global COLOUR_SCHEMES
    original_schemes = COLOUR_SCHEMES
    style_indices = {
        "ESTATE": (0, 1, 2, 5),
        "CLASSIC": (0, 1, 5),
        "TUSCAN": (1, 4, 0),
        "STORYBOOK": (2, 3, 4, 5),
    }
    try:
        COLOUR_SCHEMES = tuple(original_schemes[i]
                               for i in style_indices[layout["style"]])
        materials = create_materials(rng)
    finally:
        COLOUR_SCHEMES = original_schemes
    materials["paving"] = make_material(
        "Mansion_PavingLight", (0.75, 0.72, 0.65, 1))
    materials["paving_dark"] = make_material(
        "Mansion_PavingDark", (0.54, 0.53, 0.49, 1))
    materials["rail"] = make_material(
        "Mansion_IronRail", (0.25, 0.28, 0.29, 1), roughness=0.48)
    materials["gold"] = make_material(
        "Mansion_LightMetal", (0.67, 0.55, 0.32, 1), roughness=0.40)
    materials["fountain"] = make_material(
        "Mansion_FountainWater", (0.43, 0.68, 0.75, 1), roughness=0.22)
    return materials


def add_structure(layout, mats, groups, rng):
    w = layout["width"]
    d = layout["depth"]
    h = layout["height"]
    front = layout["front"]
    create_box("MansionMainBody", (0, 0, h * .5), (w, d, h),
               mats["wall"], groups["Structure"], bevel=.105)
    create_box("MainStoneFoundation", (0, 0, .16),
               (w + .16, d + .16, .32), mats["foundation"],
               groups["Structure"], bevel=.04)

    for level in range(1, layout["floors"]):
        z = level * layout["floor_height"]
        for name, y in (("Front", front - .085), ("Rear", -front + .085)):
            create_box("FacadeCornice_%s_%d" % (name, level), (0, y, z),
                       (w + .15, .17, .15), mats["trim"],
                       groups["Facade"], bevel=.028)

    # Distinctive top eaves trim below the main roof.
    create_box("MainEavesCornice", (0, front - .08, h - .20),
               (w + .18, .20, .22), mats["trim"], groups["Facade"], .03)

    for name, wing in layout["wings"].items():
        wx, wy = wing["x"], wing["y"]
        ww, wd, wh = wing["width"], wing["depth"], wing["height"]
        prefix = "Garage" if name == "garage" else "EastWestWing"
        create_box(prefix + "Body", (wx, wy, wh / 2), (ww, wd, wh),
                   mats["wall"], groups["Structure"], bevel=.086)
        create_box(prefix + "Foundation", (wx, wy, .14),
                   (ww + .13, wd + .13, .28), mats["foundation"],
                   groups["Structure"], bevel=.025)
        for level in range(1, wing["floors"]):
            create_box(prefix + "FloorTrim_%d" % level,
                       (wx, wing["front"] - .07,
                        level * layout["floor_height"]),
                       (ww + .12, .15, .14), mats["trim"],
                       groups["Facade"], bevel=.020)
        create_box(prefix + "TopCornice", (wx, wing["front"] - .075,
                                           wh - .14),
                   (ww + .15, .15, .17), mats["trim"],
                   groups["Facade"], bevel=.023)


def add_rooflines(layout, mats, groups, rng):
    w, d, h = layout["width"], layout["depth"], layout["height"]
    create_roof_piece(
        "MainGrandRoof", (0, 0), w + 1.15, d + 1.13,
        h, layout["roof_height"], layout["roof_main"], "X",
        mats, groups["Roof"], rng)

    for key in ("garage", "residence"):
        wing = layout["wings"][key]
        height = rng.uniform(1.25, 2.05)
        shape = rng.choice(("HIP", "HIP", "GABLE"))
        create_roof_piece(
            ("Garage" if key == "garage" else "ResidentialWing") + "Roof",
            (wing["x"], wing["y"]),
            wing["width"] + .89, wing["depth"] + .92,
            wing["height"], height, shape, "Y",
            mats, groups["Roof"], rng)

    if layout["chimneys"]:
        for i in range(rng.randint(1, 3)):
            side = -1 if i % 2 else 1
            cx = side * (w * rng.uniform(.24, .35))
            cy = rng.uniform(-d * .08, d * .22)
            base = h + layout["roof_height"] * .48
            chimney_h = rng.uniform(1.25, 1.95)
            create_box("Chimney_%d" % i,
                       (cx, cy, base + chimney_h * .5),
                       (.85, .9, chimney_h), mats["wall"],
                       groups["Roof"], bevel=.05)
            create_box("ChimneyCap_%d" % i,
                       (cx, cy, base + chimney_h),
                       (1.08, 1.10, .18), mats["stone"],
                       groups["Roof"], bevel=.032)
            create_box("ChimneyOpening_%d" % i,
                       (cx, cy, base + chimney_h + .095),
                       (.49, .55, .025), mats["dark"],
                       groups["Roof"], bevel=0)

    # Small dormers sit near the sloping roof. They use visible gables,
    # glass and surround rather than a hollow, physically cut roof.
    if layout["dormers"]:
        count = rng.choice((2, 3))
        for i in range(count):
            x = (i - (count - 1) / 2) * (w * .67 / max(1, count - 1))
            # Put dormer openings on the front roof slope, never inside house.
            y = -d * .25
            roof_z = h + layout["roof_height"] * .46
            create_box("DormerBody_%d" % i,
                       (x, y, roof_z + .53), (1.35, 1.06, 1.06),
                       mats["wall"], groups["Roof"], bevel=.035)
            create_roof_piece("DormerRoof_%d" % i,
                              (x, y), 1.68, 1.29, roof_z + 1.05,
                              .55, "GABLE", "Y", mats,
                              groups["Roof"], rng, tiles=False)
            create_window("DormerWindow_%d" % i,
                          (x, y - .55, roof_z + .54),
                          .73, .73, "FRONT", mats, groups["Windows"],
                          warm=rng.random() < .85, shutters=False)


def place_window(name, loc, w, h, mats, groups, rng, shutters=False):
    create_window(name, loc, w, h, "FRONT", mats,
                  groups["Windows"], warm=rng.random() < .88,
                  shutters=shutters)


def add_windows(layout, mats, groups, rng):
    w = layout["width"]
    front = layout["front"] - .088
    floor_h = layout["floor_height"]
    col_x = (-w * .37, -w * .19, w * .19, w * .37)

    for floor in range(layout["floors"]):
        z = floor_h * (floor + .55)
        for col, x in enumerate(col_x):
            win_w = rng.uniform(1.12, 1.49)
            win_h = rng.uniform(1.34, 1.62)
            place_window("MainFacade_Window_%d_%d" % (floor, col),
                         (x, front, z), win_w, win_h,
                         mats, groups, rng,
                         shutters=rng.random() < .20)

        if floor > 0:
            if not (floor == 1 and layout["balcony"]):
                place_window("CentreUpperWindow_%d" % floor,
                             (0, front, z), 1.64, 1.73,
                             mats, groups, rng, shutters=False)

    for key in ("residence", "garage"):
        wing = layout["wings"][key]
        base = wing["front"] - .088
        width = wing["width"]
        levels = wing["floors"]
        floor_from = 1 if key == "garage" else 0
        for floor in range(floor_from, levels):
            for index, offset in enumerate((-.25, .25)):
                x = wing["x"] + width * offset
                z = floor_h * (floor + .55)
                place_window("%sWingWindow_%d_%d" % (key, floor, index),
                             (x, base, z),
                             rng.uniform(1.05, 1.38), 1.3,
                             mats, groups, rng,
                             shutters=rng.random() < .32)

        # Windows on the exterior side walls, away from the main body.
        side = wing["side"]
        side_x = wing["x"] + side * (width / 2 + .082)
        for floor in range(levels):
            create_window("%sSideWindow_%d" % (key, floor),
                          (side_x, wing["y"] + .12,
                           floor_h * (floor + .55)),
                          1.2, 1.30, "SIDE", mats,
                          groups["Windows"], warm=True, shutters=False)


def add_double_front_door(layout, mats, groups):
    front = layout["front"] - .15
    floor_h = layout["floor_height"]
    door_w = 2.44
    door_h = min(2.76, floor_h - .18)
    door_z = .20 + door_h / 2
    for side in (-1, 1):
        x = side * door_w * .25
        create_box("GrandDoubleDoor_%s" % ("Left" if side == -1 else "Right"),
                   (x, front, door_z),
                   (door_w * .5 - .035, .12, door_h),
                   mats["door"], groups["Doors"], bevel=.045)
        for row in (-1, 1):
            create_box("RaisedDoorPanel_%s_%d" % (side, row),
                       (x, front - .074, door_z + row * door_h * .23),
                       (door_w * .35, .034, door_h * .28),
                       mats["wood"], groups["Doors"], bevel=.024)
        create_box("GoldDoorHandle_%s" % side,
                   (side * .115, front - .115, door_z - .10),
                   (.060, .063, .19), mats["gold"],
                   groups["Doors"], bevel=.016)

    create_box("GrandDoorLintel", (0, front - .02, .20 + door_h + .055),
               (door_w + .40, .21, .17), mats["trim"],
               groups["Doors"], bevel=.024)
    for side in (-1, 1):
        create_box("GrandDoorJamb_%s" % side,
                   (side * (door_w / 2 + .09), front - .02, door_z),
                   (.20, .21, door_h + .17), mats["trim"],
                   groups["Doors"], bevel=.018)

    # A bright semiclassical panel of glass above the entrance.
    place_window("TransomOverEntrance",
                 (0, front - .005, floor_h - .27),
                 1.40, .40, mats, groups,
                 random.Random(27), shutters=False)


def add_grand_entrance(layout, mats, groups, rng):
    front = layout["front"]
    floor_h = layout["floor_height"]
    width = rng.uniform(5.2, 6.0)
    depth = rng.uniform(2.25, 2.75)
    centre_y = front - depth / 2 + .05
    porch_z = .36
    create_box("GrandEntrancePlatform", (0, centre_y, porch_z / 2),
               (width, depth, porch_z), mats["stone"],
               groups["Porch"], bevel=.052)
    create_box("GrandEntranceTileEdge", (0, centre_y, porch_z + .038),
               (width + .13, depth + .1, .077), mats["foundation"],
               groups["Porch"], bevel=.025)

    for step in range(3):
        rise = .12 * (step + 1)
        y = front - depth - .69 + step * .275
        create_box("GrandEntranceStep_%d" % step,
                   (0, y, rise / 2),
                   (width * .76 + (2 - step) * .22, .50, rise),
                   mats["stone"], groups["Porch"], bevel=.038)

    top_z = floor_h + .18
    for side in (-1, 1):
        x = side * width * .405
        y = centre_y - depth * .5 + .18
        # Three-piece columns are sturdier and more readable at distance.
        create_box("EntryColumnBase_%s" % side,
                   (x, y, porch_z + .16), (.47, .47, .32),
                   mats["stone"], groups["Porch"], bevel=.042)
        create_cylinder("EntryColumn_%s" % side,
                        (x, y, (top_z + porch_z) / 2),
                        .153, top_z - porch_z - .18,
                        mats["trim"], groups["Porch"], vertices=12)
        create_box("EntryColumnCapital_%s" % side,
                   (x, y, top_z - .02), (.46, .45, .23),
                   mats["trim"], groups["Porch"], bevel=.026)

        # Two ornamental urn-style planters, away from the door.
        px = side * (width / 2 + .48)
        py = front - 1.48
        create_box("EntrancePlanter_%s" % side,
                   (px, py, .22), (.59, .59, .44),
                   mats["stone"], groups["Garden"], bevel=.055)
        create_bush("EntranceTopiary_%s" % side,
                    (px, py, .45), .45, mats,
                    groups["Garden"], rng)

    if layout["balcony"]:
        create_box("BalconyFloor", (0, centre_y, top_z + .095),
                   (width + .27, depth + .18, .19),
                   mats["stone"], groups["Porch"], bevel=.035)
        rail_front = front - depth - .025
        for x in (-width * .5, width * .5):
            create_box("BalconyEndPost_%s" % round(x, 2),
                       (x, rail_front, top_z + .59),
                       (.18, .20, 1.05), mats["trim"],
                       groups["Porch"], bevel=.025)
        count = 11
        for index in range(count):
            x = (index - (count - 1) / 2) * (width * .92 / (count - 1))
            create_box("BalconyBaluster_%02d" % index,
                       (x, rail_front, top_z + .55),
                       (.065, .075, .79), mats["trim"],
                       groups["Porch"], bevel=.012)
        create_box("BalconyHandRail", (0, rail_front, top_z + 1.04),
                   (width + .22, .15, .11), mats["trim"],
                   groups["Porch"], bevel=.022)
        # Upper double French doors open to the balcony.
        z = floor_h + 1.27
        door_front = front - .17
        for side in (-1, 1):
            x = side * .49
            create_box("BalconyGlassDoor_%s" % side,
                       (x, door_front, z), (.92, .11, 2.18),
                       mats["warm_glass"], groups["Doors"], bevel=.018)
            create_box("BalconyDoorMidRail_%s" % side,
                       (x, door_front - .067, z - .21),
                       (.88, .025, .09), mats["trim"],
                       groups["Doors"], bevel=.01)
        create_box("BalconyDoorSurround", (0, door_front - .018, z + 1.12),
                   (2.15, .16, .17), mats["trim"],
                   groups["Doors"], bevel=.020)
    else:
        create_roof_piece("GrandEntrancePorticoRoof", (0, centre_y),
                          width + .45, depth + .55, top_z,
                          rng.uniform(.73, 1.12), "GABLE", "Y",
                          mats, groups["Porch"], rng)

    # Two symmetrical wall lanterns above the front landing.
    for side in (-1, 1):
        x = side * 1.76
        create_box("GrandLanternHousing_%s" % side,
                   (x, front - .15, 2.24),
                   (.21, .13, .43), mats["dark"],
                   groups["Details"], bevel=.035)
        create_box("GrandLanternGlow_%s" % side,
                   (x, front - .237, 2.24),
                   (.13, .08, .27), mats["light"],
                   groups["Details"], bevel=.022)


def add_garage(layout, mats, groups, rng):
    wing = layout["wings"]["garage"]
    bays = layout["garage_bays"]
    x_centre, y = wing["x"], wing["front"] - .105
    available_w = wing["width"] - .65
    slot = available_w / bays
    door_w = slot - .21
    door_h = min(2.58, layout["floor_height"] - .31)
    base = .13
    for index in range(bays):
        x = x_centre + (index - (bays - 1) * .5) * slot
        create_box("GarageBayDoor_%d" % index,
                   (x, y, base + door_h * .5),
                   (door_w, .13, door_h), mats["garage"],
                   groups["Garage"], bevel=.035)
        # Outline and raised panels like the original suburban garage.
        for side in (-1, 1):
            create_box("GarageDoorSideFrame_%d_%s" % (index, side),
                       (x + side * door_w * .5, y - .015,
                        base + door_h / 2),
                       (.13, .17, door_h + .15), mats["trim"],
                       groups["Garage"], bevel=.015)
        create_box("GarageDoorTopFrame_%d" % index,
                   (x, y - .015, base + door_h),
                   (door_w + .20, .18, .14), mats["trim"],
                   groups["Garage"], bevel=.020)
        for row in range(1, 5):
            z = base + door_h * row / 5
            create_box("GaragePanelSeam_%d_%d" % (index, row),
                       (x, y - .079, z),
                       (door_w * .94, .022, .036), mats["trim"],
                       groups["Garage"], bevel=.005)
        for col in (-1, 1):
            px = x + col * door_w * .22
            create_box("GaragePanelColumn_%d_%s" % (index, col),
                       (px, y - .077, base + door_h * .51),
                       (.035, .025, door_h * .89), mats["trim"],
                       groups["Garage"], bevel=.004)
        create_box("GarageDecorativeHandle_%d" % index,
                   (x, y - .105, base + .35),
                   (.18, .035, .04), mats["dark"],
                   groups["Garage"], bevel=.009)
    create_box("GarageTrimHeader",
               (x_centre, y + .018, base + door_h + .24),
               (wing["width"] - .21, .19, .18), mats["stone"],
               groups["Garage"], bevel=.028)


def mesh_strip(name, xy_points, width, mats, parent, z=.012, closed=False):
    """A connected planar driveway/walkway following a polyline."""
    if len(xy_points) < 2:
        return
    vertices = []
    faces = []
    count = len(xy_points)
    for index, (x, y) in enumerate(xy_points):
        prev = xy_points[(index - 1) % count] if closed else xy_points[max(0, index - 1)]
        nxt = xy_points[(index + 1) % count] if closed else xy_points[min(count - 1, index + 1)]
        vx, vy = nxt[0] - prev[0], nxt[1] - prev[1]
        length = math.hypot(vx, vy) or 1
        nx, ny = -vy / length, vx / length
        vertices.append((x + nx * width / 2, y + ny * width / 2, z))
        vertices.append((x - nx * width / 2, y - ny * width / 2, z))
    edge_count = count if closed else count - 1
    for i in range(edge_count):
        j = (i + 1) % count
        faces.append((2 * i, 2 * i + 1, 2 * j + 1, 2 * j))
    create_mesh(name, vertices, faces, mats["concrete"], parent, bevel=0)


def circular_driveway(layout, mats, groups, rng):
    """A decorative loop and lawn island outside the entrance."""
    front = layout["front"]
    centre_y = front - 6.1
    radius_x = min(5.75, layout["width"] * .34)
    radius_y = 3.1
    samples = 48
    path = []
    for i in range(samples):
        a = math.tau * i / samples
        path.append((radius_x * math.cos(a),
                     centre_y + radius_y * math.sin(a)))
    mesh_strip("CircularDropoffLane", path, 2.05,
               mats, groups["Driveway"], z=.022, closed=True)

    road_y = layout["driveway_road_y"]
    circle_front = centre_y - radius_y
    mesh_strip("CircularDrivewayApproach",
               ((0, road_y), (0, circle_front)), 2.15,
               mats, groups["Driveway"], z=.025)
    # Short walkway into the front portico, stops at the steps.
    mesh_strip("MansionFrontWalk",
               ((0, centre_y + radius_y),
                (0, front - 3.0)), 1.40, mats,
               groups["Driveway"], z=.027)

    # Raised centre island, set inside the inner radius of the drive.
    island_radius = min(radius_x - 1.40, radius_y - 1.25)
    create_cylinder("RoundaboutGardenIsland", (0, centre_y, .065),
                    island_radius, .13, mats["grass"],
                    groups["Garden"], vertices=24)
    if rng.random() < .38:
        create_cylinder("FountainStonePedestal",
                        (0, centre_y, .28), .72, .34,
                        mats["stone"], groups["Garden"], vertices=16)
        create_cylinder("FountainWater", (0, centre_y, .468),
                        .62, .045, mats["fountain"],
                        groups["Garden"], vertices=16)
    else:
        create_bush("RoundaboutOrnamentalTree",
                    (0, centre_y, .14), .68,
                    mats, groups["Garden"], rng)


def add_driveways(layout, mats, groups, rng):
    """Every mansion has a road-facing garage driveway."""
    garage = layout["wings"]["garage"]
    road_y = layout["driveway_road_y"]
    gx = garage["x"]
    start_y = garage["front"] - .14
    width = garage["width"] * .88
    # Natural slight taper / bend instead of an unnaturally long giant slab.
    road_x = gx + rng.uniform(-.8, .8)
    layout["driveway_road_x"] = road_x
    points = (
        (road_x, road_y),
        ((road_x + gx) / 2, (road_y + start_y) / 2),
        (gx, start_y),
    )
    mesh_strip("GarageDrivewayMain", points, width, mats,
               groups["Driveway"], z=.014)
    # Subtle concrete expansion seams for a stylised suburban feel.
    for row in range(1, 5):
        t = row / 5
        x = road_x * (1 - t) + gx * t
        y = road_y * (1 - t) + start_y * t
        create_box("DrivewayExpansionSeam_%d" % row,
                   (x, y, .023), (width * .93, .035, .014),
                   mats["paving_dark"], groups["Driveway"], bevel=.004)
    # Small entry posts mark where the driveway meets the road.
    for side in (-1, 1):
        x = road_x + side * width * .55
        create_box("DrivewayGatePillar_%s" % side,
                   (x, road_y + .35, .62), (.54, .54, 1.24),
                   mats["stone"], groups["Driveway"], bevel=.07)
        create_box("DrivewayGatePillarCap_%s" % side,
                   (x, road_y + .35, 1.29), (.68, .68, .17),
                   mats["trim"], groups["Driveway"], bevel=.035)
    if layout["circular_drive"]:
        circular_driveway(layout, mats, groups, rng)
    else:
        # A simple paved garden path when the optional circular drive is absent.
        mesh_strip("WalkwayToFrontDoor",
                   ((0, road_y), (0, layout["front"] - 2.95)),
                   1.22, mats, groups["Driveway"], z=.022)


def add_gardens(layout, mats, groups, rng):
    if not GENERATE_LANDSCAPING:
        return
    front = layout["front"]
    w = layout["width"]
    for side in (-1, 1):
        x = side * w * .315
        create_box("FacadeFlowerBed_%s" % side,
                   (x, front - .56, .031), (w * .28, .76, .062),
                   mats["soil"], groups["Garden"], bevel=.026)
        for index in range(4):
            bx = x + (index - 1.5) * (w * .28 / 4)
            by = front - rng.uniform(.42, .75)
            create_bush("FacadeShrub_%s_%d" % (side, index),
                        (bx, by, 0), rng.uniform(.32, .46),
                        mats, groups["Garden"], rng)
        if GENERATE_FLOWERS:
            for index in range(3):
                bx = x + (index - 1) * (w * .28 / 3)
                create_flower_patch("FacadeFlowers_%s_%d" % (side, index),
                                    (bx, front - .82, .065),
                                    mats, groups["Garden"], rng)

    # Shrubs on the outer sides, keeping garage bays & driveway unobstructed.
    for key in ("residence", "garage"):
        wing = layout["wings"][key]
        outer_x = wing["x"] + wing["side"] * (wing["width"] / 2 + .55)
        for i in range(3):
            y = wing["front"] + .6 + i * (wing["depth"] / 3)
            create_bush("SideHedge_%s_%d" % (key, i),
                        (outer_x, y, 0), rng.uniform(.30, .45),
                        mats, groups["Garden"], rng)


def footprint_metadata(layout, root, groups):
    wings = layout["wings"]
    left = -layout["width"] / 2
    right = layout["width"] / 2
    rear = layout["depth"] / 2
    front = layout["front"]
    for wing in wings.values():
        left = min(left, wing["x"] - wing["width"] / 2)
        right = max(right, wing["x"] + wing["width"] / 2)
        rear = max(rear, wing["y"] + wing["depth"] / 2)
        front = min(front, wing["front"])
    # Front steps and garden need extra breathing space.
    front = min(front - .3, layout["front"] - 3.8)
    driveway_front = layout["driveway_road_y"] - .5
    desired_width = (right - left) + 3.0
    lot_center_x = (left + right) / 2
    lot_center_y = (rear + driveway_front) / 2
    root["building_width_m"] = round(right - left, 3)
    root["building_depth_m"] = round(rear - front, 3)
    root["suggested_plot_width_m"] = round(desired_width, 3)
    root["suggested_plot_depth_m"] = round(rear - driveway_front + 2.0, 3)
    root["suggested_plot_center_xy"] = [round(lot_center_x, 3),
                                          round(lot_center_y, 3)]
    root["front_axis"] = "-Y"
    root["road_connection_xy"] = [round(layout["driveway_road_x"], 3),
                                   round(layout["driveway_road_y"], 3)]
    root["terrain_note"] = "Driveway is flat: conform to procedural terrain/road in Unity"
    marker = make_empty("RoadConnection_Garage", groups["GameplayHelpers"])
    marker.location = (layout["driveway_road_x"], layout["driveway_road_y"], 0)
    marker["is_road_connection"] = True
    if layout["circular_drive"]:
        marker2 = make_empty("RoadConnection_Forecourt", groups["GameplayHelpers"])
        marker2.location = (0, layout["driveway_road_y"], 0)
        marker2["is_road_connection"] = True
    make_empty("FrontDoorMarker", groups["GameplayHelpers"]).location = (
        0, layout["front"] - .55, 0)
    make_empty("GarageApproachMarker", groups["GameplayHelpers"]).location = (
        wings["garage"]["x"], wings["garage"]["front"] - 1.3, 0)
    # Simple optional suggested delivery target at the driveway entrance.
    make_empty("DeliveryTarget", groups["GameplayHelpers"]).location = (
        wings["garage"]["x"], wings["garage"]["front"] - 2.4, 0)


def export_mansion_fbx(root):
    bpy.ops.object.select_all(action="DESELECT")
    def select_tree(obj):
        obj.select_set(True)
        for child in obj.children:
            select_tree(child)
    select_tree(root)
    bpy.context.view_layer.objects.active = root
    try:
        bpy.ops.export_scene.fbx(
            filepath=bpy.path.abspath(FBX_PATH),
            use_selection=True,
            object_types={"MESH", "EMPTY"},
            use_mesh_modifiers=True,
            apply_unit_scale=True,
            axis_forward="-Z", axis_up="Y",
            add_leaf_bones=False)
        print("FBX exported:", bpy.path.abspath(FBX_PATH))
    except Exception as exc:
        print("FBX export failed:", exc)
        print("You can still export GeneratedMansion manually as FBX")


def generate_mansion(seed=SEED):
    delete_generated_mansion()
    if seed is None:
        seed = random.SystemRandom().randrange(1, 2 ** 31)
    rng = random.Random(seed)
    layout = choose_mansion_layout(rng)
    mats = design_materials(layout, rng)
    root = make_empty("GeneratedMansion")
    group_names = (
        "Structure", "Roof", "Facade", "Windows", "Doors",
        "Porch", "Garage", "Driveway", "Garden", "Details",
        "GameplayHelpers",
    )
    groups = {name: make_empty(name, root) for name in group_names}

    add_structure(layout, mats, groups, rng)
    add_rooflines(layout, mats, groups, rng)
    add_windows(layout, mats, groups, rng)
    add_double_front_door(layout, mats, groups)
    add_grand_entrance(layout, mats, groups, rng)
    add_garage(layout, mats, groups, rng)
    add_driveways(layout, mats, groups, rng)
    add_gardens(layout, mats, groups, rng)
    footprint_metadata(layout, root, groups)

    root["generator"] = "Couch Guys Cartoon Mansion v1"
    root["seed"] = seed
    root["style"] = layout["style"]
    root["floor_count"] = layout["floors"]
    root["garage_bays"] = layout["garage_bays"]
    root["has_driveway"] = True
    root["has_circular_driveway"] = layout["circular_drive"]
    root["has_balcony"] = layout["balcony"]
    root["unity_scale_meters"] = 1.0
    root["generated_utc"] = time.strftime(
        "%Y-%m-%dT%H:%M:%SZ", time.gmtime())

    bpy.ops.object.select_all(action="DESELECT")
    root.select_set(True)
    bpy.context.view_layer.objects.active = root

    print("\n" + "=" * 60)
    print("COUCH GUYS MANSION GENERATED")
    print("=" * 60)
    print("Seed:", seed, "  Style:", layout["style"])
    print("Floors:", layout["floors"], "  Garage bays:", layout["garage_bays"])
    print("Balcony:", layout["balcony"], "  Circular drive:",
          layout["circular_drive"])
    print("Plot width x depth (m):",
          root["suggested_plot_width_m"], "x",
          root["suggested_plot_depth_m"])
    print("Road approach (x,y):", root["road_connection_xy"])
    print("=" * 60 + "\n")
    if EXPORT_FBX:
        export_mansion_fbx(root)
    return root


if __name__ == "__main__":
    generate_mansion()
