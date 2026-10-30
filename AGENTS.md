# Project conventions

- Store all project-owned prefab assets under `Assets/_Project/Prefabs/`, grouped by purpose (for example, `UI`, `Houses`, and `Vehicles`). Do not create prefabs in `Resources` or beside scripts.
- Deliver saved prefab assets that can be configured later in the Unity Editor without modifying code. Expose configurable references and settings through serialized fields and the Inspector.
- Agents may create and edit prefab assets directly through serialized asset files, Unity tooling, or explicit Editor-only authoring tools. Using the Unity Editor UI is not required, and choosing another authoring method does not require special approval. Preserve valid GUIDs, file IDs, and serialized references when editing assets.
- Do not build or rebuild prefab hierarchies at runtime. Runtime code should instantiate saved prefab assets and apply gameplay state, rather than reconstruct their UI, visuals, or components. Editor-only authoring tools are allowed when useful, but the resulting assets must remain editable without rerunning a builder or changing code. Migrate existing builders only within the requested task scope.
- Prefer serialized prefab references. If runtime lookup requires `Resources`, load a small configuration asset with a serialized reference to the prefab; keep the prefab itself in the prefab folder.
- Preserve Inspector customizations. Do not automatically generate, rebuild, or overwrite prefab assets on import, script reload, or entering Play mode.
- Keep reviews and optimizations focused on significant issues. Avoid broad refactors or unrelated asset migrations unless requested.
