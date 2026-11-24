"""Install only the eight verified thief prefab bodies, retaining every asset GUID."""
from pathlib import Path
import re,json,hashlib,shutil
ROOT=Path(__file__).resolve().parents[1];STAGE=ROOT/'.utmp/thief-prefab-upgrade-unity';OUT=ROOT/'ArtSource/Characters/Character01Thief/PrefabRestore'
NAMES=['Thief','Thief_Pistol','Thief_AssaultRifle','Thief_Shotgun','Thief_RocketLauncher','GrapplerThief','SpeakerThief','thief_enemy_01']
folder=Path('Assets/_Project/Prefabs/Enemies')
def digest(p):return hashlib.sha256(p.read_bytes()).hexdigest()
baseline=json.loads((OUT/'BeforeHashes.json').read_text());report=OUT/'UnityRestoreValidation.txt'
assert report.exists() and report.read_text().count('PASS:')==8,'All eight prefabs must pass Unity validation'
assert not (OUT/'UnityRestoreFailure.txt').exists(),'Resolve validation failure'
assert all(digest(ROOT/p)==h for p,h in baseline.items()),'Production prefab or metadata changed during authoring'
pattern=re.compile(r'^--- !u!\d+ &(-?\d+)(?: stripped)?\n.*?(?=^--- !u!|\Z)',re.M|re.S)
field=lambda text,name:re.search(r'^  '+re.escape(name)+r':[^\n]*(?:\n(?:    .*|  - .*))*',text,re.M)
allowed=['m_animator','m_ragdoll','m_muzzle','m_gun','m_heldWeaponRenderers']
changes={}
for name in NAMES:
    rel=folder/(name+'.prefab');candidate=STAGE/rel
    assert report.stat().st_mtime>candidate.stat().st_mtime,'Validate latest prefab: '+name
    assert re.search(r'^guid: (\w+)',Path(str(ROOT/rel)+'.meta').read_text(),re.M)[1]==re.search(r'^guid: (\w+)',Path(str(candidate)+'.meta').read_text(),re.M)[1],'Prefab GUID changed'
    if name!='thief_enemy_01':
        current={m[1]:m[0] for m in pattern.finditer((ROOT/rel).read_text())}
        archive={m[1]:m[0] for m in pattern.finditer((ROOT/'ArtSource/Characters/ThiefEnemy01/PrefabUpgrade/OriginalPrefabs'/(name+'.prefab')).read_text())}
        after={m[1]:m[0] for m in pattern.finditer(candidate.read_text())}
        preserved=0
        for key in current.keys()&archive.keys():
            kind=archive[key].splitlines()[1]
            if kind in ['Transform:','PrefabInstance:']:continue
            expected=current[key]
            if kind=='MonoBehaviour:':
                for prop in allowed:
                    a=field(archive[key],prop);b=field(expected,prop)
                    if a and b:expected=expected[:b.start()]+a[0]+expected[b.end():]
            assert after[key]==expected,'Current nonvisual component setting changed: '+name+'/'+key
            preserved+=1
        changes[name]={'current_component_blocks_retained':preserved,'visual_and_attachment_references_restored':True}
for name in NAMES:
    rel=folder/(name+'.prefab');shutil.copy2(STAGE/rel,ROOT/rel)
    changes.setdefault(name,{})['sha256']=digest(ROOT/rel)
for rel,h in baseline.items():
    if rel.endswith('.meta') or Path(rel).stem=='RogueCar':assert digest(ROOT/rel)==h,'Unrequested asset change'
(OUT/'InstallationValidation.json').write_text(json.dumps({'prefabs':changes,'all_prefab_guids_retained':True,'current_gameplay_and_component_settings_preserved':True,'rogue_car_unchanged':True,'validation':'UnityRestoreValidation.txt'},indent=2))
print('Restored Character01_Thief in all eight thief prefabs; gameplay settings, prefab GUIDs and RogueCar preserved.')
