"""Generate conversational neural narration with measured word timings.
Run with: uv run --with edge-tts --with imageio-ffmpeg python scripts/gta-voice.py
"""
import asyncio
import json
import pathlib
import subprocess
import edge_tts

ROOT = pathlib.Path(__file__).resolve().parents[1]
OUT = ROOT / 'public/gta6'
TEXT = """Imagine making a video game that earns more money in one day than Avatar made during its entire cinema run.
Well, GTA six just did exactly that.
On November nineteenth, GTA six finally launched.
And Rockstar reported an absolutely ridiculous three point seven nine billion dollars in first-day retail sales.
For comparison, Avatar made around two point nine two billion dollars at the worldwide box office.
But here's where things get even crazier.
Before GTA six was even released, Rockstar says thirty-two point five million copies had already been sold.
GTA five took three days to reach one billion dollars.
GTA six smashed that in its first day.
And if you spread those reported sales across twenty-four hours, that's an average of over two point six million dollars every minute.
That includes preorders, of course, and movie ticket sales aren't directly comparable to game retail sales.
But after thirteen years, GTA six didn't just break gaming records.
It became the biggest entertainment launch ever, according to Rockstar.
So here's the real question... how could GTA seven possibly top that?"""

async def main():
    import sys
    rate = sys.argv[1] if len(sys.argv)>1 else '+25%'
    voice='en-US-AndrewMultilingualNeural'
    OUT.mkdir(exist_ok=True,parents=True)
    words=[]
    speech=edge_tts.Communicate(TEXT,voice,rate=rate,pitch='+2Hz',boundary='WordBoundary')
    with (OUT/'narration.mp3').open('wb') as audio:
        async for chunk in speech.stream():
            if chunk['type']=='audio':audio.write(chunk['data'])
            elif chunk['type']=='WordBoundary':
                words.append({'text':chunk['text'],'startMs':chunk['offset']/10000,'endMs':(chunk['offset']+chunk['duration'])/10000,'timestampMs':chunk['offset']/10000,'confidence':None})
    (OUT/'narration-words-raw.json').write_text(json.dumps(words,indent=2))
    (OUT/'voice-settings.json').write_text(json.dumps({'provider':'Microsoft Edge neural TTS','voice':voice,'rate':rate,'pitch':'+2Hz','script':TEXT},indent=2))
    print(json.dumps({'voice':voice,'rate':rate,'words':len(words),'lastWordEnd':words[-1]['endMs']/1000},indent=2))

asyncio.run(main())
