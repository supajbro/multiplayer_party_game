"""Generate a randomized, low-poly suburban house for Couch Guys.

Run from Blender's Scripting workspace, or from a command line:
    blender --background --python generate_cartoon_house.py

The front of the house faces Blender -Y. With Blender's standard FBX export
settings (-Z Forward, Y Up), it faces Unity +Z. One Blender unit is one metre.

Only an existing object named ``GeneratedHouse`` and its children are removed;
the rest of the open scene is left untouched.
"""

import math
import random
import time

import bpy


# =============================================================================
# GENERATION SETTINGS -- these are the intended knobs to edit
# =============================================================================

SEED = None                 # None = a fresh house each run; integer = repeatable

HOUSE_WIDTH = (6.2, 9.0)
HOUSE_DEPTH = (4.8, 6.8)
FLOOR_HEIGHT = (2.45, 2.85)
TWO_STOREY_CHANCE = 0.48
SECONDARY_SECTION_CHANCE = 0.42
GARAGE_CHANCE = 0.58
PORCH_CHANCE = 0.72
CHIMNEY_CHANCE = 0.55

ROOF_TYPES = ("GABLE", "GABLE", "HIP", "SKILLION")
ROOF_HEIGHT = (1.35, 2.35)
ROOF_OVERHANG = (0.28, 0.52)
BEVEL_WIDTH = 0.06

# Curated schemes keep random results in the same friendly neighbourhood.
# Values are sRGB-like RGBA colours used by Blender's viewport/material nodes.
COLOUR_SCHEMES = (
    {"wall": (0.48, 0.70, 0.78, 1), "roof": (0.12, 0.18, 0.27, 1),
     "trim": (0.91, 0.88, 0.76, 1), "door": (0.12, 0.38, 0.25, 1)},
    {"wall": (0.88, 0.82, 0.66, 1), "roof": (0.27, 0.18, 0.15, 1),
     "trim": (0.97, 0.94, 0.83, 1), "door": (0.58, 0.14, 0.11, 1)},
    {"wall": (0.88, 0.72, 0.38, 1), "roof": (0.20, 0.25, 0.31, 1),
     "trim": (0.96, 0.89, 0.68, 1), "door": (0.12, 0.30, 0.52, 1)},
    {"wall": (0.56, 0.72, 0.53, 1), "roof": (0.22, 0.19, 0.16, 1),
     "trim": (0.91, 0.87, 0.72, 1), "door": (0.66, 0.22, 0.12, 1)},
    {"wall": (0.85, 0.62, 0.61, 1), "roof": (0.24, 0.22, 0.31, 1),
     "trim": (0.96, 0.89, 0.76, 1), "door": (0.18, 0.42, 0.48, 1)},
    {"wall": (0.72, 0.66, 0.54, 1), "roof": (0.22, 0.26, 0.20, 1),
     "trim": (0.94, 0.89, 0.75, 1), "door": (0.55, 0.32, 0.10, 1)},
    {"wall": (0.76, 0.77, 0.75, 1), "roof": (0.18, 0.25, 0.34, 1),
     "trim": (0.94, 0.92, 0.84, 1), "door": (0.72, 0.48, 0.08, 1)},
    {"wall": (0.79, 0.48, 0.31, 1), "roof": (0.20, 0.18, 0.17, 1),
     "trim": (0.93, 0.79, 0.59, 1), "door": (0.15, 0.34, 0.23, 1)},
)


# =============================================================================
# Basic scene, material, and mesh helpers
# =============================================================================


def delete_generated_house():
    """Delete the previous generated hierarchy, without touching other objects."""
    root = bpy.data.objects.get("GeneratedHouse")
    if root is None:
        return

    descendants = []

    def gather(obj):
        for child in list(obj.children):
            gather(child)
        descendants.append(obj)

    gather(root)
    for obj in descendants:
        bpy.data.objects.remove(obj, do_unlink=True)

    # Discard only now-unused data created by this generator.
    for mesh in list(bpy.data.meshes):
        if mesh.users == 0 and mesh.name.startswith("House_"):
            bpy.data.meshes.remove(mesh)


def make_empty(name, parent=None):
    obj = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(obj)
    obj.empty_display_type = "PLAIN_AXES"
    obj.empty_display_size = 0.25
    if parent:
        obj.parent = parent
    return obj


