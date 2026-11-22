"""Install validated crew textures once while preserving all Unity identities."""
from pathlib import Path
import hashlib,json,shutil
ROOT=Path(__file__).resolve().parents[1];OUT=ROOT/'ArtSource/Characters/ThiefEnemy01/GetawayCrew'
TEX=ROOT/'Assets/_Project/Textures/Thieves/ThiefEnemy01'
def digest(p):return hashlib.sha256(p.read_bytes()).hexdigest()
report=OUT/'UnityCrewValidation.txt'
assert report.exists() and report.read_text().count('PASS:')==49,'Expected material check and six checks for each of eight prefabs'
assert not (OUT/'UnityCrewFailure.txt').exists(),'Resolve Unity validation failure'
baseline=json.loads((OUT/'PreservedAssetHashes.json').read_text())
assert all(digest(ROOT/p)==h for p,h in baseline.items()),'A preserved asset changed during authoring'
installed={}
for source in sorted((OUT/'Before').glob('*.png')):
    target=TEX/source.name;candidate=OUT/source.name
    assert digest(target)==digest(source),'Production texture changed during authoring: '+target.name
    staged=ROOT/'.utmp/thief-prefab-upgrade-unity'/target.relative_to(ROOT)
    assert digest(staged)==digest(candidate),'Candidate differs from validated map'
    shutil.copy2(candidate,target);installed[target.relative_to(ROOT).as_posix()]=digest(target)
assert len(installed)==3
assert all(digest(ROOT/p)==h for p,h in baseline.items()),'Preserved asset changed'
(OUT/'InstalledTextureHashes.json').write_text(json.dumps(installed,indent=2))
(OUT/'AssetPreservationValidation.json').write_text(json.dumps({'unchanged_files':len(baseline),'all_hashes_match':True,'fbx_rig_mesh_weights_clips_unchanged':True,'prefabs_controllers_material_and_texture_guids_unchanged':True,'installed_textures':installed},indent=2))
print('Installed three validated texture maps; all',len(baseline),'preserved assets are byte-identical.')
