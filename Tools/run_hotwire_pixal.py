"""Submit the approved Hotwire reference to the existing local Pixal3D server."""
from pathlib import Path
import urllib.request,json,uuid
ROOT=Path(__file__).resolve().parents[1];SRC=ROOT/'ArtSource/Characters/Hotwire'
URL='http://127.0.0.1:8188'
def get(path):return json.load(urllib.request.urlopen(URL+path,timeout=20))
stats=get('/system_stats')
print('COMFY_DEVICES',stats.get('devices'),flush=True)
queue=get('/queue')
assert not queue.get('queue_running') and not queue.get('queue_pending'),'Server is busy; preserve existing queue'
data=(SRC/'HotwireReference.png').read_bytes();boundary='hotwire'+uuid.uuid4().hex
body=(f'--{boundary}\r\nContent-Disposition: form-data; name="image"; filename="HotwireReference.png"\r\nContent-Type: image/png\r\n\r\n'.encode()+data+f'\r\n--{boundary}--\r\n'.encode())
req=urllib.request.Request(URL+'/upload/image',data=body,headers={'Content-Type':'multipart/form-data; boundary='+boundary})
uploaded=json.load(urllib.request.urlopen(req,timeout=30));print('UPLOADED',uploaded,flush=True)
graph=json.loads((SRC/'Pixal3DWorkflow.json').read_text())
graph['122']['inputs']['image']='/'.join(x for x in [uploaded.get('subfolder'),uploaded['name']] if x)
(SRC/'Pixal3DWorkflow.json').write_text(json.dumps(graph,indent=2))
req=urllib.request.Request(URL+'/prompt',data=json.dumps({'prompt':graph,'client_id':'hotwire-authoring'}).encode(),headers={'Content-Type':'application/json'})
try:result=json.load(urllib.request.urlopen(req,timeout=30))
except urllib.error.HTTPError as e:print(e.read().decode());raise
(SRC/'GenerationJob.json').write_text(json.dumps(result,indent=2));print('SUBMITTED',result)
