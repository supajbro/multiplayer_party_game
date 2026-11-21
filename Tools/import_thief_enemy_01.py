"""One-shot Unity material and importer setup for the saved thief model."""
from pathlib import Path
import sys, re, json, struct
sys.path.insert(0,str(Path(__file__).resolve().parent))
import import_pixal_ammunition as author

ROOT=Path(__file__).resolve().parents[1]
SRC=ROOT/'ArtSource/Characters/ThiefEnemy01'
author.TEX_DIR=ROOT/'Assets/_Project/Textures/Thieves/ThiefEnemy01'
author.MAT_DIR=ROOT/'Assets/_Project/Materials/Thieves/ThiefEnemy01'
for p in (author.TEX_DIR,author.MAT_DIR):author.folder(p)
generated=next((SRC/'Generated').rglob('*.glb'))
_,_,_,_,base,orm=author.load_glb(generated)
material=author.write_material('thief_enemy_01',base,orm)
raw=generated.read_bytes();length=struct.unpack_from('<I',raw,12)[0]
doc=json.loads(raw[20:20+length]);binary=raw[28+length:]
normal=doc['materials'][0].get('normalTexture')
if normal:
    image=doc['images'][doc['textures'][normal['index']]['source']]
    view=doc['bufferViews'][image['bufferView']];start=view.get('byteOffset',0)
    path=author.TEX_DIR/'thief_enemy_01_Normal.png'
    path.write_bytes(binary[start:start+view['byteLength']]);author.texture_meta(path,False)
    meta=Path(str(path)+'.meta');meta.write_text(meta.read_text().replace('textureType: 0','textureType: 1'))
    text=material.read_text()
    text=re.sub(r'(- _BumpMap:\n        m_Texture:) \{fileID: 0\}',r'\1 {fileID: 2800000, guid: '+author.guid(path)+', type: 3}',text)
    if '  - _NORMALMAP' not in text:text=text.replace('  m_ValidKeywords:','  m_ValidKeywords:\n  - _NORMALMAP')
    material.write_text(text)
for tex in author.TEX_DIR.glob('*.png'):
    meta=Path(str(tex)+'.meta')
    meta.write_text(meta.read_text().replace('maxTextureSize: 512','maxTextureSize: 1024'))
model=ROOT/'Assets/_Project/Models/Thieves/thief_enemy_01.fbx'
if Path(str(model)+'.meta').exists():
    print('Preserved existing FBX importer and animation settings:',model)
    sys.exit(0)
template=ROOT/'Assets/_Project/Models/Thieves/Character01Thief/Character01_Thief.fbx.meta'
text=template.read_text()
text=re.sub(r'^guid: \w+','guid: '+author.guid(model),text,count=1,flags=re.M)
text=re.sub(r'name: Character01_Thief\s+second: \{fileID: 2100000, guid: \w+, type: 2\}',
            'name: thief_enemy_01\n    second: {fileID: 2100000, guid: '+author.guid(material)+', type: 2}',text)
# Use a Generic rest-pose rig; do not copy another character's Humanoid mapping.
start=text.find('  humanDescription:')
end=text.find('\n  ',start+3)
if start>=0:
    # All humanDescription child lines use four or more spaces.
    end=re.search(r'\n  \S',text[start+3:])
    finish=start+3+end.start() if end else len(text)
    text=text[:start]+'  humanDescription:\n    serializedVersion: 3\n    human: []\n    skeleton: []\n'+text[finish:]
text=re.sub(r'animationType: \d+','animationType: 2',text)
text=re.sub(r'importAnimation: \d+','importAnimation: 0',text)
Path(str(model)+'.meta').write_text(text)
print('Unity material',material,'Model',model)
