"""One-off, human-reviewed Fortnite comedy edit from a licensed archive excerpt.

Uses already acquired footage and source metadata in data/fortnite-test. Source
audio is excluded. The score, effects, narration and graphics are original.
This script never uploads and never changes the default GTA daily pipeline.
"""
from __future__ import annotations

import argparse
from datetime import datetime
import json
import math
from pathlib import Path
import re
import subprocess
import wave

import numpy as np
from PIL import Image, ImageDraw, ImageFont

from shorts.core import load, save_json, digest
from shorts import media

ARCHIVE_ID = '12-2-23-p-2-fn-customs'
SOURCE_URL = 'https://archive.org/details/' + ARCHIVE_ID
LICENSE_URL = 'https://creativecommons.org/licenses/by/4.0/'
DISCLAIMER = ('Portions of the materials used are trademarks and/or copyrighted works of Epic Games, Inc. '
              'All rights reserved by Epic. This material is not official and is not endorsed by Epic.')

EDIT = [
    (108.2, 2.7, 'THE PLAN WAS FLAWLESS.', 'A VERY BRIEF DOCUMENTARY', 'The plan was flawless.'),
    (44, 4.7, 'MAIN CHARACTER ENERGY', 'STEP 1 / MAKE AN ENTRANCE', 'Arrive like a final boss.'),
    (71.8, 4.4, 'FINANCIAL RESPONSIBILITY', 'STEP 2 / LOOT EVERYTHING', 'Looting like the mortgage depends on it.'),
    (84, 5, 'AIM: OPTIONAL', 'STEP 3 / TRUST THE PROCESS', 'Confidence: legendary. Accuracy: a free trial.'),
    (103.5, 5.2, 'NEW CAMERA ANGLE', 'THE PROCESS DID NOT WORK', 'We unlocked a whole new camera angle.'),
    (128.2, 4.6, 'TACTICAL LAWN CARE', 'PLEASE RESPECT THE STRATEGY', "I'm not down. I'm doing lawn care."),
    (134.8, 5.4, 'PLACEMENT: 24TH', 'ALMOST TOP TWENTY-THREE', 'Twenty-fourth. We almost cracked the top twenty-three.'),
    (174.5, 5, 'SAME TIME TOMORROW?', 'OBVIOUSLY.', "We didn't lose. We speedran the loading screen."),
]


def font(size, bold=True):
    return ImageFont.truetype('C:/Windows/Fonts/arialbd.ttf' if bold else 'C:/Windows/Fonts/arial.ttf', size)


def lines(text, face, max_width):
    out, line = [], ''
    for word in text.split():
        trial = (line+' '+word).strip()
        if face.getlength(trial) > max_width and line:
            out.append(line)
            line = word
        else:
            line = trial
    return out + ([line] if line else [])


def centred(draw, text, y, size, color, max_width=950, leading=1.18):
    face = font(size)
    for n, line in enumerate(lines(text, face, max_width)):
        draw.text((540, y+n*size*leading), line, font=face, fill=color, anchor='mt')


def artwork(path, title, phase, number):
    image = Image.new('RGBA', (1080,1920), (10,17,28,255))
    d = ImageDraw.Draw(image)
    d.rounded_rectangle((65,65,360,120), 18, fill='#b9f65b')
    d.text((212,92), 'SQUAD LOGIC', font=font(26), fill='#101a24', anchor='mm')
    d.text((1015,93), 'OG FORTNITE', font=font(25), fill='#a2b2c9', anchor='rm')
    centred(d, title, 170, 68, '#ffffff', 950)
    centred(d, phase, 350, 26, '#b9f65b')
    # Transparent viewport: native Fortnite picture is inserted here.
    d.rectangle((0,420,1080,1500), fill=(0,0,0,0))
    d.rectangle((0,416,1080,420), fill='#b9f65b')
    d.rectangle((0,1500,1080,1504), fill='#38506c')
    # Story progress, outside the caption safe area.
    for i in range(len(EDIT)):
        x=80+i*118
        d.rounded_rectangle((x,1735,x+95,1743), 4, fill='#b9f65b' if i <= number else '#2b3d50')
    centred(d, 'Gameplay: Noc (c) 2018-2026 / CC BY 4.0 / edited', 1772, 23, '#a2b2c9')
    centred(d, 'archive.org/details/'+ARCHIVE_ID, 1807, 20, '#8f9fb6')
    centred(d, 'creativecommons.org/licenses/by/4.0/', 1835, 18, '#8f9fb6')
    centred(d, DISCLAIMER, 1868, 14, '#7d8c9f', 1000, 1.15)
    image.save(path)


