"""Create separate Hotwire texture/material/model metadata, preserving originals."""
from pathlib import Path
import json,struct,re,uuid,io
from PIL import Image
import numpy as np
ROOT=Path(__file__).resolve().parents[1];SRC=ROOT/'ArtSource/Characters/Hotwire'
def guid(path):
    meta=Path(str(path)+'.meta')
    if meta.exists():return re.search(r'^guid: (\w+)',meta.read_text(),re.M)[1]
    return uuid.uuid5(uuid.NAMESPACE_URL,'couch-guys:hotwire:v1:'+path.relative_to(ROOT).as_posix()).hex
def folder(path):
    if not path.exists():folder(path.parent);path.mkdir()
    meta=Path(str(path)+'.meta')
    if path.is_relative_to(ROOT/'Assets') and not meta.exists():meta.write_text(f'fileFormatVersion: 2\nguid: {guid(path)}\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n')
TEX=ROOT/'Assets/_Project/Textures/Thieves/Hotwire';MAT=ROOT/'Assets/_Project/Materials/Thieves/Hotwire';MODEL=ROOT/'Assets/_Project/Models/Thieves/Hotwire'
for p in [TEX,MAT,MODEL]:folder(p)
raw=next((SRC/'Generated').rglob('*.glb')).read_bytes();length=struct.unpack_from('<I',raw,12)[0];doc=json.loads(raw[20:20+length]);binary=raw[28+length:]
def extract(index):
    im=doc['images'][doc['textures'][index]['source']];v=doc['bufferViews'][im['bufferView']];start=v.get('byteOffset',0)
    return Image.open(io.BytesIO(binary[start:start+v['byteLength']]))
m=doc['materials'][0];pbr=m['pbrMetallicRoughness']
extract(pbr['baseColorTexture']['index']).convert('RGB').save(TEX/'Hotwire_BaseColor.png')
orm=np.asarray(extract(pbr['metallicRoughnessTexture']['index']).convert('RGB'))
packed=np.zeros((*orm.shape[:2],4),np.uint8);packed[:,:,0]=orm[:,:,2];packed[:,:,1]=orm[:,:,0];packed[:,:,3]=255-orm[:,:,1]
Image.fromarray(packed).save(TEX/'Hotwire_MetallicOcclusionSmoothness.png')
normal=m.get('normalTexture')
if normal:extract(normal['index']).save(TEX/'Hotwire_Normal.png')
else:Image.new('RGB',(1024,1024),(128,128,255)).save(TEX/'Hotwire_Normal.png')
old=ROOT/'Assets/_Project/Textures/Thieves/ThiefEnemy01'
for p in TEX.glob('*.png'):
    source=old/('thief_enemy_01_'+p.stem.split('Hotwire_')[1]+'.png.meta')
    text=source.read_text();text=re.sub(r'^guid: \w+','guid: '+guid(p),text,flags=re.M);text=re.sub(r'  userData:.*','  userData: Hotwire approved concept / local Pixal3D',text)
    Path(str(p)+'.meta').write_text(text)
template=ROOT/'Assets/_Project/Materials/Thieves/ThiefEnemy01/thief_enemy_01.mat';text=template.read_text().replace('m_Name: thief_enemy_01','m_Name: Hotwire')
for p in TEX.glob('*.png'):
    original=old/('thief_enemy_01_'+p.name.split('Hotwire_')[1]+'.meta');oldguid=re.search(r'^guid: (\w+)',original.read_text(),re.M)[1];text=text.replace(oldguid,guid(p))
material=MAT/'Hotwire.mat';material.write_text(text)
Path(str(material)+'.meta').write_text(f'fileFormatVersion: 2\nguid: {guid(material)}\nNativeFormatImporter:\n  externalObjects: {{}}\n  mainObjectFileID: 2100000\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n')
model=MODEL/'Hotwire.fbx';template=ROOT/'Assets/_Project/Models/Thieves/thief_enemy_01.fbx.meta';text=template.read_text()
text=re.sub(r'^guid: \w+','guid: '+guid(model),text,flags=re.M)
text=text.replace('name: thief_enemy_01','name: Hotwire').replace('24b14abf9ce1537684a313ce7a275366',guid(material))
text=re.sub(r'^    clipAnimations:\n.*?(?=^  \S)', '    clipAnimations: []\n',text,flags=re.M|re.S)
text=re.sub(r'^  importAnimation: \d+','  importAnimation: 0',text,flags=re.M)
# Hotwire supplies meshes and bind poses; the saved shotgun controller retains
# every original clip and its existing avatar on the unchanged scene skeleton.
Path(str(model)+'.meta').write_text(text)
print('Imported separate Hotwire maps, material and model metadata; original assets retained.')
