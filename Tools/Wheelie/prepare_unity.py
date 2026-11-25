"""Create only Wheelie's saved toon materials and model import settings."""
from pathlib import Path
import re, uuid
root=Path(__file__).resolve().parents[2]
out=root/'Assets/_Project/Models/Wheelie'
folder=out/'Materials';folder.mkdir(exist_ok=True)
template=(root/'Assets/_Project/Materials/Toon/Generated/Props_WhiteBody_73579fa6_8237118932930581458.mat').read_text()
def guid(path):
 meta=Path(str(path)+'.meta')
 if meta.exists():return re.search(r'guid: ([a-f0-9]+)',meta.read_text())[1]
 return uuid.uuid5(uuid.NAMESPACE_URL,'couch-guys/'+str(path.relative_to(root)).replace('\\','/')).hex
palette={'Wheelie_Armour':((.964,.978,1),(0,0,0),.3),
 'Wheelie_Mechanics':((.206,.235,.283),(0,0,0),.22),
 'Wheelie_Tyre':((.133,.156,.186),(0,0,0),.04),
 'Wheelie_Cyan':((.03,.86,1),(.03,1.58,2.2),.3),
 'Wheelie_Screen':((.001,.002,.004),(0,0,0),.16)}
remaps=[]
for name,(color,emission,spec) in palette.items():
 path=folder/(name+'.mat');g=guid(path)
 text=re.sub(r'm_Name: .*','m_Name: '+name,template)
 for prop,val in [('_BaseColor',color),('_EmissionColor',emission)]:
  text=re.sub(r'(- '+prop+r': )\{[^}]+\}',lambda m:m[1]+'{r: %s, g: %s, b: %s, a: 1}'%val,text)
 text=re.sub(r'(- _SpecularIntensity: )[^\r\n]+',lambda m:m[1]+str(spec),text)
 text=text.replace('m_EnableInstancingVariants: 0','m_EnableInstancingVariants: 1')
 path.write_text(text)
 Path(str(path)+'.meta').write_text('fileFormatVersion: 2\nguid: '+g+'\nNativeFormatImporter:\n  externalObjects: {}\n  mainObjectFileID: 2100000\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n')
 remaps.append('  - first:\n      type: UnityEngine:Material\n      assembly: UnityEngine.CoreModule\n      name: '+name+'\n    second: {fileID: 2100000, guid: '+g+', type: 2}')
path=out/'Wheelie.fbx'; g=guid(path)
text=(root/'Assets/_Project/Models/Weapons/GrappleGun/CG_GrappleGun.fbx.meta').read_text()
text=re.sub(r'guid: [a-f0-9]+','guid: '+g,text,count=1)
text=text.replace('  externalObjects: {}','  externalObjects:\n'+'\n'.join(remaps))
for before,after in [('optimizeBones: 1','optimizeBones: 0'),('preserveHierarchy: 0','preserveHierarchy: 1'),('importCameras: 1','importCameras: 0'),('importLights: 1','importLights: 0'),('avatarSetup: 0','avatarSetup: 1')]:text=text.replace(before,after)
Path(str(path)+'.meta').write_text(text)
for path in [out,folder]:
 meta=Path(str(path)+'.meta')
 if not meta.exists():meta.write_text('fileFormatVersion: 2\nguid: '+guid(path)+'\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n')
print('Prepared five toon material assets and explicit FBX material remapping.')
