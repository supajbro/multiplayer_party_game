"""Install only the validated shotgun prefab; preserve all original identities."""
from pathlib import Path
import re,json,hashlib,shutil,sys
ROOT=Path(__file__).resolve().parents[1];SRC=ROOT/'ArtSource/Characters/Hotwire';STAGE=ROOT/'.utmp/thief-prefab-upgrade-unity'
REL='Assets/_Project/Prefabs/Enemies/Thief_Shotgun.prefab'
def digest(path):return hashlib.sha256(path.read_bytes()).hexdigest()
def blocks(text):return {m[1]:m[0] for m in re.finditer(r'^--- !u!\d+ &(-?\d+).*?(?=^--- !u!|\Z)',text,re.M|re.S)}
before=(SRC/'Before/Thief_Shotgun.prefab').read_text();candidate=(STAGE/REL).read_text();a=blocks(before);b=blocks(candidate)
assert a and a.keys()==b.keys(),'Existing prefab object/component identities changed'
if '--prepare' in sys.argv:
    # Unity writes newly introduced default fields while saving. Preserve the
    # user's complete serialized gameplay component blocks, then revalidate.
    for i,block in a.items():
        if block.splitlines()[1]=='MonoBehaviour:' and b[i]!=block:candidate=candidate.replace(b[i],block)
    (STAGE/REL).write_text(candidate)
    print('Restored original gameplay and ragdoll serialization in candidate; revalidate before installation.')
    sys.exit(0)
changed=[i for i in a if a[i]!=b[i]]
types={i:a[i].splitlines()[1] for i in changed}
print('Changed saved prefab blocks:',types)
# Mesh/material/bone overrides and skin bounds on the nested model instance
# are expected. Every other object and all gameplay components must be exact.
assert all(types[i] in ['PrefabInstance:','SkinnedMeshRenderer:'] for i in changed),'Unrequested component or transform change'
assert (SRC/'UnityHotwireValidation.txt').read_text().count('PASS:')==6,'Unity playback validation incomplete'
assert (SRC/'UnityHotwireValidation.txt').stat().st_mtime>(STAGE/REL).stat().st_mtime,'Validate the latest saved prefab before installation'
assert (SRC/'UnityHotwireValidation.txt').stat().st_mtime>(ROOT/'Assets/_Project/Models/Thieves/Hotwire/Hotwire.fbx').stat().st_mtime,'Validate the latest mesh before installation'
assert not (SRC/'UnityHotwireFailure.txt').exists(),'Unity validation failed'
baseline=json.loads((SRC/'BeforeHashes.json').read_text())
previous=json.loads((SRC/'InstalledAssetHashes.json').read_text()) if (SRC/'InstalledAssetHashes.json').exists() else {}
assert all(digest(ROOT/p)==h or (p==REL and digest(ROOT/p)==previous.get(p)) for p,h in baseline.items()),'Production asset changed during authoring'
for folder in ['Assets/_Project/Models/Thieves/Hotwire','Assets/_Project/Materials/Thieves/Hotwire','Assets/_Project/Textures/Thieves/Hotwire']:
    for p in (ROOT/folder).rglob('*'):
        if p.is_file() and p.suffix!='.meta':assert digest(p)==digest(STAGE/p.relative_to(ROOT)),'Production asset differs from tested candidate: '+str(p)
for i in changed:
    candidate=candidate.replace(b[i],''.join(line.rstrip()+'\n' for line in b[i].splitlines()))
(ROOT/REL).write_text(candidate)
assert all(digest(ROOT/p)==h for p,h in baseline.items() if p!=REL),'Unrelated existing asset changed'
report={'changed_prefab':REL,'prefab_guid_retained':True,'existing_object_component_ids_retained':len(a),'changed_blocks':types,'all_other_existing_asset_hashes_retained':True,'other_preserved_files':len(baseline)-1,'existing_gameplay_components_weapon_mounts_and_ragdoll_unchanged':True,'existing_controller_avatar_and_25_animation_clips_unchanged':True}
(SRC/'InstallationValidation.json').write_text(json.dumps(report,indent=2))
installed={REL:digest(ROOT/REL)}
for folder in ['Assets/_Project/Models/Thieves/Hotwire','Assets/_Project/Materials/Thieves/Hotwire','Assets/_Project/Textures/Thieves/Hotwire']:
    for p in (ROOT/folder).rglob('*'):
        if p.is_file():installed[p.relative_to(ROOT).as_posix()]=digest(p)
(SRC/'InstalledAssetHashes.json').write_text(json.dumps(installed,indent=2))
print('HOTWIRE_SHOTGUN_INSTALLED; all other character assets and gameplay settings retained.')
