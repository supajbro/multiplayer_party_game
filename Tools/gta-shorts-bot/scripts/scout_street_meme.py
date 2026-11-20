"""Inspect a fresh source excerpt locally; no queue or upload operations."""
import json
from pathlib import Path
import shutil
from datetime import datetime, timezone
import requests
from PIL import Image, ImageDraw
from shorts.core import load, digest
from shorts import media, ai

cfg = load(Path('config.json'))
root = cfg.data_dir / 'gta6-meme-test'
source = root / 'kai-street-review.mp4'
evidence = root / 'street-evidence'
evidence.mkdir(exist_ok=True)
for name in ['kai-full-campaign-rules.txt', 'kai-campaign.html', 'source-metadata.json', 'source-url.txt']:
    shutil.copy2(root / name, evidence / name)
shutil.copy2(root / 'garage-draft-20261119-191606/private-upload-rights-review.json', evidence / 'prior-campaign-review.json')
urls = {
    'campaign-current.html': 'https://beta.contentrewards.com/c/campaigns/1d2bcb00-c904-415f-aa82-f95b96baad86',
    'archive-current.json': 'https://archive.org/metadata/kai-cenats-first-time-playing-gta-6',
    'music-current.html': 'https://incompetech.com/music/royalty-free/index.html?isrc=USUAN1400011',
    'rules-current.txt': 'https://docs.google.com/document/d/1z3h2gDIdr2LFgDrSkXf6rIIA72y62nluKHdWgki5iG0/export?format=txt',
    'publisher-current.html': 'https://support.rockstargames.com/articles/7bNaeoMFTV0iUDGhStTXvz/policy-on-posting-copyrighted-rockstar-games-material',
}
log = []
for name, url in urls.items():
    try:
        r = requests.get(url, timeout=40)
        (evidence / name).write_bytes(r.content)
        log.append({'url': url, 'status': r.status_code, 'file': name, 'sha256': digest(evidence / name)})
    except requests.RequestException as exc:
        log.append({'url': url, 'error': str(exc)})
(evidence / 'retrieval.json').write_text(json.dumps({'checked': datetime.now(timezone.utc).isoformat(), 'requests': log}, indent=2))
sheet = Image.new('RGB', (1280, 4*205), '#171717')
draw = ImageDraw.Draw(sheet)
for i, t in enumerate(range(0, 180, 12)):
    p = root / f'street-{t}.jpg'
    media.frame(cfg, source, t, p)
    im = Image.open(p).resize((320,180))
    x, y = i%4*320, i//4*205
    sheet.paste(im, (x,y))
    draw.text((x+5,y+182), str(t), fill='white')
sheet.save(root / 'street-contact.jpg')
print('Contact sheet ready', flush=True)
transcript = ai.transcribe(cfg, source)
(root / 'street-transcript.json').write_text(json.dumps(transcript, indent=2), encoding='utf-8')
print(json.dumps(transcript), flush=True)
