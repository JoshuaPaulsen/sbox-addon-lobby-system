namespace LobbySystem;

/// <summary>
/// Auto-hosts a lobby so Steam friends can join, and keeps one networked pawn per connection plus optional
/// bots by cloning <see cref="PlayerPrefab"/>. The pawn only has to implement <see cref="ILobbyAgent"/>.
/// Spawning is de-duped and runs in OnUpdate so a join can't fire mid-enumeration.
/// </summary>
public sealed class LobbyNetworkManager : Component, Component.INetworkListener
{
	[Property] public GameObject PlayerPrefab { get; set; }
	[Property] public int BotCount { get; set; } = 1;

	/// <summary>When true, bots only exist during an active round.</summary>
	[Property] public bool BotsOnlyDuringRound { get; set; } = true;

	[Property] public Color BotTint { get; set; } = new Color( 1f, 0.35f, 0.3f );

	// Lobby spawn ring, used before a round map loads.
	readonly Vector3[] _spawns =
	{
		new Vector3( 0f, -300f, 40f ), new Vector3( 300f, 0f, 40f ),
		new Vector3( 0f, 300f, 40f ),  new Vector3( -300f, 0f, 40f ),
		new Vector3( 250f, 250f, 40f ), new Vector3( -250f, -250f, 40f ),
	};
	int _spawnIndex;
	readonly Dictionary<Guid, GameObject> _pawns = new();
	readonly List<GameObject> _bots = new();

	bool _reconcileNow;
	TimeUntil _nextReconcile;
	TimeUntil _nextSweep;

	protected override async Task OnLoad()
	{
		// When joining a friend the engine is mid-connect and IsActive is briefly false, so poll for a
		// moment before hosting. Otherwise a joiner would spin up its own solo lobby.
		if ( Networking.IsActive ) return;
		for ( int i = 0; i < 6 && !Networking.IsActive; i++ )
			await Task.DelayRealtimeSeconds( 0.1f );
		if ( !Networking.IsActive )
			Networking.CreateLobby( new() );
	}

	void INetworkListener.OnActive( Connection channel ) => _reconcileNow = true;

	protected override void OnUpdate()
	{
		if ( !Networking.IsHost || PlayerPrefab is null ) return;

		if ( !_reconcileNow && _nextReconcile > 0f ) return;
		_reconcileNow = false;
		_nextReconcile = 0.25f;

		try
		{
			bool wantBots = !BotsOnlyDuringRound || (LobbyDirector.Current?.State == LobbyState.Active);
			ReconcileBots( wantBots ? Math.Max( 0, BotCount ) : 0 );

			foreach ( var conn in Connection.All.ToList() )
			{
				if ( conn is null || !conn.IsActive ) continue;
				if ( _pawns.TryGetValue( conn.Id, out var pawn ) && pawn.IsValid() ) continue;
				var id = conn.Id;
				_pawns[id] = FindConnectionPawn( id ) ?? SpawnPawn( false, conn.DisplayName, conn );
			}

			Sweep();
		}
		catch
		{
			// Connection or scene list changed during the pass; retry next frame.
		}
	}

	void ReconcileBots( int target )
	{
		_bots.RemoveAll( b => !b.IsValid() );
		while ( _bots.Count > target )
		{
			var b = _bots[_bots.Count - 1];
			_bots.RemoveAt( _bots.Count - 1 );
			if ( b.IsValid() ) b.Destroy();
		}
		while ( _bots.Count < target )
			_bots.Add( SpawnPawn( true, "Bot", null ) );
	}

	GameObject FindConnectionPawn( Guid id )
	{
		foreach ( var a in Scene.GetAllComponents<ILobbyAgent>() )
		{
			if ( !a.IsValid() || a.IsBot ) continue;
			if ( a is Component c && c.Network.OwnerId == id ) return c.GameObject;
		}
		return null;
	}

	void Sweep()
	{
		if ( _nextSweep > 0f ) return;
		_nextSweep = 1f;
		foreach ( var key in _pawns.Where( kv => !kv.Value.IsValid() ).Select( kv => kv.Key ).ToList() )
			_pawns.Remove( key );
	}

	GameObject SpawnPawn( bool isBot, string displayName, Connection owner )
	{
		var go = PlayerPrefab.Clone( NextSpawn() );
		go.Name = isBot ? "Bot" : $"Player - {displayName}";
		go.Enabled = true;

		var agent = go.Components.Get<ILobbyAgent>() ?? go.Components.GetInChildren<ILobbyAgent>();
		agent?.InitAgent( isBot, displayName );

		if ( isBot )
		{
			var rend = go.Components.GetInChildren<SkinnedModelRenderer>();
			if ( rend is not null ) rend.Tint = BotTint;
		}

		if ( owner is not null ) go.NetworkSpawn( owner );
		else go.NetworkSpawn();
		return go;
	}

	Vector3 NextSpawn()
	{
		int idx = _spawnIndex++;
		var dir = LobbyDirector.Current;
		if ( dir is not null && dir.UseRoundMap && dir.MapReady )
			return dir.RoundSpawnPoint( idx );
		return _spawns[idx % _spawns.Length];
	}
}