def make_material(name, colour, roughness=0.72, metallic=0.0, emission=0.0):
    """Get or update a reusable flat-colour material."""
    material = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    material.use_nodes = True
    material.diffuse_color = colour
    node = material.node_tree.nodes.get("Principled BSDF")
    if node:
        node.inputs["Base Color"].default_value = colour
        node.inputs["Roughness"].default_value = roughness
        node.inputs["Metallic"].default_value = metallic
        emission_input = node.inputs.get("Emission Color")
        if emission_input is None:
            emission_input = node.inputs.get("Emission")
        if emission_input and emission > 0.0:
            emission_input.default_value = colour
        strength_input = node.inputs.get("Emission Strength")
        if strength_input:
            strength_input.default_value = emission
    return material


def create_materials(rng):
    scheme = dict(rng.choice(COLOUR_SCHEMES))
    # Small, bounded variation prevents every use of a scheme being identical.
    def vary(colour, amount=0.035):
        return tuple(max(0.0, min(1.0, c + rng.uniform(-amount, amount)))
                     for c in colour[:3]) + (1.0,)

    scheme["wall"] = vary(scheme["wall"])
    scheme["door"] = vary(scheme["door"], 0.025)
    window_frame = rng.choice((scheme["trim"], (0.25, 0.30, 0.31, 1),
                               (0.93, 0.93, 0.88, 1)))
    garage_colour = rng.choice((scheme["trim"], vary(scheme["wall"], 0.02)))
    return {
        "wall": make_material("House_Walls", scheme["wall"]),
        "roof": make_material("House_Roof", scheme["roof"], roughness=0.85),
        "trim": make_material("House_Trim", scheme["trim"]),
        "door": make_material("House_Door", scheme["door"]),
        "frame": make_material("House_WindowFrames", window_frame),
        "glass": make_material("House_WindowGlass", (0.43, 0.76, 0.88, 1),
                               roughness=0.28, emission=0.08),
        "warm_glass": make_material("House_WarmWindows", (1.0, 0.72, 0.25, 1),
                                    roughness=0.35, emission=0.22),
        "garage": make_material("House_GarageDoor", garage_colour),
        "dark": make_material("House_DarkDetails", (0.12, 0.13, 0.13, 1)),
        "porch": make_material("House_PorchWood", (0.50, 0.29, 0.14, 1)),
        "foundation": make_material("House_Foundation", (0.38, 0.40, 0.38, 1)),
    }


def apply_transforms(obj):
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    obj.select_set(False)


def add_bevel(obj, width=BEVEL_WIDTH, segments=2):
    if width <= 0.0:
        return
    modifier = obj.modifiers.new("CartoonSoftEdges", "BEVEL")
    modifier.width = width
    modifier.segments = segments
    modifier.limit_method = "ANGLE"
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    obj.select_set(False)


def create_box(name, location, dimensions, material, parent, bevel=BEVEL_WIDTH,
               rotation=(0.0, 0.0, 0.0)):
    bpy.ops.mesh.primitive_cube_add(location=location, rotation=rotation)
    obj = bpy.context.object
    obj.name = name
    obj.data.name = "House_" + name
    obj.dimensions = dimensions
    apply_transforms(obj)
    add_bevel(obj, min(bevel, min(dimensions) * 0.22), 2)
    obj.data.materials.append(material)
    obj.parent = parent
    return obj


def create_mesh(name, vertices, faces, material, parent, bevel=0.0):
    # Keep each mesh's origin near its own geometry, which makes Unity-side
    # inspection and any later hand editing much less surprising.
    bounds_min = tuple(min(vertex[axis] for vertex in vertices) for axis in range(3))
    bounds_max = tuple(max(vertex[axis] for vertex in vertices) for axis in range(3))
    centre = tuple((bounds_min[axis] + bounds_max[axis]) * 0.5 for axis in range(3))
    local_vertices = [tuple(vertex[axis] - centre[axis] for axis in range(3))
                      for vertex in vertices]
    mesh = bpy.data.meshes.new("House_" + name)
    mesh.from_pydata(local_vertices, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    obj.location = centre
    obj.data.materials.append(material)
    obj.parent = parent
    if bevel:
        add_bevel(obj, bevel, 2)
    return obj


def create_cylinder(name, location, radius, depth, material, parent,
                    rotation=(0.0, 0.0, 0.0), vertices=12):
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=vertices, radius=radius, depth=depth,
        location=location, rotation=rotation
    )
    obj = bpy.context.object
    obj.name = name
    obj.data.name = "House_" + name
    obj.data.materials.append(material)
    obj.parent = parent
    return obj


