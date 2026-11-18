"""Music-led Fortnite meme recut. No narration, no uploads, licensed source/music."""
from __future__ import annotations

import argparse
from datetime import datetime
import json
from pathlib import Path
import wave

import numpy as np
from PIL import Image, ImageDraw, ImageFont

from shorts.core import load, save_json, digest
from shorts import media

BEAT = 60 / 144
# source timestamp, beat count, playback rate, caption, treatment
SHOTS = [
    (44,    6, 1.25, 'the squad:\n"easy win"', 'normal'),
    (71.8,  4, 1.5, 'me with zero warmup:', 'normal'),
    (84,    6, 1.5, 'my aim is a subscription\ni cancelled', 'normal'),
    (104.2, 4, .9, 'the crosshair is\njust decoration', 'punch'),
    (105.9, 5, .9, 'please hit\nliterally anything', 'shake'),
    (108.2, 3, 1, 'connection to skill lost', 'skull'),
    (108.4, 6, 1.7, 'bro became a roomba', 'normal'),
    (110,   4, 1.2, 'cleaning the SAME spot', 'reverse'),
    (108.2, 4, 1, '', 'error'),
    (128.2, 6, 1.8, 'tree please adopt me', 'normal'),
    (134.6, 6, 1, '24th out of 25 squads', 'normal'),
    (136.8, 4, 1, 'WE BEAT ONE TEAM', 'result'),
    (148,   4, 1.4, 'that was just the warmup', 'normal'),
    (174.5, 6, 2, 'yeah definitely the warmup', 'normal'),
    (44,    4, 1.5, 'run it back', 'normal'),
]


def face(size, impact=False):
    path='C:/Windows/Fonts/impact.ttf' if impact else 'C:/Windows/Fonts/arialbd.ttf'
    return ImageFont.truetype(path,size)


def text(draw, value, y, size=75, color='white', stroke=6):
    f=face(size,True)
    for n,line in enumerate(value.split('\n')):
        # Keep captions inside mobile safe margins, reducing only an oversized line.
        ff=f
        while ff.getlength(line)>950:
            ff=face(ff.size-2,True)
        draw.text((540,y+n*size*1.15),line,font=ff,fill=color,anchor='mt',
                  stroke_width=stroke,stroke_fill='#080808')


def frame_overlay(path, caption, mode):
    im=Image.new('RGBA',(1080,1920),(0,0,0,0));d=ImageDraw.Draw(im)
    if caption:
        text(d,caption,250 if mode!='result' else 1260,
             size=80 if mode!='result' else 93,color='#ffffff' if mode!='result' else '#aaff28')
    if mode=='skull':
        # Original skull doodle: a reaction overlay, not an imported meme image.
        d.ellipse((355,720,725,1060),fill='white',outline='#050505',width=14)
        d.rounded_rectangle((420,980,660,1160),25,fill='white',outline='#050505',width=12)
        d.ellipse((402,835,493,940),fill='#050505');d.ellipse((588,835,679,940),fill='#050505')
        d.polygon([(540,930),(506,998),(571,998)],fill='#050505')
        for x in (468,515,562,609):d.line((x,1060,x,1145),fill='#050505',width=9)
    if mode=='error':
        # Editorial joke UI; not an actual Windows dialog or captured OS prompt.
        d.rectangle((90,675,990,1210),fill='#c8c8c8',outline='#111111',width=9)
        d.rectangle((102,687,978,755),fill='#172b81')
        d.text((126,702),'skill.exe',font=face(40),fill='white')
        d.rectangle((910,698,962,744),fill='#c8c8c8',outline='white',width=3)
        d.text((936,717),'x',font=face(32),fill='black',anchor='mm')
        d.ellipse((130,805,230,905),fill='#d12929',outline='white',width=4)
        d.line((158,832,202,878),fill='white',width=11);d.line((202,832,158,878),fill='white',width=11)
        d.text((270,815),'Skill not found.',font=face(43),fill='#101010')
        d.text((270,886),'Would you like to blame lag?',font=face(31),fill='#101010')
        for x,label in [(210,'BLAME LAG'),(600,'REQUEUE')]:
            d.rectangle((x,1035,x+290,1135),fill='#dddddd',outline='#555555',width=5)
            d.text((x+145,1085),label,font=face(28),fill='#101010',anchor='mm')
    # Credits and publisher disclaimer accompany this cut in its description and
    # sidecar file; keep the gameplay frame clear. Retain that text on publication.
    im.save(path)


