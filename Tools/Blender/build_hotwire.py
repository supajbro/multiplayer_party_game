"""Fit the approved generated Hotwire surface to the existing thief skeleton.

Explicit offline authoring only. Leaves the original character and animations
untouched; exports four meshes with the original rest skeleton, no new clips.
"""
from pathlib import Path
import bpy,bmesh,math,json,numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from mathutils.geometry import barycentric_transform
ROOT=Path(__file__).resolve().parents[2];SRC=ROOT/'ArtSource/Characters/Hotwire'
MODEL=ROOT/'Assets/_Project/Models/Thieves/Hotwire'
MODEL.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(ROOT/'ArtSource/Characters/ThiefEnemy01/thief_enemy_01.blend'))
rig=bpy.data.objects['thief_enemy_01_Rig'];rig.animation_data.action=None
for p in rig.pose.bones:p.matrix_basis.identity()
ref=bpy.data.objects['thief_enemy_01'];ref.data.calc_loop_triangles()
tris=[tuple(t.vertices) for t in ref.data.loop_triangles]
tree=BVHTree.FromPolygons([v.co for v in ref.data.vertices],tris,all_triangles=True)
weights=[{ref.vertex_groups[g.group].name:g.weight for g in v.groups} for v in ref.data.vertices]
originals=[o for o in bpy.context.scene.objects if o.type=='MESH']
source=next((SRC/'Generated').rglob('*.glb'))
before=set(bpy.data.objects);bpy.ops.import_scene.gltf(filepath=str(source))
parts=[o for o in bpy.context.scene.objects if o not in before and o.type=='MESH']
bpy.ops.object.select_all(action='DESELECT')
for o in parts:o.select_set(True)
bpy.context.view_layer.objects.active=parts[0];bpy.ops.object.join();body=bpy.context.object
bpy.ops.object.transform_apply(location=False,rotation=True,scale=True)
coords=[body.matrix_world@v.co for v in body.data.vertices]
lo=Vector(tuple(min(v[i] for v in coords) for i in range(3)));hi=Vector(tuple(max(v[i] for v in coords) for i in range(3)))
scale=1.65/(hi.z-lo.z);center=Vector(((lo.x+hi.x)/2,(lo.y+hi.y)/2,lo.z))
for v in body.data.vertices:v.co=(body.matrix_world@v.co-center)*scale
body.matrix_world.identity()
width=max(abs(v.co.x) for v in body.data.vertices)
for v in body.data.vertices:v.co.x*=.8041/width
# A whole-object bounding-box centre is biased by the cap brim and toes.
# Register depth to the actual arm cross-section, so the hands sit on the
# original wrist axes rather than orbiting them when the shotgun pose twists.
arm_depth=[v.co.y for v in body.data.vertices if .40<abs(v.co.x)<.56 and .80<v.co.z<1.22]
reference_depth=[v.co.y for v in ref.data.vertices if .40<abs(v.co.x)<.56 and .80<v.co.z<1.22]
depth_shift=(min(reference_depth)+max(reference_depth)-min(arm_depth)-max(arm_depth))/2
for v in body.data.vertices:v.co.y+=depth_shift
# Match the horizontal arm centre to the existing 1.025m shoulder/elbow/wrist plane.
arm=[v.co.z for v in body.data.vertices if .40<abs(v.co.x)<.56 and .80<v.co.z<1.22]
mid=(min(arm)+max(arm))/2;delta=1.025-mid
for v in body.data.vertices:
    blend=max(0.,1-abs(v.co.z-mid)/.20)
    v.co.z+=delta*blend
