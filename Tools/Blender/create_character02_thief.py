"""Explicit, one-shot Character02 model authoring; never runs on Unity import."""
from pathlib import Path
import bpy
import json
import re
import shutil
import uuid
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
SRC = ROOT / 'ArtSource/Characters/Character01Thief'
MASTER = ROOT / 'ArtSource/Characters/Character02Thief'
OUT = ROOT / 'Assets/_Project/Models/Thieves/Character02Thief'
PACK = ROOT / 'Assets/ThirdParty/Hodaart/HodaartLowPolyCharacterCollection3'
MASTER.mkdir(parents=True, exist_ok=True)
OUT.mkdir(parents=True, exist_ok=True)

def guid(): return uuid.uuid4().hex
def meta(path, text):
    target=Path(str(path)+'.meta')
    if target.exists():
        existing=re.search(r'^guid: (\w+)',target.read_text(),re.M)
        if existing:text=re.sub(r'^guid: \w+','guid: '+existing[1],text,count=1,flags=re.M)
    target.write_text(text, encoding='utf-8')
def folder(path):
    path.mkdir(parents=True, exist_ok=True)
    meta(path, f'fileFormatVersion: 2\nguid: {guid()}\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n')

folder(OUT)
folder(OUT/'Armed')
palette = OUT/'Character02_Thief_Palette.png'
shutil.copyfile(ROOT/'Assets/_Project/Models/Thieves/Character01Thief/Character01_Thief_Palette.png', palette)
palette_guid=re.search(r'^guid: (\w+)',Path(str(palette)+'.meta').read_text(),re.M)[1] if Path(str(palette)+'.meta').exists() else guid()
source_meta=Path(str(ROOT/'Assets/_Project/Models/Thieves/Character01Thief/Character01_Thief_Palette.png')+'.meta').read_text()
meta(palette, re.sub(r'guid: [a-f0-9]{32}', 'guid: '+palette_guid, source_meta, count=1))
mat_guid=re.search(r'^guid: (\w+)',(OUT/'Character02_Thief.mat.meta').read_text(),re.M)[1] if (OUT/'Character02_Thief.mat.meta').exists() else guid()
mat_source=ROOT/'Assets/_Project/Models/Thieves/Character01Thief/Character01_Thief.mat'
old_palette=re.search(r'^guid: (\w+)',Path(str(mat_source.parent/'Character01_Thief_Palette.png')+'.meta').read_text(),re.M)[1]
(OUT/'Character02_Thief.mat').write_text(mat_source.read_text().replace('Character01_Thief','Character02_Thief').replace(old_palette,palette_guid))
meta(OUT/'Character02_Thief.mat',f'fileFormatVersion: 2\nguid: {mat_guid}\nNativeFormatImporter:\n  externalObjects: {{}}\n  mainObjectFileID: 2100000\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n')

bpy.ops.wm.open_mainfile(filepath=str(SRC/'Character01_Thief.blend'))
template_rig=bpy.data.objects['Character01_Thief_Rig']
# Retain the original, editable accessory meshes and their palette UVs.
accessories=[]
for obj in bpy.context.scene.objects:
    if obj.type=='MESH' and obj.name.startswith('Thief_'):
        accessories.append((obj.name,obj.data.copy(),[g.name for g in obj.vertex_groups]))
eye_uv={o.name.replace('Original_',''):[tuple(x.uv) for x in o.data.uv_layers.active.data] for o in bpy.data.objects if o.name.startswith('Original_')}
material=bpy.data.materials['Character01_Thief'].copy()
material.name='Character02_Thief'
material.use_fake_user=True
for node in material.node_tree.nodes:
    if node.type=='TEX_IMAGE': node.image=bpy.data.images.load(str(palette),check_existing=True)
# Derive actual palette swatches from the existing recolored body.
body01=bpy.data.objects['Character01_Thief_Body']
pixels=list(next(n.image for n in material.node_tree.nodes if n.type=='TEX_IMAGE').pixels)
def nearest_swatch(rgb):
    candidates=[]
    for y in range(16):
        for x in range(16):
            i=((y*16+8)*256+x*16+8)*4
            candidates.append((sum((pixels[i+k]-rgb[k])**2 for k in range(3)),((x+.5)/16,(y+.5)/16)))
    return min(candidates)[1]
