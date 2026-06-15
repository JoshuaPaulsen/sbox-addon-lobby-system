# Lobby System

An s&box library that does the boring, fiddly part of a networked game for you. Drop it into your project and you get auto-hosting, Citizen players with a working third-person controller, and a small lobby/round framework you build your own gamemode on top of.

It stays out of your way. The framework never needs to know what your game is; you write the rules.

## What you get

- Auto-hosting. `LobbyNetworkManager` opens a lobby on launch (Steam friends can join) and keeps one networked player per connection, plus optional bots.
- A real player. The example `LobbyPlayer` uses the standard Citizen with WASD, run, jump, a third-person camera, and animation. The owner drives it; proxies stay in sync and wear the right avatar.
- A lobby to round to lobby loop. `LobbyDirector` runs a free-roam lobby, an in-world button that opens a mode menu, a synced countdown, and the return to the lobby afterwards. Ignore the round side and it's just a free-roam multiplayer sandbox.
- One clean extension point. Write your gameplay as an `IGameMode`; make any pawn lobby-aware by implementing `ILobbyAgent`.

## Installing it

This library lives in `Libraries/LobbySystem`. To use it in your own game, copy that folder into your project's `Libraries/` folder and commit it. The editor's Library Manager will pick it up, and your game code can use any of its types.

The repo itself is a small game project so you can open it and press Play to see everything working before you pull it in.

## Quick start

1. Open the project and press Play on `Assets/scenes/demo.scene`. You spawn as the Citizen and can run around. Host and have a friend join over Steam to see the networking.
2. Write your gamemode: implement `IGameMode` (starting roles, win condition, result text) and list it in an `IGameModeCatalog` component. `ExampleMode` is the reference.
3. Use your own player: edit `LobbyPlayer`, or put `ILobbyAgent` on your existing pawn. The framework only needs `IsBot`, `DisplayName`, `InitAgent`, `ResetForRound` and `TeleportTo`.

## Docs

- [Getting Started](docs/GETTING_STARTED.md)
- [API Reference](docs/API.md)

## License

MIT. See [LICENSE](LICENSE).
