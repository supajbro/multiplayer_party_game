"""Prepare an isolated Unity project for explicit magnetic carry asset authoring.

Run Unity with -batchmode -nographics -projectPath <printed path>
-executeMethod AuthorMagneticCarry.Build. No asset changes run on Editor reload.
"""
from pathlib import Path
import os
import shutil
import json
import uuid

root = Path(__file__).resolve().parents[1]
project = Path(os.environ['TEMP']) / 'CouchGuysMagneticCarryAuthoring'
for folder in ('Assets/Editor', 'Packages', 'ProjectSettings'):
    (project / folder).mkdir(parents=True, exist_ok=True)
paths = [
    'Assets/_Project/Models/CouchGuy/CouchGuy_Redesigned.fbx',
    'Assets/_Project/Animations/CouchGuy.controller',
    'Assets/_Project/Scripts/Player/MagneticCarryEffect.cs',
    'Assets/_Project/Shaders/MagneticAura.shader',
]
for path in paths:
    src, dest = root / path, project / path
    meta = Path(str(src) + '.meta')
    if not meta.exists():
        meta.write_text('fileFormatVersion: 2\nguid: ' + uuid.uuid4().hex + '\n')
    dest.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(src, dest)
    shutil.copy2(meta, str(dest) + '.meta')
shutil.copy2(root / 'Tools/Unity/AuthorMagneticCarry.cs', project / 'Assets/Editor/AuthorMagneticCarry.cs')
shutil.copy2(root / 'Tools/Unity/ValidateMagneticCarry.cs', project / 'Assets/Editor/ValidateMagneticCarry.cs')
shutil.copy2(root / 'ProjectSettings/ProjectVersion.txt', project / 'ProjectSettings/ProjectVersion.txt')
for relative in ['Assets/_Project/Scripts/Player/MagneticCarryAura.cs']:
    meta = root / (relative + '.meta')
    if not meta.exists():
        meta.write_text('fileFormatVersion: 2\nguid: ' + uuid.uuid4().hex + '\n')
# Preserve generated asset GUIDs when authoring again.
for relative in ['Assets/_Project/Animations/MagneticCarry', 'Assets/_Project/Prefabs/VFX']:
    source = root / relative
    if source.exists():
        shutil.copytree(source, project / relative, dirs_exist_ok=True)
for relative in ['Assets/_Project/Materials/Player/MagneticAura.mat']:
    source = root / relative
    if source.exists():
        target = project / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, target)
        shutil.copy2(str(source) + '.meta', str(target) + '.meta')
(project / 'Packages/manifest.json').write_text(json.dumps({'dependencies': {
    'com.unity.modules.' + name: '1.0.0' for name in ['animation', 'particlesystem', 'physics', 'imgui']
}}))
print(project)
