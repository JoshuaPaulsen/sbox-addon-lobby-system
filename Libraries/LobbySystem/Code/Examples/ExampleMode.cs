namespace LobbySystem.Examples;

/// <summary>
/// The simplest reference mode: it runs the round timer and nothing else, so you can see the full lobby,
/// round and back-to-lobby loop. Replace the bodies with your own rules.
/// </summary>
public sealed class ExampleMode : IGameMode
{
	public string DisplayName => "FREE PLAY";

	public void OnRoundStart( LobbyDirector director, IReadOnlyList<ILobbyAgent> agents )
	{
	}

	public bool IsRoundOver( LobbyDirector director, IReadOnlyList<ILobbyAgent> agents ) => false;

	public string ResultText( LobbyDirector director, IReadOnlyList<ILobbyAgent> agents ) => "Round over!";
}

/// <summary>
/// Declares which modes the menu offers and in what order. Put one on the Game Manager.
/// </summary>
public sealed class ExampleModeCatalog : Component, IGameModeCatalog
{
	static readonly IGameMode[] _modes = { new ExampleMode() };
	public IReadOnlyList<IGameMode> Modes => _modes;
}
