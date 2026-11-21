"""Normalize and align the neural narration, captions and scene cue sheet."""
import json, pathlib, subprocess, re
import imageio_ffmpeg
ROOT=pathlib.Path(__file__).resolve().parents[1]
PUBLIC=ROOT/'public/gta6'
words=json.loads((PUBLIC/'narration-words-raw.json').read_text())
delay=120
speed=words[-1]['endMs']/(58900-delay)
for w in words:
    for k in ['startMs','endMs','timestampMs']:w[k]=round(w[k]/speed+delay)
norm=lambda s:re.sub(r'[^a-z0-9]','',s.lower())
tokens=[norm(w['text']) for w in words]
def cue(phrase):
    target=[norm(x) for x in phrase.split()]
    for i in range(len(tokens)-len(target)+1):
        if tokens[i:i+len(target)]==target:return round(words[i]['startMs']/1000*30)
    raise ValueError(phrase)
timing={'revenue':cue('And Rockstar'),'preorders':cue('Before GTA six'),'race':cue('GTA five took'),'minute':cue('And if you'),'ending':cue('But after thirteen'),'black':cue('So here\'s the real'),'lastWordMs':words[-1]['endMs']}
groups=[]; group=[]
for i,w in enumerate(words):
    group.append(w)
    gap=words[i+1]['startMs']-w['endMs'] if i+1<len(words) else 1000
    if len(group)>=5 or gap>250 or re.search(r'[.!?]$',w['text']):
        groups.append({'text':' '.join(x['text'] for x in group),'startMs':group[0]['startMs'],'endMs':min(w['endMs']+120,words[i+1]['startMs'] if i+1<len(words) else 60000),'timestampMs':group[0]['startMs'],'confidence':None,'words':group});group=[]
if group:raise ValueError('Unflushed captions')
(ROOT/'src/GTA6/voice-captions.json').write_text(json.dumps(groups,indent=2))
(ROOT/'src/GTA6/voice-timing.json').write_text(json.dumps(timing,indent=2))
(PUBLIC/'narration-words.json').write_text(json.dumps(words,indent=2))
ffmpeg=imageio_ffmpeg.get_ffmpeg_exe()
subprocess.run([ffmpeg,'-y','-v','error','-i',str(PUBLIC/'narration.mp3'),'-af',f'atempo={speed},loudnorm=I=-16:TP=-2:LRA=7,adelay={delay}:all=1,apad','-t','60','-ar','48000','-ac','2',str(PUBLIC/'voiceover.wav')],check=True)
def stamp(ms):
    h=ms//3600000;m=ms//60000%60;s=ms//1000%60
    return f'{h:02}:{m:02}:{s:02},{ms%1000:03}'
(ROOT/'out/gta6/GTA6RecordShort-voiced.srt').write_text('\n\n'.join(f'{i+1}\n{stamp(c["startMs"])} --> {stamp(c["endMs"])}\n{c["text"]}' for i,c in enumerate(groups)))
print(json.dumps({'speedAdjustment':speed,'timingFrames':timing,'captions':len(groups)},indent=2))
