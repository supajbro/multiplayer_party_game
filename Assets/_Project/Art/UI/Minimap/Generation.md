# Minimap artwork

Generated with the built-in image-generation tool using Editor previews of the existing House_01, House_02, House_03 and MainMenuRobotVisual prefabs. The reference renderer shows the robot shell; the prompt supplies the existing digital-face design. Full source generations remain in the session's generated_images directory; project assets are resized copies preserving alpha.

## Assets and editing

- HouseGabled.png, HouseCompact.png, HouseLarge.png: 256 × 256 transparent sprites, high-quality compression, Read/Write enabled for the one-time CPU map bake, no mipmaps or Crunch.
- RobotHead.png: 128 × 128 neutral artwork multiplied by the existing player colours.
- DirectionTriangle.png: 128 × 128 simple white geometry authored in the Editor, independently rotated and colour tinted.
- Saved marker: Assets/_Project/Prefabs/UI/MinimapPlayerMarker.prefab. Edit head/triangle references, offsets and sizes here.
- Each house prefab exposes Minimap House Icon on GeneratedProperty. Five existing house prefabs reference the three shared icons. New/placeholder houses use the Player prefab's Default House Icon.
- NeighbourhoodMap exposes sidewalk width, curb width, house icon fill and the saved marker reference. The original HUD frame and coordinate/zoom settings are retained.

Roads, curbs, sidewalk joints, driveways and alpha-composited house artwork are cached in the existing map texture when the neighbourhood is generated. Trees, streetlights, bushes, fountains, statues and other decorative props receive no minimap geometry. Runtime actors instantiate the saved marker prefab; the head remains upright while the direction triangle rotates in both compact and expanded views. Existing cyan local, blue teammate and green AI colours are retained.

## Verification

14 focused Unity EditMode tests passed, covering existing multiplayer transport/navigation behaviour, saved asset wiring, compressed texture CPU access/transparency, four house rotations, independent heading/tint, continuous road joins and omission of miscellaneous props. A representative crossroad layout was rendered through the production rasteriser and the saved HUD frame at its saved UI dimensions. See output/minimap/HudPreview.png. This is an isolated Editor fixture, not a multiplayer play-session capture. Preview cameras and RenderTextures are Editor-only and are never added to runtime prefabs.

Manual Editor tools are under Couch Guys/Minimap. Configure Generated Artwork is a one-time initial setup command and refuses to overwrite an existing marker prefab. Normal Inspector edits need no builder or re-generation.

## Final generation prompts

House prompt shared prefix:

Use case: stylized-concept. Asset type: transparent minimap house sprite for Couch Guys Unity game, legible at 20-35 pixels. Reference images are actual existing house model previews, use their low-poly suburban architecture. Create ONE isolated building, TRUE TOP DOWN orthographic roof plan, front entrance toward BOTTOM of image. Clean chunky stylized 3D game art, soft baked shading, restrained roof tiles, pale siding/eaves and attached right-side garage. Fully transparent background, tight square centered crop with 6% transparent padding. No ground, lawn, shrubs, trees, props, driveway, text, border or icons. 

Variants appended to that prefix:

1. HouseGabled: Broad family house, dark slate-purple stepped gabled roof, raised central roof section and two lower side wings, white eaves.
2. HouseCompact: Compact single storey blue-grey bungalow, simple broad muted blue hipped roof, small front porch notch, garage on right; clear compact rectangular silhouette.
3. HouseLarge: Larger suburban house with red terracotta cross-gabled roof, central front portico, two broad roof wings, light cream eaves; distinct L-shaped roof footprint.

RobotHead:

Use case: stylized-concept. Asset type: single tiny HUD robot head icon with genuine transparent background, for Couch Guys minimap, readable at 24 pixels. Reference is existing game's rounded rectangular robot head model: preserve its broad rounded box silhouette (NOT a round helmet). Front facing symmetrical head only, no body. Smooth chunky pale silver WHITE / neutral grey shell, dark inset rounded rectangular digital face screen, two large bright WHITE vertical rounded eyes, small symmetric side ear discs. Greyscale neutral art deliberately designed for Unity UI player colour multiplication: all white/grey shell and eyes, almost black screen and fine dark outline; no baked cyan or coloured accents. Crisp game UI illustration, minimal shallow shading. Centered front facing icon fills 88% square. Fully transparent background. No badge, circle surround, triangle, arrow, lettering, glow outside icon, shadow on ground, or props.

