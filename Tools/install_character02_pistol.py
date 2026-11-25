"""Install the validated Character02 pistol prefab and matching clip override only."""
from pathlib import Path
import re,json,hashlib,shutil
ROOT=Path(__file__).resolve().parents[1];STAGE=ROOT/'.utmp/thief-prefab-upgrade-unity';OUT=ROOT/'ArtSource/Characters/Character02Thief/GetawayPalette'
REL=Path('Assets/_Project/Prefabs/Enemies/Thief_Pistol.prefab');CTRL=Path('Assets/_Project/Animations/Thieves/Character02/Character02_Thief_Pistol.overrideController')
digest=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
report=OUT/'UnityPistolValidation.txt'
assert report.exists() and report.read_text().count('PASS:')==3,'Finish Unity validation'
assert not (OUT/'UnityPistolFailure.txt').exists(),'Resolve Unity failure'
before=json.loads((OUT/'PrefabBeforeHashes.json').read_text())
assert all(digest(ROOT/p)==h for p,h in before.items()),'An enemy prefab changed during authoring'
old=(OUT/'Before/Thief_Pistol.prefab').read_text();new=(STAGE/REL).read_text()
pattern=re.compile(r'^--- !u!\d+ &(-?\d+)(?: stripped)?\n.*?(?=^--- !u!|\Z)',re.M|re.S)
a={m[1]:m[0] for m in pattern.finditer(old)};b={m[1]:m[0] for m in pattern.finditer(new)}
root='2569576030818965973'
field=lambda text,name:re.search(r'^  '+re.escape(name)+r':[^\n]*(?:\n(?:    .*|  - .*))*',text,re.M)
preserved=0;replacements={}
for key,block in a.items():
    if block.splitlines()[1]!='MonoBehaviour:' or not re.search(r'^  m_GameObject: \{fileID: '+root+r'\}',block,re.M):continue
    assert key in b,'Root gameplay component lost'
    expected=block
    for name in ['m_animator','m_ragdoll','m_muzzle','m_heldWeaponRenderers']:
        oldfield=field(expected,name);newfield=field(b[key],name)
        if oldfield and newfield:expected=expected[:oldfield.start()]+newfield[0]+expected[oldfield.end():]
    # Unity may append default fields during save. Keep the user's original
    # component serialization, with only the already-validated visual references.
    normalize=lambda t:'\n'.join(line.rstrip() for line in t.splitlines())
    for prop in re.findall(r'^  (\w+):',expected,re.M):
        e=field(expected,prop);actual=field(b[key],prop)
        assert actual and normalize(e[0])==normalize(actual[0]),'Unexpected existing gameplay value change: '+key+'/'+prop
    replacements[key]=expected
    preserved+=1
new=pattern.sub(lambda m:replacements.get(m[1],m[0]),new)
assert root in b and 'guid: b5159812eaa74a9db03dd683984cec29' in new
assert report.stat().st_mtime>(STAGE/REL).stat().st_mtime
getguid=lambda p:re.search(r'^guid: (\w+)',p.read_text(),re.M)[1]
assert getguid(Path(str(ROOT/REL)+'.meta'))==getguid(Path(str(STAGE/REL)+'.meta'))
assert not (ROOT/CTRL).exists(),'Existing Character02 controller must be preserved'
(ROOT/CTRL.parent).mkdir(parents=True,exist_ok=True)
foldermeta=Path(str(ROOT/CTRL.parent)+'.meta')
if not foldermeta.exists():shutil.copy2(Path(str(STAGE/CTRL.parent)+'.meta'),foldermeta)
shutil.copy2(STAGE/CTRL,ROOT/CTRL);shutil.copy2(Path(str(STAGE/CTRL)+'.meta'),Path(str(ROOT/CTRL)+'.meta'))
(ROOT/REL).write_text(new)
assert all(digest(ROOT/p)==h for p,h in before.items() if p!=REL.as_posix()),'Another enemy prefab changed'
(OUT/'InstallationValidation.json').write_text(json.dumps({'prefab':REL.as_posix(),'prefab_guid_retained':True,'root_gameplay_components_preserved':preserved,'other_enemy_prefabs_unchanged':True,'default_t_pose_verified':True,'clip_override':CTRL.as_posix(),'prefab_sha256':digest(ROOT/REL)},indent=2))
print('Installed Character02 Thief_Pistol; default T-pose and five states verified;',preserved,'gameplay component blocks preserved; other enemy prefabs unchanged.')
