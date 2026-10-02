# Steam Multiplayer Implementation

## Outcome

The project uses a small host/client architecture for one to four Steam users:

```text
Steam lobby
    └── FishySteamworks peer-to-peer transport
        └── FishNet host/client session
            └── PlayerSpawner creates one owned Player per connection
```

The existing `CharacterController`, input reader, movement controller, and third-person camera remain the gameplay foundation. FishNet's owner-authoritative `NetworkTransform` synchronises only the Player root position and rotation. Camera transforms and look input remain local.

## Packages

- FishNet — network lifecycle, ownership, spawning, despawning, and transform synchronisation.
- FishySteamworks — Steam peer-to-peer transport for FishNet.
- Steamworks.NET — Steam initialisation, lobbies, discovery, invitations, identity, and callbacks.

FishNet and Steamworks.NET are pinned in `Packages/manifest.json`, with embedded copies under `Packages/` so this checkout can import without fetching them again. FishySteamworks 4.1.1 (upstream commit `21e8582`) is vendored under `Assets/ThirdParty/FishySteamworks/` with its licence and README. Its small assembly definition adapts the maintained Unity asset integration to this project's package-based FishNet and Steamworks.NET dependencies.

## Development App ID

The repository-root `steam_appid.txt` contains Valve's Spacewar development App ID:

```text
480
```

The App ID is deliberately not embedded in gameplay code. Before shipping, replace `steam_appid.txt` and the Steam build/depot configuration with the real Couch Guys App ID. Do not ship Spacewar configuration as production configuration.

Steam must be running and signed in before starting the game. Initialisation logs the local persona name and Steam ID. A failed initialisation reports a clear Console error and disables lobby actions. Desktop builds automatically copy the project-root file beside the executable through `SteamAppIdBuildProcessor`.

## Generated Assets and Runtime Startup

`Couch Guys > Build Steam Multiplayer Assets` upgrades the Player prefab and creates:

- `Assets/_Project/Resources/SteamMultiplayerBootstrap.prefab`
- `Assets/_Project/Settings/NetworkSpawnablePrefabs.asset`

The builder is repeatable and also runs after import when either generated asset is missing. `SteamMultiplayerAutoLoader` loads the bootstrap before the first scene, so scenes do not need their own NetworkManager.

The bootstrap contains Steam initialisation, FishNet's NetworkManager, FishySteamworks transport, lobby controller, developer UI, PlayerSpawner, and four spawn transforms. The spawn positions form a 4-by-4 metre square centred on the origin:

- `(-2, 0, -2)`
- `(2, 0, -2)`
- `(-2, 0, 2)`
- `(2, 0, 2)`

Move the `SpawnPoints` child transforms on the generated prefab if the environment uses a different playable origin or floor height. This implementation does not create a floor.

## Host and Join Flow

The temporary IMGUI panel appears at the top-left in development and provides:

- **Host Game** — creates a friends-only Steam lobby with a maximum of four members, starts the FishNet server, then connects the host as a local client.
- **Find Lobbies** — searches for lobbies carrying the Couch Guys development metadata.
- **Join Lobby ID** — connects directly to a pasted numeric Steam lobby ID.
- **Invite Steam Friend** — opens Steam's lobby invitation overlay.
- **Leave Session** — leaves the lobby and stops the local network session.

Accepting a Steam friend lobby invitation is handled through `GameLobbyJoinRequested_t` and automatically starts the join flow. The host address is stored once as Steam lobby metadata; clients pass that Steam ID to FishySteamworks.

## Ownership and Player Lifecycle

FishNet's PlayerSpawner creates exactly one `Player.prefab` for each authenticated connection and assigns that connection as owner. Its default owned-object cleanup removes a player's object after disconnect.

`NetworkPlayerOwnership` starts every spawned Player with local gameplay components disabled, then enables them only when `IsOwner` is true:

- `PlayerInputReader`
- `ThirdPersonPlayerController`
- `ThirdPersonCameraController`
- the Player Camera
- the Player AudioListener

Remote players therefore cannot read the local keyboard or mouse, create an active gameplay camera, alter the camera target, or add an active AudioListener. Temporary blue, red, green, and yellow material property colours are selected from the network owner ID so connected characters are easy to distinguish without duplicating materials.

Each spawned `PlayerInputReader` clones the Input Action Asset at runtime. This prevents one remote instance disabling the same shared action map used by the local owner.

## Two-User Test

Steam does not allow two simultaneous clients under the same Steam account. Test on two machines or two OS/user environments, each signed into a different Steam account which can run Spacewar.

1. Ensure Steam is running on both machines and both users are Steam friends.
2. Open the same game build on both machines. For Editor testing, keep `steam_appid.txt` in the project root. The Unity build processor copies it beside desktop executables automatically; verify it exists when distributing a test build through another pipeline.
3. On machine A, select **Host Game** and note the logged/displayed lobby ID.
4. Select **Invite Steam Friend**, or give the lobby ID to machine B.
5. On machine B, accept the Steam invitation or paste the number and select **Join Lobby ID**.
6. Confirm both users see two differently coloured players.
7. Confirm each keyboard and mouse controls only its locally owned Player and local camera.
8. Move and rotate both players and confirm the other machine receives both transforms.
9. Leave from the client and confirm only that Player disappears while the host continues.
10. Leave from the host and confirm the session ends for the client.

## Logs

Development logging covers Steam initialisation, persona name, Steam ID, lobby success/failure and ID, lobby members entering/leaving, local client/server connection state, remote network connections, and connection failures. No per-frame gameplay logging is performed.

## Current Scope

- Host migration is not implemented; host departure ends the session.
- Lobby UI is intentionally a developer/test interface.
- The movement transform is client-authoritative for this prototype. Competitive validation and anti-cheat are outside this cooperative milestone.
- Couch carrying extends this authority model without changing the Steam session flow; see `COUCH_CARRYING_IMPLEMENTATION.md`. Matchmaking polish, save data, and relay fallback are not included.
