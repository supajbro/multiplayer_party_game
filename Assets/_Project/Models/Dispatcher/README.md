# Dispatcher 01

Integrated into `Assets/_Project/Prefabs/DeliveryNPC.prefab` through saved FBX instances. The original frog model assets are preserved. Runtime gameplay scripts and scene transforms are unchanged.

- `dispatcher_01.blend`: editable source, with three coincident LOD collections. Only LOD0 is visible initially; unhide the other collections' objects to inspect them.
- `dispatcher_01_LOD0.fbx`: 5,416 triangles.
- `dispatcher_01_LOD1.fbx`: 3,088 triangles.
- `dispatcher_01_LOD2.fbx`: 1,000 triangles; vents and wheel hubs omitted, lower-sided forms.
- `dispatcher_01_preview.png`: rendered preview of the round-tripped LOD0 FBX. Preview lighting and floor are excluded from the model assets.
- `dispatcher_01_geometry.json` and `dispatcher_01_validation.json`: mesh counts and FBX round-trip results.

All source LODs use a ground-centre pivot, metre scale, and matching proportions. Overall source size is approximately 1.09 m wide, 0.89 m deep, and 2.464 m tall. Blender front is -Y, exported with standard FBX -Z forward / Y up settings. The Unity prefab preserves the FBX axis conversion, fits the visual to 2.2 m height, grounds and centres it under the existing gameplay root, and faces the display along root +Z. The existing capsule collider is retained and resized to the visual bounds.

`TVVisual` contains a cross-fading `LODGroup` with screen-height thresholds 0.22 / 0.09 / 0.008 and 20% transition widths. At a 60-degree vertical FOV and default LOD bias this corresponds to approximately 9 m / 21 m / 238 m. All six body/screen renderers use saved URP Lit materials under `Materials/`. `Materials/Dispatcher_Screen.mat` is independently editable and shared by all three screen meshes. The screen stays blank per the follow-up correction; no static waveform or animated shader is included.

Each LOD contains a named root and exactly two meshes:

1. `dispatcher_01_LODn_Body`: casing, bezel, buttons, beacon, pole, chassis and four wheels; named material slots support later editing.
2. `dispatcher_01_LODn_Screen`: separate gently convex black surface, with its own `Dispatcher_Screen` material and `ScreenUV` UV0. UVs map left-to-right and bottom-to-top across the full display into [0,1]. The open-backed display patch faces outward and is suitable for a future screen shader.

The screen is blank. No static waveform geometry or waveform material is included; the waveform will be implemented as a separate shader later.

One-shot authoring and validation scripts are under `Tools/Blender/`. They are manual tools and never run on Unity import, script reload or Play mode. Running the authoring tool again explicitly overwrites these newly authored dispatcher model files, so preserve any subsequent manual edits first.

Model validation reimported all three FBXs into isolated Blender scenes and checked mesh separation, material presence, screen UVs, common origins, finite vertices, metre-scale bounds, ground contact, and decreasing triangle counts.

Unity integration was subsequently validated with the explicit `Couch Guys/Dispatcher` Editor commands. See `Tools/DispatcherIntegrationValidation.txt` for saved-prefab checks. The legacy automatic frog rebuild on script reload was removed to preserve the integrated TV and Inspector edits. No runtime player-facing rotation logic was added; the existing spawn marker controls dispatcher orientation. Live multiplayer interaction and camera-driven transition playtests remain manual checks.