def make_mix(cfg, root, folder, seconds):
    music=root/'music'/'Monkeys Spinning Monkeys.mp3'
    if not music.is_file():raise ValueError('Official licensed music file is missing')
    decoded=folder/'music-decoded.wav'
    media.ffmpeg(cfg,['-i',music,'-t',str(seconds+2),'-ac','2','-ar','48000','-c:a','pcm_s16le',decoded])
    rate=48000
    with wave.open(str(decoded),'rb') as f:
        audio=np.frombuffer(f.readframes(f.getnframes()),dtype='<i2').astype(np.float64).reshape(-1,2)/32768
    length=round(seconds*rate)
    audio=audio[:length]*.65
    if len(audio)<length:raise ValueError('Music is shorter than the edit')
    offsets=np.cumsum([0]+[s[1]*BEAT for s in SHOTS])
    # Beat-synchronous hard stop for the knock; the next phrase restarts with crawling.
    stop=float(offsets[5]);resume=float(offsets[6])
    a,b=round(stop*rate),round(resume*rate)
    audio[a:b]*=.015
    def effect(when, samples, pan=0):
        at=round(when*rate);end=min(length,at+len(samples));samples=samples[:end-at]
        audio[at:end,0]+=samples*(1-max(0,pan)*.4)
        audio[at:end,1]+=samples*(1+min(0,pan)*.4)
    u=np.arange(round(.38*rate))/rate
    rng=np.random.default_rng(731)
    scratch=(.15*rng.normal(size=len(u))*np.exp(-14*u)+
             .25*np.sin(2*np.pi*(1200*u-1300*u*u))*np.exp(-8*u))
    effect(stop,scratch)
    # A short original bass bonk under the skull frame.
    u=np.arange(round(.4*rate))/rate
    effect(stop+.04,.45*np.sin(2*np.pi*(115*u-80*u*u))*np.exp(-12*u))
    # Two original error beeps on the editorial 'skill.exe' reaction card.
    u=np.arange(round(.14*rate))/rate
    envelope=np.sin(np.linspace(0,np.pi,len(u)))**2
    for delay,freq in [(0,740),(.19,554)]:
        effect(float(offsets[8])+.08+delay,.19*np.sin(2*np.pi*freq*u)*envelope)
    # Rewind chirp is aligned to the reversed crawl.
    u=np.arange(round(.28*rate))/rate
    effect(float(offsets[7]),.1*np.sin(2*np.pi*(700*u+2100*u*u))*np.exp(-9*u),-.6)
    # A small rising victory chirp for the absurdly low placement celebration.
    u=np.arange(round(.16*rate))/rate
    for delay,freq in [(0,523.25),(.14,659.25),(.28,783.99)]:
        effect(float(offsets[11])+.1+delay,.11*np.sin(2*np.pi*freq*u)*np.exp(-18*u))
    # Short output fade only, retaining the musical opening for a clean replay.
    fade=round(.04*rate);audio[-fade:]*=np.linspace(1,0,fade)[:,None]
    output=folder/'meme-mix.wav'
    with wave.open(str(output),'wb') as f:
        f.setnchannels(2);f.setsampwidth(2);f.setframerate(rate)
        f.writeframes((np.clip(audio,-.98,.98)*32767).astype('<i2').tobytes())
    return output


