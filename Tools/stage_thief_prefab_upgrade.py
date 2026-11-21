"""Stage production prefab dependencies and runtime scripts without Editor hooks."""
from pathlib import Path
import shutil,re,json,hashlib
ROOT=Path(__file__).resolve().parents[1]
STAGE=ROOT/'.utmp/thief-prefab-upgrade-unity'
REPORT=ROOT/'ArtSource/Characters/ThiefEnemy01/PrefabUpgrade'
REPORT.mkdir(parents=True,exist_ok=True)
for p in ['Assets/Editor','Packages','ProjectSettings']:(STAGE/p).mkdir(parents=True,exist_ok=True)
shutil.copy2(ROOT/'.utmp/hoverboard-recovery-unity/Packages/manifest.json',STAGE/'Packages/manifest.json')
shutil.copy2(ROOT/'ProjectSettings/ProjectVersion.txt',STAGE/'ProjectSettings/ProjectVersion.txt')
shutil.copy2(ROOT/'ProjectSettings/TagManager.asset',STAGE/'ProjectSettings/TagManager.asset')
index={}
for meta in (ROOT/'Assets').rglob('*.meta'):
    m=re.search(r'^guid: (\w+)',meta.read_text(errors='replace'),re.M)
    if m:index[m[1]]=Path(str(meta)[:-5])
def copy(p):
    target=STAGE/p.relative_to(ROOT);target.parent.mkdir(parents=True,exist_ok=True)
    shutil.copy2(p,target)
    meta=Path(str(p)+'.meta')
    if meta.exists():shutil.copy2(meta,Path(str(target)+'.meta'))
names=['Thief','Thief_Pistol','Thief_AssaultRifle','Thief_Shotgun','Thief_RocketLauncher','GrapplerThief','SpeakerThief']
pending=[ROOT/f'Assets/_Project/Prefabs/Enemies/{n}.prefab' for n in names]+[ROOT/'Assets/_Project/Prefabs/Enemies/thief_enemy_01.prefab']
visited=set()
while pending:
    p=pending.pop()
    if p in visited or not p.is_file():continue
    visited.add(p)
    if p.suffix=='.cs':continue # Runtime source is copied below; Editor scripts stay out.
    copy(p)
    meta=Path(str(p)+'.meta')
    contents=meta.read_text(errors='replace') if meta.exists() else ''
    if p.suffix.lower() in ['.prefab','.asset','.mat','.controller','.overridecontroller','.anim']:
        contents+=p.read_text(errors='replace')
    for guid in re.findall(r'guid: ([a-f0-9]{32})',contents):
        if guid in index:pending.append(index[guid])
for folder in ['Assets/_Project/Scripts','Assets/ThirdParty/FishySteamworks','Assets/CaptureAndPromote/Runtime']:
    for p in (ROOT/folder).rglob('*'):
        if p.is_file() and 'Editor' not in p.relative_to(ROOT/folder).parts and p.suffix in ['.cs','.asmdef','.dll']:
            copy(p)
manifest={}
for name in names:
    p=ROOT/f'Assets/_Project/Prefabs/Enemies/{name}.prefab'
    backup=REPORT/'OriginalPrefabs'/p.name;backup.parent.mkdir(exist_ok=True)
    if not backup.exists():shutil.copy2(p,backup)
    manifest[p.relative_to(ROOT).as_posix()]=hashlib.sha256(p.read_bytes()).hexdigest()
(REPORT/'OriginalPrefabHashes.json').write_text(json.dumps(manifest,indent=2))
print('Staged',len(visited),'asset dependencies, runtime code, and seven prefab backups at',STAGE)
