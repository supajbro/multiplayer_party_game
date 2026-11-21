"""Install only this new bin's validated assets; preserve existing depot assets."""
from pathlib import Path
import shutil,json,re,uuid
ROOT=Path(__file__).resolve().parents[1];stage=ROOT/'Temp/RecyclingBinAuthoring'
validation=(stage/'validation.txt').read_text();assert validation.startswith('PASS:')
for relative in ['Assets/_Project/Models/Props/RecyclingBin','Assets/_Project/Prefabs/Props']:
    folder=stage/relative
    for source in folder.rglob('*'):
        if not source.is_file():continue
        # Install only the dedicated bin prefab, never anything else in shared folders.
        if relative.endswith('/Props') and source.name not in ['RecyclingBin.prefab','RecyclingBin.prefab.meta']:continue
        dst=ROOT/source.relative_to(stage);dst.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(source,dst)
    if relative.endswith('RecyclingBin'):
        source=Path(str(folder)+'.meta')
        if source.exists():shutil.copy2(source,ROOT/source.relative_to(stage))
# Create folder metas only where Unity has not already assigned one.
for relative in ['Assets/_Project/Models/Props','Assets/_Project/Prefabs/Props']:
    p=ROOT/(relative+'.meta')
    if not p.exists():p.write_text('fileFormatVersion: 2\nguid: '+uuid.uuid4().hex+'\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n')
src=ROOT/'ArtSource/Props/RecyclingBin';data=json.loads((src/'validation.json').read_text());data['unityValidation']=validation
(src/'validation.json').write_text(json.dumps(data,indent=2))
(src/'unity_validation.txt').write_text(validation)
# Resolve every exported prefab GUID against the final project assets.
prefab=ROOT/'Assets/_Project/Prefabs/Props/RecyclingBin.prefab'
guids=set(re.findall(r'guid: ([0-9a-f]{32})',prefab.read_text()))
known=set()
for folder in [ROOT/'Assets/_Project/Models/Props/RecyclingBin',ROOT/'Assets/_Project/Generated/Depot02']:
    for f in folder.rglob('*.meta'):
        match=re.search(r'guid: ([0-9a-f]{32})',f.read_text())
        if match:known.add(match[1])
assert guids<=known,guids-known
print(validation);print('Installed prefab GUID references:',len(guids))
