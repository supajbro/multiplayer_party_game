"""Explicitly import completed Pixal3D GLBs as editable Unity assets.

This is an offline asset authoring step, never an import or Play-mode hook.
The generated topology and UVs are retained; only alignment, unit conversion,
texture channel packing and Unity serialization are performed here.
"""
import argparse
import hashlib
import io
import json
import re
import shutil
import struct
import uuid
from pathlib import Path

import numpy as np
from PIL import Image, ImageChops
from scipy.sparse import coo_matrix
from scipy.sparse.csgraph import connected_components

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'ArtSource/Weapons/Ammunition'
OUTPUT = Path(r'C:\Users\Jaden\AI\Pixal3D\output\Ammunition')
PROJECT = ROOT / 'Assets/_Project'
MESH_DIR = PROJECT / 'Models/Weapons/Ammunition'
MAT_DIR = PROJECT / 'Materials/Weapons/Ammunition'
TEX_DIR = PROJECT / 'Textures/Weapons/Ammunition'
PREFAB_DIR = PROJECT / 'Prefabs/Weapons/Ammunition'
NAMES = ['Shotgun_Shell', 'AssaultRifle_Cartridge', 'Pistol_Cartridge',
         'AssaultRifle_Bullet', 'Pistol_Bullet', 'Shotgun_Pellet']
HEIGHTS = {'Shotgun_Shell': .07, 'AssaultRifle_Cartridge': .057, 'Pistol_Cartridge': .03}
DIAMETER_RATIOS = {'Shotgun_Shell':2.5, 'AssaultRifle_Cartridge':4.5, 'Pistol_Cartridge':3.0}


def guid(path):
    meta = Path(str(path) + '.meta')
    if meta.exists():
        return re.search(r'^guid: (\w+)', meta.read_text(), re.M)[1]
    return uuid.uuid5(uuid.NAMESPACE_URL, 'couch-guys:pixal-ammunition:v1:' + path.relative_to(ROOT).as_posix()).hex


def asset_meta(path, file_id):
    meta = Path(str(path) + '.meta')
    if not meta.exists():
        meta.write_text(f'''fileFormatVersion: 2
guid: {guid(path)}
NativeFormatImporter:
  externalObjects: {{}}
  mainObjectFileID: {file_id}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
''')


def folder(path):
    if not path.exists():
        folder(path.parent)
        path.mkdir()
    if path != ROOT/'Assets' and path.is_relative_to(ROOT/'Assets'):
        meta = Path(str(path) + '.meta')
        if not meta.exists():
            meta.write_text(f'fileFormatVersion: 2\nguid: {guid(path)}\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n')


def texture_meta(path, srgb):
    meta = Path(str(path) + '.meta')
    if meta.exists():
        return
    meta.write_text(f'''fileFormatVersion: 2
guid: {guid(path)}
TextureImporter:
  internalIDToNameTable: []
  externalObjects: {{}}
  serializedVersion: 13
  mipmaps:
    mipMapMode: 0
    enableMipMap: 1
    sRGBTexture: {int(srgb)}
    linearTexture: {int(not srgb)}
    fadeOut: 0
    borderMipMap: 0
    mipMapsPreserveCoverage: 0
    alphaTestReferenceValue: 0.5
    mipMapFadeDistanceStart: 1
    mipMapFadeDistanceEnd: 3
  isReadable: 0
  streamingMipmaps: 0
  streamingMipmapsPriority: 0
  vTOnly: 0
  ignoreMipmapLimit: 0
  textureFormat: 1
  maxTextureSize: 512
  textureSettings:
    serializedVersion: 2
    filterMode: 1
    aniso: 1
    mipBias: 0
    wrapU: 0
    wrapV: 0
    wrapW: 0
  nPOTScale: 0
  lightmap: 0
  compressionQuality: 50
  spriteMode: 0
  textureType: 0
  textureShape: 1
  alphaSource: 1
  alphaIsTransparency: 0
  platformSettings: []
  userData: Pixal3D generated ammunition texture
  assetBundleName: 
  assetBundleVariant: 
''')


