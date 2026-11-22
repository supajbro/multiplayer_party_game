"""Render one reviewed, local-only meme. No API client, queue or uploader."""
from datetime import datetime, timezone
import json
from pathlib import Path
import re
import shutil
import subprocess
from PIL import Image, ImageDraw, ImageFont
from shorts.core import load, digest, save_json
from shorts import media

# Relative to the retained VOD excerpt at 01:22:00. All ranges are unused.
SHOTS = [
    (48.5, 3, 1, 'GTA VI has a permanent record?!', False),
    (62.8, 3, 1, 'just testing the controls...', False),
    (66.7, 4.1, 1, 'combat tutorial: understood.', False),
    (73, 3.6, 1.5, 'terms & conditions: unread.', False),
    (81, 3.6, 1, 'THE GAME IS TAKING NOTES.', True),
    (84.6, 3.5, 1, 'NOW you tell me.', True),
]

def graphic(path, caption, warning):
    im=Image.new('RGBA',(1080,1920))
    d=ImageDraw.Draw(im)
    def line(text,y,size,color='white'):
        f=ImageFont.truetype('C:/Windows/Fonts/impact.ttf',size)
        while f.getlength(text)>940:
            f=ImageFont.truetype('C:/Windows/Fonts/impact.ttf',f.size-2)
        d.text((540,y),text,font=f,fill=color,stroke_width=5,stroke_fill='black',anchor='mt')
    line('Kai Cenat meets the GTA VI tutorial',92,50)
    line(caption,1680 if warning else 1420,76,'#ffe66b')
    im.save(path)

