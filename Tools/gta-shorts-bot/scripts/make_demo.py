"""Render an original animated driving demo through the real production renderer.

No discovery, gameplay analysis, external footage, OAuth or uploads. Synthetic
sources and a deliberately authored test edit live in their own SQLite database.
"""
from __future__ import annotations

import argparse
from datetime import datetime
import json
import math
from pathlib import Path
import subprocess
import time

from PIL import Image, ImageDraw, ImageFont

from shorts.core import load, Store, now, save_json
from shorts.rights import Rights, import_file
from shorts import media, pipeline

W, H, FPS, SECONDS = 720, 1280, 24, 8


def font(size):
    return ImageFont.truetype('C:/Windows/Fonts/arialbd.ttf', size)


def label(draw, xy, text, size=28, fill='#ffffff', anchor='la'):
    draw.text(xy, text, font=font(size), fill=fill, anchor=anchor)


def car(image, x, y, angle, color='#40e0b2', scale=1):
    sprite = Image.new('RGBA', (110, 180))
    d = ImageDraw.Draw(sprite)
    d.rounded_rectangle((22, 19, 91, 171), 18, fill='#101821')
    for wx, wy in ((17, 35), (81, 35), (17, 123), (81, 123)):
        d.rounded_rectangle((wx, wy, wx+14, wy+31), 4, fill='#080b10')
    d.rounded_rectangle((25, 10, 85, 166), 16, fill=color, outline='#c5ffe9', width=2)
    d.polygon([(34, 45), (76, 45), (80, 74), (30, 74)], fill='#102b3b')
    d.rounded_rectangle((33, 82, 77, 120), 4, fill='#193a49')
    d.rectangle((32, 16, 45, 22), fill='#fcffd4')
    d.rectangle((65, 16, 78, 22), fill='#fcffd4')
    d.rectangle((30, 152, 43, 159), fill='#ff7474')
    d.rectangle((67, 152, 80, 159), fill='#ff7474')
    sprite = sprite.resize((max(1, int(110*scale)), max(1, int(180*scale))))
    sprite = sprite.rotate(angle, expand=True, resample=Image.Resampling.BICUBIC)
    image.paste(sprite, (round(x-sprite.width/2), round(y-sprite.height/2)), sprite)


