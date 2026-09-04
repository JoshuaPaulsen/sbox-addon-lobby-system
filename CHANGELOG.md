# Changelog

## 0.2.0
- Reworked into a clean multiplayer template you press Play on, with no game-specific theming.
- Player: the standard s&box Citizen with a working third-person controller (`LobbyPlayer`: WASD, run, jump, camera, animation) that implements `ILobbyAgent`.
- Bots fall to the ground and wander instead of freezing in mid-air.
- Example gamemode: `ExampleMode` and `ExampleModeCatalog`, a bare reference mode that just runs the round timer.
- HUD: `LobbyHud` for lobby state, the mode menu, and the timer.
- Framework: `LobbyDirector`, `LobbyNetworkManager`, `LobbyModeButton`, `IGameMode`, `IGameModeCatalog`, `ILobbyAgent`, `LobbyState`.

## 0.1.0
- Reusable lobby and round framework: round lifecycle, auto-host with Steam join, per-connection pawn spawning, mode menu, synced timer, map load, and agent tracking.
