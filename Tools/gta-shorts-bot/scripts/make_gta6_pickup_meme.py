"""Source-specific local pickup meme. Never uploads or touches the daily queue."""
from datetime import datetime
import json
from pathlib import Path
import shutil
import subprocess
from PIL import Image, ImageDraw, ImageFont
from shorts.core import load, digest, save_json
from shorts import media

# Fresh excerpt starts at 01:22:00 in the archived VOD. Replay is visibly labelled.
SHOTS = [
    (92, 4, 1, 'GTA VI: budget taxi', False),
    (99.5, 4.7, 1, 'wait for your passenger!', False),
    (104, 4, 2, 'taxi app: $0.00', True),
    (108, 3, 1, 'economy class acquired', True),
    (111, 3.5, 1, 'seatbelt sold separately', False),
    (111.3, 2.2, .7, 'REPLAY: complimentary exit', True),
    (115, 2, 1, 'walking is free too.', True),
]

def graphic(path, caption):
    im = Image.new('RGBA', (1080,1920))
    d = ImageDraw.Draw(im)
    def text(value, y, size, color='white'):
        f = ImageFont.truetype('C:/Windows/Fonts/impact.ttf', size)
        while f.getlength(value) > 950:
            f = ImageFont.truetype('C:/Windows/Fonts/impact.ttf', f.size-2)
        d.text((540,y),value,font=f,fill=color,stroke_fill='black',stroke_width=5,anchor='mt')
    text('Kai Cenat takes the scenic route', 90, 48)
    text(caption, 1410, 76)
    im.save(path)