def scene(index, t):
    image = Image.new('RGB', (W, H), '#102536')
    d = ImageDraw.Draw(image)
    # An original schematic city, not a recreation of a game environment.
    for x in range(0, W, 90):
        d.line((x, 180, x, H), fill='#193444', width=1)
    for y in range(180, H, 90):
        d.line((0, y, W, y), fill='#193444', width=1)
    d.rounded_rectangle((25, 25, 695, 146), 20, fill='#0b1521', outline='#3d657c', width=2)
    label(d, (50, 46), 'DRIVING SCHOOL: SYSTEM TEST', 29, '#52e3b8')
    label(d, (50, 90), 'ORIGINAL ANIMATION / NOT GTA GAMEPLAY', 21, '#a9bfca')
    titles = ['01 / THE POOL ROUTE', '02 / THE SHORTCUT', '03 / THE PERFECT LANDING']
    label(d, (35, 190), titles[index], 30)
    if index == 0:
        d.rounded_rectangle((235, 265, 485, 1080), 20, fill='#354555')
        for y in range(300, 1100, 100):
            d.rectangle((353, y, 367, y+50), fill='#b5bdba')
        d.rounded_rectangle((150, 275, 570, 540), 28, fill='#e5dcbe')
        d.rounded_rectangle((169, 294, 551, 520), 20, fill='#179fc3')
        for j in range(5):
            d.arc((190+j*12, 325+j*10, 520-j*12, 485-j*10), 0, 350, fill='#63d4e8', width=2)
        label(d, (360, 370), 'POOL', 38, '#ddfbff', 'mm')
        y = 1050 - min(t/5, 1)*625
        car(image, 360, y, 0)
        d = ImageDraw.Draw(image)
        if t > 5:
            radius = 35 + ((t-5)*65) % 120
            d.ellipse((360-radius, 425-radius/2, 360+radius, 425+radius/2), outline='#e7fcff', width=5)
        caption = 'GPS: CONTINUE STRAIGHT' if t < 4.5 else 'GPS: RECALCULATING YOUR LIFE'
    elif index == 1:
        d.rounded_rectangle((100, 300, 620, 1030), 150, fill='#3b4a57')
        d.rounded_rectangle((230, 450, 490, 880), 110, fill='#2b6954')
        label(d, (360, 620), 'EXIT', 36, '#c4f1d4', 'mm')
        label(d, (360, 670), 'EVENTUALLY', 22, '#c4f1d4', 'mm')
        theta = -math.pi/2 + t*1.7
        x, y = 360+205*math.cos(theta), 665+285*math.sin(theta)
        for j in range(12):
            a = theta-j*.12
            px, py = 360+205*math.cos(a), 665+285*math.sin(a)
            d.ellipse((px-3, py-3, px+3, py+3), fill='#78818a')
        car(image, x, y, -math.degrees(theta))
        d = ImageDraw.Draw(image)
        caption = 'ETA: 4 SECONDS' if t < 4 else 'LAP 2 / STILL SAVING TIME'
    else:
        d.rounded_rectangle((220, 300, 500, 1080), 20, fill='#354555')
        for y in range(310, 1080, 100):
            d.rectangle((353, y, 367, y+48), fill='#b5bdba')
        d.rounded_rectangle((118, 295, 602, 455), 14, fill='#ffcb58', outline='#fff2b6', width=4)
        label(d, (360, 345), 'SAFE DRIVER', 42, '#192736', 'mm')
        label(d, (360, 400), 'PARKING AWARD', 24, '#192736', 'mm')
        d.polygon([(250, 670), (470, 670), (435, 790), (285, 790)], fill='#c9835b')
        for y in (690, 720, 750):
            d.line((275, y, 445, y), fill='#f0be83', width=4)
        if t < 3:
            x, y, angle, scale = 360, 1060-t*110, 0, 1
        elif t < 5.5:
            u = (t-3)/2.5
            x, y, angle, scale = 360, 730-u*350, u*90, 1+.35*math.sin(u*math.pi)
        else:
            x, y, angle, scale = 360, 380, 90, 1
        car(image, x, y, angle, '#ff976d', scale)
        d = ImageDraw.Draw(image)
        caption = 'ONE CLEAN LANDING. PLEASE.' if t < 5.5 else 'PARKING VALIDATED. SIGN DESTROYED.'
    d.rounded_rectangle((30, 1100, 690, 1195), 16, fill='#0b1521', outline='#3d657c', width=2)
    # Two-line labels stay readable inside mobile safe margins.
    if len(caption) > 29:
        words = caption.split()
        mid = len(words)//2
        label(d, (360, 1125), ' '.join(words[:mid]), 24, '#ffffff', 'mm')
        label(d, (360, 1162), ' '.join(words[mid:]), 24, '#ffffff', 'mm')
    else:
        label(d, (360, 1147), caption, 25, '#ffffff', 'mm')
    label(d, (360, 1230), 'LOCAL DEMO / NO EXTERNAL FOOTAGE', 19, '#9eb8c6', 'mm')
    return image


