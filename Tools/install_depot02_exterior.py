"""Install explicitly authored exterior assets without reserializing the depot interior."""
import re, shutil, json
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
STAGE=ROOT/'Temp/Depot02ExteriorConversion'
assert (STAGE/'validation.txt').read_text().startswith('PASS:')
relative=Path('Assets/_Project/Generated/Depot02/Exterior')
shutil.copytree(STAGE/relative,ROOT/relative,dirs_exist_ok=True)
foldermeta=STAGE/Path(str(relative)+'.meta')
if foldermeta.exists():shutil.copy2(foldermeta,ROOT/Path(str(relative)+'.meta'))
prefab=ROOT/'Assets/_Project/Prefabs/World/depot_02.prefab'
text=prefab.read_text()
assert 'm_Name: Depot02_ExteriorDetails' not in text, 'Exterior already installed; use explicit mesh asset updates'
blocks=re.split(r'(?=--- !u!)',text)
source=(STAGE/'Assets/ExteriorDetails.prefab').read_text().replace('m_Name: ExteriorDetails\n','m_Name: Depot02_ExteriorDetails\n')
extra=re.split(r'(?=--- !u!)',source)[1:]
ids=lambda t:set(re.findall(r'^--- !u!\d+ &(-?\d+)',t,re.M))
assert not ids(text)&ids(source), 'Serialized ID collision'
rootblock=next(b for b in extra if 'm_Name: Depot02_ExteriorDetails\n' in b)
rootgo=re.search(r'&(-?\d+)',rootblock)[1]
rt=next(b for b in extra if b.startswith('--- !u!4 ') and f'm_GameObject: {{fileID: {rootgo}}}' in b)
rootid=re.search(r'&(-?\d+)',rt)[1]
visualgo=next(b for b in blocks if 'm_Name: Depot02_Visuals\n' in b)
visualgoid=re.search(r'&(-?\d+)',visualgo)[1]
vt=next(b for b in blocks if b.startswith('--- !u!4 ') and f'm_GameObject: {{fileID: {visualgoid}}}' in b)
visualid=re.search(r'&(-?\d+)',vt)[1]
newvt=vt.replace('  m_Children:\n',f'  m_Children:\n  - {{fileID: {rootid}}}\n',1)
newrt=rt.replace('  m_Father: {fileID: 0}',f'  m_Father: {{fileID: {visualid}}}')
extra[extra.index(rt)]=newrt
result=text.replace(vt,newvt,1)+''.join(extra)
# Every original serialized object must remain byte-equivalent, except the single child-list insertion.
resultblocks=re.split(r'(?=--- !u!)',result)
for block in blocks[1:]:
    expected=newvt if block==vt else block
    assert expected in resultblocks, 'Original prefab object modified'
prefab.write_text(result,newline='\n')
report=ROOT/'ArtSource/Depot02/depot_02_exterior_validation.json'
data=json.loads(report.read_text());data['unityValidation']=(STAGE/'validation.txt').read_text()
data['preservedPrefabObjects']=len(blocks)-1
data['prefabChange']='Added exterior child; all original serialized objects unchanged except parent child list.'
report.write_text(json.dumps(data,indent=2))
print(data['unityValidation']);print('Preserved prefab objects:',len(blocks)-1)
