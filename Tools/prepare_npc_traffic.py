"""Explicit reference crops and copies of the existing local Pixal3D workflow."""
import copy
import json
import shutil
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'ArtSource/Vehicles/NPCTraffic'
SOURCE.mkdir(parents=True, exist_ok=True)
reference = Path.home() / 'Downloads/Couch Guys Traffic Car Concepts.png'
shutil.copy2(reference, SOURCE / 'ConceptSheet.png')
image = Image.open(reference)
base = json.loads((ROOT / 'ArtSource/Vehicles/Flamecouch/Pixal3DWorkflow.json').read_text())
# Coordinates exclude neighbouring vehicles, text, and the unrequested van.
boxes = {
    'NPC_CityHatch': (65, 180, 548, 486),
    'NPC_FamilySUV': (579, 157, 1107, 486),
    'NPC_UtilityPickup': (1127, 145, 1650, 486),
    'NPC_SportCoupe': (622, 583, 1144, 861),
}
for name, box in boxes.items():
    crop = image.crop(box)
    path = SOURCE / 'References' / (name + '.png')
    path.parent.mkdir(exist_ok=True)
    crop.save(path)
    workflow = copy.deepcopy(base)
    workflow['122']['inputs']['image'] = path.name
    workflow['186']['inputs']['target_face_count'] = 10000
    workflow['322']['inputs']['filename_prefix'] = 'NPCTraffic/' + name
    # Preserve segmentation, geometry, UV, and texture generation from the proven graph.
    workflow['400'] = {'class_type':'RenderMesh','inputs':{'mesh':['210',0], 'mode':'auto','width':768,'height':768,'background':'#252b34'}}
    workflow['401'] = {'class_type':'SaveImage','inputs':{'images':['400',0],'filename_prefix':'NPCTraffic/Previews/'+name}}
    workflow['402'] = {'class_type':'SaveImage','inputs':{'images':['312',0],'filename_prefix':'NPCTraffic/Inputs/'+name}}
    output = SOURCE / 'Workflows' / (name + '.json')
    output.parent.mkdir(exist_ok=True)
    output.write_text(json.dumps(workflow, indent=2))
    print(output)
