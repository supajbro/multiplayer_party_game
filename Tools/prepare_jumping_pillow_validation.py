"""Prepare a local isolated Unity project; preserve GUIDs and the user's open project."""
import json
import os
import shutil
import uuid
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
PROJECT=Path(os.environ['TEMP'])/'CouchGuysJumpingPillowValidation'
for directory in ('Assets/Editor','Packages','ProjectSettings'):
    (PROJECT/directory).mkdir(parents=True,exist_ok=True)

def copy(path):
    target=PROJECT/path.relative_to(ROOT)
    target.parent.mkdir(parents=True,exist_ok=True)
    shutil.copy2(path,target)
    meta=Path(str(path)+'.meta')
    if meta.exists(): shutil.copy2(meta,str(target)+'.meta')

for relative in ('Assets/_Project/Scripts/ProceduralGeneration/JumpingPillow.cs',
                 'Assets/_Project/Scripts/Editor/JumpingPillowAuthoring.cs'):
    meta=ROOT/(relative+'.meta')
    if not meta.exists(): meta.write_text('fileFormatVersion: 2\nguid: '+uuid.uuid4().hex+'\n')

for path in (ROOT/'Assets').rglob('*'):
    if not path.is_file() or path.suffix not in ('.cs','.asmdef','.dll','.rsp','.shader','.hlsl','.cginc'): continue
    relative=path.relative_to(ROOT).as_posix()
    if '/Editor/' in relative or '/Tests/' in relative: continue
    copy(path)
copy(ROOT/'Assets/_Project/Scripts/Editor/JumpingPillowAuthoring.cs')
for path in (ROOT/'Assets/_Project/Models/JumpingPillow').glob('*.fbx'): copy(path)
for path in (ROOT/'Tools/Unity').glob('*JumpingPillow*.cs'):
    shutil.copy2(path,PROJECT/'Assets/Editor'/path.name)
for name in ('ProjectVersion.txt','ProjectSettings.asset','TagManager.asset'):
    copy(ROOT/'ProjectSettings'/name)

cache={}
for path in list((ROOT/'Library/PackageCache').glob('*/package.json'))+list((ROOT/'Packages').glob('*/package.json')):
    data=json.loads(path.read_text(encoding='utf-8')); cache[data['name']]=(path.parent,data)
dependencies={}
def package(name,version):
    if name in dependencies: return
    if name in cache:
        path,data=cache[name]; dependencies[name]='file:'+path.as_posix()
        for child,v in data.get('dependencies',{}).items(): package(child,v)
    else: dependencies[name]=version
manifest=json.loads((ROOT/'Packages/manifest.json').read_text())
for name,version in manifest['dependencies'].items():
    if name in ('com.unity.collab-proxy','com.unity.ide.rider','com.unity.ide.visualstudio','com.unity.multiplayer.center'): continue
    package(name,version)
(PROJECT/'Packages/manifest.json').write_text(json.dumps({'dependencies':dependencies},indent=2))
print(PROJECT)
