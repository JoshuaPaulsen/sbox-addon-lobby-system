namespace LobbySystem;

/// <summary>
/// Implemented by a player or bot pawn so the lobby can count, place, reset and teleport it without knowing
/// anything about the gameplay. Keep roles (who is "it", score, team) on your own type and cast back to it
/// inside an <see cref="IGameMode"/>.
/// </summary>
public interface ILobbyAgent : IValid
{
	/// <summary>True for AI pawns.</summary>
	bool IsBot { get; }

	/// <summary>True on peers that don't own this pawn.</summary>
	bool IsProxy { get; }

	/// <summary>Steam name for players, a label for bots.</summary>
	string DisplayName { get; }

	/// <summary>Used for spawn placement and proximity checks.</summary>
	Vector3 WorldPosition { get; }

	/// <summary>Host brands the pawn just after spawn. Network these fields.</summary>
	void InitAgent( bool isBot, string displayName );

	/// <summary>Return all gameplay state to a neutral pre-round condition.</summary>
	void ResetForRound();

	/// <summary>Move the pawn on every peer. Implement with <c>[Rpc.Broadcast]</c>.</summary>
	void TeleportTo( Vector3 position );
}