# =============================================================================
# Architectural pieces
# =============================================================================


def create_house_body(layout, materials, groups, rng):
    """Create the primary shell and optional offset secondary section."""
    w, d, h = layout["width"], layout["depth"], layout["body_height"]
    create_box("MainHouseBody", (0, 0, h * 0.5), (w, d, h),
               materials["wall"], groups["Structure"], bevel=0.10)
    create_box("FoundationBand", (0, 0, 0.18), (w + 0.10, d + 0.10, 0.36),
               materials["foundation"], groups["Structure"], bevel=0.04)
    create_box("UpperTrimBand", (0, -d * 0.502, h - 0.22),
               (w + 0.08, 0.10, 0.16), materials["trim"],
               groups["Structure"], bevel=0.025)

    section = None
    if layout["has_secondary"]:
        side = layout["secondary_side"]
        sw = rng.uniform(2.5, 3.7)
        sd = rng.uniform(d * 0.60, d * 0.90)
        sh = layout["floor_height"] * rng.uniform(0.92, 1.12)
        sx = side * (w * 0.5 + sw * 0.36)
        sy = rng.uniform(-0.30, 0.45)
        create_box("SecondaryBody", (sx, sy, sh * 0.5), (sw, sd, sh),
                   materials["wall"], groups["Structure"], bevel=0.09)
        section = {"x": sx, "y": sy, "width": sw, "depth": sd,
                   "height": sh, "side": side}
    layout["secondary"] = section
    return layout


def roof_vertices(width, depth, base_z, height, roof_type, ridge_direction):
    """Return a closed, deliberately chunky roof volume."""
    x, y = width * 0.5, depth * 0.5
    low = base_z - 0.12
    if roof_type == "GABLE" and ridge_direction == "X":
        verts = [(-x, -y, low), (x, -y, low), (x, y, low), (-x, y, low),
                 (-x, 0, base_z + height), (x, 0, base_z + height)]
        faces = [(0, 1, 5, 4), (3, 4, 5, 2), (0, 4, 3),
                 (1, 2, 5), (0, 3, 2, 1)]
    elif roof_type == "GABLE":
        verts = [(-x, -y, low), (x, -y, low), (x, y, low), (-x, y, low),
                 (0, -y, base_z + height), (0, y, base_z + height)]
        faces = [(0, 4, 1), (3, 2, 5), (0, 3, 5, 4),
                 (1, 4, 5, 2), (0, 1, 2, 3)]
    elif roof_type == "HIP":
        if ridge_direction == "X":
            ridge = x * 0.42
            verts = [(-x, -y, low), (x, -y, low), (x, y, low), (-x, y, low),
                     (-ridge, 0, base_z + height), (ridge, 0, base_z + height)]
            faces = [(0, 1, 5, 4), (3, 4, 5, 2), (0, 4, 3),
                     (1, 2, 5), (0, 3, 2, 1)]
        else:
            ridge = y * 0.42
            verts = [(-x, -y, low), (x, -y, low), (x, y, low), (-x, y, low),
                     (0, -ridge, base_z + height), (0, ridge, base_z + height)]
            faces = [(0, 4, 1), (3, 2, 5), (0, 3, 5, 4),
                     (1, 4, 5, 2), (0, 1, 2, 3)]
    else:  # SKILLION: a friendly asymmetric wedge, never a thin plane.
        if ridge_direction == "X":
            verts = [(-x, -y, low), (x, -y, low), (x, y, low), (-x, y, low),
                     (-x, -y, base_z + height), (x, -y, base_z + height),
                     (x, y, base_z + height * 0.32), (-x, y, base_z + height * 0.32)]
        else:
            verts = [(-x, -y, low), (x, -y, low), (x, y, low), (-x, y, low),
                     (-x, -y, base_z + height), (x, -y, base_z + height * 0.32),
                     (x, y, base_z + height * 0.32), (-x, y, base_z + height)]
        faces = [(0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6),
                 (3, 0, 4, 7), (4, 5, 6, 7), (0, 3, 2, 1)]
    return verts, faces


def create_roof_piece(name, centre, width, depth, base_z, height, roof_type,
                      direction, materials, parent):
    verts, faces = roof_vertices(width, depth, base_z, height, roof_type, direction)
    verts = [(x + centre[0], y + centre[1], z) for x, y, z in verts]
    return create_mesh(name, verts, faces, materials["roof"], parent, bevel=0.045)


