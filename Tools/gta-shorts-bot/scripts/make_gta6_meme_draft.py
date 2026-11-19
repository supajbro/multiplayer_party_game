"""Local editorial draft from reviewed Kai footage; never imports or uploads.

Campaign rules remain unreviewed behind sign-in. This output is NOT cleared for
publication or commercial use. Keep that status in the accompanying manifest.
"""
from datetime import datetime
import json
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

from shorts.core import load, digest, save_json
from shorts import media

# Seconds relative to the review excerpt (original VOD offset 4680 seconds).
# Keep source reactions intact. Accelerate only the silent browsing montage.
SHOTS = [
    (0, 4.9, 1, '13 YEARS WAITING FOR GTA VI', False),
    (18.5, 4, 1, 'me on launch day:', False),
    (26.5, 8, 4, 'paint sample simulator', True),
    (40.6, 2.3, 1, 'all that just to pick BLACK', False),
    (61.7, 5.4, 1, 'wait... there are body kits?', False),
    (92.0, 2, 1, 'okay. time to play the game.', False),
    (128, 8, 2, 'finally leaving the garage', True),
    (139.5, 3.5, 1, 'straight back in.', False),
]


def overlay(path, caption):
    im = Image.new('RGBA', (1080, 1920), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    font_path = 'C:/Windows/Fonts/impact.ttf'
    words = caption.split()
    lines, line = [], ''
    font = ImageFont.truetype(font_path, 76)
    for word in words:
        candidate = (line + ' ' + word).strip()
        if font.getlength(candidate) > 940 and line:
            lines.append(line)
            line = word
        else:
            line = candidate
    lines.append(line)
    for n, line in enumerate(lines):
        d.text((540, 1400 + n * 90), line, font=font, fill='white',
               stroke_fill='black', stroke_width=6, anchor='mt')
    im.save(path)


def main():
    cfg = load(Path('config.json'))
    root = cfg.data_dir / 'gta6-meme-test'
    source = root / 'kai-driving-review.mp4'
    music_root = cfg.data_dir / 'fortnite-test/music'
    music = music_root / 'Monkeys Spinning Monkeys.mp3'
    if not source.is_file() or not music.is_file() or not (music_root / 'licence-source.html').is_file():
        raise ValueError('Reviewed footage and music licence evidence are required')
    folder = root / ('garage-draft-' + datetime.now().strftime('%Y%m%d-%H%M%S'))
    folder.mkdir()
    parts = []
    for i, (start, length, speed, caption, montage) in enumerate(SHOTS):
        duration = round(length / speed * 30) / 30
        graphic = folder / f'caption-{i}.png'
        overlay(graphic, caption)
        # Preserve the complete gameplay view, including menus. Enlarge the
        # actual facecam above it; do not invent a reaction or hide source credit.
        vf = (
            f'[0:v]setpts=(PTS-STARTPTS)/{speed},fps=30,split=3[bg][game][cam];'
            '[bg]scale=1080:1920:force_original_aspect_ratio=increase,crop=1080:1920,'
            'boxblur=30:2,eq=brightness=-0.30[back];'
            '[game]scale=1080:608,setsar=1[g];'
            '[cam]crop=196:110:0:216,scale=832:468,setsar=1[c];'
            '[back][c]overlay=124:180[b];[b][g]overlay=0:740[laid];'
            '[laid][1:v]overlay=0:0:format=auto,format=yuv420p[v]'
        )
        af = ('anull' if speed == 1 else f'atempo=2,atempo={speed/2}')
        # Montage is musical; preserve speech/reactions on normal-speed shots.
        af += ',volume=' + ('0' if montage else '0.9')
        part = folder / f'part-{i}.mp4'
        media.ffmpeg(cfg, ['-ss', str(start), '-t', str(length), '-i', source,
                          '-loop', '1', '-i', graphic, '-filter_complex', vf,
                          '-map', '[v]', '-map', '0:a:0', '-af', af, '-t', str(duration),
                          '-c:v', 'libx264', '-preset', 'fast', '-crf', '20',
                          '-c:a', 'aac', '-ar', '48000', '-ac', '2', part], timeout=300)
        parts.append(part)
        print(f'Rendered shot {i+1}/{len(SHOTS)}', flush=True)
    concat = folder / 'concat.txt'
    concat.write_text(''.join(f"file '{p.name}'\n" for p in parts), encoding='utf-8')
    seconds = sum(round(media.duration(cfg, p)*30) for p in parts) / 30
    output = folder / 'gta6-garage-meme-DRAFT.mp4'
    # Low music under speech; silence it for the return-to-garage punchline.
    stop = seconds - 3.5
    mix = (f"[1:a]volume='if(gte(t,{stop}),0,0.14)':eval=frame[m];"
           '[0:a][m]amix=inputs=2:duration=first:normalize=0,'
           'loudnorm=I=-15:TP=-1.5:LRA=9[a]')
    media.ffmpeg(cfg, ['-f', 'concat', '-safe', '1', '-i', concat,
                      '-i', music, '-filter_complex', mix, '-map', '0:v', '-map', '[a]',
                      '-c:v', 'copy', '-c:a', 'aac', '-b:a', '192k', '-ar', '48000',
                      '-t', str(seconds), '-movflags', '+faststart', output])
    info = media.probe(cfg, output)
    video = next(s for s in info['streams'] if s['codec_type'] == 'video')
    if video['width'] != 1080 or video['height'] != 1920 or not 15 <= float(info['format']['duration']) <= 35:
        raise ValueError('Draft dimensions/duration failed')
    media.ffmpeg(cfg, ['-xerror', '-i', output, '-f', 'null', '-'], timeout=300)
    description = (
        'LOCAL REVIEW DRAFT — NOT CLEARED FOR PUBLICATION OR COMMERCIAL USE.\n\n'
        'Gameplay/reactions: Kai Cenat. Source archive: '
        'https://archive.org/details/kai-cenats-first-time-playing-gta-6\n'
        'Excerpt offset: 01:18:00 in archived VOD. Edited selections, musical montage, '
        'reframing and original editorial captions; no invented dialogue.\n'
        'Public clipping campaign: https://contentrewards.com/c/campaigns/'
        '1d2bcb00-c904-415f-aa82-f95b96baad86\n'
        'Full campaign rules/reference files require sign-in and have NOT been reviewed. '
        'Verify source eligibility, campaign authority and conditions before any upload.\n'
        'Rockstar policy: https://support.rockstargames.com/articles/'
        '7bNaeoMFTV0iUDGhStTXvz/policy-on-posting-copyrighted-rockstar-games-material\n'
        'Rockstar describes non-commercial fan use; no commercial clearance is claimed. '
        'Original stream audio still needs a music review before publication.\n\n'
        'Music: "Monkeys Spinning Monkeys" Kevin MacLeod (incompetech.com). '
        'CC BY 4.0: https://creativecommons.org/licenses/by/4.0/\n'
        'Track: https://incompetech.com/music/royalty-free/index.html?isrc=USUAN1400011\n'
        'Music excerpted, mixed under speech and stopped for comedic timing.\n'
    )
    (folder / 'credits-and-review-status.txt').write_text(description, encoding='utf-8')
    save_json(folder / 'manifest.json', {
        'title': '13 years waiting for GTA VI... straight to the garage',
        'output': str(output), 'sha256': digest(output), 'source_sha256': digest(source),
        'shots': SHOTS, 'source_vod_offset': 4680, 'duration': seconds,
        'decode': 'passed', 'uploaded': False, 'publication_cleared': False,
        'commercial_use_cleared': False, 'rights_review': description,
        'narration': False, 'visible_footer': False,
    })
    for n, t in enumerate([1, 7, 17, seconds-2]):
        media.frame(cfg, output, t, folder / f'preview-{n}.jpg')
    print(json.dumps({'output': str(output), 'seconds': seconds,
                      'publication_cleared': False}), flush=True)


if __name__ == '__main__':
    main()
