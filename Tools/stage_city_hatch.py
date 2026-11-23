"""Stage only this visual prefab and its dependencies for isolated Unity authoring."""
from pathlib import Path
import shutil,re,uuid,json
ROOT=Path(__file__).resolve().parents[1]
STAGE=ROOT/'.utmp/cityhatch-unity'
for folder in ['Packages','ProjectSettings','Assets/Editor']:(STAGE/folder).mkdir(parents=True,exist_ok=True)
shutil.copy2(ROOT/'.utmp/fountain-unity/Packages/manifest.json',STAGE/'Packages/manifest.json')
shutil.copy2(ROOT/'ProjectSettings/ProjectVersion.txt',STAGE/'ProjectSettings/ProjectVersion.txt')
model=ROOT/'Assets/_Project/Models/Vehicles/NPCTraffic'
template=(model/'NPC_CityHatch.fbx.meta').read_text()
for level in [1,2]:
    meta=model/f'NPC_CityHatch_LOD{level}.fbx.meta'
    if not meta.exists():meta.write_text(re.sub(r'^guid: \w+',f'guid: {uuid.uuid4().hex}',template,flags=re.M))
index={}
for meta in (ROOT/'Assets').rglob('*.meta'):
    m=re.search(r'^guid: (\w+)',meta.read_text(errors='replace'),re.M)
    if m:index[m[1]]=Path(str(meta)[:-5])
def copy(p):
    dest=STAGE/p.relative_to(ROOT);dest.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(p,dest)
    meta=Path(str(p)+'.meta')
    if meta.exists():shutil.copy2(meta,Path(str(dest)+'.meta'))
pending=[ROOT/'Assets/_Project/Prefabs/Vehicles/NPCTraffic/NPC_CityHatch.prefab',model/'NPC_CityHatch_LOD1.fbx',model/'NPC_CityHatch_LOD2.fbx'];seen=set()
while pending:
    p=pending.pop()
    if p in seen or not p.is_file():continue
    seen.add(p);copy(p)
    meta=Path(str(p)+'.meta');text=meta.read_text() if meta.exists() else ''
    if p.suffix in ['.prefab','.mat','.asset']:text+=p.read_text()
    for g in re.findall(r'guid: ([a-f0-9]{32})',text):
        if g in index:pending.append(index[g])
shutil.copy2(ROOT/'Tools/Unity/AuthorCityHatchLODs.cs',STAGE/'Assets/Editor/AuthorCityHatchLODs.cs')
print('Staged',len(seen),'visual assets')
