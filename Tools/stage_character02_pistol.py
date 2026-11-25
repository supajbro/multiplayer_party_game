"""Stage only Character02 colour assets and the current pistol prefab for validation."""
from pathlib import Path
import re,shutil,json,hashlib
ROOT=Path(__file__).resolve().parents[1];STAGE=ROOT/'.utmp/thief-prefab-upgrade-unity';OUT=ROOT/'ArtSource/Characters/Character02Thief/GetawayPalette'
assert not (STAGE/'Temp/UnityLockfile').exists(),'Private project is open'
prefabs=list((ROOT/'Assets/_Project/Prefabs/Enemies').glob('*.prefab'))
if not (OUT/'PrefabBeforeHashes.json').exists():
    (OUT/'PrefabBeforeHashes.json').write_text(json.dumps({p.relative_to(ROOT).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in prefabs},indent=2))
    shutil.copy2(ROOT/'Assets/_Project/Prefabs/Enemies/Thief_Pistol.prefab',OUT/'Before/Thief_Pistol.prefab')
index={}
for meta in (ROOT/'Assets').rglob('*.meta'):
    m=re.search(r'^guid: (\w+)',meta.read_text(errors='replace'),re.M)
    if m:index[m[1]]=Path(str(meta)[:-5])
pending=[ROOT/'Assets/_Project/Prefabs/Enemies/Thief_Pistol.prefab']+list((ROOT/'Assets/_Project/Models/Thieves/Character02Thief').rglob('*.fbx'));seen=set()
while pending:
    p=pending.pop()
    if p in seen or not p.is_file():continue
    seen.add(p)
    if p.suffix=='.cs':continue
    dest=STAGE/p.relative_to(ROOT);dest.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(p,dest)
    meta=Path(str(p)+'.meta');text=''
    if meta.exists():shutil.copy2(meta,Path(str(dest)+'.meta'));text=meta.read_text()
    if p.suffix in ['.prefab','.mat','.asset','.controller','.anim','.overrideController']:text+=p.read_text(errors='replace')
    for g in re.findall(r'guid: ([a-f0-9]{32})',text):
        if g in index:pending.append(index[g])
shutil.copy2(ROOT/'Tools/Unity/UseCharacter02Pistol.cs',STAGE/'Assets/Editor/UseCharacter02Pistol.cs')
print('Staged Character02 models and one pistol prefab;',len(seen),'dependencies.')
