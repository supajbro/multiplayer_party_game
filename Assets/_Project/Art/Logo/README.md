# Couch Guys logo

Original vector construction inspired by the supplied concept's stacked wording,
warm palette, and playful proportions. The reference image is not embedded or traced.

## Deliverables

- `couch-guys.svg`: primary wordmark with a restrained solid offset shadow.
- `couch-guys-flat.svg`: simplified three-color wordmark without the shadow.
- `couch-guys-{1024,2048,4096}.png`: transparent primary exports at the named pixel width.
- `couch-guys-flat-{1024,2048,4096}.png`: matching transparent flat exports.
- `logo-review.jpg`: review sheet only, showing primary above flat on light and dark backgrounds, with 256 px and 128 px samples.

Both SVGs share the same canvas and proportions. SVG lettering consists entirely
of vector paths, individually named and grouped by word. Outline and shadow layers
are separate editable path groups. There are no live text objects, raster images,
external dependencies, gradients, filters, or clipping masks. Open the SVG in a
vector editor to adjust paths or recolor the layers. Keep each letter's outline
and shadow aligned when moving its fill path.

## Palette and usage

| Element | sRGB color |
| --- | --- |
| COUCH / warm cream | `#FFF0D2` |
| GUYS / golden orange | `#FFB52E` |
| Outline / dark brown | `#44251B` |
| Primary shadow | `#2E1914` |

Preserve the aspect ratio. Use the flat version for especially small placements.
The review sheet includes 128 px wide samples; use at least that width when possible.
Allow clear space outside the supplied canvas when placing against other graphics.
The PNGs contain straight RGBA transparency with no background matte. Rendered
exports were checked for exact width, transparent corners, and unclipped artwork.

## Font and editing source

Letter shapes originate from **Lilita One**, designed by Juan Montoreano, under the
SIL Open Font License 1.1. This license permits commercial use. The original font
and full license are preserved in `Source/`; no font installation is needed to
use the delivered SVGs or PNGs. The font license does not impose its licensing
requirements on artwork created using the font.

Font source: https://github.com/google/fonts/tree/main/ofl/lilitaone

To reconstruct exports with Python 3.12 or later:

```sh
python -m pip install -r Source/requirements.txt
python Source/build_logo.py
```

Run these commands from this directory. Geometry, optical spacing, per-letter
rotations, arch, palette, and outline thickness are explicit in `build_logo.py`.
The script only writes the named logo exports and review sheet in this directory.
It does not run automatically in Unity. Existing SVG edits are preserved unless
you explicitly rerun the generator, which replaces the exports.
