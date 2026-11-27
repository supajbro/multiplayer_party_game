# Project conventions

- Work directly in the project files. Do not open or control the interactive Unity Editor, including through UI automation or a live Editor integration. Use Unity in headless batch mode and other non-interactive tools as needed for validation. Report any Unity checks that could not be run.
- Store all project-owned prefab assets under `Assets/_Project/Prefabs/`, grouped by purpose (for example, `UI`, `Houses`, and `Vehicles`). Do not create prefabs in `Resources` or beside scripts.
- Deliver saved prefab assets that can be configured later in the Unity Editor without modifying code. Expose configurable references and settings through serialized fields and the Inspector.
- Create and edit prefab assets through serialized asset files or non-interactive authoring tools. Preserve valid GUIDs, file IDs, and serialized references when editing assets.
- Do not build or rebuild prefab hierarchies at runtime. Runtime code should instantiate saved prefab assets and apply gameplay state, rather than reconstruct their UI, visuals, or components. Editor-only authoring tools are allowed when useful, but the resulting assets must remain editable without rerunning a builder or changing code. Migrate existing builders only within the requested task scope.
- Prefer serialized prefab references. If runtime lookup requires `Resources`, load a small configuration asset with a serialized reference to the prefab; keep the prefab itself in the prefab folder.
- Preserve Inspector customizations. Do not automatically generate, rebuild, or overwrite prefab assets on import, script reload, or entering Play mode.
- Keep reviews and optimizations focused on significant issues. Avoid broad refactors or unrelated asset migrations unless requested.