def create_roof(layout, materials, groups, rng):
    overhang = layout["overhang"]
    create_roof_piece(
        "MainRoof", (0, 0), layout["width"] + overhang * 2,
        layout["depth"] + overhang * 2, layout["body_height"],
        layout["roof_height"], layout["roof_type"], layout["roof_direction"],
        materials, groups["Roof"]
    )

    section = layout.get("secondary")
    if section:
        secondary_type = rng.choice(("GABLE", "HIP"))
        create_roof_piece(
            "SecondaryRoof", (section["x"], section["y"]),
            section["width"] + overhang * 1.7,
            section["depth"] + overhang * 1.7, section["height"],
            rng.uniform(0.9, 1.45), secondary_type,
            "Y" if layout["roof_direction"] == "X" else "X",
            materials, groups["Roof"]
        )


def create_window(name, centre, width, height, facing, materials, parent, warm=False):
    """Create one inset-looking window with deep, oversized cartoon trim."""
    frame = 0.105
    depth = 0.12
    glass_mat = materials["warm_glass"] if warm else materials["glass"]
    x, y, z = centre
    if facing == "FRONT":
        create_box(name + "_Glass", (x, y, z), (width, 0.055, height),
                   glass_mat, parent, bevel=0.025)
        for suffix, loc, dims in (
            ("Top", (x, y - 0.025, z + height * 0.5), (width + frame * 2, depth, frame)),
            ("Bottom", (x, y - 0.025, z - height * 0.5), (width + frame * 2, depth, frame)),
            ("Left", (x - width * 0.5, y - 0.025, z), (frame, depth, height)),
            ("Right", (x + width * 0.5, y - 0.025, z), (frame, depth, height)),
            ("Mullion", (x, y - 0.04, z), (frame * 0.58, depth * 1.05, height)),
        ):
            create_box(name + "_" + suffix, loc, dims, materials["frame"], parent,
                       bevel=0.025)
    else:
        create_box(name + "_Glass", (x, y, z), (0.055, width, height),
                   glass_mat, parent, bevel=0.025)
        for suffix, loc, dims in (
            ("Top", (x, y, z + height * 0.5), (depth, width + frame * 2, frame)),
            ("Bottom", (x, y, z - height * 0.5), (depth, width + frame * 2, frame)),
            ("Near", (x, y - width * 0.5, z), (depth, frame, height)),
            ("Far", (x, y + width * 0.5, z), (depth, frame, height)),
            ("Mullion", (x, y, z), (depth * 1.05, frame * 0.58, height)),
        ):
            create_box(name + "_" + suffix, loc, dims, materials["frame"], parent,
                       bevel=0.025)


def create_windows(layout, materials, groups, rng):
    w, d = layout["width"], layout["depth"]
    floors = layout["floors"]
    door_x = layout["door_x"]
    # Broad slots make the facade readable and leave a deliberate door gap.
    slots = [-w * 0.34, 0.0, w * 0.34]
    for floor in range(floors):
        z = layout["floor_height"] * floor + layout["floor_height"] * 0.58
        candidates = [x for x in slots if floor > 0 or abs(x - door_x) > 1.25]
        rng.shuffle(candidates)
        count = min(len(candidates), rng.randint(2, 3))
        for index, x in enumerate(sorted(candidates[:count])):
            ww = rng.uniform(0.82, 1.20)
            wh = rng.uniform(1.05, 1.38)
            create_window(f"FrontWindow_F{floor + 1}_{index + 1}",
                          (x, -d * 0.5 - 0.075, z), ww, wh, "FRONT",
                          materials, groups["Windows"], warm=rng.random() < 0.46)

    # A few side windows keep the asset convincing when seen in a neighbourhood.
    for side_name, side in (("L", -1), ("R", 1)):
        if layout["has_garage"] and side == layout["garage_side"]:
            continue
        if layout["has_secondary"] and side == layout["secondary_side"]:
            continue
        for floor in range(floors):
            if rng.random() < 0.78:
                z = layout["floor_height"] * floor + layout["floor_height"] * 0.58
                create_window(f"SideWindow_{side_name}_F{floor + 1}",
                              (side * (w * 0.5 + 0.075), rng.uniform(-d * 0.18, d * 0.18), z),
                              rng.uniform(0.78, 1.05), rng.uniform(1.0, 1.3), "SIDE",
                              materials, groups["Windows"], warm=rng.random() < 0.35)

    section = layout.get("secondary")
    if section:
        create_window("SecondaryFrontWindow",
                      (section["x"], section["y"] - section["depth"] * 0.5 - 0.075,
                       section["height"] * 0.56),
                      rng.uniform(0.85, 1.15), rng.uniform(1.0, 1.28), "FRONT",
                      materials, groups["Windows"], warm=rng.random() < 0.45)


