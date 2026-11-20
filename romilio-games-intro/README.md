# Romilio Games Intro

Reusable React/TypeScript composition in `src/RomilioGamesIntro.tsx`.
1920x1080, 30 fps, 120 frames (4 seconds), silent H.264 MP4.

```sh
npm ci
npm run dev
npm run render
npm run lint
npm run check-frames
```

Studio: http://localhost:3000/RomilioGamesIntro (the CLI prints another port if occupied).
Rendered video: `out/RomilioGamesIntro.mp4`.

## Timing

- ROMILIO starts at frames 0, 4, 7, 11, 14, 18, 22 (approximately 0.12-second spacing).
- Each letter rises 22 pixels from 90% scale with a restrained spring and fades from zero opacity. Dots share their letter's transform. Each spring locks at its destination after 38 frames; the last finishes at frame 60.
- GAMES and both lines fade together at frames 60-75 with a smooth 0.5-second opacity curve and no transforms.
- Frames 75-119 hold the complete original image (1.5 seconds).

## Original pixels and extracted assets

`public/logo/reference.png` is an unchanged copy of the supplied PNG, whose actual filename had no `(1)` suffix. No typefaces, replacement lettering, or generated artwork are used.

`public/logo/` includes twelve transparent letter/dot/GAMES/line layers and an unobstructed navy background. The background interpolates original navy samples above and below the artwork, leaving the remainder unchanged. Extraction unmattes the original pixels against this background, including the original glows. Feathered masks overlap by 48 pixels and compensate alpha so overlaps do not double the glow. All crops include 24 pixels of padding on every side; component positions compensate exactly.

To regenerate layers on Windows, use Windows PowerShell (System.Drawing is built in; no extra image dependencies):

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/extract-layers.ps1
```

Extraction writes numerical reconstruction error to `public/logo/extraction-check.json`. After the fade, the unchanged reference is displayed at its native coordinates to eliminate alpha quantization entirely. The lossless final PNG can be compared pixel-for-pixel with the reference; MP4 uses normal H.264 compression and YUV 4:2:0 chroma subsampling.

To reuse in another Remotion project, copy `src/RomilioGamesIntro.tsx` and `public/logo/`, then register the exported `RomilioGamesIntro` with the same dimensions, fps, and duration. Keep the source artwork at its native size.