def main():
    cfg=load(Path('config.json'))
    assert not cfg.upload_enabled and not cfg.auto_publish
    root=cfg.data_dir/'gta6-meme-test'
    scout=root/'new-moment-20261122'
    source=root/'kai-street-review.mp4'
    music=cfg.data_dir/'fortnite-test/music/Monkeys Spinning Monkeys.mp3'
    rules=(scout/'evidence/rules.txt').read_text(encoding='utf-8-sig')
    assert all(s in rules for s in ['YouTube Shorts','Kai Cenat','#KAICENAT'])
    folder=root/('permanent-record-draft-'+datetime.now().strftime('%Y%m%d-%H%M%S'))
    folder.mkdir()
    shutil.copytree(scout/'evidence',folder/'evidence')
    shutil.copy2(root/'garage-draft-20261119-191606/private-upload-rights-review.json',folder/'evidence/prior-campaign-membership.json')
    shutil.copy2(root/'street-transcript.json',folder/'source-transcript.json')
    shutil.copy2(root/'source-metadata.json',folder/'evidence/retained-source-metadata.json')
    duplicate_review=[]
    ranges=[(4920+s,4920+s+n) for s,n,*_ in SHOTS]
    for p in cfg.data_dir.rglob('manifest.json'):
        m=json.loads(p.read_text(encoding='utf-8-sig'))
        if not (m.get('uploaded') or m.get('upload_id') or (p.parent/'youtube-upload-state.json').exists()):
            continue
        oldranges=[(m['source_vod_offset']+s[0],m['source_vod_offset']+s[0]+s[1]) for s in m.get('shots',[]) if 'source_vod_offset' in m]
        if m.get('source_url')==(root/'source-url.txt').read_text().strip():
            assert not any(max(a,c)<min(b,d) for a,b in ranges for c,d in oldranges), f'Previously uploaded action: {p}'
        duplicate_review.append(dict(manifest=str(p),sha256=m.get('sha256'),ranges=oldranges))
    parts=[]
    for i,(start,length,speed,caption,warning) in enumerate(SHOTS):
        overlay=folder/f'caption-{i}.png'
        graphic(overlay,caption,warning)
        # Source is 960x540. Isolate Kai's camera, excluding chat and platform logos.
        vf=(f'[0:v]setpts=(PTS-STARTPTS)/{speed},fps=30,split={4 if warning else 3}[bg][game][cam]'+('[tip];' if warning else ';')+
            '[bg]scale=1080:1920:force_original_aspect_ratio=increase,crop=1080:1920,boxblur=30:2,eq=brightness=-0.32[b];'
            '[game]scale=1080:608,setsar=1[g];'
            '[cam]crop=196:110:0:216,scale=832:468,setsar=1[c];'
            '[b][c]overlay=124:210[bc];[bc][g]overlay=0:740[layout];')
        if warning:
            vf+='[tip]crop=220:65:10:140,scale=924:273,setsar=1[t];[layout][t]overlay=78:1370[withtip];'
        vf+=f'[{"withtip" if warning else "layout"}][1:v]overlay=0:0:format=auto,format=yuv420p[v]'
        part=folder/f'part-{i}.mp4'
        media.ffmpeg(cfg,['-ss',str(start),'-t',str(length),'-i',source,'-loop','1','-i',overlay,
            '-filter_complex',vf,'-map','[v]','-map','0:a:0','-af',f'atempo={speed},volume={0.85 if i in [0,5] else 0}',
            '-t',str(round(length/speed*30)/30),'-c:v','libx264','-preset','fast','-crf','20','-c:a','aac','-ar','48000','-ac','2',part],timeout=300)
        parts.append(part)
        print(f'Shot {i+1}/{len(SHOTS)}',flush=True)
    concat=folder/'concat.txt'
    concat.write_text(''.join(f"file '{p.name}'\n" for p in parts))
    seconds=sum(round(n/s*30) for _,n,s,*_ in SHOTS)/30
    output=folder/'gta6-permanent-record-PREVIEW.mp4'
    # Musical hard stop reveals the real warning; softer return under Kai reading it.
    mix=("[1:a]volume='if(between(t,12.5,13.05),0,if(gt(t,13.05),0.055,0.14))':eval=frame[m];"
         '[0:a][m]amix=inputs=2:duration=first:normalize=0,loudnorm=I=-15:TP=-1.5:LRA=9[a]')
    media.ffmpeg(cfg,['-f','concat','-safe','1','-i',concat,'-i',music,'-filter_complex',mix,'-map','0:v','-map','[a]',
        '-c:v','copy','-c:a','aac','-b:a','192k','-t',str(seconds),'-movflags','+faststart',output])
    info=media.probe(cfg,output)
    v=next(s for s in info['streams'] if s['codec_type']=='video')
    assert (v['width'],v['height'])==(1080,1920) and 15<float(info['format']['duration'])<30
    media.ffmpeg(cfg,['-xerror','-i',output,'-f','null','-'],timeout=180)
    qc=subprocess.run([str(x) for x in [cfg.ffmpeg,'-hide_banner','-i',output,'-af','volumedetect,silencedetect=n=-45dB:d=2',
        '-vf','blackdetect=d=0.5:pix_th=0.10','-f','null','-']],capture_output=True,text=True,check=True,timeout=180).stderr
    (folder/'qc-log.txt').write_text(qc,encoding='utf-8')
    assert not re.search(r'black_start:|silence_start:',qc), 'Review extended black/silence before delivery'
    assert float(re.findall(r'max_volume: ([-\d.]+)',qc)[0]) < -0.1, 'Audio peak exceeds review limit'
    description=('Kai Cenat discovers GTA VI keeps a permanent record. #KAICENAT #GTA6 #Shorts\n\n'
        'Gameplay and reactions: Kai Cenat (@KaiCenat), https://www.twitch.tv/kaicenat\n'
        'Archived marathon: https://archive.org/details/kai-cenats-first-time-playing-gta-6\n'
        'Original stream excerpt: 01:22:48.5–01:23:28.1. Fresh editorial cut; no existing Short downloaded.\n'
        'Original captions, selective cuts, reframing, speed change and enlarged actual game warning. '
        'Captions are editorial jokes, not quotations. No affiliation implied.\n\n'
        '"Monkeys Spinning Monkeys" Kevin MacLeod (incompetech.com). '
        'Licensed under Creative Commons: By Attribution 4.0 https://creativecommons.org/licenses/by/4.0/\n'
        'Track: https://incompetech.com/music/royalty-free/index.html?isrc=USUAN1400011\n'
        'Music excerpted and mixed, with a timed pause.\n\n'
        'LOCAL PREVIEW ONLY. Updated campaign rules, music terms, retrieval dates and hashes retained in evidence/. '
        'Campaign requires official-channel marathon footage; this retained recording was acquired through a third-party archive, '
        'so confirm its official-source provenance before any campaign submission or upload. '
        'Current publisher policy retrieval timed out; commercial/public release clearance is not established. '
        'Campaign audience eligibility and remaining stream audio need final review before release. '
        'No upload or publication authorization.\n')
    (folder/'credits-and-review-status.txt').write_text(description,encoding='utf-8')
    assert digest(output) not in {m['sha256'] for m in duplicate_review}
    save_json(folder/'manifest.json',dict(title='Kai Cenat discovers GTA VI keeps a permanent record',
        output=str(output),sha256=digest(output),source=str(source),source_sha256=digest(source),
        source_url=(root/'source-url.txt').read_text().strip(),source_vod_offset=4920,shots=SHOTS,source_ranges=ranges,
        duration=float(info['format']['duration']),narration=False,visible_footer=False,uploaded=False,upload_authorized=False,
        publication_cleared=False,commercial_use_cleared=False,rights_review=description,music_sha256=digest(music),
        duplicate_review=duplicate_review,duplicate_result='No overlap with uploaded source ranges; new output hash.',
        technical_qc=dict(decode='passed',peak_db=re.findall(r'max_volume: ([-\d.]+)',qc),
            black_events=re.findall(r'black_start:[^\n]+',qc),silence_events=re.findall(r'silence_start:[^\n]+',qc)),
        automation=dict(upload_enabled=False,auto_publish=False,scheduled_task='Disabled; verified this session'),
        created=datetime.now(timezone.utc).isoformat()))
    for i,t in enumerate([1,4.5,8,11,14,18]):
        media.frame(cfg,output,t,folder/f'preview-{i}.jpg')
    (root/'latest-permanent-record-preview.txt').write_text(str(output))
    print(output,flush=True)

if __name__=='__main__':
    main()
