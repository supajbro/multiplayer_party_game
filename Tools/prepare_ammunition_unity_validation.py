"""Copy only ammunition dependencies into a fresh isolated Unity project."""
import json
import argparse
import os
import shutil
from datetime import datetime
from pathlib import Path

root=Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--reuse',action='store_true')
args=parser.parse_args()
if args.reuse:
    project=Path((root/'ArtSource/Weapons/Ammunition/ValidationProject.txt').read_text()).resolve()
    project.relative_to(Path(os.environ['TEMP']).resolve())
    assert project.name.startswith('PixalAmmunitionValidation_')
else:
    project=Path(os.environ['TEMP'])/('PixalAmmunitionValidation_'+datetime.now().strftime('%Y%m%d_%H%M%S'))
    project.mkdir()
for directory in ('Assets/Editor','Packages','ProjectSettings'):
    (project/directory).mkdir(parents=True,exist_ok=True)

def copy_file(relative):
    source=root/relative
    target=project/relative
    target.parent.mkdir(parents=True,exist_ok=True)
    shutil.copy2(source,target)
    if Path(str(source)+'.meta').exists():
        shutil.copy2(Path(str(source)+'.meta'),Path(str(target)+'.meta'))

for folder in ('Models/Weapons/Ammunition','Materials/Weapons/Ammunition',
               'Textures/Weapons/Ammunition','Prefabs/Weapons/Ammunition'):
    for path in (root/'Assets/_Project'/folder).rglob('*'):
        if path.is_file(): copy_file(path.relative_to(root))
for name in ('Pistol_Bullet','AssaultRifle_Bullet','Shotgun_Pellet'):
    copy_file(f'Assets/_Project/Prefabs/Weapons/Projectiles/{name}.prefab')
for name in ('Bullet_Yellow','Bullet_Orange'):
    copy_file(f'Assets/_Project/Prefabs/Weapons/Materials/{name}.mat')
for name in ('WeaponDefinition','WeaponProjectileVisual','PlayerWeaponType','ReticleSettings'):
    copy_file(f'Assets/_Project/Scripts/Gameplay/Weapons/{name}.cs')
shutil.copy2(root/'Tools/Unity/ValidatePixalAmmunition.cs',project/'Assets/Editor/ValidatePixalAmmunition.cs')
shutil.copy2(root/'ProjectSettings/ProjectVersion.txt',project/'ProjectSettings/ProjectVersion.txt')
packages={}
for path in (root/'Library/PackageCache').glob('*/package.json'):
    data=json.loads(path.read_text(encoding='utf-8'))
    packages[data['name']] = (path.parent,data)
dependencies={}
def add_package(name,version):
    if name in dependencies: return
    if name in packages:
        path,data=packages[name]
        dependencies[name]='file:'+path.as_posix()
        for child,child_version in data.get('dependencies',{}).items():
            add_package(child,child_version)
    else: dependencies[name]=version
add_package('com.unity.render-pipelines.universal','17.3.0')
for name in ('particlesystem','physics','imageconversion','jsonserialize'):
    dependencies['com.unity.modules.'+name]='1.0.0'
(project/'Packages/manifest.json').write_text(json.dumps({'dependencies':dependencies},indent=2))
(root/'ArtSource/Weapons/Ammunition/ValidationProject.txt').write_text(str(project))
print(project)
