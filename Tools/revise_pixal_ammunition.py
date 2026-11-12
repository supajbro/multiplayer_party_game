"""Apply revised source references and save the corrected generation revision."""
import json
import shutil
from pathlib import Path

root=Path(__file__).resolve().parents[1]/'ArtSource/Weapons/Ammunition'
revisions=json.loads((root/'RevisionPrompts.json').read_text())
(root/'References/Originals').mkdir(exist_ok=True)
for item in revisions['assets']:
    name=item['name']
    source=root/'References'/(name+'.png')
    shutil.copy2(source,root/'References/Originals'/(name+'.png'))
    shutil.copy2(item['source'],source)
    shutil.copy2(source,root/'References'/(name+'_v2.png'))
for path in (root/'Workflows').glob('*.json'):
    name=path.stem
    if name.endswith('_v2'): continue
    graph=json.loads(path.read_text())
    if any(a['name']==name for a in revisions['assets']):
        graph['122']['inputs']['image']=name+'_v2.png'
    graph['322']['inputs']['filename_prefix']='Ammunition/'+name+'_v2'
    graph['401']['inputs']['filename_prefix']='Ammunition/Previews/'+name+'_v2'
    graph['402']['inputs']['filename_prefix']='Ammunition/Inputs/'+name+'_v2'
    (root/'Workflows'/(name+'_v2.json')).write_text(json.dumps(graph,indent=2))
print('Saved six corrected generation-revision workflows.')
