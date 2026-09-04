namespace LobbySystem;

/// <summary>
/// The rules of one gamemode. The <see cref="LobbyDirector"/> owns the lifecycle and calls into the active
/// mode for gameplay decisions. Keep implementations stateless so they can be shared as singletons. All
/// three methods run on the host.
/// </summary>
public interface IGameMode
{
	/// <summary>Name shown on the HUD and in the menu.</summary>
	string DisplayName { get; }

	/// <summary>Called once when a round begins. Assign starting roles here.</summary>
	void OnRoundStart( LobbyDirector director, IReadOnlyList<ILobbyAgent> agents );

	/// <summary>Return true to end the round before the timer expires.</summary>
	bool IsRoundOver( LobbyDirector director, IReadOnlyList<ILobbyAgent> agents );

	/// <summary>Result line shown when the round ends.</summary>
	string ResultText( LobbyDirector director, IReadOnlyList<ILobbyAgent> agents );
}

/// <summary>
/// One component in the scene implements this to declare the available modes and their order. The menu
/// hotkeys 1, 2 and 3 map to the list indices.
/// </summary>
public interface IGameModeCatalog
{
	IReadOnlyList<IGameMode> Modes { get; }
}