def load_glb(path):
    raw = path.read_bytes()
    magic, version, total = struct.unpack_from('<4sII', raw)
    assert magic == b'glTF' and version == 2 and total == len(raw)
    length, kind = struct.unpack_from('<I4s', raw, 12)
    assert kind == b'JSON'
    doc = json.loads(raw[20:20+length])
    binary_length, kind = struct.unpack_from('<I4s', raw, 20+length)
    assert kind == b'BIN\0'
    binary = raw[28+length:28+length+binary_length]
    assert len(doc['meshes']) == 1 and len(doc['meshes'][0]['primitives']) == 1
    assert all(set(n) <= {'mesh', 'name'} for n in doc['nodes']), 'Unexpected GLB transform; inspect before import'

    def accessor(index):
        a = doc['accessors'][index]
        v = doc['bufferViews'][a['bufferView']]
        dtype = {5126:'<f4', 5125:'<u4', 5123:'<u2', 5121:'u1'}[a['componentType']]
        components = {'SCALAR':1, 'VEC2':2, 'VEC3':3, 'VEC4':4}[a['type']]
        offset = v.get('byteOffset', 0) + a.get('byteOffset', 0)
        itemsize = np.dtype(dtype).itemsize
        return np.ndarray((a['count'],components),dtype=dtype,buffer=binary,offset=offset,
                          strides=(v.get('byteStride',components*itemsize),itemsize)).copy()

    primitive = doc['meshes'][0]['primitives'][0]
    attributes = primitive['attributes']
    p = accessor(attributes['POSITION']).astype(np.float64)
    n = accessor(attributes['NORMAL']).astype(np.float64)
    uv = accessor(attributes['TEXCOORD_0']).astype(np.float64)
    faces = accessor(primitive['indices']).reshape(-1,3).astype(np.int64)
    material = doc['materials'][primitive.get('material',0)]

    def embedded_image(texture_index):
        image = doc['images'][doc['textures'][texture_index]['source']]
        view = doc['bufferViews'][image['bufferView']]
        start = view.get('byteOffset',0)
        return binary[start:start+view['byteLength']]
    pbr = material['pbrMetallicRoughness']
    return p, n, uv, faces, embedded_image(pbr['baseColorTexture']['index']), embedded_image(pbr['metallicRoughnessTexture']['index'])


def align(p, n, name):
    p -= p.mean(axis=0)
    if name != 'Shotgun_Pellet':
        _, axes = np.linalg.eigh(np.cov(p.T))
        axis = axes[:,-1]
        if axis[1] < 0:
            axis = -axis
        target = np.array([0.,1.,0.])
        cross = np.cross(axis,target)
        cosine = float(np.dot(axis,target))
        skew = np.array([[0.,-cross[2],cross[1]],[cross[2],0.,-cross[0]],[-cross[1],cross[0],0.]])
        rotation = np.eye(3) + skew + skew@skew/(1+cosine)
        p = p@rotation.T
        n = n@rotation.T
    # Source is GLTF Y-up. Unity ammunition stands Y-up, flying visuals point +Z.
    projectile = name.endswith(('Bullet','Pellet'))
    basis = np.array([[1,0,0],[0,0,1],[0,1,0]]) if projectile else np.diag([1,1,-1])
    p, n = p@basis.T, n@basis.T
    bounds = np.ptp(p,axis=0)
    if projectile:
        p -= (p.min(axis=0)+p.max(axis=0))*.5
        scales = 1/np.maximum(bounds,1e-8)
        p *= scales
        n /= scales
    else:
        p *= HEIGHTS[name]/bounds[1]
        # A single reference leaves the hidden depth ambiguous. Correct the
        # generated cross-section to a round cartridge rather than retaining
        # a flattened or over-extruded reconstruction. Topology and UVs stay
        # unchanged. Use the intended prop proportions rather than the image's
        # screen-space bounding box, which changes when the reference is tilted.
        diameter=HEIGHTS[name]/DIAMETER_RATIOS[name]
        cross_scales=diameter/np.maximum(np.ptp(p,axis=0)[[0,2]],1e-8)
        p[:,[0,2]]*=cross_scales
        n[:,[0,2]]/=cross_scales
        p[:,0] -= (p[:,0].min()+p[:,0].max())*.5
        p[:,2] -= (p[:,2].min()+p[:,2].max())*.5
        p[:,1] -= p[:,1].min()
    n /= np.maximum(np.linalg.norm(n,axis=1,keepdims=True),1e-8)
    assert np.isfinite(p).all() and np.isfinite(n).all()
    return p,n


def remove_small_fragments(p,n,uv,faces):
    # UV seams duplicate vertices. Weld only for component analysis, keeping
    # the original UV vertices and generated topology in the exported mesh.
    welded,inverse=np.unique(np.round(p,5),axis=0,return_inverse=True)
    triangles=inverse[faces]
    edges=np.concatenate([triangles[:,[0,1]],triangles[:,[1,2]],triangles[:,[2,0]]])
    graph=coo_matrix((np.ones(len(edges)),(edges[:,0],edges[:,1])),shape=(len(welded),len(welded)))
    _,labels=connected_components(graph,directed=False)
    counts=np.bincount(labels[triangles[:,0]])
    keep=counts[labels[triangles[:,0]]] >= max(1,int(counts.max()*.08))
    faces=faces[keep]
    used,remapping=np.unique(faces,return_inverse=True)
    return p[used],n[used],uv[used],remapping.reshape(-1,3)


