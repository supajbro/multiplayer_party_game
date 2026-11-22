"""Print retained source dialogue and a timestamped action contact sheet."""
import json
from pathlib import Path
from PIL import Image, ImageDraw
from shorts.core import load
from shorts import media
cfg=load(Path('config.json'))
root=cfg.data_dir/'gta6-meme-test'
for s in json.loads((root/'street-transcript.json').read_text()):
    if 40 < s['start'] < 92:
        print(f"{s['start']:.2f}-{s['end']:.2f}: {s['text']}",flush=True)
sheet=Image.new('RGB',(1280,5*205),'#171717')
d=ImageDraw.Draw(sheet)
for i,t in enumerate(range(48,88,2)):
    p=root/'new-moment-20261122'/f'fight-{t}.jpg'
    media.frame(cfg,root/'kai-street-review.mp4',t,p)
    x,y=i%4*320,i//4*205
    sheet.paste(Image.open(p).resize((320,180)),(x,y))
    d.text((x+5,y+182),str(t),fill='white')
sheet.save(root/'new-moment-20261122/fight-timed.jpg')
