"""Isolate explicit traffic prefab authoring from the user's open Unity project."""
import json
import re
import shutil
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
DEST=ROOT/'Temp/NPCTrafficPrefabAuthoring'
for part in ('Assets/Editor','Packages','ProjectSettings'):(DEST/part).mkdir(parents=True,exist_ok=True)
def copy(path):
    target=DEST/path.relative_to(ROOT);target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(path,target)
    meta=Path(str(path)+'.meta')
    if meta.exists():shutil.copy2(meta,str(target)+'.meta')
for path in (ROOT/'Assets').rglob('*'):
    if path.is_file() and path.suffix in ('.cs','.asmdef','.dll','.rsp','.shader','.hlsl','.cginc') and not any(p in ('Editor','Tests') for p in path.relative_to(ROOT/'Assets').parts):copy(path)
by_guid={}
for meta in (ROOT/'Assets').rglob('*.meta'):
    match=re.search(r'^guid: ([a-f0-9]{32})',meta.read_text(errors='ignore'),re.M)
    if match:by_guid[match[1]]=Path(str(meta)[:-5])
seen=set()
def dependency(path):
    if path in seen or not path.is_file():return
    seen.add(path);copy(path)
    if path.suffix in ('.prefab','.asset','.mat','.controller','.anim','.fbx'):
        text=Path(str(path)+'.meta').read_text(errors='ignore') if path.suffix=='.fbx' else path.read_text(errors='ignore')
        for guid in re.findall(r'guid: ([a-f0-9]{32})',text):
            if guid in by_guid:dependency(by_guid[guid])
for relative in ('Assets/_Project/Prefabs/Vehicles/TrafficCar.prefab','Assets/_Project/Resources/Traffic/TrafficSettings.asset'):
    dependency(ROOT/relative)
shutil.copytree(ROOT/'Assets/_Project/Models/Vehicles/NPCTraffic',DEST/'Assets/_Project/Models/Vehicles/NPCTraffic',dirs_exist_ok=True)
for relative in ('ProjectSettings/ProjectVersion.txt','ProjectSettings/TagManager.asset'):copy(ROOT/relative)
shutil.copy2(ROOT/'Tools/Unity/AuthorNPCTrafficPrefab.cs',DEST/'Assets/Editor/AuthorNPCTrafficPrefab.cs')
cache={}
for path in list((ROOT/'Library/PackageCache').glob('*/package.json'))+list((ROOT/'Packages').glob('*/package.json')):
    info=json.loads(path.read_text(encoding='utf-8'));cache[info['name']]=(path.parent,info)
deps={}
def package(name,version):
    if name in deps:return
    if name in cache:
        path,info=cache[name];deps[name]='file:'+path.as_posix()
        for child,value in info.get('dependencies',{}).items():package(child,value)
    else:deps[name]=version
for name,version in json.loads((ROOT/'Packages/manifest.json').read_text())['dependencies'].items():
    if name not in ('com.unity.collab-proxy','com.unity.ide.rider','com.unity.ide.visualstudio','com.unity.multiplayer.center'):package(name,version)
(DEST/'Packages/manifest.json').write_text(json.dumps({'dependencies':deps},indent=2))
print(DEST)
