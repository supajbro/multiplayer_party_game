"""Acquire and inspect a fresh marathon excerpt; never uploads."""
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime, timezone
import json
from pathlib import Path
import requests
from PIL import Image, ImageDraw
from shorts.core import load, digest
from shorts import media, ai

cfg = load(Path('config.json'))
root = cfg.data_dir / 'gta6-meme-test' / 'new-moment-20261122'
root.mkdir(exist_ok=True)
evidence = root / 'evidence'
evidence.mkdir(exist_ok=True)
urls = {
    'campaign.html': 'https://beta.contentrewards.com/c/campaigns/1d2bcb00-c904-415f-aa82-f95b96baad86',
    'rules.txt': 'https://docs.google.com/document/d/1z3h2gDIdr2LFgDrSkXf6rIIA72y62nluKHdWgki5iG0/export?format=txt',
    'archive.json': 'https://archive.org/metadata/kai-cenats-first-time-playing-gta-6',
    'music.html': 'https://incompetech.com/music/royalty-free/index.html?isrc=USUAN1400011',
    'music-license.html': 'https://creativecommons.org/licenses/by/4.0/',
    'publisher.html': 'https://support.rockstargames.com/articles/7bNaeoMFTV0iUDGhStTXvz/policy-on-posting-copyrighted-rockstar-games-material',
}
def fetch(item):
    name, url = item
    try:
        r = requests.get(url, timeout=35)
        (evidence/name).write_bytes(r.content)
        return dict(url=url,status=r.status_code,file=name,sha256=digest(evidence/name))
    except requests.RequestException as exc:
        return dict(url=url,error=str(exc))
if not (evidence/'retrieval.json').exists():
    with ThreadPoolExecutor(max_workers=6) as pool:
        records = list(pool.map(fetch, urls.items()))
    (evidence/'retrieval.json').write_text(json.dumps(dict(checked=datetime.now(timezone.utc).isoformat(),requests=records),indent=2))
    print(json.dumps(records),flush=True)
source = root/'source-5100.mp4'
url=(cfg.data_dir/'gta6-meme-test/source-url.txt').read_text().strip().replace('archive.org/download/', 'ia601807.us.archive.org/20/items/')
try:
    assert 179 < media.duration(cfg,source) < 183
except (AssertionError, RuntimeError):
    temporary=root/'download.partial.mp4'
    media.ffmpeg(cfg,['-rw_timeout','20000000','-ss','5100','-i',url,'-t','180','-map','0:v:0','-map','0:a:0','-c','copy',temporary],timeout=180)
    assert 179 < media.duration(cfg,temporary) < 183
    temporary.replace(source)
(root/'source.json').write_text(json.dumps(dict(url=url,vod_offset=5100,sha256=digest(source)),indent=2))
sheet=Image.new('RGB',(1280,5*205),'#171717')
d=ImageDraw.Draw(sheet)
for i,t in enumerate(range(0,180,9)):
    p=root/f'frame-{t}.jpg'
    media.frame(cfg,source,t,p)
    x,y=i%4*320,i//4*205
    sheet.paste(Image.open(p).resize((320,180)),(x,y))
    d.text((x+5,y+182),str(t),fill='white')
sheet.save(root/'contact.jpg')
print('Frames ready',flush=True)
transcript=ai.transcribe(cfg,source)
(root/'transcript.json').write_text(json.dumps(transcript,indent=2),encoding='utf-8')
print(json.dumps([{k:s[k] for k in ['start','end','text']} for s in transcript]),flush=True)
