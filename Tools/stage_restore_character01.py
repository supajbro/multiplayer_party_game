"""Explicitly restore archived Character01 visuals with current gameplay serialization."""
from pathlib import Path
import re,json,hashlib,shutil
ROOT=Path(__file__).resolve().parents[1];STAGE=ROOT/'.utmp/thief-prefab-upgrade-unity'
OUT=ROOT/'ArtSource/Characters/Character01Thief/PrefabRestore';OUT.mkdir(parents=True,exist_ok=True)
assert not (STAGE/'Temp/UnityLockfile').exists(),'Private authoring project is open'
NAMES=['Thief','Thief_Pistol','Thief_AssaultRifle','Thief_Shotgun','Thief_RocketLauncher','GrapplerThief','SpeakerThief']
ARCHIVE=ROOT/'ArtSource/Characters/ThiefEnemy01/PrefabUpgrade/OriginalPrefabs'
folder=Path('Assets/_Project/Prefabs/Enemies')
pattern=re.compile(r'^--- !u!\d+ &(-?\d+)(?: stripped)?\n.*?(?=^--- !u!|\Z)',re.M|re.S)
field=lambda text,name:re.search(r'^  '+re.escape(name)+r':[^\n]*(?:\n(?:    .*|  - .*))*',text,re.M)
allowed=['m_animator','m_ragdoll','m_muzzle','m_gun','m_heldWeaponRenderers']
manifest={};report={};candidates={}
for name in NAMES+['thief_enemy_01','RogueCar']:
    p=ROOT/folder/(name+'.prefab');backup=OUT/'Before'/p.name;backup.parent.mkdir(exist_ok=True)
    if not backup.exists():shutil.copy2(p,backup)
    manifest[p.relative_to(ROOT).as_posix()]=hashlib.sha256(p.read_bytes()).hexdigest()
    meta=Path(str(p)+'.meta');manifest[meta.relative_to(ROOT).as_posix()]=hashlib.sha256(meta.read_bytes()).hexdigest()
for name in NAMES:
    original=(ARCHIVE/(name+'.prefab')).read_text();current=(ROOT/folder/(name+'.prefab')).read_text()
    old={m[1]:m[0] for m in pattern.finditer(original)};now={m[1]:m[0] for m in pattern.finditer(current)}
    replacements={};changes=[]
    for key in old.keys()&now.keys():
        kind=old[key].splitlines()[1]
        # Restore skeletal transforms/model instances from the original fit.
        # Retain current component settings, including specialist props/audio.
        if kind in ['Transform:','PrefabInstance:']:continue
        text=now[key]
        if kind=='MonoBehaviour:':
            for prop in allowed:
                a=field(old[key],prop);b=field(text,prop)
                assert bool(a)==bool(b),(name,prop)
                if a and a[0]!=b[0]:
                    text=text[:b.start()]+a[0]+text[b.end():];changes.append(prop)
        replacements[key]=text
    candidate=pattern.sub(lambda m:replacements.get(m[1],m[0]),original)
    # Verify every common non-transform component keeps current values apart
    # from the explicitly restored visual/weapon references.
    after={m[1]:m[0] for m in pattern.finditer(candidate)}
    for key,text in replacements.items():assert after[key]==text
    candidates[name]=candidate
    report[name]={'preserved_common_component_blocks':len(replacements),'restored_references':changes,'archive':str((ARCHIVE/(name+'.prefab')).relative_to(ROOT))}
(OUT/'BeforeHashes.json').write_text(json.dumps(manifest,indent=2))
(OUT/'GameplayPreservation.json').write_text(json.dumps(report,indent=2))
index={}
for meta in (ROOT/'Assets').rglob('*.meta'):
    m=re.search(r'^guid: (\w+)',meta.read_text(errors='replace'),re.M)
    if m:index[m[1]]=Path(str(meta)[:-5])
def copy(p):
    dest=STAGE/p.relative_to(ROOT);dest.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(p,dest)
    if p.suffix=='.prefab' and p.stem in candidates and p.parent==ROOT/folder:dest.write_text(candidates[p.stem])
    meta=Path(str(p)+'.meta')
    if meta.exists():shutil.copy2(meta,Path(str(dest)+'.meta'))
pending=[ROOT/folder/(n+'.prefab') for n in NAMES+['thief_enemy_01']]+[ROOT/'Assets/_Project/Models/Thieves/Character01Thief/Character01_Thief.fbx']
visited=set()
while pending:
    p=pending.pop()
    if p in visited or not p.is_file():continue
    visited.add(p)
    if p.suffix=='.cs':continue
    copy(p);meta=Path(str(p)+'.meta');text=meta.read_text(errors='replace') if meta.exists() else ''
    if p.suffix in ['.prefab','.mat','.asset','.controller','.anim','.overridecontroller']:
        text+=(STAGE/p.relative_to(ROOT)).read_text(errors='replace')
    for g in re.findall(r'guid: ([a-f0-9]{32})',text):
        if g in index:pending.append(index[g])
for folder_name in ['Assets/_Project/Scripts','Assets/ThirdParty/FishySteamworks','Assets/CaptureAndPromote/Runtime']:
    for p in (ROOT/folder_name).rglob('*'):
        if p.is_file() and 'Editor' not in p.relative_to(ROOT/folder_name).parts and p.suffix in ['.cs','.asmdef','.dll']:copy(p)
copy(ROOT/'ProjectSettings/TagManager.asset')
shutil.copy2(ROOT/'Tools/Unity/RestoreCharacter01Thieves.cs',STAGE/'Assets/Editor/RestoreCharacter01Thieves.cs')
print('Prepared seven restored gameplay prefabs, current component settings and',len(visited),'dependencies; production prefabs untouched.')
