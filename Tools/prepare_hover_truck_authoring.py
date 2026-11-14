"""Prepare an isolated Unity project for explicit hover truck authoring and validation."""
import json
import os
import re
import shutil
import uuid
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PROJECT = Path(os.environ['TEMP']) / 'CouchGuysHoverTruckAuthoring'
SOURCE = ROOT / 'ArtSource/Vehicles/HoverTruck'
for directory in ('Assets/Editor', 'Packages', 'ProjectSettings'):
    (PROJECT / directory).mkdir(parents=True, exist_ok=True)


def copy_file(path):
    relative = path.relative_to(ROOT)
    target = PROJECT / relative
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(path, target)
    meta = Path(str(path) + '.meta')
    if meta.exists():
        shutil.copy2(meta, str(target) + '.meta')


# Stable script GUIDs are required before the prefab authoring project imports them.
for path in (ROOT / 'Assets/_Project/Scripts/Gameplay/Vehicles').glob('*.cs'):
    meta = Path(str(path) + '.meta')
    if not meta.exists():
        meta.write_text('fileFormatVersion: 2\nguid: ' + uuid.uuid4().hex + '\n')
for path in (ROOT / 'Assets').rglob('*'):
    if not path.is_file() or path.suffix not in ('.cs', '.asmdef', '.dll', '.rsp', '.shader', '.hlsl', '.cginc'):
        continue
    relative = path.relative_to(ROOT).as_posix()
    if '/_Project/Scripts/Editor/' in relative or '/_Project/Tests/' in relative:
        continue
    copy_file(path)

# Follow the existing prefab's serialized dependencies, keeping their GUIDs.
by_guid = {}
for meta in (ROOT / 'Assets').rglob('*.meta'):
    match = re.search(r'^guid: ([a-f0-9]{32})', meta.read_text(errors='ignore'), re.M)
    if match:
        by_guid[match[1]] = Path(str(meta)[:-5])
seen = set()


def dependency(path):
    if path in seen or not path.is_file():
        return
    seen.add(path)
    copy_file(path)
    if path.suffix in ('.prefab', '.asset', '.mat', '.controller', '.anim'):
        for guid in re.findall(r'guid: ([a-f0-9]{32})', path.read_text(errors='ignore')):
            if guid in by_guid:
                dependency(by_guid[guid])


for relative in ('Assets/_Project/Prefabs/Vehicles/Truck.prefab',
                 'Assets/_Project/Prefabs/Player.prefab',
                 'Assets/_Project/Prefabs/Couch.prefab',
                 'Assets/_Project/Prefabs/Weapons/Effects/Impact_MetalSparks.prefab'):
    dependency(ROOT / relative)
for name in ('TruckSeatingTests', 'TruckCargoTests', 'MultipleDeliveryTests', 'MinimapNavigationTests', 'CameraCollisionTests'):
    copy_file(ROOT / ('Assets/_Project/Tests/Editor/' + name + '.cs'))
for path in (ROOT / 'Assets/_Project/Models/Vehicles/HoverTruck').rglob('*'):
    if path.is_file():
        copy_file(path)
for relative in ('ProjectSettings/ProjectVersion.txt', 'ProjectSettings/TagManager.asset', 'ProjectSettings/ProjectSettings.asset'):
    copy_file(ROOT / relative)
for path in (ROOT / 'Tools/Unity').glob('*HoverTruck*.cs'):
    shutil.copy2(path, PROJECT / 'Assets/Editor' / path.name)

cache = {}
for package in list((ROOT / 'Library/PackageCache').glob('*/package.json')) + list((ROOT / 'Packages').glob('*/package.json')):
    data = json.loads(package.read_text(encoding='utf-8'))
    cache[data['name']] = (package.parent, data)
manifest = json.loads((ROOT / 'Packages/manifest.json').read_text())
dependencies = {}


def package(name, version):
    if name in dependencies:
        return
    if name in cache:
        path, data = cache[name]
        dependencies[name] = 'file:' + path.as_posix()
        for child, child_version in data.get('dependencies', {}).items():
            package(child, child_version)
    else:
        dependencies[name] = version


for name, version in manifest['dependencies'].items():
    if name in ('com.unity.collab-proxy', 'com.unity.ide.rider', 'com.unity.ide.visualstudio', 'com.unity.multiplayer.center'):
        continue
    package(name, version)
(PROJECT / 'Packages/manifest.json').write_text(json.dumps({'dependencies': dependencies}, indent=2))
(SOURCE / 'AuthoringProject.txt').write_text(str(PROJECT))
print(PROJECT)
