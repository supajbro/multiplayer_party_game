from pathlib import Path
import bpy,json,numpy as np
ROOT=Path(__file__).resolve().parents[2];OUT=ROOT/'ArtSource/Characters/ThiefEnemy01/ExpressivePoses'
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.fbx(filepath=str(OUT/'thief_enemy_01.fbx'))
rig=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
meshes=sorted([o for o in bpy.context.scene.objects if o.type=='MESH'],key=lambda o:o.name)
assert len(bpy.data.actions)==25 and len(rig.data.bones)==22 and len(meshes)==4
rig.animation_data.action=None
for p in rig.pose.bones:p.matrix_basis.identity()
bpy.context.view_layer.update();report={'rest_pose':'T-pose','bones':22,'lods':[],'actions':{}}
for obj,budget in zip(meshes,[10000,4000,1500,700]):
    triangles=sum(len(p.vertices)-2 for p in obj.data.polygons)
    assert triangles==budget and len(obj.data.uv_layers)==1 and abs(obj.dimensions.z-1.65)<.01
    assert all(abs(sum(g.weight for g in v.groups)-1)<.002 for v in obj.data.vertices)
    report['lods'].append({'name':obj.name,'triangles':triangles,'dimensions_m':list(obj.dimensions)})
for a in bpy.data.actions:
    rig.animation_data.action=a;rig.animation_data.action_slot=a.slots[0]
    first,last=a.frame_range
    for frame in np.linspace(first,last,13):
        bpy.context.scene.frame_set(int(frame),subframe=float(frame-int(frame)))
        deps=bpy.context.evaluated_depsgraph_get()
        for o in meshes:
            ev=o.evaluated_get(deps);m=ev.to_mesh();coords=np.empty(len(m.vertices)*3,dtype=np.float32);m.vertices.foreach_get('co',coords)
            coords=coords.reshape((-1,3));matrix=np.array(o.matrix_world)
            points=coords@matrix[:3,:3].T+matrix[:3,3]
            assert np.isfinite(points).all() and np.abs(points).max()<3,(a.name,frame,o.name)
            ev.to_mesh_clear()
    report['actions'][a.name]={'frames':[float(first),float(last)],'sampled_poses_all_lods':13}
(OUT/'FBXRoundtripValidation.json').write_text(json.dumps(report,indent=2))
print('EXPRESSIVE_FBX_ROUNDTRIP_PASS',len(report['actions']),report['lods'])
