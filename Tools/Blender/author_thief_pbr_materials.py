"""Author actual lit Blender palette shaders; preserve all skinning and animation."""
from pathlib import Path
import bpy,json,hashlib,math
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[2];SRC=ROOT/'ArtSource/Characters/ThiefEnemy01';OUT=SRC/'PBRSurfaces'
bpy.ops.wm.open_mainfile(filepath=str(OUT/'Before/thief_enemy_01.blend'))
bpy.context.preferences.filepaths.save_version=0
def fingerprint():
    h=hashlib.sha256()
    def add(v):h.update(repr(v).encode())
    for o in sorted([o for o in bpy.data.objects if o.type in {'MESH','ARMATURE'}],key=lambda o:o.name):
        add((o.name,o.type,tuple(tuple(r) for r in o.matrix_world)))
        if o.type=='MESH':
            for v in o.data.vertices:add((tuple(v.co),tuple((g.group,g.weight) for g in v.groups)))
            for p in o.data.polygons:add(tuple(p.vertices))
            for layer in o.data.uv_layers:
                for v in layer.data:add(tuple(v.uv))
            add(tuple(g.name for g in o.vertex_groups))
            add(tuple((m.type,m.object.name if m.type=='ARMATURE' else m.name) for m in o.modifiers))
        else:
            for b in o.data.bones:add((b.name,b.parent.name if b.parent else None,tuple(tuple(r) for r in b.matrix_local)))
    for a in sorted(bpy.data.actions,key=lambda a:a.name):
        add((a.name,tuple(a.frame_range)))
        for layer in a.layers:
            for strip in layer.strips:
                for bag in strip.channelbags:
                    for c in bag.fcurves:
                        add((c.data_path,c.array_index))
                        for k in c.keyframe_points:add((tuple(k.co),tuple(k.handle_left),tuple(k.handle_right),k.interpolation))
    return h.hexdigest()
before=fingerprint();palette=json.loads((OUT/'SurfacePalette.json').read_text())
linear=lambda c:c/12.92 if c<=.04045 else ((c+.055)/1.055)**2.4
groups=[]
for spec in palette:
    group=bpy.data.node_groups.new('CG Thief / '+spec['name'],'ShaderNodeTree')
    for name,kind in [('Colour','NodeSocketColor'),('Metallic','NodeSocketFloat'),('Roughness','NodeSocketFloat')]:group.interface.new_socket(name=name,in_out='OUTPUT',socket_type=kind)
    color=group.nodes.new('ShaderNodeRGB');color.label=spec['name']+' colour';color.outputs[0].default_value=(*[linear(c) for c in spec['color_srgb']],1)
    metal=group.nodes.new('ShaderNodeValue');metal.label='Metallic';metal.outputs[0].default_value=spec['metallic']
    rough=group.nodes.new('ShaderNodeValue');rough.label='Roughness';rough.outputs[0].default_value=spec['roughness']
    output=group.nodes.new('NodeGroupOutput');output.location=(280,0)
    for socket,node in [('Colour',color),('Metallic',metal),('Roughness',rough)]:group.links.new(node.outputs[0],output.inputs[socket])
    mat=bpy.data.materials.new('CG_Thief_'+spec['name'].replace(' ','_'));mat.use_nodes=True;mat.use_fake_user=True
    mat.node_tree.nodes.clear();gn=mat.node_tree.nodes.new('ShaderNodeGroup');gn.node_tree=group
    bs=mat.node_tree.nodes.new('ShaderNodeBsdfPrincipled')
    for source,target in [('Colour','Base Color'),('Metallic','Metallic'),('Roughness','Roughness')]:mat.node_tree.links.new(gn.outputs[source],bs.inputs[target])
    out=mat.node_tree.nodes.new('ShaderNodeOutputMaterial');mat.node_tree.links.new(bs.outputs[0],out.inputs['Surface'])
    mat.diffuse_color=(*spec['color_srgb'],1);groups.append(group)
master=bpy.data.materials['thief_enemy_01'];nodes=master.node_tree.nodes;nodes.clear()
mask=nodes.new('ShaderNodeTexImage');mask.name='Material regions (no baked lighting)'
mask.image=bpy.data.images.load(str(OUT/'MaterialRegions.png'));mask.image.colorspace_settings.name='Non-Color';mask.interpolation='Closest';mask.image.pack();mask.location=(-650,0)
first=nodes.new('ShaderNodeGroup');first.node_tree=groups[0];first.location=(-200,400);previous=[first.outputs[name] for name in ['Colour','Metallic','Roughness']]
for i,group in enumerate(groups[1:],1):
    shader=nodes.new('ShaderNodeGroup');shader.node_tree=group;shader.location=(-200,-i*200)
    compare=nodes.new('ShaderNodeMath');compare.operation='COMPARE';compare.inputs[1].default_value=i*20/255;compare.inputs[2].default_value=.012;compare.location=(-400,-i*200)
    master.node_tree.links.new(mask.outputs['Color'],compare.inputs[0])
    following=[]
    for j,name in enumerate(['Colour','Metallic','Roughness']):
        mix=nodes.new('ShaderNodeMixRGB');mix.location=(100+i*140,-j*220)
        master.node_tree.links.new(compare.outputs[0],mix.inputs[0]);master.node_tree.links.new(previous[j],mix.inputs[1]);master.node_tree.links.new(shader.outputs[name],mix.inputs[2]);following.append(mix.outputs[0])
    previous=following
