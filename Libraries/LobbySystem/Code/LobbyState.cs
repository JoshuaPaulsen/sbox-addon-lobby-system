namespace LobbySystem;

/// <summary>Lifecycle of the lobby: Lobby, then Active, then Ended before looping back.</summary>
public enum LobbyState
{
	/// <summary>Free roam before and after a round. The mode button works here.</summary>
	Lobby,
	/// <summary>A round is running.</summary>
	Active,
	/// <summary>Round finished; results show before returning to the lobby.</summary>
	Ended
}
