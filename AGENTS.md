# Project conventions

- Store all project-owned prefab assets under `Assets/_Project/Prefabs/`, grouped by purpose (for example, `UI`, `Houses`, and `Vehicles`). Do not create prefabs in `Resources` or beside scripts.
- Author and edit configurable prefab hierarchies directly in the Unity Editor. Do not add scripts that generate or rebuild prefabs, including Editor-only builders. Runtime code should instantiate saved prefab assets rather than reconstruct their UI, visuals, or components. Existing builder scripts are legacy code, not a pattern to copy; migrate them only within the requested task scope.
- Prefer serialized prefab references. If runtime lookup requires `Resources`, load a small configuration asset with a serialized reference to the prefab; keep the prefab itself in the prefab folder.
- Preserve Inspector customizations. Do not automatically generate, rebuild, or overwrite prefab assets on import, script reload, or entering Play mode.
- Keep reviews and optimizations focused on significant issues. Avoid broad refactors or unrelated asset migrations unless requested.
