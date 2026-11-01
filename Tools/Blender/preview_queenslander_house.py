import bpy, math, json
from pathlib import Path
from mathutils import Vector
P=Path(__file__).resolve().parents[2]
bpy.ops.wm.open_mainfile(filepath=str(P/'Assets/_Project/Models/Houses/SmallHouses/small_house_02.blend'))
scene=bpy.context.scene
scene.render.engine='BLENDER_WORKBENCH'
scene.display.shading.light='STUDIO'
scene.display.shading.color_type='MATERIAL'
scene.display.shading.show_shadows=True
scene.display.shading.show_cavity=True
scene.display.shading.cavity_type='BOTH'
scene.display.shading.background_type='WORLD'
scene.world.color=(.16,.21,.27)
bpy.ops.object.camera_add(location=(13,-22,12))
cam=bpy.context.object
cam.rotation_euler=(Vector((0,-.6,1.65))-cam.location).to_track_quat('-Z','Y').to_euler()
cam.data.type='ORTHO';cam.data.ortho_scale=16
scene.camera=cam
scene.render.resolution_x=1100;scene.render.resolution_y=750;scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG'
(P/'Tools/Blender/Previews').mkdir(exist_ok=True)
scene.render.filepath=str(P/'Tools/Blender/Previews/small_house_02_blender.png')
bpy.ops.render.render(write_still=True)
meshes=[o for o in scene.objects if o.type=='MESH']
coords=[o.matrix_world@Vector(c) for o in meshes for c in o.bound_box]
print('BOUNDS',[(min(v[i] for v in coords),max(v[i] for v in coords)) for i in range(3)])
print('TRIANGLES',sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in meshes))
print('MATERIALS',len({m.name for o in meshes for m in o.data.materials}))
