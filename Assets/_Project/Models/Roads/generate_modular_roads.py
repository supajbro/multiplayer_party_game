"""Generate a Unity-ready modular suburban road kit in Blender.

Run from Blender's Scripting workspace, or from a terminal:

    blender --background --python generate_modular_roads.py

The script creates five canonical tiles. Unity can rotate these to represent every
combination used by Couch Guys' RoadTile system:

    Road_Straight  North + South
    Road_Corner    North + East
    Road_T         North + East + West
    Road_Cross     North + East + South + West
    Road_End       North

FBX files and an arranged preview .blend are written to GeneratedRoads beside this
script. Dimensions are metres, with a centred origin and a 10 x 10 metre footprint.
"""

from __future__ import annotations

import math
import os
from dataclasses import dataclass
from typing import Iterable, Sequence

import bpy


TILE_SIZE = 10.0
ROAD_WIDTH = 8.0
ASPHALT_HEIGHT = 0.0
MARKING_HEIGHT = 0.012
MARKING_WIDTH = 0.14
EDGE_INSET = 0.45
INTERSECTION_CLEARANCE = 1.35
PREVIEW_SPACING = 13.0

SCRIPT_DIRECTORY = (
    os.path.dirname(os.path.abspath(__file__))
    if "__file__" in globals()
    else bpy.path.abspath("//")
)
OUTPUT_DIRECTORY = os.path.join(SCRIPT_DIRECTORY, "GeneratedRoads")


@dataclass(frozen=True)
class TileDefinition:
    name: str
    connections: tuple[str, ...]
    preview_position: tuple[float, float, float]


TILES = (
    TileDefinition("Road_Straight", ("N", "S"), (0.0, 0.0, 0.0)),
    TileDefinition("Road_Corner", ("N", "E"), (PREVIEW_SPACING, 0.0, 0.0)),
    TileDefinition("Road_T", ("N", "E", "W"), (PREVIEW_SPACING * 2.0, 0.0, 0.0)),
    TileDefinition("Road_Cross", ("N", "E", "S", "W"), (0.0, -PREVIEW_SPACING, 0.0)),
    TileDefinition("Road_End", ("N",), (PREVIEW_SPACING, -PREVIEW_SPACING, 0.0)),
)

DIRECTION_VECTOR = {
    "N": (0.0, 1.0),
    "E": (1.0, 0.0),
    "S": (0.0, -1.0),
    "W": (-1.0, 0.0),
}


def clear_scene() -> None:
    if bpy.context.object is not None and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)

    for data_collection in (bpy.data.meshes, bpy.data.curves, bpy.data.materials):
        for data_block in list(data_collection):
            if data_block.users == 0:
                data_collection.remove(data_block)


def make_material(
    name: str,
    colour: tuple[float, float, float, float],
    roughness: float,
) -> bpy.types.Material:
    material = bpy.data.materials.new(name=name)
    material.diffuse_color = colour
    material.use_nodes = True
    principled = material.node_tree.nodes.get("Principled BSDF")
    if principled is not None:
        principled.inputs["Base Color"].default_value = colour
        principled.inputs["Roughness"].default_value = roughness
        if "Specular IOR Level" in principled.inputs:
            principled.inputs["Specular IOR Level"].default_value = 0.25
    return material


def create_quad_mesh(
    name: str,
    rectangles: Iterable[tuple[float, float, float, float]],
    height: float,
    material: bpy.types.Material,
    parent: bpy.types.Object,
) -> bpy.types.Object:
    vertices: list[tuple[float, float, float]] = []
    faces: list[tuple[int, int, int, int]] = []

    for centre_x, centre_y, size_x, size_y in rectangles:
        half_x = size_x * 0.5
        half_y = size_y * 0.5
        first = len(vertices)
        vertices.extend(
            (
                (centre_x - half_x, centre_y - half_y, height),
                (centre_x + half_x, centre_y - half_y, height),
                (centre_x + half_x, centre_y + half_y, height),
                (centre_x - half_x, centre_y + half_y, height),
            )
        )
        faces.append((first, first + 1, first + 2, first + 3))

    mesh = bpy.data.meshes.new(f"{name}_Mesh")
    mesh.from_pydata(vertices, [], faces)
    mesh.materials.append(material)
    mesh.update()

    road_object = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(road_object)
    road_object.parent = parent
    return road_object


def road_rectangles(connections: Sequence[str]) -> list[tuple[float, float, float, float]]:
    half_road = ROAD_WIDTH * 0.5
    extension = (TILE_SIZE - ROAD_WIDTH) * 0.5
    extension_centre = half_road + extension * 0.5
    rectangles = [(0.0, 0.0, ROAD_WIDTH, ROAD_WIDTH)]

    for direction in connections:
        direction_x, direction_y = DIRECTION_VECTOR[direction]
        centre_x = direction_x * extension_centre
        centre_y = direction_y * extension_centre
        if direction in ("N", "S"):
            rectangles.append((centre_x, centre_y, ROAD_WIDTH, extension))
        else:
            rectangles.append((centre_x, centre_y, extension, ROAD_WIDTH))
    return rectangles


def oriented_rectangle(
    direction: str,
    forward_centre: float,
    forward_length: float,
    sideways_centre: float,
    sideways_width: float,
) -> tuple[float, float, float, float]:
    direction_x, direction_y = DIRECTION_VECTOR[direction]
    right_x, right_y = direction_y, -direction_x
    centre_x = direction_x * forward_centre + right_x * sideways_centre
    centre_y = direction_y * forward_centre + right_y * sideways_centre
    if direction in ("N", "S"):
        return centre_x, centre_y, sideways_width, forward_length
    return centre_x, centre_y, forward_length, sideways_width


