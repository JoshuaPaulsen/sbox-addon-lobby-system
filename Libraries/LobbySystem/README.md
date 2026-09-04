# Lobby System

Networked multiplayer setup for s&box, done for you. Install this and you get auto-hosting, Citizen players with a working third-person controller, and a small lobby and round framework you build your own gamemode on top of. It never needs to know what your game is; you write the rules.

## What you get

- Auto-hosting. `LobbyNetworkManager` opens a lobby on launch (Steam friends can join) and keeps one networked player per connection, plus optional bots.
- A real player. The example `LobbyPlayer` uses the standard Citizen with WASD, run, jump, a third-person camera, and animation. The owner drives it; proxies stay in sync and wear the right avatar.
- A lobby to round to lobby loop. `LobbyDirector` runs a free-roam lobby, an in-world button that opens a mode menu, a synced countdown, and the return to the lobby. Ignore the round side and it is just a free-roam multiplayer sandbox.
- One clean extension point. Write your gameplay as an `IGameMode`; make any pawn lobby-aware by implementing `ILobbyAgent`.

## Using it

Everything lives under this library. Drop your gamemode in as an `IGameMode`, list it in an `IGameModeCatalog`, and you are running. Use the included `LobbyPlayer`, or put `ILobbyAgent` on your own pawn; the framework only needs `IsBot`, `DisplayName`, `InitAgent`, `ResetForRound` and `TeleportTo`.

The example scene shows the whole thing wired up: lights, floor, a Game Manager with the director and network manager, the HUD, and the in-world mode button.

## Layout

- `Code/` holds the framework (`LobbyDirector`, `LobbyNetworkManager`, `LobbyModeButton`, `ILobbyAgent`, `IGameMode`, `IGameModeCatalog`, `LobbyState`) plus a worked example (`LobbyPlayer`, `ExampleMode`, `LobbyHud`).
- `Assets/` holds the Citizen player prefab.

MIT licensed.
