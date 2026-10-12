# Couch Guys Toon Material Workflow

The project uses Unity 6 URP. `Couch Guys/Toon Lit` is an opaque, lightweight URP shader with one forward lighting pass plus shadow and depth passes. It supports baked or probe GI, the main light, additional lights, realtime shadows, fog, GPU instancing, alpha clipping, rim light, compact toon specular, and emission.

## Setup

Use **Couch Guys > Art > Create or Refresh Toon Presets** to create the seven category presets in `Assets/_Project/Materials/Toon`.

Use **Couch Guys > Art > Apply Toon Workflow to Project Prefabs** to convert the current house, road, couch, character, enemy, and vehicle prefab renderer assignments. The command:

- leaves imported FBX materials unchanged;
- creates reusable toon copies in `Materials/Toon/Generated`;
- preserves each source material's base colour, base texture, emission, and alpha cutoff;
- skips particle shaders;
- can safely be run again after rebuilding model wrapper prefabs.

## Inspector controls

- **Base Colour / Base Map**: the material's colour and optional texture.
- **Colour Saturation**: `1` is unchanged; the presets use a mild boost.
- **Shadow Tint / Strength**: controls the colour and depth of the dark lighting band.
- **Light Threshold**: moves the boundary between shadow and light.
- **Band Softness**: near `0` gives a hard cel edge; larger values soften it.
- **Rim**: optional fresnel edge light for distant silhouette readability.
- **Specular**: optional compact toon highlight, enabled by default only for props/characters.
- **Emission**: texture and HDR colour for windows, lamps, signs, and other glowing parts.
- **Alpha Clipping / Cutoff / Cull**: useful for foliage cards and double-sided leaves.

Outlines are intentionally not included in this first pass. A screen-space or inverted-hull outline would add render-feature/setup cost and platform-specific edge cases; rim lighting supplies most of the desired silhouette separation at substantially lower risk.