def main():
    p=argparse.ArgumentParser();p.add_argument('--config',type=Path,default=Path('config.json'));args=p.parse_args()
    cfg=load(args.config);root=cfg.data_dir/'fortnite-test'
    source=root/'noc-game-one-silent.mp4'
    metadata=json.loads((root/'noc-metadata.json').read_text())
    if metadata['metadata'].get('licenseurl')!='https://creativecommons.org/licenses/by/4.0/':
        raise ValueError('Review the footage licence before making this edit')
    folder=root/('meme-clean-'+datetime.now().strftime('%Y%m%d-%H%M%S'));folder.mkdir()
    parts=[]
    for i,(start,beats,speed,caption,mode) in enumerate(SHOTS):
        duration=beats*BEAT
        frozen=mode in {'skull','error','result'}
        src_length=.08 if frozen else duration*speed
        overlay=folder/f'caption-{i}.png';frame_overlay(overlay,caption,mode)
        # Full-screen mobile crop. Player and central crosshair stay in frame.
        prep='trim=duration=0.04,setpts=PTS-STARTPTS,tpad=stop_mode=clone:stop_duration=4,' if frozen else ''
        if mode=='reverse':prep+='reverse,'
        prep+=f'setpts=(PTS-STARTPTS)/{speed},fps=30,crop=406:720:(iw-ow)/2:0,scale=1080:1920,setsar=1'
        if mode=='punch':prep+=',scale=1242:2208,crop=1080:1920:81:144'
        if mode=='shake':prep+=",scale=1188:2112,crop=1080:1920:x='54+10*sin(49*t)':y='96+7*cos(39*t)'"
        if mode=='result':prep+=',scale=1296:2304,crop=1080:1920:108:0,eq=saturation=1.5'
        if mode=='error':prep+=',boxblur=8:2,eq=saturation=0.3:brightness=-0.12'
        vf=f'[0:v]{prep}[game];[game][1:v]overlay=0:0:format=auto,format=yuv420p[v]'
        part=folder/f'part-{i}.mp4'
        print(f'Rendering meme cut {i+1}/{len(SHOTS)}',flush=True)
        media.ffmpeg(cfg,['-ss',str(start),'-t',str(src_length),'-i',source,'-loop','1','-i',overlay,
                          '-filter_complex',vf,'-map','[v]','-an','-t',str(duration),'-c:v','libx264',
                          '-preset','fast','-crf','19','-movflags','+faststart',part],timeout=300)
        parts.append(part)
    # Use encoded frame durations for the final mix: not all beat lengths fall on a 30fps boundary.
    actual_lengths=[media.duration(cfg,p) for p in parts]
    # Count 30fps frames rather than summing container timestamps: concat metadata
    # can be fractions of a millisecond short of an exact 30-second timeline.
    total=sum(round(seconds*30) for seconds in actual_lengths)/30
    audio=make_mix(cfg,root,folder,total)
    concat=folder/'concat.txt';concat.write_text(''.join(f"file '{p.name}'\n" for p in parts),encoding='utf-8')
    output=folder/'fortnite-roomba-meme-clean.mp4'
    media.ffmpeg(cfg,['-f','concat','-safe','1','-i',concat,'-i',audio,'-map','0:v','-map','1:a',
                      '-c:v','copy','-af','loudnorm=I=-15:TP=-1.5:LRA=9','-c:a','aac','-b:a','192k',
                      '-ar','48000','-t',str(total),'-movflags','+faststart',output])
    qc=media.quality_check(cfg,output)
    old=json.loads((root/'edit-20261118-195028'/'manifest.json').read_text())
    credits=old['description'].split('Gameplay by ',1)[-1]
    credits='Gameplay by '+credits
    credits=credits.replace('Changes: selected excerpts, reframing, jump cuts, graphics, original comedy narration and original audio.',
                            'Changes: selected excerpts, full-screen reframing, speed changes, reverse playback, freeze frames, original reaction graphics and sound effects.')
    credits+='\n\n"Monkeys Spinning Monkeys" Kevin MacLeod (incompetech.com)\n'
    credits+='Licensed under Creative Commons: By Attribution 4.0 License\nhttps://creativecommons.org/licenses/by/4.0/\n'
    credits+='Track source: https://incompetech.com/music/royalty-free/index.html?isrc=USUAN1400011\n'
    credits+='Music changes: excerpted and interrupted for comedic timing; original effects added.\n'
    description=('POV: the squad says easy win. Original caption-led Fortnite meme edit. '
                 'Archival OG gameplay, no original streamer dialogue. No narration.\n\n'+credits)
    save_json(folder/'manifest.json',{'title':'bro queued Fortnite as a Roomba','description':description,
              'output':str(output),'sha256':digest(output),'qc':qc,'shots':SHOTS,'encoded_lengths':actual_lengths,
              'music':'Monkeys Spinning Monkeys / Kevin MacLeod / CC BY 4.0',
              'source_audio_removed':True,'narration':False,'uploaded':False,
              'credits_location':'Accompanying description and credits-and-description.txt; retain on publication'})
    (folder/'credits-and-description.txt').write_text(description,encoding='utf-8')
    for label,t in [('hook',1),('roomba',12.8),('error',16.8),('result',23.3)]:
        media.frame(cfg,output,t,folder/f'preview-{label}.jpg')
    print(json.dumps({'output':str(output),'qc':qc,'uploaded':False},indent=2),flush=True)


if __name__=='__main__':main()
