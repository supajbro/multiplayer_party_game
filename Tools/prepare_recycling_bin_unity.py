"""Prepare a disposable Unity authoring project; existing assets are read-only inputs."""
from pathlib import Path
import shutil,re,uuid
ROOT=Path(__file__).resolve().parents[1]
stage=ROOT/'Temp/RecyclingBinAuthoring'
(stage/'Packages').mkdir(parents=True,exist_ok=True)
(stage/'Packages/manifest.json').write_text('{"dependencies":{"com.unity.modules.physics":"1.0.0","com.unity.modules.jsonserialize":"1.0.0"}}')
(stage/'ProjectSettings').mkdir(exist_ok=True)
shutil.copy2(ROOT/'ProjectSettings/ProjectVersion.txt',stage/'ProjectSettings/ProjectVersion.txt')
target=stage/'Assets/_Project/Models/Props/RecyclingBin'
target.mkdir(parents=True,exist_ok=True)
for f in (ROOT/'Assets/_Project/Models/Props/RecyclingBin').glob('*.fbx'):shutil.copy2(f,target/f.name)
for n in ['D02_Silver','D02_Shutter','D02_Steel','D02_Teal','D02_Yellow','D02_Rubber','D02_Light']:
    for ext in ['.mat','.mat.meta']:
        src=ROOT/'Assets/_Project/Generated/Depot02'/(n+ext);dst=stage/src.relative_to(ROOT);dst.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(src,dst)
template=(ROOT/'Assets/_Project/Generated/Depot02/D02_Light.mat').read_text()
for name,color,strength in [('RB_GreenEmission',(.10,.95,.025),2.6),('RB_InteriorGlow',(.015,.16,.025),.8)]:
    t=template.replace('m_Name: D02_Light','m_Name: '+name)
    for prop,mult in [('_BaseColor',1),('_Color',1),('_EmissionColor',strength)]:
        value='{r: %g, g: %g, b: %g, a: 1}'%tuple(c*mult for c in color)
        t=re.sub(r'('+prop+r': )\{[^}]+\}',lambda m:m[1]+value,t)
    path=target/'Materials'/(name+'.mat');path.parent.mkdir(exist_ok=True);path.write_text(t)
    Path(str(path)+'.meta').write_text('fileFormatVersion: 2\nguid: '+uuid.uuid4().hex+'\nNativeFormatImporter:\n  externalObjects: {}\n  mainObjectFileID: 2100000\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n')
editor=stage/'Assets/Editor';editor.mkdir(parents=True,exist_ok=True)
shutil.copy2(ROOT/'Tools/Unity/AuthorRecyclingBin.cs',editor/'AuthorRecyclingBin.cs')
print(stage)