def create_door(layout, materials, groups, rng):
    x = layout["door_x"]
    y = -layout["depth"] * 0.5 - 0.10
    width, height = rng.uniform(1.02, 1.28), rng.uniform(2.12, 2.38)
    z = height * 0.5 + 0.10
    create_box("FrontDoor", (x, y, z), (width, 0.16, height),
               materials["door"], groups["Doors"], bevel=0.055)
    frame = 0.13
    for suffix, loc, dims in (
        ("Top", (x, y - 0.035, z + height * 0.5), (width + frame * 2, 0.16, frame)),
        ("Left", (x - width * 0.5, y - 0.035, z), (frame, 0.16, height)),
        ("Right", (x + width * 0.5, y - 0.035, z), (frame, 0.16, height)),
    ):
        create_box("DoorFrame_" + suffix, loc, dims, materials["trim"],
                   groups["Doors"], bevel=0.025)
    create_cylinder("DoorKnob", (x + width * 0.30, y - 0.12, z), 0.075, 0.10,
                    materials["dark"], groups["Doors"],
                    rotation=(math.radians(90), 0, 0), vertices=10)


def create_garage(layout, materials, groups, rng):
    if not layout["has_garage"]:
        return
    side = layout["garage_side"]
    gw = rng.uniform(3.0, 3.8)
    gd = rng.uniform(layout["depth"] * 0.72, layout["depth"] * 0.95)
    gh = rng.uniform(2.55, 2.95)
    gx = side * (layout["width"] * 0.5 + gw * 0.47)
    gy = (layout["depth"] - gd) * -0.5
    create_box("GarageBody", (gx, gy, gh * 0.5), (gw, gd, gh),
               materials["wall"], groups["Garage"], bevel=0.09)
    create_roof_piece("GarageRoof", (gx, gy), gw + 0.62, gd + 0.62, gh,
                      rng.uniform(0.75, 1.25), rng.choice(("GABLE", "HIP")),
                      "Y", materials, groups["Garage"])

    door_w, door_h = gw * 0.78, rng.uniform(2.0, 2.28)
    front_y = gy - gd * 0.5 - 0.09
    create_box("GarageDoor", (gx, front_y, door_h * 0.5 + 0.08),
               (door_w, 0.15, door_h), materials["garage"], groups["Garage"],
               bevel=0.045)
    # Three chunky seams read as a garage door without expensive detail.
    for i in range(1, 4):
        z = 0.08 + door_h * i / 4.0
        create_box(f"GarageDoorSeam_{i}", (gx, front_y - 0.085, z),
                   (door_w * 0.92, 0.035, 0.035), materials["trim"],
                   groups["Garage"], bevel=0.01)


def create_porch(layout, materials, groups, rng):
    if not layout["has_porch"]:
        return
    x = layout["door_x"]
    front = -layout["depth"] * 0.5
    width = rng.uniform(2.7, 4.2)
    depth = rng.uniform(1.15, 1.75)
    centre_y = front - depth * 0.47
    create_box("PorchDeck", (x, centre_y, 0.16), (width, depth, 0.32),
               materials["porch"], groups["Porch"], bevel=0.07)
    awning_z = rng.uniform(2.55, 2.85)
    create_box("PorchAwning", (x, centre_y, awning_z),
               (width + 0.35, depth + 0.30, 0.22), materials["roof"],
               groups["Porch"], bevel=0.06,
               rotation=(math.radians(-5.0), 0, 0))
    post_offset = width * 0.40
    for label, px in (("L", x - post_offset), ("R", x + post_offset)):
        create_box("PorchPost_" + label, (px, centre_y - depth * 0.35, awning_z * 0.5),
                   (0.18, 0.18, awning_z), materials["trim"], groups["Porch"],
                   bevel=0.035)
    # Optional broad railing bars; still legible and cheap at game distance.
    if rng.random() < 0.55:
        for label, direction in (("L", -1), ("R", 1)):
            rail_x = x + direction * width * 0.32
            create_box("PorchRail_" + label, (rail_x, centre_y - depth * 0.40, 0.72),
                       (width * 0.22, 0.10, 0.12), materials["trim"],
                       groups["Porch"], bevel=0.025)


