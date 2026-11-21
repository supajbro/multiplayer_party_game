"""Validate the shipped LOD/animation FBX independently of the Blender master."""
from pathlib import Path
import bpy,json,math,bmesh
ROOT=Path(__file__).resolve().parents[2];SRC=ROOT/'ArtSource/Characters/ThiefEnemy01'
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.fbx(filepath=str(ROOT/'Assets/_Project/Models/Thieves/thief_enemy_01.fbx'))
rig=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
meshes=sorted([o for o in bpy.context.scene.objects if o.type=='MESH'],key=lambda o:o.name)
rig.animation_data.action=None
for p in rig.pose.bones:p.matrix_basis.identity()
bpy.context.view_layer.update()
report={'lods':[],'animations':{},'bones':len(rig.data.bones),'rest_pose':True}
assert len(meshes)==4 and len(bpy.data.actions)==15
for obj,budget in zip(meshes,[10000,4000,1500,700]):
    tri=sum(len(p.vertices)-2 for p in obj.data.polygons)
    assert tri<=budget and len(obj.data.uv_layers)==1
    assert all(abs(sum(g.weight for g in v.groups)-1)<.002 for v in obj.data.vertices)
    assert abs(obj.dimensions.z-1.65)<.01
    bm=bmesh.new();bm.from_mesh(obj.data)
    bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.000005)
    topology={'nonmanifold_edges':sum(not e.is_manifold for e in bm.edges),'boundary_edges':sum(e.is_boundary for e in bm.edges),'loose_vertices':sum(not v.link_faces for v in bm.verts)}
    bm.free()
    assert topology['loose_vertices']==0
    report['lods'].append({'name':obj.name,'triangles':tri,'vertices_after_uv_split':len(obj.data.vertices),'dimensions_m':list(obj.dimensions),'unweighted_vertices':sum(not any(g.weight>1e-5 for g in v.groups) for v in obj.data.vertices),'max_influences':max(sum(g.weight>1e-5 for g in v.groups) for v in obj.data.vertices),'topology_after_seam_welding':topology})
for action in bpy.data.actions:
    rig.animation_data.action=action;rig.animation_data.action_slot=action.slots[0]
    first,last=map(round,action.frame_range)
    lows=[float('inf')]*3;highs=[-float('inf')]*3
    samples=0
    for frame in range(first,last+1):
        bpy.context.scene.frame_set(frame)
        for obj in meshes:
            ev=obj.evaluated_get(bpy.context.evaluated_depsgraph_get());mesh=ev.to_mesh()
            for v in mesh.vertices:
                world=obj.matrix_world@v.co
                assert all(math.isfinite(c) and abs(c)<3 for c in world), (action.name,frame,list(world))
                for i in range(3):lows[i]=min(lows[i],world[i]);highs[i]=max(highs[i],world[i])
            ev.to_mesh_clear()
        samples+=1
    report['animations'][action.name]={'frames':[first,last],'sampled_frames_all_lods':samples,'bounds_min_m':lows,'bounds_max_m':highs}
(SRC/'FBXRoundtripValidation.json').write_text(json.dumps(report,indent=2))
print('FINAL_FBX_ROUNDTRIP_PASS',json.dumps(report['lods']))