bs=nodes.new('ShaderNodeBsdfPrincipled');bs.location=(1850,0)
for output,target in zip(previous,['Base Color','Metallic','Roughness']):master.node_tree.links.new(output,bs.inputs[target])
output=nodes.new('ShaderNodeOutputMaterial');output.location=(2150,0);master.node_tree.links.new(bs.outputs[0],output.inputs['Surface'])
master['CG_SurfaceWorkflow']='Editable lit palette shaders; UV mask selects surfaces. Runtime bakes colour/metallic/roughness only; no illumination, emission or generated normal map.'
assert fingerprint()==before,'Material authoring changed rig, UVs, mesh, weights or clips'
candidate=OUT/'thief_enemy_01.blend';bpy.ops.wm.save_as_mainfile(filepath=str(candidate))
bpy.ops.wm.open_mainfile(filepath=str(candidate));after=fingerprint();assert before==after
(OUT/'SourcePreservationValidation.json').write_text(json.dumps({'before_sha256':before,'after_sha256':after,'identical':True,'actions':len(bpy.data.actions),'meshes':len([o for o in bpy.data.objects if o.type=='MESH']),'fbx_reexported':False},indent=2))
rig=bpy.data.objects['thief_enemy_01_Rig'];rig.animation_data.action=None
for p in rig.pose.bones:p.matrix_basis.identity()
scene=bpy.context.scene;scene.frame_set(1)
lods=[bpy.data.objects['thief_enemy_01'+('_LOD'+str(i) if i else '')] for i in range(4)]
for i,o in enumerate(lods):o.hide_render=i!=0;o.hide_set(i!=0)
scene.render.engine='CYCLES';scene.cycles.samples=32;scene.render.resolution_x=850;scene.render.resolution_y=850;scene.render.resolution_percentage=100
scene.world=bpy.data.worlds.new('CG Material Studio');scene.world.use_nodes=True
next(n for n in scene.world.node_tree.nodes if n.type=='BACKGROUND').inputs[0].default_value=(.16,.18,.21,1)
lights=[]
for name,loc,power,size in [('Key',(3,-4,5),500,4),('Fill',(-4,-2,3),200,4),('Rim',(1,4,4),450,3)]:
    bpy.ops.object.light_add(type='AREA',location=loc);o=bpy.context.object;o.name=name;o.data.energy=power;o.data.shape='DISK';o.data.size=size;o.rotation_euler=(Vector((0,0,.85))-o.location).to_track_quat('-Z','Y').to_euler();lights.append(o)
bpy.ops.object.camera_add();cam=bpy.context.object;scene.camera=cam;cam.data.type='ORTHO';cam.data.ortho_scale=2.05
def render(name,angle=0):
    a=math.radians(angle);cam.location=(4*math.sin(a),-4*math.cos(a),1.06);cam.rotation_euler=(Vector((0,0,.82))-cam.location).to_track_quat('-Z','Y').to_euler()
    scene.render.filepath=str(OUT/(name+'.png'));bpy.ops.render.render(write_still=True)
render('Front');render('Quarter',30);render('Back',180)
for o in lights[1:]:o.data.energy=0
lights[0].data.energy=550;lights[0].data.size=2
for name,x in [('Light_Left',-3),('Light_Right',3)]:
    lights[0].location=(x,-2,2.7);lights[0].rotation_euler=(Vector((0,0,.85))-lights[0].location).to_track_quat('-Z','Y').to_euler();render(name)
lights[0].data.size=4;lights[0].data.energy=500
for o,e in zip(lights[1:],[200,450]):o.data.energy=e
for action in ['Unarmed_Thief_Idle','Unarmed_Thief_Walk','Pistol_Thief_Shoot']:
    a=bpy.data.actions[action];rig.animation_data.action=a;rig.animation_data.action_slot=a.slots[0];scene.frame_set(round(sum(a.frame_range)/2));render(action,25)
print('BLENDER_PBR_MATERIALS_VERIFIED',before,after,flush=True)