def create_chimney(layout, materials, groups, rng):
    if not layout["has_chimney"]:
        return
    x = rng.uniform(-layout["width"] * 0.28, layout["width"] * 0.28)
    y = rng.uniform(-layout["depth"] * 0.16, layout["depth"] * 0.16)
    z = layout["body_height"] + layout["roof_height"] * 0.68
    height = rng.uniform(1.35, 1.85)
    create_box("Chimney", (x, y, z), (0.62, 0.72, height),
               materials["wall"], groups["Details"], bevel=0.055)
    create_box("ChimneyCap", (x, y, z + height * 0.5), (0.80, 0.90, 0.18),
               materials["trim"], groups["Details"], bevel=0.04)


def create_front_details(layout, materials, groups, rng):
    """Add a step and one simple porch light for a toy-like focal point."""
    x = layout["door_x"]
    front = -layout["depth"] * 0.5
    create_box("FrontStep", (x, front - 0.42, 0.10), (1.48, 0.78, 0.20),
               materials["foundation"], groups["Details"], bevel=0.06)
    light_x = x + rng.choice((-0.82, 0.82))
    create_box("PorchLight", (light_x, front - 0.11, 1.75), (0.22, 0.17, 0.34),
               materials["warm_glass"], groups["Details"], bevel=0.055)
    create_box("PorchLightCap", (light_x, front - 0.09, 1.96), (0.28, 0.20, 0.10),
               materials["dark"], groups["Details"], bevel=0.025)


# =============================================================================
# Layout and assembly
# =============================================================================


def choose_layout(rng):
    floors = 2 if rng.random() < TWO_STOREY_CHANCE else 1
    width = rng.uniform(*HOUSE_WIDTH)
    depth = rng.uniform(*HOUSE_DEPTH)
    floor_height = rng.uniform(*FLOOR_HEIGHT)
    has_garage = rng.random() < GARAGE_CHANCE
    garage_side = rng.choice((-1, 1))
    # Put a secondary section opposite the garage to avoid tangled silhouettes.
    has_secondary = rng.random() < SECONDARY_SECTION_CHANCE
    secondary_side = -garage_side if has_garage else rng.choice((-1, 1))
    door_choices = (-width * 0.22, 0.0, width * 0.22)
    door_x = rng.choice(door_choices)
    return {
        "width": width,
        "depth": depth,
        "floors": floors,
        "floor_height": floor_height,
        "body_height": floor_height * floors,
        "roof_type": rng.choice(ROOF_TYPES),
        "roof_direction": rng.choice(("X", "Y")),
        "roof_height": rng.uniform(*ROOF_HEIGHT) * (0.90 if floors == 2 else 1.0),
        "overhang": rng.uniform(*ROOF_OVERHANG),
        "has_secondary": has_secondary,
        "secondary_side": secondary_side,
        "has_garage": has_garage,
        "garage_side": garage_side,
        "has_porch": rng.random() < PORCH_CHANCE,
        "has_chimney": rng.random() < CHIMNEY_CHANCE,
        "door_x": door_x,
    }


def generate_house(seed=SEED):
    delete_generated_house()
    actual_seed = seed if seed is not None else random.SystemRandom().randrange(1, 2 ** 31)
    rng = random.Random(actual_seed)
    materials = create_materials(rng)
    layout = choose_layout(rng)

    root = make_empty("GeneratedHouse")
    groups = {name: make_empty(name, root) for name in
              ("Structure", "Roof", "Windows", "Doors", "Garage", "Porch", "Details")}

    create_house_body(layout, materials, groups, rng)
    create_roof(layout, materials, groups, rng)
    create_windows(layout, materials, groups, rng)
    create_door(layout, materials, groups, rng)
    create_garage(layout, materials, groups, rng)
    create_porch(layout, materials, groups, rng)
    create_chimney(layout, materials, groups, rng)
    create_front_details(layout, materials, groups, rng)

    root["generator"] = "Couch Guys Cartoon House v1"
    root["seed"] = actual_seed
    root["forward_axis"] = "-Y"
    root["unity_scale_meters"] = 1.0
    root["floors"] = layout["floors"]
    root["roof_type"] = layout["roof_type"]
    root["generated_utc"] = time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())

    bpy.ops.object.select_all(action="DESELECT")
    root.select_set(True)
    bpy.context.view_layer.objects.active = root
    print(f"Generated cartoon house successfully. Seed: {actual_seed}")
    return root


if __name__ == "__main__":
    generate_house()
