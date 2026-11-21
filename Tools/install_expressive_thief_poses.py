"""Install validated pose edits while preserving existing prefab identities/settings."""
from pathlib import Path
import re,json,hashlib,shutil
ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'ArtSource/Characters/ThiefEnemy01/ExpressivePoses'
STAGE=ROOT/'.utmp/thief-prefab-upgrade-unity'
before=json.loads((OUT/'BeforeHashes.json').read_text())
for rel,digest in before.items():assert hashlib.sha256((ROOT/rel).read_bytes()).hexdigest()==digest,'Changed during authoring: '+rel
assert (OUT/'UnityPoseValidation.txt').read_text().count('PASS ')==8
check=json.loads((OUT/'FBXRoundtripValidation.json').read_text());assert len(check['actions'])==25
clearance=json.loads((OUT/'ArmClearanceValidation.json').read_text())
assert min(v['min_hand_surface_distance_m'] for k,v in clearance.items() if 'Death' not in k)>.035

rx=re.compile(r'^--- !u!\d+ &(-?\d+)(?: stripped)?\n(.*?)(?=^--- !u!|\Z)',re.M|re.S)
names=['thief_enemy_01','Thief','Thief_Pistol','Thief_AssaultRifle','Thief_Shotgun','Thief_RocketLauncher','GrapplerThief','SpeakerThief']
paths=['Assets/_Project/Models/Thieves/thief_enemy_01.fbx','Assets/_Project/Models/Thieves/thief_enemy_01.fbx.meta']
preservation={}
for name in names:
    rel=f'Assets/_Project/Prefabs/Enemies/{name}.prefab';target=ROOT/rel;candidate=(STAGE/rel).read_text()
    old={int(m[1]):m[2] for m in rx.finditer(target.read_text())}
    new={int(m[1]):m[2] for m in rx.finditer(candidate)}
    rootid=next(k for k,v in old.items() if v.startswith('GameObject:') and re.search(r'^  m_Name: '+re.escape(name)+r'\s*$',v,re.M))
    keep={}
    for key,value in old.items():
        if value.startswith('MonoBehaviour:') and re.search(r'^  m_GameObject: \{fileID: '+str(rootid)+r'\}',value,re.M):
            assert key in new,(name,'root component removed')
            references=lambda text:sorted(i for i in re.findall(r'fileID: (-?\d+)',text) if i!='0')
            assert references(value)==references(new[key]),(name,'root references changed')
            assert sorted(re.findall(r'guid: (\w+)',value))==sorted(re.findall(r'guid: (\w+)',new[key])),(name,'root asset references changed')
            keep[key]=value
    def preserve(m):
        if int(m[1]) not in keep:return m[0]
        return m[0][:m[0].index('\n')+1]+keep[int(m[1])]
    candidate=rx.sub(preserve,candidate)
    meta=Path(str(target)+'.meta').read_text();stage_meta=Path(str(STAGE/rel)+'.meta').read_text()
    assert re.search(r'^guid: (\w+)',meta,re.M)[1]==re.search(r'^guid: (\w+)',stage_meta,re.M)[1]
    (STAGE/rel).write_text(candidate)
    preservation[name]={'root_game_object_id':rootid,'gameplay_components_preserved':len(keep)}
    paths.append(rel)

for p in (STAGE/'Assets/_Project/Animations/Thieves/ThiefEnemy01').glob('*.controller*'):
    paths.append(p.relative_to(STAGE).as_posix())
model_meta=paths[1]
assert re.search(r'^guid: (\w+)',(ROOT/model_meta).read_text(),re.M)[1]==re.search(r'^guid: (\w+)',(STAGE/model_meta).read_text(),re.M)[1]
installed={}
for rel in paths:
    src=STAGE/rel;dst=ROOT/rel
    if src.suffix in ['.prefab','.controller','.meta']:
        dst.write_text('\n'.join(line.rstrip() for line in src.read_text().splitlines())+'\n')
    else:shutil.copy2(src,dst)
    installed[rel]=hashlib.sha256(dst.read_bytes()).hexdigest()
shutil.copy2(OUT/'thief_enemy_01.blend',ROOT/'ArtSource/Characters/ThiefEnemy01/thief_enemy_01.blend')
(OUT/'InstalledPoseHashes.json').write_text(json.dumps(installed,indent=2))
(OUT/'PrefabIdentityPreservation.json').write_text(json.dumps(preservation,indent=2))
print('Installed expressive animations, five controllers, revised bounds and eight prefab assignments; root gameplay fields and identities retained.')
