# Couch Guys Project Conventions

All Couch Guys code and assets belong beneath `Assets/_Project/`. Third-party packages belong in `Assets/Plugins/` or `Assets/ThirdParty/` and should keep their original structure.

Project-specific naming, comments, documentation, Inspector labels, variables, methods, classes, and files use Australian English wherever it differs from American English. Unity API identifiers remain unchanged.

Use these C# conventions:

- PascalCase for classes, methods, properties, and public types.
- `m_camelCase` for private serialised fields and private member variables.
- Clear descriptive names instead of abbreviations.
- One primary type per file, with the filename matching the type.
- Keep input, movement, camera, and future networking responsibilities separated.

Current top-level folders are `Animations`, `Audio`, `Input`, `Prefabs`, `Resources`, `Scenes`, `Scripts`, `Settings`, and `Shaders`. Add new custom content to the closest appropriate folder rather than the root `Assets` directory.

Steam multiplayer uses FishNet and Steamworks.NET packages plus the FishySteamworks transport under `Assets/ThirdParty/`. Project-specific networking code remains under `Assets/_Project/Scripts/Networking/`. See `STEAM_MULTIPLAYER.md` for setup, testing, and production App ID migration notes.

The vendored FishySteamworks folder includes its upstream licence and README; keep those files with the transport when updating it.
