"""Snapshot the current shotgun prefab and stage a private authoring project."""
from pathlib import Path
import json,hashlib,re,shutil
ROOT=Path(__file__).resolve().parents[1];SRC=ROOT/'ArtSource/Characters/Hotwire';STAGE=ROOT/'.utmp/thief-prefab-upgrade-unity'
assert not (STAGE/'Temp/UnityLockfile').exists(),'Private project is open'
before=SRC/'Before';before.mkdir(exist_ok=True)
manifest=SRC/'BeforeHashes.json'
if not manifest.exists():
    paths=[p for p in (ROOT/'Assets/_Project/Prefabs/Enemies').glob('*') if p.is_file()]
    for folder in ['Assets/_Project/Models/Thieves','Assets/_Project/Materials/Thieves','Assets/_Project/Textures/Thieves','Assets/_Project/Animations/Thieves']:
        paths.extend(p for p in (ROOT/folder).rglob('*') if p.is_file() and 'Hotwire' not in p.parts)
    manifest.write_text(json.dumps({p.relative_to(ROOT).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in paths},indent=2))
    shutil.copy2(ROOT/'Assets/_Project/Prefabs/Enemies/Thief_Shotgun.prefab',before/'Thief_Shotgun.prefab')
index={}
for meta in (ROOT/'Assets').rglob('*.meta'):
    m=re.search(r'^guid: (\w+)',meta.read_text(errors='replace'),re.M)
    if m:index[m[1]]=Path(str(meta)[:-5])
def copy(p):
    dest=STAGE/p.relative_to(ROOT);dest.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(p,dest)
    meta=Path(str(p)+'.meta')
    if meta.exists():shutil.copy2(meta,Path(str(dest)+'.meta'))
pending=[ROOT/'Assets/_Project/Prefabs/Enemies/Thief_Shotgun.prefab',ROOT/'Assets/_Project/Models/Thieves/Hotwire/Hotwire.fbx',ROOT/'Assets/_Project/Materials/Thieves/Hotwire/Hotwire.mat']
visited=set()
while pending:
    p=pending.pop()
    if p in visited or not p.is_file():continue
    visited.add(p)
    if p.suffix=='.cs':continue
    copy(p);meta=Path(str(p)+'.meta');text=meta.read_text(errors='replace') if meta.exists() else ''
    if p.suffix in ['.prefab','.mat','.asset','.controller','.overridecontroller','.anim']:text+=p.read_text(errors='replace')
    for g in re.findall(r'guid: ([a-f0-9]{32})',text):
        if g in index:pending.append(index[g])
for folder in ['Assets/_Project/Scripts','Assets/ThirdParty/FishySteamworks','Assets/CaptureAndPromote/Runtime']:
    for p in (ROOT/folder).rglob('*'):
        if p.is_file() and 'Editor' not in p.relative_to(ROOT/folder).parts and p.suffix in ['.cs','.asmdef','.dll']:copy(p)
copytool=STAGE/'Assets/Editor/AuthorHotwireShotgun.cs';shutil.copy2(ROOT/'Tools/Unity/AuthorHotwireShotgun.cs',copytool)
print('Staged',len(visited),'dependencies and current runtime script definitions; originals archived.')
