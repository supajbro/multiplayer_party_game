"""Replace only the interior logo hierarchy, adding saved interior detail meshes."""
import re, shutil, json
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
STAGE=ROOT/'Temp/Depot02InteriorConversion'
assert (STAGE/'validation.txt').read_text().startswith('PASS:')
relative=Path('Assets/_Project/Generated/Depot02/Interior')
shutil.copytree(STAGE/relative,ROOT/relative,dirs_exist_ok=True)
shutil.copy2(STAGE/Path(str(relative)+'.meta'),ROOT/Path(str(relative)+'.meta'))
prefab=ROOT/'Assets/_Project/Prefabs/World/depot_02.prefab'
text=prefab.read_text()
assert 'm_Name: Depot02_InteriorDetails' not in text
blocks=re.split(r'(?=--- !u!)',text)
blockid=lambda b:re.search(r'^--- !u!\d+ &(-?\d+)',b)[1]
byid={blockid(b):b for b in blocks[1:]}
go=next(b for b in blocks if 'm_Name: D02_BrandSign\n' in b)
removed=set()
def remove_go(gid):
    g=byid[gid];removed.add(gid)
    comps=re.findall(r'component: \{fileID: (-?\d+)\}',g)
    removed.update(comps)
    for cid in comps:
        b=byid[cid]
        if b.startswith('--- !u!4 '):
            childtext=b.split('  m_Children:')[1].split('  m_Father:')[0]
            for tid in re.findall(r'fileID: (-?\d+)',childtext):
                childgo=re.search(r'm_GameObject: \{fileID: (-?\d+)\}',byid[tid])[1]
                remove_go(childgo)
remove_go(blockid(go))
logo_transform=next(cid for cid in removed if byid[cid].startswith('--- !u!4 ') and f'm_GameObject: {{fileID: {blockid(go)}}}' in byid[cid])
source=(STAGE/'Assets/InteriorDetails.prefab').read_text().replace('m_Name: InteriorDetails\n','m_Name: Depot02_InteriorDetails\n')
extra=re.split(r'(?=--- !u!)',source)[1:]
assert not set(byid)&{blockid(b) for b in extra}
rootgo=blockid(next(b for b in extra if 'm_Name: Depot02_InteriorDetails\n' in b))
rt=next(b for b in extra if b.startswith('--- !u!4 ') and f'm_GameObject: {{fileID: {rootgo}}}' in b)
rootid=blockid(rt)
visualgo=blockid(next(b for b in blocks if 'm_Name: Depot02_Visuals\n' in b))
vt=next(b for b in blocks if b.startswith('--- !u!4 ') and f'm_GameObject: {{fileID: {visualgo}}}' in b)
visualid=blockid(vt)
newvt=vt.replace(f'  - {{fileID: {logo_transform}}}\n','').replace('  m_Children:\n',f'  m_Children:\n  - {{fileID: {rootid}}}\n',1)
extra[extra.index(rt)]=rt.replace('  m_Father: {fileID: 0}',f'  m_Father: {{fileID: {visualid}}}')
result=blocks[0]+''.join(newvt if b==vt else b for b in blocks[1:] if blockid(b) not in removed)+''.join(extra)
resultblocks=re.split(r'(?=--- !u!)',result)
assert all(b in resultblocks for b in blocks[1:] if blockid(b) not in removed and b!=vt)
assert 'm_Name: D02_BrandSign\n' not in result
# Ensure nothing outside the removed logo tree refers to deleted objects/components.
assert not removed & set(re.findall(r'\{fileID: (-?\d+)\}',result))
prefab.write_text(result,newline='\n')
report=ROOT/'ArtSource/Depot02/depot_02_interior_validation.json'
data=json.loads(report.read_text());data.update(unityValidation=(STAGE/'validation.txt').read_text(),removedLogoSerializedObjects=len(removed),preservedPrefabObjects=len(blocks)-1-len(removed))
report.write_text(json.dumps(data,indent=2));print(json.dumps(data,indent=2))
