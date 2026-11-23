"""Render the actual Hotwire master, or surfaces captured from the Unity prefab."""
from pathlib import Path
import bpy,json,math,sys
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[2];SRC=ROOT/'ArtSource/Characters/Hotwire'
if '--unity' in sys.argv:
    names=['Tpose','Idle','Walk','Shoot','Frustration','Death','LOD1','LOD2','LOD3']
else:names=['Front','Back','Quarter','Idle','Walk','Shoot']
if '--other' in sys.argv:names=['Idle']
for name in names:
    if '--unity' in sys.argv:
        bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
        report=json.loads((SRC/('Hotwire_'+name+'_Preview.json')).read_text())
        for surface in report['surfaces']:
            coords=[(v['x'],-v['z'],v['y']) for v in surface['vertices']];tris=[surface['triangles'][i:i+3] for i in range(0,len(surface['triangles']),3)]
            mesh=bpy.data.meshes.new('Surface');mesh.from_pydata(coords,[],tris);mesh.update();obj=bpy.data.objects.new('Surface',mesh);bpy.context.collection.objects.link(obj)
            if len(surface['normals'])==len(coords):mesh.normals_split_custom_set_from_vertices([(n['x'],-n['z'],n['y']) for n in surface['normals']])
            if len(surface['uv'])==len(coords):
                uv=mesh.uv_layers.new(name='UVMap')
                for loop in mesh.loops:
                    value=surface['uv'][loop.vertex_index];uv.data[loop.index].uv=(value['x'],value['y'])
            for p in mesh.polygons:p.use_smooth=True
            mat=bpy.data.materials.new('Preview');mat.use_nodes=True;shader=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED');color=surface['color'];shader.inputs['Base Color'].default_value=(color['r'],color['g'],color['b'],1);shader.inputs['Roughness'].default_value=.7
            if surface['texture']:
                node=mat.node_tree.nodes.new('ShaderNodeTexImage');node.image=bpy.data.images.load(surface['texture'],check_existing=True);mat.node_tree.links.new(node.outputs['Color'],shader.inputs['Base Color'])
            obj.data.materials.append(mat)
        output='Unity_'+name
    else:
        bpy.ops.wm.open_mainfile(filepath=str(SRC/'Hotwire.blend'))
        rig=bpy.data.objects['thief_enemy_01_Rig'];rig.animation_data.action=None
        for p in rig.pose.bones:p.matrix_basis.identity()
        if name in ['Idle','Walk','Shoot']:
            a=bpy.data.actions['Shotgun_Thief_'+name];rig.animation_data.action=a;rig.animation_data.action_slot=a.slots[0];first,last=a.frame_range;bpy.context.scene.frame_set(round(first+(last-first)*.4))
        output=name
    scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.device='CPU';scene.cycles.samples=20
    scene.render.resolution_x=800;scene.render.resolution_y=800;scene.render.resolution_percentage=100;scene.world.color=(.25,.25,.25);scene.view_settings.view_transform='Standard'
    for label,loc,power in [('Key',(3,-4,5),450),('Fill',(-4,-2,3),300),('Rim',(1,4,4),400)]:
        d=bpy.data.lights.new(label,'AREA');d.energy=power;d.shape='DISK';d.size=4;o=bpy.data.objects.new(label,d);scene.collection.objects.link(o);o.location=loc;o.rotation_euler=(Vector((0,0,.9))-o.location).to_track_quat('-Z','Y').to_euler()
    d=bpy.data.cameras.new('QA');cam=bpy.data.objects.new('QA',d);scene.collection.objects.link(cam);scene.camera=cam;d.type='ORTHO';d.ortho_scale=2.5 if '--unity' in sys.argv else 2.1
    angle=180 if name=='Back' else 0 if name=='Front' else 25
    if '--other' in sys.argv:angle=-25;output='Other_'+name
    a=math.radians(angle);cam.location=(4*math.sin(a),-4*math.cos(a),1.3);cam.rotation_euler=(Vector((0,0,.85))-cam.location).to_track_quat('-Z','Y').to_euler()
    scene.render.filepath=str(SRC/(output+'.png'));bpy.ops.render.render(write_still=True)
print('HOTWIRE_PREVIEWS_COMPLETE')
