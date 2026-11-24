"""Stage just the shared character visual, controllers and candidate PBR surfaces."""
from pathlib import Path
import re,shutil
ROOT=Path(__file__).resolve().parents[1];STAGE=ROOT/'.utmp/thief-enemy-01-unity';OUT=ROOT/'ArtSource/Characters/ThiefEnemy01/PBRSurfaces'
assert not (STAGE/'Temp/UnityLockfile').exists(),'Isolated project is already open'
index={}
for meta in (ROOT/'Assets').rglob('*.meta'):
    m=re.search(r'^guid: (\w+)',meta.read_text(errors='replace'),re.M)
    if m:index[m[1]]=Path(str(meta)[:-5])
pending=[ROOT/'Assets/_Project/Prefabs/Enemies/thief_enemy_01.prefab']+list((ROOT/'Assets/_Project/Animations/Thieves/ThiefEnemy01').glob('*.controller'))
seen=set()
while pending:
    p=pending.pop()
    if p in seen or not p.is_file():continue
    seen.add(p);dest=STAGE/p.relative_to(ROOT);dest.parent.mkdir(parents=True,exist_ok=True)
    shutil.copy2(OUT/p.name if p.suffix=='.png' and (OUT/p.name).exists() else p,dest)
    meta=Path(str(p)+'.meta');text=''
    if meta.exists():shutil.copy2(meta,Path(str(dest)+'.meta'));text=meta.read_text()
    if p.suffix in ['.mat','.prefab','.controller','.asset']:text+=p.read_text()
    for g in re.findall(r'guid: ([a-f0-9]{32})',text):
        if g in index:pending.append(index[g])
shutil.copy2(ROOT/'Tools/Unity/ValidateThiefPBRSurfaces.cs',STAGE/'Assets/Editor/ValidateThiefPBRSurfaces.cs')
print('Staged',len(seen),'shared visual dependencies; production assets untouched.')
