"""Single FBX round-trip validation and presentation render; no Unity integration."""
import bpy, json, math
from pathlib import Path
from mathutils import Vector
OUT=Path(r'C:/Users/Jaden/Documents/MyUnityProjects/multiplayer_party_game/Assets/_Project/Models/Dispatcher')
report={}
lod0=None
for lod in range(3):
    scene=bpy.data.scenes.new('Validate_LOD'+str(lod)); bpy.context.window.scene=scene
    bpy.ops.import_scene.fbx(filepath=str(OUT/('dispatcher_01_LOD'+str(lod)+'.fbx')))
    meshes=[o for o in scene.objects if o.type=='MESH']
    assert len(meshes)==2
    assert not any('Waveform' in o.name for o in scene.objects)
    assert not any('Waveform' in m.name for o in meshes for m in o.data.materials)
    screen=next(o for o in meshes if o.name.endswith('_Screen'))
    assert len(screen.data.materials)==1 and screen.data.uv_layers
    assert all(abs(v)<1e-5 for o in meshes for v in o.location)
    coords=[o.matrix_world@Vector(c) for o in meshes for c in o.bound_box]
    low=[min(c[i] for c in coords) for i in range(3)]
    high=[max(c[i] for c in coords) for i in range(3)]
    assert abs(low[2])<.002 and 2.4<high[2]<2.5, (low,high)
    assert all(math.isfinite(v) for o in meshes for vert in o.data.vertices for v in vert.co)
    tris=sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in meshes)
    report['LOD'+str(lod)]={'triangles':tris,'mesh_count':len(meshes),'bounds_min':low,'bounds_max':high,'screen_uv':screen.data.uv_layers.active.name,'materials':sorted({m.name for o in meshes for m in o.data.materials})}
    if lod==0: lod0=scene
assert report['LOD0']['triangles']>report['LOD1']['triangles']>report['LOD2']['triangles']
(OUT/'dispatcher_01_validation.json').write_text(json.dumps(report,indent=2))
bpy.context.window.scene=lod0
scene=lod0
# Preview-only lighting and floor are never written into the model source or FBX.
bpy.ops.mesh.primitive_plane_add(size=200)
floor=bpy.context.object; floor.name='PreviewFloor'; floor.location.z=-.002
mat=bpy.data.materials.new('PreviewFloor'); mat.diffuse_color=(.19,.20,.24,1); mat.use_nodes=True
p=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED'); p.inputs['Base Color'].default_value=(.19,.20,.24,1); p.inputs['Roughness'].default_value=.8
floor.data.materials.append(mat)
bpy.ops.object.camera_add(location=(3.6,-6.2,3.1))
camera=bpy.context.object; camera.rotation_euler=(Vector((0,0,1.23))-camera.location).to_track_quat('-Z','Y').to_euler(); camera.data.type='ORTHO'; camera.data.ortho_scale=3.05; scene.camera=camera
for loc,power,size in [(( -3,-4,6),700,4),((4,-1,4),450,3),((0,4,5),650,3)]:
    bpy.ops.object.light_add(type='AREA',location=loc)
    o=bpy.context.object; o.data.energy=power; o.data.shape='DISK'; o.data.size=size
    o.rotation_euler=(Vector((0,0,1.2))-o.location).to_track_quat('-Z','Y').to_euler()
scene.world=bpy.data.worlds.new('PreviewWorld'); scene.world.use_nodes=True
next(n for n in scene.world.node_tree.nodes if n.type=='BACKGROUND').inputs[0].default_value=(.18,.20,.26,1)
try: scene.render.engine='CYCLES'
except TypeError: pass
scene.cycles.samples=32
scene.render.resolution_x=700; scene.render.resolution_y=800; scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG'; scene.render.filepath=str(OUT/'dispatcher_01_preview.png')
bpy.ops.render.render(write_still=True)
print('VALIDATION_PASSED',json.dumps(report))

