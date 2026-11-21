"""Render posed meshes captured from the actual upgraded Unity prefabs."""
from pathlib import Path
import bpy,json,sys
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[2]
SRC=ROOT/'ArtSource/Characters/ThiefEnemy01'/('GripFix' if '--grips' in sys.argv else 'ExpressivePoses' if '--expressive' in sys.argv else 'PrefabUpgrade')
names=[n+'_'+pose for n in ['Thief_Pistol','Thief_AssaultRifle','Thief_Shotgun','GrapplerThief'] for pose in ['Tpose','Idle','Shoot']] if '--grips' in sys.argv else ['Thief','Thief_Pistol','Thief_AssaultRifle','Thief_Shotgun','Thief_RocketLauncher','GrapplerThief','SpeakerThief']
for name in names:
    path=SRC/(name+'_Preview.json')
    if not path.exists():continue
    bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
    report=json.loads(path.read_text())
    for index,s in enumerate(report['surfaces']):
        coords=[(v['x'],-v['z'],v['y']) for v in s['vertices']]
        tris=[s['triangles'][i:i+3] for i in range(0,len(s['triangles']),3)]
        mesh=bpy.data.meshes.new('Surface');mesh.from_pydata(coords,[],tris);mesh.update()
        obj=bpy.data.objects.new('Surface',mesh);bpy.context.collection.objects.link(obj)
        if len(s['normals'])==len(coords):mesh.normals_split_custom_set_from_vertices([(n['x'],-n['z'],n['y']) for n in s['normals']])
        if len(s['uv'])==len(coords):
            uv=mesh.uv_layers.new(name='UVMap')
            for loop in mesh.loops:
                value=s['uv'][loop.vertex_index];uv.data[loop.index].uv=(value['x'],value['y'])
        for face in mesh.polygons:face.use_smooth=True
        mat=bpy.data.materials.new('Material');mat.use_nodes=True
        bsdf=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
        c=s['color'];bsdf.inputs['Base Color'].default_value=(c['r'],c['g'],c['b'],1);bsdf.inputs['Roughness'].default_value=.65
        if s['texture'] and Path(s['texture']).exists():
            tex=mat.node_tree.nodes.new('ShaderNodeTexImage');tex.image=bpy.data.images.load(s['texture'],check_existing=True)
            mat.node_tree.links.new(tex.outputs['Color'],bsdf.inputs['Base Color'])
        obj.data.materials.append(mat)
    scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.device='CPU';scene.cycles.samples=16
    scene.render.resolution_x=600;scene.render.resolution_y=600;scene.render.resolution_percentage=100
    scene.world.color=(.25,.25,.25);scene.view_settings.view_transform='Standard'
    for label,loc,power in [('Key',(3,-4,5),450),('Fill',(-4,-2,3),300),('Rim',(1,4,4),400)]:
        d=bpy.data.lights.new(label,'AREA');d.energy=power;d.shape='DISK';d.size=4
        o=bpy.data.objects.new(label,d);scene.collection.objects.link(o);o.location=loc;o.rotation_euler=(Vector((0,0,.9))-o.location).to_track_quat('-Z','Y').to_euler()
    d=bpy.data.cameras.new('QA');cam=bpy.data.objects.new('QA',d);scene.collection.objects.link(cam);scene.camera=cam
    d.type='ORTHO';d.ortho_scale=2.6;cam.location=(2,-4,1.7);cam.rotation_euler=(Vector((0,0,.9))-cam.location).to_track_quat('-Z','Y').to_euler()
    scene.render.filepath=str(SRC/(name+'_Preview.png'));bpy.ops.render.render(write_still=True)
print('PREFAB_PREVIEW_RENDERS_COMPLETE')
