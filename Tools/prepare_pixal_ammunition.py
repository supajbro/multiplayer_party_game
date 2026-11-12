"""Prepare saved references and reproducible local-only Pixal3D workflows."""
import copy
import json
import shutil
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'ArtSource/Weapons/Ammunition'
INSTALL = Path(r'C:\Users\Jaden\AI\Pixal3D')
manifest = json.loads((SOURCE / 'ReferencePrompts.json').read_text(encoding='utf-8'))
(SOURCE / 'References').mkdir(parents=True, exist_ok=True)
(SOURCE / 'Workflows').mkdir(exist_ok=True)
base = json.loads((INSTALL / 'workflows/pixal3d-1024-api.json').read_text(encoding='utf-8'))
for asset in manifest['assets']:
    name = asset['name']
    shutil.copy2(asset['source'], SOURCE / 'References' / (name + '.png'))
    workflow = copy.deepcopy(base)
    workflow['122']['inputs']['image'] = name + '.png'
    is_projectile = name.endswith(('Bullet', 'Pellet'))
    budget = 96 if name.endswith('Pellet') else 256 if is_projectile else 1200
    workflow['186']['inputs']['target_face_count'] = budget
    workflow['241']['inputs']['resolution'] = 128
    workflow['241']['inputs']['smooth_iters'] = 2
    workflow['288']['inputs']['value'] = 256 if is_projectile else 512
    workflow['224']['inputs']['resolution'] = 256 if is_projectile else 512
    workflow['233']['inputs'].update(resolution=256, samples=8)
    workflow['322']['inputs']['filename_prefix'] = 'Ammunition/' + name
    # Use the official foreground segmentation path. LoadImage's mask is
    # inverse alpha, so its optional bypass needs an explicit inversion too.
    workflow['500']={'class_type':'InvertMask','inputs':{'mask':['122',1]}}
    workflow['248']['inputs']['on_false']=['500',0]
    workflow['248']['inputs']['switch'] = True
    # One saved GLB and an actual rendered thumbnail for visual QA.
    workflow['400'] = {'class_type':'RenderMesh','inputs':{
        'mesh':['210',0], 'mode':'auto', 'width':512, 'height':512,
        'background':'#252b34'}}
    workflow['401'] = {'class_type':'SaveImage','inputs':{
        'images':['400',0], 'filename_prefix':'Ammunition/Previews/'+name}}
    workflow['402'] = {'class_type':'SaveImage','inputs':{
        'images':['312',0], 'filename_prefix':'Ammunition/Inputs/'+name}}
    (SOURCE/'Workflows'/(name+'.json')).write_text(json.dumps(workflow,indent=2),encoding='utf-8')
print('Prepared six references and six local-only Pixal3D workflows.')
