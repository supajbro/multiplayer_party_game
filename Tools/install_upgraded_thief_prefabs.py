"""Install only validated prefab upgrades and their new attachment mesh assets."""
from pathlib import Path
import re,json,hashlib,shutil,runpy
ROOT=Path(__file__).resolve().parents[1]
STAGE=ROOT/'.utmp/thief-prefab-upgrade-unity'
REPORT=ROOT/'ArtSource/Characters/ThiefEnemy01/PrefabUpgrade'
assert (REPORT/'SavedPrefabValidation.txt').read_text().count('PASS ')==7
# Drop unrelated default-field serialization which Unity may have added while importing.
runpy.run_path(str(ROOT/'Tools/preserve_thief_prefab_root_settings.py'))
manifest=json.loads((REPORT/'OriginalPrefabHashes.json').read_text())
for rel,digest in manifest.items():
    assert hashlib.sha256((ROOT/rel).read_bytes()).hexdigest()==digest,'Prefab changed during authoring: '+rel
    src=STAGE/rel
    a=Path(str(ROOT/rel)+'.meta').read_text();b=Path(str(src)+'.meta').read_text()
    assert re.search(r'^guid: (\w+)',a,re.M)[1]==re.search(r'^guid: (\w+)',b,re.M)[1]
attachments='Assets/_Project/Models/Thieves/ThiefEnemy01Attachments'
assert not (ROOT/attachments).exists(),'Existing attachment assets must not be overwritten implicitly'
shutil.copytree(STAGE/attachments,ROOT/attachments)
shutil.copy2(STAGE/(attachments+'.meta'),ROOT/(attachments+'.meta'))
for rel in manifest:
    (ROOT/rel).write_text('\n'.join(line.rstrip() for line in (STAGE/rel).read_text().splitlines())+'\n')
    # Keep original prefab metas/identities; trim only Unity's empty-value whitespace.
installed={}
for p in [*(ROOT/rel for rel in manifest),*(ROOT/attachments).rglob('*'),ROOT/(attachments+'.meta')]:
    if p.is_file():installed[p.relative_to(ROOT).as_posix()]=hashlib.sha256(p.read_bytes()).hexdigest()
(REPORT/'InstalledPrefabHashes.json').write_text(json.dumps(installed,indent=2))
print('Installed seven upgraded prefabs and saved weapon attachment meshes; preserved all original prefab GUIDs.')