def centre_markings(connections: Sequence[str]) -> list[tuple[float, float, float, float]]:
    markings: list[tuple[float, float, float, float]] = []
    straight = len(connections) == 2 and (
        set(connections) == {"N", "S"} or set(connections) == {"E", "W"}
    )

    if straight:
        direction = "N" if "N" in connections else "E"
        for forward in (-3.75, -1.25, 1.25, 3.75):
            markings.append(
                oriented_rectangle(direction, forward, 1.25, 0.0, MARKING_WIDTH)
            )
        return markings

    dash_start = INTERSECTION_CLEARANCE + 0.35
    dash_end = TILE_SIZE * 0.5 - 0.35
    dash_length = dash_end - dash_start
    dash_centre = (dash_start + dash_end) * 0.5
    for direction in connections:
        markings.append(
            oriented_rectangle(direction, dash_centre, dash_length, 0.0, MARKING_WIDTH)
        )
    return markings


def edge_markings(connections: Sequence[str]) -> list[tuple[float, float, float, float]]:
    markings: list[tuple[float, float, float, float]] = []
    line_offset = ROAD_WIDTH * 0.5 - EDGE_INSET
    straight = len(connections) == 2 and (
        set(connections) == {"N", "S"} or set(connections) == {"E", "W"}
    )

    if straight:
        direction = "N" if "N" in connections else "E"
        for side in (-line_offset, line_offset):
            markings.append(
                oriented_rectangle(direction, 0.0, TILE_SIZE, side, MARKING_WIDTH)
            )
        return markings

    arm_start = INTERSECTION_CLEARANCE
    arm_end = TILE_SIZE * 0.5
    arm_length = arm_end - arm_start
    arm_centre = (arm_start + arm_end) * 0.5
    for direction in connections:
        for side in (-line_offset, line_offset):
            markings.append(
                oriented_rectangle(
                    direction,
                    arm_centre,
                    arm_length,
                    side,
                    MARKING_WIDTH,
                )
            )
    return markings


def add_end_cap_marking(connections: Sequence[str]) -> list[tuple[float, float, float, float]]:
    if len(connections) != 1:
        return []

    direction = connections[0]
    opposite = {"N": "S", "E": "W", "S": "N", "W": "E"}[direction]
    return [
        oriented_rectangle(
            opposite,
            ROAD_WIDTH * 0.5 - EDGE_INSET,
            MARKING_WIDTH,
            0.0,
            ROAD_WIDTH - EDGE_INSET * 2.0,
        )
    ]


def create_tile(
    definition: TileDefinition,
    asphalt: bpy.types.Material,
    white_paint: bpy.types.Material,
) -> bpy.types.Object:
    root = bpy.data.objects.new(definition.name, None)
    bpy.context.collection.objects.link(root)
    root.empty_display_type = "CUBE"
    root.empty_display_size = 1.0
    root["unity_connections"] = "|".join(definition.connections)
    root["tile_size_metres"] = TILE_SIZE
    root["road_width_metres"] = ROAD_WIDTH

    create_quad_mesh(
        f"{definition.name}_Asphalt",
        road_rectangles(definition.connections),
        ASPHALT_HEIGHT,
        asphalt,
        root,
    )
    paint = (
        edge_markings(definition.connections)
        + centre_markings(definition.connections)
        + add_end_cap_marking(definition.connections)
    )
    create_quad_mesh(
        f"{definition.name}_WhiteMarkings",
        paint,
        MARKING_HEIGHT,
        white_paint,
        root,
    )
    return root


def hierarchy(root: bpy.types.Object) -> list[bpy.types.Object]:
    result = [root]
    for child in root.children:
        result.extend(hierarchy(child))
    return result


def export_tile(root: bpy.types.Object) -> None:
    bpy.ops.object.select_all(action="DESELECT")
    for road_object in hierarchy(root):
        road_object.select_set(True)
    bpy.context.view_layer.objects.active = root

    output_path = os.path.join(OUTPUT_DIRECTORY, f"{root.name}.fbx")
    bpy.ops.export_scene.fbx(
        filepath=output_path,
        use_selection=True,
        object_types={"EMPTY", "MESH"},
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_UNITS",
        axis_forward="-Z",
        axis_up="Y",
        bake_anim=False,
        add_leaf_bones=False,
        path_mode="AUTO",
    )


def configure_scene() -> None:
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0
    scene.world.color = (0.06, 0.06, 0.06)


def main() -> None:
    os.makedirs(OUTPUT_DIRECTORY, exist_ok=True)
    clear_scene()
    configure_scene()

    asphalt = make_material("M_Road_Asphalt", (0.018, 0.021, 0.024, 1.0), 0.88)
    white_paint = make_material("M_Road_WhitePaint", (0.92, 0.92, 0.88, 1.0), 0.62)

    roots: list[tuple[bpy.types.Object, TileDefinition]] = []
    for definition in TILES:
        root = create_tile(definition, asphalt, white_paint)
        export_tile(root)
        roots.append((root, definition))

    # Arrange the saved Blender scene for convenient visual inspection. FBXs were
    # exported while every tile was centred at the origin.
    for root, definition in roots:
        root.location = definition.preview_position

    bpy.ops.object.select_all(action="SELECT")
    blend_path = os.path.join(OUTPUT_DIRECTORY, "ModularRoadKit.blend")
    bpy.ops.wm.save_as_mainfile(filepath=blend_path)
    print(f"Generated {len(roots)} road tiles in: {OUTPUT_DIRECTORY}")


if __name__ == "__main__":
    main()
