# Getting Started

This template is a **game project** you press Play on. Out of the box it spawns networked Citizen players in a shared world. This guide shows the pieces and how to extend them.

## The scene

`Assets/scenes/demo.scene` (the startup scene) contains:

- **Camera** — the main camera; the player controller drives it third-person.
- **Sun / 2D Skybox** — lighting.
- **Floor** — ground with a collider.
- **Game Manager** — `LobbyDirector` + `LobbyNetworkManager` + `ExampleModeCatalog`.
- **ScreenPanel** — the `LobbyHud`.
- **Mode Button** — an in-world `LobbyModeButton`; walk up and press **Use** (E) to open the mode menu.

`LobbyNetworkManager.PlayerPrefab` points at `Assets/lobbysystem/player.prefab` — the Citizen player.

## The player

`Assets/lobbysystem/player.prefab` is a standard s&box Citizen: a `CharacterController`, a `Body` child with the Citizen `SkinnedModelRenderer` + `CitizenAnimationHelper`, and a `Head` child. The `LobbyPlayer` component on it does two jobs:

1. **Movement / camera / animation** — WASD + run + jump, a third-person camera (created/driven from the scene camera), and Citizen animation.
2. **Lobby glue** — it implements `ILobbyAgent` so the framework can name, spawn, place and reset it.

To use your own player, copy `LobbyPlayer` onto your pawn, or just implement `ILobbyAgent` next to your existing controller.

## Writing your gamemode

The template ships `ExampleMode` — a do-nothing mode that just runs the round timer so you can see the full loop. Replace it with your rules:

```csharp
using LobbySystem;

public sealed class MyMode : IGameMode
{
    public string DisplayName => "MY MODE";

    public void OnRoundStart( LobbyDirector dir, IReadOnlyList<ILobbyAgent> agents )
    {
        // Assign starting roles. Cast ILobbyAgent to your pawn type to set your own state.
    }

    public bool IsRoundOver( LobbyDirector dir, IReadOnlyList<ILobbyAgent> agents ) => false; // ends on the timer

    public string ResultText( LobbyDirector dir, IReadOnlyList<ILobbyAgent> agents ) => "Round over!";
}
```

Then list it in the catalog on the Game Manager:

```csharp
using LobbySystem;

public sealed class MyModeCatalog : Component, IGameModeCatalog
{
    static readonly IGameMode[] _modes = { new MyMode() };
    public IReadOnlyList<IGameMode> Modes => _modes;
}
```

(The menu's 1 / 2 / 3 hotkeys map to the array order. Add as many modes as you like.)

## Interactions (your "verbs")

The framework deliberately doesn't define what a "hit", "shot" or "pickup" is — that's your game. Detect it on your pawn (trace / proximity, owner-side), then apply the effect **host-side** so it's authoritative:

```csharp
// on the owner pawn, when it interacts with someone:
RequestInteract( other );

[Rpc.Host]
void RequestInteract( MyPawn target )
{
    if ( target is null || !target.IsValid() ) return;
    // validate on the host, then change state + LobbyDirector.Current.PulseEvent() for a HUD flash
}
```

## HUD

Any Razor `PanelComponent` can read `LobbyDirector.Current` — `LobbyHud` is the reference. Useful members: `State`, `RoundLive`, `TimeLeftSeconds`, `StatusMessage`, `MenuOpen`/`SuggestMenuOpen`, `Modes`, `PickMode(i)`, `ChatLine`/`ChatVisible`, `EventPulse`.

Full type reference: [API.md](API.md).
