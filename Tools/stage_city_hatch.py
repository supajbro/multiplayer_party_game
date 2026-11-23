"""Stage only this visual prefab and its dependencies for isolated Unity authoring."""
from pathlib import Path
import shutil,re,uuid,json
ROOT=Path(__file__).resolve().parents[1]
STAGE=ROOT/'.utmp/cityhatch-unity'
for folder in ['Packages','ProjectSettings','Assets/Editor']:(STAGE/folder).mkdir(parents=True,exist_ok=True)
# Resolve the installed packages locally; no other task's scratch project needed.
packages={}
for manifest in (ROOT/'Library/PackageCache').glob('*/package.json'):
    info=json.loads(manifest.read_text(encoding='utf-8-sig'))
    packages[info['name']]=(manifest.parent,info)
deps={};pending_packages=['com.unity.render-pipelines.universal','com.unity.modules.imageconversion','com.unity.modules.physics','com.unity.modules.imgui']
while pending_packages:
    name=pending_packages.pop()
    if name in deps:continue
    if name in packages:
        folder,info=packages[name];deps[name]='file:'+folder.as_posix()
        pending_packages.extend(info.get('dependencies',{}))
    else:deps[name]='1.0.0'
# Leave a running Unity project's package manifest undisturbed.
if not (STAGE/'Packages/manifest.json').exists():
    (STAGE/'Packages/manifest.json').write_text(json.dumps({'dependencies':deps},indent=2))
shutil.copy2(ROOT/'ProjectSettings/ProjectVersion.txt',STAGE/'ProjectSettings/ProjectVersion.txt')
model=ROOT/'Assets/_Project/Models/Vehicles/NPCTraffic'
template=(model/'NPC_CityHatch.fbx.meta').read_text()
for level in [1,2]:
    meta=model/f'NPC_CityHatch_LOD{level}.fbx.meta'
    if not meta.exists():meta.write_text(re.sub(r'^guid: \w+',f'guid: {uuid.uuid4().hex}',template,flags=re.M))
index={}
for meta in (ROOT/'Assets').rglob('*.meta'):
    m=re.search(r'^guid: (\w+)',meta.read_text(errors='replace'),re.M)
    if m:index[m[1]]=Path(str(meta)[:-5])
def copy(p):
    dest=STAGE/p.relative_to(ROOT);dest.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(p,dest)
    meta=Path(str(p)+'.meta')
    if meta.exists():shutil.copy2(meta,Path(str(dest)+'.meta'))
pending=[ROOT/'Assets/_Project/Prefabs/Vehicles/NPCTraffic/NPC_CityHatch.prefab',model/'NPC_CityHatch_LOD1.fbx',model/'NPC_CityHatch_LOD2.fbx'];seen=set()
while pending:
    p=pending.pop()
    if p in seen or not p.is_file():continue
    seen.add(p);copy(p)
    meta=Path(str(p)+'.meta');text=meta.read_text() if meta.exists() else ''
    if p.suffix in ['.prefab','.mat','.asset']:text+=p.read_text()
    for g in re.findall(r'guid: ([a-f0-9]{32})',text):
        if g in index:pending.append(index[g])
shutil.copy2(ROOT/'Tools/Unity/AuthorCityHatchLODs.cs',STAGE/'Assets/Editor/AuthorCityHatchLODs.cs')
spec=ROOT/'ArtSource/Vehicles/NPCTraffic/NPC_CityHatch_PBRMaterials.json'
if spec.exists():shutil.copy2(spec,STAGE/'CityHatchPBRMaterials.json')
# Compile and exercise the actual production tint method against Unity Renderers,
# without importing FishNet or the rest of gameplay into this visual-only project.
controller=(ROOT/'Assets/_Project/Scripts/Gameplay/Traffic/TrafficVehicleController.cs').read_text()
start=controller.index('private void SetVisualColour(Color colour)')
brace=controller.index('{',start);depth=1;end=brace+1
while depth:
    depth+=(controller[end]=='{')-(controller[end]=='}');end+=1
method=controller[start:end]
probe='''using System;
using UnityEngine;
public static class CityHatchTintProbe {
 class Driver {
  public Renderer[] m_renderers; MaterialPropertyBlock m_propertyBlock;
  public void Apply(Color color){SetVisualColour(color);}
  METHOD
 }
 public static void Check(Renderer[] renderers){
  var driver=new Driver{m_renderers=renderers};
  foreach(var tint in new[]{new Color(.4f,.5f,.6f),Color.white}){
   driver.Apply(tint);
   foreach(var renderer in renderers){
    var materials=renderer.sharedMaterials;
    for(int i=0;i<materials.Length;i++){
     var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block,i);
     var original=materials[i].GetColor("_BaseColor");var expected=original*tint;expected.a=original.a;
     if(Vector4.Distance(block.GetColor("_BaseColor"),expected)>.0001f)throw new Exception("Per-material damage/reset tint failed: "+materials[i].name);
    }
   }
  }
 }
}
'''.replace('METHOD',method)
(STAGE/'Assets/Editor/CityHatchTintProbe.cs').write_text(probe)
print('Staged',len(seen),'visual assets')
