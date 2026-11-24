"""Install the checked surface assets only; never edit models, controllers or prefabs."""
from pathlib import Path
import shutil,json,hashlib,re
ROOT=Path(__file__).resolve().parents[1];SRC=ROOT/'ArtSource/Characters/ThiefEnemy01';OUT=SRC/'PBRSurfaces';STAGE=ROOT/'.utmp/thief-enemy-01-unity'
def digest(p):return hashlib.sha256(p.read_bytes()).hexdigest()
report=OUT/'UnityPBRValidation.txt'
assert report.exists() and report.read_text().count('PASS:')==7,'Complete the Unity material/animation/lighting checks'
assert not (OUT/'UnityPBRFailure.txt').exists(),'Resolve Unity failure'
source_report=json.loads((OUT/'SourcePreservationValidation.json').read_text())
assert source_report['identical'] and source_report['actions']==25 and source_report['meshes']==4
baseline=json.loads((OUT/'PreservedAssetHashes.json').read_text())
assert all(digest(ROOT/p)==h for p,h in baseline.items()),'A preserved model/prefab/controller/metadata file changed during authoring'
mat=Path('Assets/_Project/Materials/Thieves/ThiefEnemy01/thief_enemy_01.mat')
assert digest(ROOT/mat)==digest(OUT/'Before/thief_enemy_01.mat'),'Production material changed during authoring'
assert digest(SRC/'thief_enemy_01.blend')==digest(OUT/'Before/thief_enemy_01.blend'),'Production Blender source changed during authoring'
candidates=[]
for suffix in ['BaseColor','Normal','MetallicOcclusionSmoothness']:
    name=f'thief_enemy_01_{suffix}.png';target=Path('Assets/_Project/Textures/Thieves/ThiefEnemy01')/name
    assert digest(ROOT/target)==digest(OUT/'Before'/name),'Production texture changed: '+name
    assert digest(STAGE/target)==digest(OUT/name),'Candidate differs from validated atlas: '+name
    assert report.stat().st_mtime>(STAGE/target).stat().st_mtime,'Validate the latest atlas'
    candidates.append((OUT/name,ROOT/target))
assert report.stat().st_mtime>(STAGE/mat).stat().st_mtime,'Validate the latest material'
candidates.extend([(STAGE/mat,ROOT/mat),(OUT/'thief_enemy_01.blend',SRC/'thief_enemy_01.blend')])
for source,target in candidates:shutil.copy2(source,target)
assert all(digest(ROOT/p)==h for p,h in baseline.items()),'Preserved asset changed during installation'
installed={str(target.relative_to(ROOT).as_posix()):digest(target) for _,target in candidates}
(OUT/'InstallationValidation.json').write_text(json.dumps({'preserved_files':len(baseline),'all_preserved_hashes_match':True,'fbx_geometry_rig_weights_uvs_25_clips_unchanged':True,'all_prefabs_controllers_weapon_mounts_and_guids_unchanged':True,'source_geometry_animation_fingerprint_identical':True,'unity_validation':'UnityPBRValidation.txt','installed':installed},indent=2))
print('Installed clean PBR surfaces; preserved',len(baseline),'model, prefab, controller and metadata files byte-for-byte.')