bm=bmesh.new();bm.from_mesh(body.data);bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.000005);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(body.data);bm.free()
body.data.materials[0].name='Hotwire'
for p in body.data.polygons:p.use_smooth=True
# Barycentric interpolation of the existing body's skinning, rather than a
# fresh auto-rig. Headwear, hair and face are rigid to the existing Head bone.
for g in list(body.vertex_groups):body.vertex_groups.remove(g)
groups={b.name:body.vertex_groups.new(name=b.name) for b in rig.data.bones}
max_distance=0
for v in body.data.vertices:
    if v.co.z>1.14:result={'Head':1.}
    elif abs(v.co.x)>.225 and v.co.z>.85:
        # Uniform rings around the arms avoid irregular nearest-surface weights
        # on the new cuffs/gloves during the tightly bent shotgun poses.
        x=abs(v.co.x);side='L' if v.co.x>0 else 'R'
        def smooth(t):t=max(0,min(1,t));return t*t*(3-2*t)
        if x<.46:
            t=smooth((x-.34)/.12);result={'UpperArm.'+side:1-t,'Forearm.'+side:t}
        else:
            t=smooth((x-.565)/.06);result={'Forearm.'+side:1-t,'Hand.'+side:t}
    elif abs(v.co.x)>.635:result={'Hand.'+('L' if v.co.x>0 else 'R'):1.}
    else:
        point,normal,index,distance=tree.find_nearest(v.co);max_distance=max(max_distance,distance)
        ids=tris[index];a,b,c=[ref.data.vertices[i].co for i in ids]
        bary=barycentric_transform(point,a,b,c,Vector((1,0,0)),Vector((0,1,0)),Vector((0,0,1)))
        result={}
        for i,factor in zip(ids,bary):
            for name,weight in weights[i].items():result[name]=result.get(name,0)+max(0,factor)*weight
        result=dict(sorted(result.items(),key=lambda x:x[1],reverse=True)[:4])
    total=sum(result.values());assert total>0
    for name,value in result.items():
        if value>1e-6:groups[name].add([v.index],value/total,'REPLACE')
body.parent=rig;body.matrix_parent_inverse=rig.matrix_world.inverted()
mod=body.modifiers.new('Original thief skeleton','ARMATURE');mod.object=rig
for o in originals:bpy.data.objects.remove(o,do_unlink=True)
body.name='Hotwire_Source'
body.data.calc_loop_triangles()
np.savez_compressed(SRC/'SurfaceMapping.npz',xyz=np.array([[tuple(body.data.vertices[i].co) for i in t.vertices] for t in body.data.loop_triangles]),uv=np.array([[tuple(body.data.uv_layers.active.data[i].uv) for i in t.loops] for t in body.data.loop_triangles]))
source_triangles=sum(len(p.vertices)-2 for p in body.data.polygons)
lods=[];reports=[]
for i,budget in enumerate([10000,4000,1500,700]):
    obj=body.copy();obj.data=body.data.copy();bpy.context.collection.objects.link(obj);obj.name='thief_enemy_01'+('_LOD'+str(i) if i else '')
    bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj
    dec=obj.modifiers.new('Offline LOD reduction','DECIMATE');dec.ratio=min(1,budget/source_triangles);dec.use_collapse_triangulate=True
    while obj.modifiers.find(dec.name)>0:bpy.ops.object.modifier_move_up(modifier=dec.name)
    bpy.ops.object.modifier_apply(modifier=dec.name)
    bpy.ops.object.mode_set(mode='WEIGHT_PAINT');bpy.ops.object.vertex_group_limit_total(limit=4 if i<2 else 2);bpy.ops.object.vertex_group_normalize_all(lock_active=False);bpy.ops.object.mode_set(mode='OBJECT')
    assert all(abs(sum(g.weight for g in v.groups)-1)<.002 for v in obj.data.vertices)
    reports.append({'lod':i,'triangles':sum(len(p.vertices)-2 for p in obj.data.polygons),'vertices':len(obj.data.vertices),'max_influences':max(len(v.groups) for v in obj.data.vertices)})
    lods.append(obj)
bpy.data.objects.remove(body,do_unlink=True)
for im in bpy.data.images:
    if im.has_data and im.name not in ['Render Result','Viewer Node']:im.pack()
for i,o in enumerate(lods):o.hide_set(i!=0);o.hide_render=i!=0
bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=str(SRC/'Hotwire.blend'))
bpy.ops.object.select_all(action='DESELECT');rig.select_set(True)
for o in lods:o.hide_set(False);o.select_set(True)
bpy.context.view_layer.objects.active=rig
bpy.ops.export_scene.fbx(filepath=str(MODEL/'Hotwire.fbx'),use_selection=True,object_types={'ARMATURE','MESH'},axis_forward='-Z',axis_up='Y',add_leaf_bones=False,bake_anim=False,path_mode='AUTO',use_mesh_modifiers=True)
report={'height_m':1.65,'skeleton':'Original thief_enemy_01 22-bone rest skeleton','new_bones':0,'existing_preview_actions':len(bpy.data.actions),'gameplay_animation_source':'Unchanged thief_enemy_01.fbx / Shotgun controller','source_triangles':source_triangles,'original_surface_weight_transfer_max_distance_m':max_distance,'arm_center_correction_m':delta,'arm_depth_registration_m':depth_shift,'lods':reports}
(SRC/'ModelBuildValidation.json').write_text(json.dumps(report,indent=2));print('HOTWIRE_BUILD_COMPLETE',json.dumps(report))