def captions(path, sentence, start, stop):
    media.subtitles(path, [])
    chunks=lines(sentence, font(61), 900)
    text=r'\N'.join(re.sub(r'[{}\\\r\n]', ' ', x) for x in chunks)
    with path.open('a',encoding='utf-8') as f:
        f.write(f'Dialogue: 0,{media.ass_time(start)},{media.ass_time(stop)},Default,,0,0,250,,{text}\n')


def soundtrack(folder, length):
    rate=48000
    t=np.arange(round(length*rate))/rate
    music=np.zeros(len(t),dtype=np.float64)
    # An original, deliberately sparse plucked ostinato, written as frequencies.
    notes=[164.81,0,196,0,146.83,0,196,220]
    beat=.5
    for n in range(math.ceil(length/beat)):
        a=round(n*beat*rate);b=min(len(t),a+round(.35*rate));u=np.arange(b-a)/rate
        freq=notes[n%len(notes)]
        if freq:
            music[a:b]+=.04*np.sin(2*np.pi*freq*u)*np.exp(-12*u)
        # Quiet brushed tick; deterministic noise is original audio.
        if n%2:
            rng=np.random.default_rng(n)
            music[a:b]+=.008*rng.normal(size=b-a)*np.exp(-55*u)
    # Very quiet continuous bed prevents dead digital gaps between plucks.
    music+=.002*np.sin(2*np.pi*82.4*t)
    offsets=np.cumsum([0]+[x[1] for x in EDIT])
    # Original descending boop at the actual knock, not an imitated meme sample.
    when=float(offsets[4])+4.0
    a=round(when*rate);b=min(len(t),a+round(.32*rate));u=np.arange(b-a)/rate
    music[a:b]+=.11*np.sin(2*np.pi*(750*u-700*u*u))*np.exp(-9*u)
    path=folder/'original-score.wav'
    write_wav(path,music,rate)
    return path