colors={n:nearest_swatch(c) for n,c in {'skin':(.72,.48,.32),'jacket':(.12,.14,.17),'trim':(.055,.065,.075),'pants':(.07,.085,.10),'white':(.7,.72,.7)}.items()}
old_objects=list(bpy.data.objects)
bpy.ops.import_scene.fbx(filepath=str(PACK/'Models/Character 02/Character 02 T-Pose.fbx'))
new=[o for o in bpy.data.objects if o not in old_objects]
rig=next(o for o in new if o.type=='ARMATURE'); body=next(o for o in new if o.type=='MESH')
rig.name='Character02_Thief_Rig'; body.name='Character02_Thief_Body'
rig.data.name='Character02_Thief_Skeleton'
original_positions=[tuple(v.co) for v in body.data.vertices]
original_weights=[[(g.group,g.weight) for g in v.groups] for v in body.data.vertices]
body.data.materials.clear(); body.data.materials.append(material)
# Connected source pieces identify garments without changing source geometry or weights.
adj={v.index:set() for v in body.data.vertices}
for e in body.data.edges:
    a,b=e.vertices;adj[a].add(b);adj[b].add(a)
components={};seen=set()
for v in body.data.vertices:
    if v.index in seen: continue
    todo=[v.index]; seen.add(v.index); component=[]
    while todo:
        i=todo.pop();component.append(i)
        for j in adj[i]-seen: seen.add(j);todo.append(j)
    for i in component: components[i]=len(component)
uv=body.data.uv_layers.active
for poly in body.data.polygons:
    size=components[poly.vertices[0]]
    p=body.matrix_world @ poly.center
    groups={}
    for i in poly.vertices:
        for g in body.data.vertices[i].groups:
            name=body.vertex_groups[g.group].name;groups[name]=groups.get(name,0)+g.weight
    dominant=max(groups,key=groups.get)
    color='skin'
    if size==958:
        color='trim' if 'Hand' in dominant else ('skin' if 'ForeArm' in dominant else 'jacket')
    elif size==298: color='pants'
    elif size==312: color='trim'
    elif size in (36,79): color='trim'
    elif size==218: color='white' if p.z<.025 else 'trim'
    elif size==30: color='trim'
    for i in poly.loop_indices: uv.data[i].uv=colors[color]

before=set(bpy.data.objects)
bpy.ops.import_scene.fbx(filepath=str(PACK/'Models/Character 02/Character 02 Head Parts.fbx'))
parts=[o for o in bpy.data.objects if o not in before and o.type=='MESH']
for o in parts:
    name=o.name.split('.')[0]
    o.data.materials.clear();o.data.materials.append(material)
    if name in eye_uv and len(o.data.uv_layers.active.data)==len(eye_uv[name]):
        for u,v in zip(o.data.uv_layers.active.data,eye_uv[name]): u.uv=v
    for mod in list(o.modifiers):o.modifiers.remove(mod)
    o.vertex_groups.clear();o.vertex_groups.new(name='mixamorig:Head').add(list(range(len(o.data.vertices))),1,'REPLACE')
    mod=o.modifiers.new('Thief skin','ARMATURE');mod.object=rig
    world=o.matrix_world.copy();o.parent=rig;o.matrix_world=world

for name,mesh,groups in accessories:
    o=bpy.data.objects.new(name,mesh);bpy.context.collection.objects.link(o)
    bone=groups[0];o.vertex_groups.new(name=bone).add(list(range(len(mesh.vertices))),1,'REPLACE')
    for v in mesh.vertices:
        if bone=='mixamorig:Head':
            if 'Mask' in name:
                v.co.x*=1.42;v.co.y*=1.12;v.co.z-=.025
            else:
                v.co.x*=1.53;v.co.y*=1.28;v.co.z+=.035
        else:
            v.co.x*=1.12;v.co.y*=1.22;v.co.z+=.09
    mesh.materials.clear();mesh.materials.append(material)
    mod=o.modifiers.new('Thief skin','ARMATURE');mod.object=rig
    parts.append(o)
for o in old_objects:
    bpy.data.objects.remove(o,do_unlink=True)
# Remove the additional head-parts rig; the supplied facial geometry follows this rig.
for o in list(bpy.data.objects):
    if o.type=='ARMATURE' and o!=rig:bpy.data.objects.remove(o,do_unlink=True)
assert original_positions==[tuple(v.co) for v in body.data.vertices]
assert original_weights==[[(g.group,g.weight) for g in v.groups] for v in body.data.vertices]
bpy.ops.object.select_all(action='DESELECT')
for o in [body]+parts:o.select_set(True)
bpy.context.view_layer.objects.active=body
bpy.ops.object.join();body.name='Character02_Thief'
rig.animation_data_create()
bpy.context.scene.render.fps=30
bpy.context.preferences.filepaths.save_version=0
base_objects=[rig,body]
base_path=MASTER/'Character02_Thief_Base.blend'
bpy.ops.wm.save_as_mainfile(filepath=str(base_path))
validation={}