def write_mesh(path, p, n, uv, faces):
    # Reflection into Unity coordinates reverses winding; keep front faces and
    # vertex normals consistent. GLTF's top-left UV origin also needs V flipped.
    faces = faces[:,[0,2,1]]
    uv[:,1] = 1-uv[:,1]
    vertices = np.concatenate([p,n,uv],axis=1).astype('<f4')
    assert len(p) < 65536 and faces.min() >= 0 and faces.max() < len(p)
    indices = faces.astype('<u2').tobytes().hex()
    vmin, vmax = p.min(axis=0), p.max(axis=0)
    center, extent = (vmin+vmax)/2, (vmax-vmin)/2
    vector = lambda a: '{x: %.9g, y: %.9g, z: %.9g}' % tuple(a)
    template = (PROJECT/'Prefabs/Weapons/RocketLauncher/Meshes/BlastSpike.asset').read_text()
    template = re.sub(r'^  m_Name: .*', '  m_Name: '+path.stem, template,flags=re.M)
    template = re.sub(r'^    indexCount: \d+', f'    indexCount: {faces.size}',template,flags=re.M)
    template = re.sub(r'^    vertexCount: \d+', f'    vertexCount: {len(p)}',template,flags=re.M)
    template = re.sub(r'm_Center: \{[^}]+\}', 'm_Center: '+vector(center),template)
    template = re.sub(r'm_Extent: \{[^}]+\}', 'm_Extent: '+vector(extent),template)
    template = re.sub(r'^  m_IndexBuffer: .*', '  m_IndexBuffer: '+indices,template,flags=re.M)
    channels=[]
    for i in range(14):
        dimension,offset = (3,0) if i==0 else (3,12) if i==1 else (2,24) if i==4 else (0,0)
        channels.append(f'    - stream: 0\n      offset: {offset}\n      format: 0\n      dimension: {dimension}\n')
    data=f'''  m_VertexData:
    serializedVersion: 3
    m_VertexCount: {len(p)}
    m_Channels:
{''.join(channels)}    m_DataSize: {vertices.nbytes}
    _typelessdata: {vertices.tobytes().hex()}
'''
    template=re.sub(r'  m_VertexData:\n.*?(?=  m_CompressedMesh:)',lambda _:data,template,flags=re.S)
    template=re.sub(r'm_IndexCount: \d+',f'm_IndexCount: {faces.size}',template)
    path.write_text(template,encoding='utf-8')
    asset_meta(path,4300000)


def write_material(name, base, orm):
    albedo_path=TEX_DIR/(name+'_BaseColor.png')
    packed_path=TEX_DIR/(name+'_MetallicOcclusionSmoothness.png')
    albedo_path.write_bytes(base)
    texture_meta(albedo_path,True)
    generated=Image.open(io.BytesIO(orm)).convert('RGB')
    occlusion,roughness,metallic=generated.split()
    packed=Image.merge('RGBA',(metallic,occlusion,Image.new('L',generated.size,0),ImageChops.invert(roughness)))
    packed.save(packed_path)
    texture_meta(packed_path,False)
    path=MAT_DIR/(name+'.mat')
    if not path.exists():
        text=(PROJECT/'Materials/Vehicles/ThiefTruck/TT01_Silver.mat').read_text()
        text=re.sub(r'^  m_Name: TT01_Silver$', '  m_Name: '+name,text,flags=re.M)
        text=text.replace('m_ValidKeywords: []','m_ValidKeywords:\n  - _METALLICSPECGLOSSMAP\n  - _OCCLUSIONMAP')
        for prop,texture in [('_BaseMap',albedo_path),('_MainTex',albedo_path),('_MetallicGlossMap',packed_path),('_OcclusionMap',packed_path)]:
            text=re.sub(r'(- '+prop+r':\n        m_Texture:) \{fileID: 0\}',r'\1 {fileID: 2800000, guid: '+guid(texture)+', type: 3}',text)
        for prop in ('_BaseColor','_Color'):
            text=re.sub(r'(- '+prop+r':) \{[^}]+\}',r'\1 {r: 1, g: 1, b: 1, a: 1}',text)
        for prop in ('_Metallic','_Smoothness','_GlossMapScale'):
            text=re.sub(r'(- '+prop+r':) [^\n]+',r'\1 1',text)
        text=text.replace('m_EnableInstancingVariants: 0','m_EnableInstancingVariants: 1')
        path.write_text(text,encoding='utf-8')
        asset_meta(path,2100000)
    return path


