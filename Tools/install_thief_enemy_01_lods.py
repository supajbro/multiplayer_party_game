"""Copy explicitly authored and validated Unity assets into the project once."""
from pathlib import Path
import shutil,re,json,hashlib
ROOT=Path(__file__).resolve().parents[1]
STAGE=ROOT/'.utmp/thief-enemy-01-unity'
SRC=ROOT/'ArtSource/Characters/ThiefEnemy01'
assert 'PASS:' in (SRC/'UnityLODAnimationValidation.txt').read_text()
assert (SRC/'UnityAnimatorPlaybackValidation.txt').read_text().count('PASS:')==18
def guid(path):return re.search(r'^guid: (\w+)',path.read_text(),re.M)[1]
model='Assets/_Project/Models/Thieves/thief_enemy_01.fbx'
assert guid(ROOT/(model+'.meta'))==guid(STAGE/(model+'.meta'))
prefab='Assets/_Project/Prefabs/Enemies/thief_enemy_01.prefab'
assert not (ROOT/prefab).exists(), 'Existing prefab must not be overwritten implicitly'
animations='Assets/_Project/Animations/Thieves/ThiefEnemy01'
assert not (ROOT/animations).exists(), 'Existing controllers must not be overwritten implicitly'
paths=[model,model+'.meta',prefab,prefab+'.meta',animations,animations+'.meta']
for rel in paths:
    source=STAGE/rel;target=ROOT/rel;target.parent.mkdir(parents=True,exist_ok=True)
    if source.is_dir():shutil.copytree(source,target)
    else:shutil.copy2(source,target)
manifest={}
for rel in paths:
    target=ROOT/rel
    for file in target.rglob('*') if target.is_dir() else [target]:
        if file.is_file():manifest[file.relative_to(ROOT).as_posix()]=hashlib.sha256(file.read_bytes()).hexdigest()
old=SRC/'GeometryValidation.json';archive=SRC/'OriginalGeometryValidation.json'
if not archive.exists():shutil.copy2(old,archive)
report=json.loads((SRC/'LODAnimationValidation.json').read_text())
report['unity_import_validation']='UnityLODAnimationValidation.txt'
report['unity_playback_validation']='UnityAnimatorPlaybackValidation.txt'
report['fbx_roundtrip_validation']='FBXRoundtripValidation.json'
old.write_text(json.dumps(report,indent=2))
portable=SRC/'thief_enemy_01.glb'
if portable.exists() and not (SRC/'thief_enemy_01_20000_source.glb').exists():
    portable.rename(SRC/'thief_enemy_01_20000_source.glb')
(SRC/'InstalledAssetManifest.json').write_text(json.dumps(manifest,indent=2))
print('Installed saved LOD prefab, importer, and three controllers; retained model GUID and original source.')