def make_source(cfg, index, output):
    args = [cfg.ffmpeg, '-hide_banner', '-loglevel', 'error', '-nostdin', '-y',
            '-f', 'rawvideo', '-pix_fmt', 'rgb24', '-s', f'{W}x{H}', '-r', str(FPS), '-i', 'pipe:0',
            '-f', 'lavfi', '-i', f'aevalsrc=0.035*sin(2*PI*(65*t+9*t*t))+0.015*sin(2*PI*120*t):s=48000:d={SECONDS}',
            '-t', str(SECONDS), '-c:v', 'libx264', '-preset', 'fast', '-crf', '20', '-pix_fmt', 'yuv420p',
            '-c:a', 'aac', '-ar', '48000', '-movflags', '+faststart', str(output)]
    with output.with_suffix('.ffmpeg.log').open('wb') as log:
        process = subprocess.Popen(args, stdin=subprocess.PIPE, stdout=subprocess.DEVNULL, stderr=log)
        try:
            for n in range(FPS*SECONDS):
                process.stdin.write(scene(index, n/FPS).tobytes())
            process.stdin.close()
            if process.wait(timeout=180):
                raise RuntimeError(f'Source render failed; inspect {output.with_suffix(".ffmpeg.log")}')
        finally:
            if process.poll() is None:
                process.kill()
                process.wait()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--config', type=Path, default=Path('config.json'))
    args = parser.parse_args()
    cfg = load(args.config)
    stamp = datetime.now().strftime('%Y%m%d-%H%M%S')
    folder = cfg.data_dir/'demo-tests'/stamp
    folder.mkdir(parents=True)
    # Never mix synthetic fixtures into the real source pool or daily history.
    cfg = cfg.model_copy(update={'data_dir': folder, 'upload_enabled': False,
                                 'auto_publish': False, 'expected_channel_id': '', 'payoff_sound': True})
    store = Store(cfg)
    started = time.perf_counter()
    clips, credits = [], []
    contexts = ['A schematic car follows GPS straight into a swimming pool.',
                'A schematic car keeps circling a roundabout while claiming to save time.',
                'A schematic car jumps a ramp and lands across a safe-driver parking sign.']
    for index in range(3):
        print(f'Generating original animation {index+1}/3', flush=True)
        source = folder/f'original-{index+1}.mp4'
        make_source(cfg, index, source)
        rights = Rights(source_id=f'local:synthetic-demo-{index+1}', creator='Local bot demo generator',
                        basis='owned', evidence='Original programmatic animation and synthesized engine audio; scripts/make_demo.py',
                        commercial_use=True, editing=True, youtube_distribution=True, third_party_audio_cleared=True,
                        game_content_cleared=True, access_authorized=True,
                        attribution='Original schematic animation generated locally. Not GTA footage.')
        import_file(cfg, store, source, rights)
        store.db.execute('UPDATE sources SET analysis=? WHERE id=?',
                         (json.dumps({'transcript': [], 'synthetic_demo': True,
                                      'selection': 'Authored test fixture; no humour model evaluation'}), rights.source_id))
        clips.append({'source_id': rights.source_id, 'moment': {'start': 0, 'end': SECONDS,
                       'context': contexts[index], 'synthetic_demo': True}})
        credits.append(rights.attribution)
    production_id = 'original-driving-demo'
    manifest = {'id': production_id, 'day': datetime.now().date().isoformat(), 'clips': clips,
                'source_ids': [c['source_id'] for c in clips], 'credits': credits,
                'synthetic_demo': True, 'script_origin': 'Authored original test narration; Piper speech synthesis',
                'script': {'hook': 'Three cars. Zero driving talent.',
                           'jokes': ['The GPS said pool. He heard carpool.',
                                     "That's not a shortcut. That's a subscription.",
                                     'Perfect landing. Unfortunately, the parking spot is vertical.'],
                           'title': 'Driving School: Three Original Animated Fails | Bot Demo',
                           'description': 'Technical production demo with original generated animation. Not GTA gameplay. No external footage.'}}
    store.db.execute("INSERT INTO productions VALUES(?,?,'planned',?,NULL,NULL,?)",
                     (production_id, manifest['day'], json.dumps(manifest), now()))
    store.db.commit()
    print('Rendering real Piper voices and the production edit', flush=True)
    output = pipeline.render(cfg, store, production_id)
    media.frame(cfg, output, 6, folder/'preview-pool.jpg')
    media.frame(cfg, output, 27, folder/'preview-finale.jpg')
    result = {'output': str(output), 'elapsed_seconds': round(time.perf_counter()-started, 1),
              'qc': json.loads(store.db.execute('SELECT manifest FROM productions').fetchone()[0])['qc'],
              'uploaded': False, 'synthetic_demo': True}
    save_json(folder/'result.json', result)
    store.db.close()
    print(json.dumps(result, indent=2), flush=True)


if __name__ == '__main__':
    main()
