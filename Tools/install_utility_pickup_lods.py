"""Install the Unity-validated visual prefab while retaining existing identities."""
from pathlib import Path
import re,shutil,json,hashlib
ROOT=Path(__file__).resolve().parents[1];STAGE=ROOT/'.utmp/cityhatch-unity'
REL=Path('Assets/_Project/Prefabs/Vehicles/NPCTraffic/NPC_UtilityPickup.prefab')
report=STAGE/'UtilityPickupValidation.txt'
assert report.exists() and report.read_text().count('PASS:')==4,'Unity validation incomplete'
assert not (STAGE/'UtilityPickupFailure.txt').exists(),'Unity authoring failed'
assert not (STAGE/'UtilityPickupPreviewFailure.txt').exists(),'Resolve Unity preview failure'
assert report.stat().st_mtime>(STAGE/REL).stat().st_mtime,'Validate the latest prefab before installation'
for level in range(3):
    suffix='' if level==0 else f'_LOD{level}'
    mesh=Path(f'Assets/_Project/Models/Vehicles/NPCTraffic/NPC_UtilityPickup{suffix}.fbx')
    assert report.stat().st_mtime>(STAGE/mesh).stat().st_mtime,'Validate the latest meshes'
    assert hashlib.sha256((ROOT/mesh).read_bytes()).digest()==hashlib.sha256((STAGE/mesh).read_bytes()).digest(),'Validated mesh differs from production'
old=(ROOT/REL).read_text();new=(STAGE/REL).read_text()
backup=ROOT/'ArtSource/Vehicles/NPCTraffic/UtilityPickup_BeforeCleanup'
assert (ROOT/REL).read_bytes()==(backup/REL.name).read_bytes(),'Production UtilityPickup prefab changed during authoring'
for mat in (backup).glob('NPC_UtilityPickup_*.mat'):
    assert (ROOT/'Assets/_Project/Models/Vehicles/NPCTraffic/Materials'/mat.name).read_bytes()==mat.read_bytes(),'Production UtilityPickup material changed during authoring'
ids=lambda t:set(re.findall(r'^--- !u!\d+ &(-?\d+)',t,re.M))
assert ids(old)<=ids(new),'Existing prefab identities changed'
for token in ['guid: ae7d458be2510464db2073184bd247d7','value: 0.6914539','&9006897940752889773','&8895828114043080774']:
    assert token in old and token in new,'Original model reference or placement changed: '+token
assert len(re.findall(r'^LODGroup:',new,re.M))==1
assert len(re.findall(r'^  - screenRelativeHeight:',new,re.M))==3
for level in [0,1,2]:
    suffix='' if level==0 else f'_LOD{level}'
    rel=Path(f'Assets/_Project/Models/Vehicles/NPCTraffic/NPC_UtilityPickup{suffix}.fbx.meta')
    before=re.search(r'^guid: (\w+)',(ROOT/rel).read_text(),re.M)[1]
    after=re.search(r'^guid: (\w+)',(STAGE/rel).read_text(),re.M)[1]
    assert before==after,'LOD GUID changed'
    shutil.copy2(STAGE/rel,ROOT/rel)
for mat in (STAGE/'Assets/_Project/Models/Vehicles/NPCTraffic/Materials').glob('NPC_UtilityPickup_*.mat'):
    rel=mat.relative_to(STAGE)
    target_meta=Path(str(ROOT/rel)+'.meta')
    if target_meta.exists():
        getguid=lambda p:re.search(r'^guid: (\w+)',p.read_text(),re.M)[1]
        assert getguid(target_meta)==getguid(Path(str(mat)+'.meta')),'Existing material GUID changed'
    shutil.copy2(mat,ROOT/rel)
    shutil.copy2(Path(str(mat)+'.meta'),Path(str(ROOT/rel)+'.meta'))
shutil.copy2(STAGE/REL,ROOT/REL)
shutil.copy2(report,ROOT/'ArtSource/Vehicles/NPCTraffic/NPC_UtilityPickup_UnityValidation.txt')
src=ROOT/'ArtSource/Vehicles/NPCTraffic'
for level in range(3):
    preview=STAGE/f'UtilityPickup_Unity_LOD{level}.png'
    if preview.exists():shutil.copy2(preview,src/f'NPC_UtilityPickup_Unity_LOD{level}.png')
for view in ['Front','Rear']:shutil.copy2(src/f'NPC_UtilityPickup_PBR_{view}.png',src/f'NPC_UtilityPickup_Clean_{view}.png')
for level in [1,2]:shutil.copy2(src/f'NPC_UtilityPickup_PBR_LOD{level}.png',src/f'NPC_UtilityPickup_Clean_LOD{level}.png')
old_report=src/'NPC_UtilityPickup_MeshReport.json'
archive=src/'UtilityPickup_BeforeCleanup/NPC_UtilityPickup_MeshReport.json'
if not archive.exists():shutil.copy2(old_report,archive)
old_report.write_text(json.dumps({'name':'NPC_UtilityPickup','surface':'Texture-free PBR; authored hard surfaces; opaque glass','lods_blender':json.loads((src/'NPC_UtilityPickup_CleanupReport.json').read_text()),'lods_unity_triangles':[int(v) for v in re.findall(r'triangles=(\d+)',report.read_text())],'materials':[s['name'] for s in json.loads((src/'NPC_UtilityPickup_PBRMaterials.json').read_text())],'original_prefab_identities_preserved':len(ids(old))},indent=2))
print('Installed editable LODGroup prefab; all',len(ids(old)),'original prefab identities and original model scale retained.')