def main():
    cfg = load(Path('config.json'))
    if cfg.upload_enabled or cfg.auto_publish:
        raise ValueError('Keep uploading and publication disabled')
    root = cfg.data_dir / 'gta6-meme-test'
    source = root / 'kai-street-review.mp4'
    music = cfg.data_dir / 'fortnite-test/music/Monkeys Spinning Monkeys.mp3'
    rules = root / 'street-evidence/rules-current.txt'
    if 'YouTube Shorts' not in rules.read_text(encoding='utf-8-sig'):
        raise ValueError('Retained campaign rules required')
    folder = root / ('pickup-draft-' + datetime.now().strftime('%Y%m%d-%H%M%S'))
    folder.mkdir()
    shutil.copytree(root / 'street-evidence', folder / 'evidence')
    shutil.copy2(root / 'street-transcript.json', folder / 'source-transcript.json')
    parts = []
    for i,(start,length,speed,caption,montage) in enumerate(SHOTS):
        p = folder / f'part-{i}.mp4'
        overlay = folder / f'caption-{i}.png'
        graphic(overlay, caption)
        vf = (f'[0:v]setpts=(PTS-STARTPTS)/{speed},fps=30,split=3[bg][game][cam];'
              '[bg]scale=1080:1920:force_original_aspect_ratio=increase,crop=1080:1920,boxblur=30:2,eq=brightness=-0.3[b];'
              '[game]scale=1080:608,setsar=1[g];'
              '[cam]crop=196:110:0:216,scale=832:468,setsar=1[c];'
              '[b][c]overlay=124:210[bc];[bc][g]overlay=0:740[layout];'
              '[layout][1:v]overlay=0:0:format=auto,format=yuv420p[v]')
        media.ffmpeg(cfg,['-ss',str(start),'-t',str(length),'-i',source,'-loop','1','-i',overlay,
                         '-filter_complex',vf,'-map','[v]','-map','0:a:0',
                         '-af',f'atempo={speed},volume={0 if montage else 0.9}',
                         '-t',str(round(length/speed*30)/30),'-c:v','libx264','-preset','fast','-crf','20',
                         '-c:a','aac','-ar','48000','-ac','2',p],timeout=300)
        parts.append(p)
        print(f'Shot {i+1}/{len(SHOTS)}',flush=True)
    concat = folder / 'concat.txt'
    concat.write_text(''.join(f"file '{p.name}'\n" for p in parts))
    seconds = sum(round(media.duration(cfg,p)*30) for p in parts)/30
    output = folder / 'gta6-budget-taxi-PREVIEW.mp4'
    # Music drops out at the tumble, then returns for the labelled slow replay.
    mix = ("[1:a]volume='if(between(t,13.7,17.2),0,if(lt(t,8.7),0.09,0.17))':eval=frame[m];"
           '[0:a][m]amix=inputs=2:duration=first:normalize=0,loudnorm=I=-15:TP=-1.5:LRA=9[a]')
    media.ffmpeg(cfg,['-f','concat','-safe','1','-i',concat,'-i',music,'-filter_complex',mix,
                     '-map','0:v','-map','[a]','-c:v','copy','-c:a','aac','-b:a','192k',
                     '-t',str(seconds),'-movflags','+faststart',output])
    info = media.probe(cfg, output)
    v = next(s for s in info['streams'] if s['codec_type']=='video')
    assert (v['width'],v['height'])==(1080,1920) and 15<float(info['format']['duration'])<30
    media.ffmpeg(cfg,['-xerror','-i',output,'-f','null','-'],timeout=300)
    result = subprocess.run([str(a) for a in [cfg.ffmpeg,'-hide_banner','-i',output,
                            '-af','volumedetect,silencedetect=n=-45dB:d=2',
                            '-vf','blackdetect=d=0.5:pix_th=0.10','-f','null','-']],
                            capture_output=True,text=True,timeout=180,check=True)
    stats = result.stderr
    (folder/'qc-log.txt').write_text(stats,encoding='utf-8')
    description = (
        'Kai Cenat tries GTA VI budget transport. #KaiCenat #GTA6 #Shorts\n\n'
        'Gameplay and reactions: Kai Cenat (@KaiCenat), https://www.twitch.tv/kaicenat\n'
        'Source: https://archive.org/details/kai-cenats-first-time-playing-gta-6\n'
        'Excerpt starts at 01:22:00; selected action at 01:23:32 onward. Original editorial captions, '
        'reframing, quick cuts, speed changes and labelled replay. No affiliation implied.\n\n'
        '"Monkeys Spinning Monkeys" Kevin MacLeod (incompetech.com). CC BY 4.0: '
        'https://creativecommons.org/licenses/by/4.0/\n'
        'Track: https://incompetech.com/music/royalty-free/index.html?isrc=USUAN1400011\n'
        'Music excerpted, mixed and paused for comedic timing.\n\n'
        'LOCAL PREVIEW ONLY. Campaign reuse rules and previous signed-in review retained in evidence/. '
        'No campaign submission. Publisher commercial clearance is not established. '
        'Original stream audio requires final listening review before any upload. '
        'Current publisher policy retrieval timed out; recorded in evidence/retrieval.json.\n')
    (folder/'credits-and-review-status.txt').write_text(description,encoding='utf-8')
    uploaded_manifests=[]
    for old in cfg.data_dir.rglob('manifest.json'):
        m=json.loads(old.read_text(encoding='utf-8-sig'))
        if m.get('uploaded') or m.get('upload_id'):
            uploaded_manifests.append({'manifest':str(old),'sha256':m.get('sha256'),'shots':m.get('shots'),'vod_offset':m.get('source_vod_offset')})
    assert digest(output) not in {m['sha256'] for m in uploaded_manifests}
    save_json(folder/'manifest.json',{'title':'Kai Cenat tries GTA VI budget transport',
        'output':str(output),'sha256':digest(output),'source':str(source),'source_sha256':digest(source),
        'source_url':(root/'source-url.txt').read_text().strip(),'source_vod_offset':4920,
        'shots':SHOTS,'duration':float(info['format']['duration']),'decode':'passed',
        'narration':False,'visible_footer':False,'uploaded':False,'upload_authorized':False,
        'publication_cleared':False,'commercial_use_cleared':False,'rights_review':description,
        'music_sha256':digest(music),'duplicate_review':uploaded_manifests,
        'duplicate_result':'New footage range 5012-5037; prior garage uses 4680-4823.5. New output hash.',
        'automation':{'upload_enabled':False,'auto_publish':False,'scheduled_task':'Disabled (verified)'}})
    for i,t in enumerate([1,6,12,15,19]):
        media.frame(cfg,output,t,folder/f'preview-{i}.jpg')
    (root/'latest-pickup-preview.txt').write_text(str(output))
    print(json.dumps({'output':str(output),'duration':seconds}),flush=True)

if __name__=='__main__':
    main()