def write_wav(path, samples, rate):
    with wave.open(str(path),'wb') as f:
        f.setnchannels(1);f.setsampwidth(2);f.setframerate(rate)
        f.writeframes((np.clip(samples,-.95,.95)*32767).astype('<i2').tobytes())


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--config',type=Path,default=Path('config.json'))
    args=parser.parse_args()
    cfg=load(args.config)
    root=cfg.data_dir/'fortnite-test'
    source=root/'noc-game-one-silent.mp4'
    metadata=json.loads((root/'noc-metadata.json').read_text())
    if metadata['metadata'].get('licenseurl') != LICENSE_URL:
        raise ValueError('Source licence record changed; review before rendering')
    if not source.is_file():
        raise ValueError('Acquire the licensed silent excerpt first')
    folder=root/('edit-'+datetime.now().strftime('%Y%m%d-%H%M%S'))
    folder.mkdir()
    parts=[]
    narration=np.zeros(round(sum(x[1] for x in EDIT)*48000))
    offset=0.0
    for i,(start,length,title,phase,sentence) in enumerate(EDIT):
        print(f'Voicing and rendering story beat {i+1}/{len(EDIT)}',flush=True)
        voice=folder/f'voice-{i}.wav'
        media.voice(cfg,sentence,voice)
        vd=media.duration(cfg,voice)
        lead=.12 if i==0 else .3
        if vd+lead > length-.08:
            raise ValueError(f'Narration beat {i} too long ({vd:.2f}s); shorten the line')
        with wave.open(str(voice),'rb') as f:
            samples=np.frombuffer(f.readframes(f.getnframes()),dtype='<i2').astype(np.float64)/32768
            rate=f.getframerate()
        samples=np.interp(np.arange(round(len(samples)*48000/rate))*rate/48000,np.arange(len(samples)),samples)
        pos=round((offset+lead)*48000)
        narration[pos:pos+len(samples)]+=samples
        overlay=folder/f'layout-{i}.png';artwork(overlay,title,phase,i)
        sub=folder/f'captions-{i}.ass';captions(sub,sentence,lead,lead+vd)
        part=folder/f'part-{i}.mp4'
        # The central square retains the avatar, crosshair and most combat action.
        # A small, static punch-in emphasizes the knock without hiding the payoff.
        crop=680 if i==4 else 720
        vf=(f'[0:v]crop={crop}:{crop}:(iw-ow)/2:(ih-oh)/2,scale=1080:1080,setsar=1,fps=30[game];'
            f'[1:v]format=rgba[layout];[game]pad=1080:1920:0:420:color=0x0a111c[base];'
            f"[base][layout]overlay=0:0,ass=filename='{media.escaped_filter_path(sub)}',format=yuv420p[v]")
        media.ffmpeg(cfg,['-ss',str(start),'-i',source,'-loop','1','-i',overlay,'-filter_complex',vf,
                          '-map','[v]','-an','-t',str(length),'-c:v','libx264','-preset','fast','-crf','19',
                          '-movflags','+faststart',part],timeout=300)
        parts.append(part)
        offset+=length
    voice_path=folder/'narration.wav';write_wav(voice_path,narration,48000)
    score=soundtrack(folder,offset)
    concat=folder/'concat.txt';concat.write_text(''.join(f"file '{p.name}'\n" for p in parts),encoding='utf-8')
    output=folder/'fortnite-one-quick-game.mp4'
    # Duck original score under speech, then normalize the full mix.
    af=('[1:a]asplit=2[voice][side];[2:a][side]sidechaincompress=threshold=0.025:ratio=5:'
        'attack=15:release=220[bed];[voice][bed]amix=inputs=2:normalize=0,'
        'loudnorm=I=-16:TP=-1.5:LRA=9[a]')
    media.ffmpeg(cfg,['-f','concat','-safe','1','-i',concat,'-i',voice_path,'-i',score,
                      '-filter_complex',af,'-map','0:v','-map','[a]','-c:v','copy','-c:a','aac','-b:a','192k',
                      '-ar','48000','-t',str(offset),'-movflags','+faststart',output],timeout=300)
    qc=media.quality_check(cfg,output)
    title='Our Fortnite plan lasted one gunfight'
    credit=(f'Gameplay by Noc / Noc VODs. Noc Videos copyright 2018-2026 Noc. Source: {metadata["metadata"]["title"]}.\n'
            f'{SOURCE_URL}\nLicensed CC BY 4.0: {LICENSE_URL}\n'
            'Changes: selected excerpts, reframing, jump cuts, graphics, original comedy narration and original audio. '
            'Original source audio removed. Archival gameplay from December 2023. No endorsement implied.\n\n'+DISCLAIMER)
    description=('A cinematic entrance. A questionable strategy. A very efficient trip back to matchmaking. '
                 'Original scripted comedy commentary over licensed OG Fortnite gameplay.\n\n'+credit)
    (folder/'credits-and-description.txt').write_text(title+'\n\n'+description,encoding='utf-8')
    save_json(folder/'manifest.json',{'title':title,'description':description,'output':str(output),
              'output_sha256':digest(output),'source_url':SOURCE_URL,'licence':LICENSE_URL,
              'epic_policy':'https://legal.epicgames.com/epicgames/fan-art-policy',
              'source_excerpt_sha256':digest(source),'edit':EDIT,'qc':qc,'uploaded':False,
              'selection':'Human-reviewed one-off edit; default GTA pipeline unchanged',
              'audio':'Original speech synthesis, original procedural score and effects; source audio excluded'})
    for label,t in [('hook',1),('action',18),('payoff',29)]:
        media.frame(cfg,output,t,folder/f'preview-{label}.jpg')
    print(json.dumps({'output':str(output),'qc':qc,'credits':str(folder/'credits-and-description.txt'),'uploaded':False},indent=2),flush=True)


if __name__=='__main__':
    main()