def replace_visuals(text, mesh, material):
    text=re.sub(r'(\nMeshFilter:\n.*?  m_Mesh:) \{[^}]+\}',r'\1 {fileID: 4300000, guid: '+guid(mesh)+', type: 2}',text,count=1,flags=re.S)
    match=re.search(r'\nMeshRenderer:\n.*?(?=\n---|\Z)',text,re.S)
    block=match[0]
    block=re.sub(r'(  m_Materials:\n  -) \{[^}]+\}',r'\1 {fileID: 2100000, guid: '+guid(material)+', type: 2}',block,count=1)
    return text[:match.start()]+block+text[match.end():]


def write_prefab(name, mesh, material):
    if name.endswith(('Bullet','Pellet')):
        path=PROJECT/'Prefabs/Weapons/Projectiles'/(name+'.prefab')
        text=replace_visuals(path.read_text(),mesh,material)
        path.write_text(text,encoding='utf-8')
    else:
        path=PREFAB_DIR/(name+'.prefab')
        if path.exists():
            return path
        source=(PROJECT/'Prefabs/Weapons/Projectiles/Pistol_Bullet.prefab').read_text()
        blocks=re.findall(r'--- !u!(\d+) &(\d+)\n.*?(?=\n---|\Z)',source,re.S)
        ids={kind:int(file_id) for kind,file_id in blocks if kind in ('1','4','33','23')}
        pieces=re.findall(r'--- !u!(?:1|4|33|23) &\d+\n.*?(?=\n---|\Z)',source,re.S)
        text='%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n'+'\n'.join(pieces)+'\n'
        allowed=set(ids.values())
        text=re.sub(r'^  - component: \{fileID: (\d+)\}\n',lambda m:m[0] if int(m[1]) in allowed else '',text,flags=re.M)
        text=text.replace('m_Name: Pistol_Bullet','m_Name: '+name)
        text=replace_visuals(text,mesh,material)
        text=text.replace('m_CastShadows: 0','m_CastShadows: 1').replace('m_ReceiveShadows: 0','m_ReceiveShadows: 1')
        path.write_text(text,encoding='utf-8')
        meta=Path(str(path)+'.meta')
        meta.write_text(f'fileFormatVersion: 2\nguid: {guid(path)}\nPrefabImporter:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n')
    return path


def import_asset(name):
    candidates=sorted(OUTPUT.glob(name+'_*.glb'))
    if not candidates:
        raise FileNotFoundError(name+' has not completed generation')
    selection_path=SOURCE/'SourceSelection.json'
    selections=json.loads(selection_path.read_text()) if selection_path.exists() else {}
    generated=OUTPUT/selections[name]['file'] if name in selections else candidates[-1]
    generated.resolve().relative_to(OUTPUT.resolve())
    p,n,uv,faces,base,orm=load_glb(generated)
    p,n,uv,faces=remove_small_fragments(p,n,uv,faces)
    p,n=align(p,n,name)
    mesh=MESH_DIR/(name+'.asset')
    write_mesh(mesh,p,n,uv,faces)
    material=write_material(name,base,orm)
    prefab=write_prefab(name,mesh,material)
    (SOURCE/'Generated').mkdir(exist_ok=True)
    shutil.copy2(generated,SOURCE/'Generated'/(name+'.glb'))
    preview_stem=generated.stem.rsplit('_',1)[0]
    exact_preview=OUTPUT/'Previews'/(generated.stem+'_.png')
    previews=[exact_preview] if exact_preview.exists() else sorted((OUTPUT/'Previews').glob(preview_stem+'_*.png'))
    if previews:
        (SOURCE/'Previews').mkdir(exist_ok=True)
        shutil.copy2(previews[-1],SOURCE/'Previews'/(name+'.png'))
    return {'name':name,'generator':'Pixal3D native ComfyUI, local INT8',
            'source_sha256':hashlib.sha256(generated.read_bytes()).hexdigest(),
            'vertices':len(p),'triangles':len(faces),
            'bounds_min':p.min(axis=0).tolist(),'bounds_max':p.max(axis=0).tolist(),
            'mesh':mesh.relative_to(ROOT).as_posix(), 'material':material.relative_to(ROOT).as_posix(),
            'prefab':prefab.relative_to(ROOT).as_posix()}


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--names',nargs='+',choices=NAMES,default=NAMES)
    args=parser.parse_args()
    for path in (MESH_DIR,MAT_DIR,TEX_DIR,PREFAB_DIR):
        folder(path)
    report_path=SOURCE/'AssetManifest.json'
    report={a['name']:a for a in json.loads(report_path.read_text())} if report_path.exists() else {}
    for name in args.names:
        report[name]=import_asset(name)
        print(json.dumps(report[name]),flush=True)
    report_path.write_text(json.dumps(list(report.values()),indent=2),encoding='utf-8')


if __name__=='__main__':
    main()
