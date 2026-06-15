# API Reference

All types live in the `LobbySystem` namespace.

---

## `LobbyDirector : Component`

The reusable round/lobby lifecycle. One per scene. Host-authoritative.

### Static
- `static LobbyDirector Current` — the director in the current scene.

### Inspector properties
| Property | Type | Default | Notes |
| --- | --- | --- | --- |
| `RoundDuration` | float | 180 | Round length in seconds. |
| `RestartDelay` | float | 4 | Seconds the results show before returning to the lobby. |
| `MinPlayers` | int | 2 | Below this (after a grace period) the round ends early. |
| `MapPrefab` | GameObject | null | Optional map cloned in when a round starts. |
| `LobbyFloor` | GameObject | null | Its `ModelRenderer` is hidden once a round map loads. |
| `UseRoundMap` | bool (synced) | false | Enable map loading + spawn-point placement. |

### Synced state (read from your HUD/pawn)
| Member | Type | Meaning |
| --- | --- | --- |
| `State` | `LobbyState` | Lobby / Active / Ended. |
| `RoundLive` | bool | True during a round. `= _roundLive \|\| State == Active` — the RPC flag flips instantly for connected peers, the `State` term catches mid-round joiners. **Use this, not `State`, to drive lobby-vs-round UI.** |
| `ActiveModeIndex` | int | Index into `Modes` of the running mode. |
| `ActiveMode` | `IGameMode` | The running mode (or null if no catalog). |
| `Modes` | `IReadOnlyList<IGameMode>` | From your `IGameModeCatalog`. |
| `MenuOpen` | bool | Host's mode menu is open. |
| `SuggestMenuOpen` | bool | LOCAL: a client's suggestion menu is open. |
| `TimeLeftSeconds` | int | Countdown (whole seconds — int so it syncs once a second, not every frame). |
| `StatusMessage` | string | Lobby/idle status text. |
| `Banner` | string | Round banner ("ROUND OVER", mode name, etc.). |
| `EventPulse` | int | Increments on `PulseEvent()`; use as a HUD flash key. |
| `ChatLine` / `ChatVisible` | string / bool | Transient broadcast line (mode suggestions). |
| `MapReady` | bool | The round map has been cloned in. |
| `Agents` | `IReadOnlyList<ILobbyAgent>` | Currently-tracked pawns (re-check `IsValid()`). |

### Methods
- `void RequestModeMenu()` — host toggles the real menu; a client toggles its suggestion menu. (Wired to `LobbyModeButton`.)
- `void PickMode(int index)` — host starts that mode; a client broadcasts it as a suggestion.
- `void ChooseMode(int index)` / `void StartRound(int index)` — start a round (host).
- `void RequestCloseMenu()` — leave the menu without starting.
- `bool TryRoundSpawn(int i, out Vector3 pos)` — a spawn-point position (spread by `i`); false until the map's collision has loaded, so **retry**.
- `Vector3 RoundSpawnPoint(int i)` — best-effort version of the above.
- `void PulseEvent()` — bump `EventPulse` to flash every HUD.

### Lifecycle, in order
`Lobby` → (host picks a mode) → `StartRound` sets `RoundLive`, resets agents, calls `IGameMode.OnRoundStart` → `Active` (ticks the timer, checks `IGameMode.IsRoundOver` and `MinPlayers`) → `EndRound` shows `IGameMode.ResultText` → `Ended` (waits `RestartDelay`) → clears `RoundLive`, back to `Lobby`.

---

## `IGameMode`

Your gameplay rules. Implement as a **stateless** class (safe to share as a singleton). All methods run host-side.

```csharp
string DisplayName { get; }
void   OnRoundStart(LobbyDirector director, IReadOnlyList<ILobbyAgent> agents);
bool   IsRoundOver (LobbyDirector director, IReadOnlyList<ILobbyAgent> agents);
string ResultText  (LobbyDirector director, IReadOnlyList<ILobbyAgent> agents);
```

- `OnRoundStart` — assign starting roles (cast agents to your pawn type).
- `IsRoundOver` — return true to end before the timer. Iterate, don't allocate (it's polled every frame).
- `ResultText` — the end-of-round line.

Interactions (your "interaction") are **not** in this interface — detect them on your pawn and apply them host-side (see GETTING_STARTED §4).

---

## `IGameModeCatalog`

```csharp
IReadOnlyList<IGameMode> Modes { get; }
```

One Component implementing this declares your modes and their order (menu hotkeys 1/2/3 map to indices 0/1/2). The director reads it once at runtime.

---

## `ILobbyAgent : IValid`

Your pawn implements this so the framework can manage it generically.

```csharp
bool    IsBot       { get; }   // AI pawn?
bool    IsProxy     { get; }   // inherited from Component
string  DisplayName { get; }
Vector3 WorldPosition { get; } // inherited from Component
void InitAgent(bool isBot, string displayName); // host brands the pawn after spawn
void ResetForRound();                            // clear all gameplay state
void TeleportTo(Vector3 position);               // implement as [Rpc.Broadcast]
```

Network `IsBot`/`DisplayName` (and any role fields) with `[Sync(SyncFlags.FromHost)]`. Implement `TeleportTo` as a broadcast RPC so proxies move too.

---

## `LobbyNetworkManager : Component, INetworkListener`

Auto-hosts and maintains pawns. Put it beside the director.

| Property | Type | Default | Notes |
| --- | --- | --- | --- |
| `PlayerPrefab` | GameObject | null | Cloned per connection. Root must implement `ILobbyAgent`. |
| `BotCount` | int | 1 | Number of bots to maintain. |
| `BotsOnlyDuringRound` | bool | true | Bots exist only while `State == Active`. |
| `BotTint` | Color | reddish | Applied to a bot's `SkinnedModelRenderer`. |

Behavior: polls briefly on launch before hosting (so a joining friend isn't beaten to it); spawns/reconciles one pawn per active connection + bots at a safe point; de-dupes via a connection→pawn dictionary and a periodic sweep.

---

## `LobbyModeButton : Component`

In-world button that opens the mode menu. Put it on an object with a `ModelRenderer` in the lobby; hidden while a round is live.

| Property | Type | Default |
| --- | --- | --- |
| `UseRange` | float | 130 |
| `GlowWhenInRange` | bool | true |
| `IdleTint` / `ActiveTint` | Color | orange / yellow |

Reads the `Use` input action. Calls `LobbyDirector.Current.RequestModeMenu()`.

---

## `LobbyState`

`Lobby` (free-roam, no round) · `Active` (round running) · `Ended` (results, then back to lobby).

---

## Netcode model

- **Host-authoritative state** is `[Sync(SyncFlags.FromHost)]` on the director and your pawn's role fields. The host writes; clients read.
- **Owner-authoritative state** (look direction, input-driven animation flags) is plain `[Sync]` on your pawn — the owner writes, proxies read.
- **State mutations flow** owner-detects → `[Rpc.Host]` validates → `[Rpc.Broadcast]` applies. Don't let a client write host-authoritative state directly.
- **Teleports broadcast** so proxies don't visually desync.
- **`RoundLive`** combines an instant RPC flag with synced `State` so both connected and mid-round-joining players agree.

## Known limitations

- **Host migration** (host leaves mid-round) is not handled — round state lives on the host. Decide whether to block it or end the round.
- One `LobbyDirector` and one `IGameModeCatalog` per scene.
- The in-world button position is yours to place; if you load a map that overlaps the lobby origin, make sure the button stays reachable (or open the menu via your own hotkey calling `RequestModeMenu`).
