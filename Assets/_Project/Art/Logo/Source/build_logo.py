"""Construct the Couch Guys wordmark from genuine Lilita One glyph outlines.

Run with Python 3.12+ and fonttools, pillow, resvg-py installed.
All outputs are confined to this logo directory. No Unity authoring hooks.
"""
from pathlib import Path
from math import cos, sin, radians
from io import BytesIO

from fontTools.ttLib import TTFont
from fontTools.pens.boundsPen import BoundsPen
from fontTools.pens.svgPathPen import SVGPathPen
from fontTools.pens.transformPen import TransformPen
from PIL import Image, ImageDraw
import resvg_py

ROOT = Path(__file__).resolve().parent.parent
FONT = TTFont(ROOT / 'Source' / 'LilitaOne-Regular.ttf')
GLYPHS = FONT.getGlyphSet()
CMAP = FONT.getBestCmap()
CREAM, GOLD, BROWN, SHADOW = '#FFF0D2', '#FFB52E', '#44251B', '#2E1914'
OUTLINE = 30


def glyph_bounds(char):
    pen = BoundsPen(GLYPHS)
    GLYPHS[CMAP[ord(char)]].draw(pen)
    return pen.bounds


def word(word, height, gap, baseline, angles, arch):
    """Optical spacing uses rotated ink bounds rather than advance widths."""
    cap_height = glyph_bounds('H')[3]
    scale = height / cap_height
    letters = []
    cursor = 0
    for i, (char, angle) in enumerate(zip(word, angles)):
        x0, y0, x1, y1 = glyph_bounds(char)
        a = radians(angle)
        c, s = cos(a), sin(a)
        # Normalize around each letter's center, flip font Y, then rotate.
        midx, midy = (x0 + x1) / 2, (y0 + y1) / 2
        matrix = (scale*c, scale*s, scale*s, -scale*c,
                  -scale*c*midx-scale*s*midy,
                  -scale*s*midx+scale*c*midy)
        bp = BoundsPen(GLYPHS)
        GLYPHS[CMAP[ord(char)]].draw(TransformPen(bp, matrix))
        bx0, by0, bx1, by1 = bp.bounds
        # Parabolic arch: the center rises gently, outer letters sit lower.
        t = (i - (len(word)-1)/2) / ((len(word)-1)/2)
        tx = cursor - bx0
        ty = baseline - height/2 + arch*t*t
        final = (*matrix[:4], matrix[4]+tx, matrix[5]+ty)
        pen = SVGPathPen(GLYPHS, ntos=lambda v: f'{v:.3f}'.rstrip('0').rstrip('.'))
        GLYPHS[CMAP[ord(char)]].draw(TransformPen(pen, final))
        letters.append({'char': char, 'd': pen.getCommands(),
                        'bounds': (cursor, by0+ty, cursor+bx1-bx0, by1+ty)})
        cursor += bx1-bx0 + gap
    width = cursor-gap
    return letters, width


top, tw = word('COUCH', 258, 23, 304, [-6, -3, 0, 3, 6], 25)
bottom, bw = word('GUYS', 310, 25, 624, [-5, -2, 2, 5], 19)
WIDTH = max(tw, bw) + 88
for letters, width in ((top, tw), (bottom, bw)):
    dx = (WIDTH-width)/2
    for letter in letters:
        letter['dx'] = dx

# Identical canvas across both variants allows direct replacement in layouts.
HEIGHT = max(l['bounds'][3] for l in top+bottom) + 54


def make_svg(flat=False):
    lines = [f'<svg xmlns="http://www.w3.org/2000/svg" width="{WIDTH:.3f}" height="{HEIGHT:.3f}" viewBox="0 0 {WIDTH:.3f} {HEIGHT:.3f}" role="img" aria-labelledby="title desc">',
             '<title id="title">Couch Guys</title>',
             '<desc id="desc">Original two-line arched wordmark. Cream COUCH above golden orange GUYS, with dark brown outlines'+('.' if flat else ' and a subtle offset shadow.')+'</desc>',
             '<metadata>Lettering: Lilita One by Juan Montoreano, SIL OFL 1.1. All letters are editable vector paths; no font installation is required.</metadata>']
    if not flat:
        lines.append(f'<g id="offset-shadow" fill="{SHADOW}" stroke="{SHADOW}" stroke-width="{OUTLINE}" stroke-linejoin="round" transform="translate(5 12)">')
        for l in top+bottom:
            lines.append(f'<path transform="translate({l["dx"]:.3f} 0)" d="{l["d"]}"/>')
        lines.append('</g>')
    # Render outlines as one pass, then all fills, so neighbors never overpaint faces.
    lines.append(f'<g id="outlines" fill="{BROWN}" stroke="{BROWN}" stroke-width="{OUTLINE}" stroke-linejoin="round">')
    for l in top+bottom:
        lines.append(f'<path transform="translate({l["dx"]:.3f} 0)" d="{l["d"]}"/>')
    lines.append('</g>')
    for name, letters, color in [('COUCH', top, CREAM), ('GUYS', bottom, GOLD)]:
        lines.append(f'<g id="{name}" fill="{color}">')
        for i, l in enumerate(letters):
            lines.append(f'<path id="{name}-{i+1}-{l["char"]}" transform="translate({l["dx"]:.3f} 0)" d="{l["d"]}"/>')
        lines.append('</g>')
    lines.append('</svg>')
    return '\n'.join(lines)+'\n'


def main():
    for flat in (False, True):
        stem = 'couch-guys-flat' if flat else 'couch-guys'
        svg = make_svg(flat)
        (ROOT / f'{stem}.svg').write_text(svg, encoding='utf-8')
        for width in (1024, 2048, 4096):
            data = resvg_py.svg_to_bytes(svg_string=svg, width=width, skip_system_fonts=True)
            (ROOT / f'{stem}-{width}.png').write_bytes(data)
            im = Image.open(BytesIO(data))
            assert im.width == width and im.mode == 'RGBA'
            assert im.getpixel((0, 0))[3] == 0
            box = im.getchannel('A').getbbox()
            assert box[0] > 0 and box[1] > 0 and box[2] < im.width and box[3] < im.height
    # Review sheet is explicitly separate from transparent production exports.
    sheet = Image.new('RGB', (1600, 1130), '#F1E7D7')
    draw = ImageDraw.Draw(sheet)
    draw.rectangle((800, 0, 1600, 1130), fill='#242325')
    for flat, y in ((False, 30), (True, 560)):
        svg = make_svg(flat)
        for x in (30, 830):
            im = Image.open(BytesIO(resvg_py.svg_to_bytes(svg_string=svg, width=740)))
            sheet.paste(im, (x, y), im)
            for size, sx in ((256, x+60), (128, x+410)):
                small = Image.open(BytesIO(resvg_py.svg_to_bytes(svg_string=svg, width=size)))
                sheet.paste(small, (sx, y+400), small)
    sheet.save(ROOT / 'logo-review.jpg', quality=94)
    print(f'Exported main and flat SVGs + six transparent PNGs. Canvas {WIDTH:.1f} x {HEIGHT:.1f}.')


if __name__ == '__main__':
    main()
