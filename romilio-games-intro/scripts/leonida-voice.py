"""Generate a new neural documentary VO and word-accurate editorial cues."""
import asyncio, json, pathlib, re, subprocess
import edge_tts, imageio_ffmpeg

ROOT=pathlib.Path(__file__).resolve().parents[1]
OUT=ROOT/'public/leonida'
SRC=ROOT/'src/Leonida'
SCRIPT="""GTA six is laughing at you.
And the giveaway is the phone.
Look past Leonida's neon and chaos. Rockstar's version of Florida turns ordinary life into something to watch, judge, and share.
But the joke goes deeper than wild people doing wild things.
Take Real Dimez. Rockstar describes a rap duo turning street hustles, catchy tracks, and relentless social media into a shot at fame.
Attention is part of the hustle.
Then there's Cal Hampton. He hides at home, monitoring Coast Guard communications, with private browser tabs open.
He's the other side of internet culture: suspicion instead of celebrity.
Here's my read. These aren't just jokes about Florida. They're two ways of getting trapped by the same screen.
One person needs everyone watching. Another can't stop looking.
And us? We turn their world into clips, then scroll straight to the next one.
So... who's the punchline?"""

async def main():
    OUT.mkdir(parents=True,exist_ok=True);SRC.mkdir(parents=True,exist_ok=True)
    voice='en-US-AndrewMultilingualNeural'
    words=[]
    speech=edge_tts.Communicate(SCRIPT,voice,rate='+8%',pitch='-2Hz',boundary='WordBoundary')
    with (OUT/'narration.mp3').open('wb') as audio:
        async for c in speech.stream():
            if c['type']=='audio':audio.write(c['data'])
            elif c['type']=='WordBoundary':words.append(dict(text=c['text'],startMs=c['offset']/10000,endMs=(c['offset']+c['duration'])/10000,timestampMs=c['offset']/10000,confidence=None))
    (OUT/'raw-words.json').write_text(json.dumps(words,indent=2))
    # Fit within 58 seconds with a small initial impact and final breathing room.
    speed=words[-1]['endMs']/56800
    for w in words:
        for k in ['startMs','endMs','timestampMs']:w[k]=round(w[k]/speed+160)
    norm=lambda s:re.sub('[^a-z0-9]','',s.lower())
    tokens=[norm(w['text']) for w in words]
    def cue(phrase):
        target=[norm(t) for t in phrase.split()]
        for i in range(len(tokens)-len(target)+1):
            if tokens[i:i+len(target)]==target:return round(words[i]['startMs']*.03)
        raise ValueError(phrase)
    cues={k:cue(p) for k,p in {'setup':'Look past','dimez':'Take Real Dimez','attention':'Attention is','cal':"Then there","interpretation":"Here's my read","two":"One person","audience":"And us","ending":"So"}.items()}
    groups=[];group=[]
    for i,w in enumerate(words):
        group.append(w)
        if len(group)>=4 or re.search('[.!?]$',w['text']) or i==len(words)-1:
            groups.append(dict(text=' '.join(t['text'] for t in group),startMs=group[0]['startMs'],endMs=min(w['endMs']+120,words[i+1]['startMs'] if i+1<len(words) else 58000),timestampMs=group[0]['startMs'],confidence=None,words=group));group=[]
    (SRC/'captions.json').write_text(json.dumps(groups,indent=2))
    (SRC/'cues.json').write_text(json.dumps(cues,indent=2))
    (OUT/'voice-settings.json').write_text(json.dumps(dict(provider='Microsoft Edge neural TTS',voice=voice,rate='+8%',pitch='-2Hz',fitSpeed=speed,script=SCRIPT),indent=2))
    subprocess.run([imageio_ffmpeg.get_ffmpeg_exe(),'-y','-v','error','-i',str(OUT/'narration.mp3'),'-af',f'atempo={speed},loudnorm=I=-16:TP=-2:LRA=7,adelay=160:all=1,apad','-t','58','-ar','48000','-ac','2',str(OUT/'voiceover.wav')],check=True)
    dest=ROOT/'out/leonida';dest.mkdir(parents=True,exist_ok=True)
    def stamp(ms):return f'{ms//3600000:02}:{ms//60000%60:02}:{ms//1000%60:02},{ms%1000:03}'
    (dest/'GTA6-Punchline.srt').write_text('\n\n'.join(f'{i+1}\n{stamp(c["startMs"])} --> {stamp(c["endMs"])}\n{c["text"]}' for i,c in enumerate(groups)),encoding='utf-8')
    (dest/'narration.txt').write_text(SCRIPT,encoding='utf-8')
    print(json.dumps(dict(wordCount=len(words),speed=speed,cues=cues,lastWordMs=words[-1]['endMs']),indent=2))

asyncio.run(main())