for variant in ('Pistol','AssaultRifle','Shotgun'):
    bpy.ops.wm.open_mainfile(filepath=str(base_path))
    rig=bpy.data.objects['Character02_Thief_Rig'];body=bpy.data.objects['Character02_Thief']
    with bpy.data.libraries.load(str(SRC/f'Armed/Character01_Thief_{variant}.blend'),link=False) as (src,dst):
        dst.objects=[n for n in src.objects if n=='Character01_Thief_Rig' or n.startswith('Weapon_')]
        dst.actions=src.actions
    old=next(o for o in dst.objects if o.type=='ARMATURE')
    # Carry over the weapon socket and animation auxiliary bones in the matching hand space.
    bpy.context.view_layer.objects.active=rig;rig.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT')
    for b in old.data.bones:
        if b.name not in rig.data.edit_bones:
            parent=rig.data.edit_bones.get(b.parent.name) if b.parent else None
            transform=parent.matrix @ b.parent.matrix_local.inverted() if parent else old.matrix_world.copy().inverted()@rig.matrix_world
            e=rig.data.edit_bones.new(b.name);e.head=transform@b.head_local;e.tail=transform@b.tail_local
            e.align_roll(transform.to_3x3()@b.matrix_local.to_3x3().col[2]);e.parent=parent;e.use_deform=b.use_deform
    bpy.ops.object.mode_set(mode='OBJECT')
    weapons=[]
    for o in dst.objects:
        if o.type!='MESH':continue
        bpy.context.collection.objects.link(o)
        bone=o.vertex_groups[0].name
        transform=rig.matrix_world @ rig.data.bones[bone].matrix_local @ old.data.bones[bone].matrix_local.inverted() @ old.matrix_world.inverted()
        for v in o.data.vertices:v.co=transform@v.co
        for m in o.modifiers:
            if m.type=='ARMATURE':m.object=rig
        weapons.append(o)
    bpy.data.objects.remove(old,do_unlink=True)
    for a in bpy.data.actions:a.use_fake_user=True
    actions={a.name:a for a in dst.actions}
    assert set(actions)=={'Thief_Idle','Thief_Walk','Thief_Shoot','Thief_Death','Thief_Frustration'}
    rig.animation_data_create();rig.animation_data.action=actions['Thief_Idle']
    rig.animation_data.action_slot=actions['Thief_Idle'].slots[0]
    bpy.context.scene.frame_set(1)
    rig.animation_data.action=None
    for bone in rig.pose.bones:bone.matrix_basis.identity()
    bpy.context.view_layer.update()
    target=MASTER/'Armed'/f'Character02_Thief_{variant}.blend';target.parent.mkdir(exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(target))
    def export(path, objs):
        bpy.ops.object.select_all(action='DESELECT')
        for o in objs:o.select_set(True)
        bpy.context.view_layer.objects.active=rig
        bpy.ops.export_scene.fbx(filepath=str(path),use_selection=True,object_types={'ARMATURE','MESH','EMPTY'},axis_forward='-Z',axis_up='Y',add_leaf_bones=False,bake_anim=True,bake_anim_use_all_actions=True,bake_anim_use_nla_strips=False,bake_anim_simplify_factor=0,path_mode='AUTO')
    path=OUT/'Armed'/f'Character02_Thief_{variant}.fbx';export(path,[rig,body]+weapons)
    old_meta=(ROOT/f'Assets/_Project/Models/Thieves/Character01Thief/Armed/Character01_Thief_{variant}.fbx.meta').read_text()
    old_mat=re.search(r'name: Character01_Thief\s+second: \{fileID: 2100000, guid: (\w+)',old_meta)[1]
    new_meta=re.sub(r'^guid: \w+', 'guid: '+guid(),old_meta, count=1,flags=re.M).replace(old_mat,mat_guid).replace('Character01_Thief','Character02_Thief')
    meta(path,new_meta)
    validation[variant]={'actions':{n:list(a.frame_range) for n,a in actions.items()},'bones':len(rig.data.bones)}
    if variant=='Pistol':
        # Also provide the unarmed body with the complete animation set.
        for o in weapons:bpy.data.objects.remove(o,do_unlink=True)
        export(OUT/'Character02_Thief.fbx',[rig,body])
        bpy.ops.wm.save_as_mainfile(filepath=str(MASTER/'Character02_Thief.blend'))
        meta(OUT/'Character02_Thief.fbx',re.sub(r'^guid: \w+', 'guid: '+guid(),new_meta,count=1,flags=re.M))

(MASTER/'Validation.json').write_text(json.dumps({'source_body_vertices_preserved':True,'source_body_weights_preserved':True,'prefabs_created':0,'variants':validation},indent=2))
print('CHARACTER02 COMPLETE',json.dumps(validation))
