namespace LobbySystem;

/// <summary>
/// Gamemode-agnostic round and lobby lifecycle. Drives the Lobby, Active and Ended states, the mode menu,
/// the synced countdown, optional map loading and agent tracking, then hands the actual rules to the active
/// <see cref="IGameMode"/>. Host-authoritative; clients read the synced values.
/// </summary>
public sealed class LobbyDirector : Component
{
	public static LobbyDirector Current { get; private set; } = null!;

	[Property] public float RoundDuration { get; set; } = 180f;
	[Property] public float RestartDelay { get; set; } = 4f;
	[Property] public int MinPlayers { get; set; } = 2;

	/// <summary>Optional map cloned in when a round starts. Leave null to play in the lobby scene.</summary>
	[Property] public GameObject MapPrefab { get; set; } = null!;

	/// <summary>
	/// Load a workshop map package by ident (e.g. "softsplit.gm_bigcity") instead of cloning
	/// <see cref="MapPrefab"/>. Takes precedence over the prefab when set. Each peer loads it
	/// locally, exactly like the prefab path — set it on every peer before the round starts.
	/// </summary>
	[Property] public string MapIdent { get; set; } = "";

	/// <summary>Lobby floor; its renderer hides once a round map has loaded.</summary>
	[Property] public GameObject LobbyFloor { get; set; } = null!;

	/// <summary>Turn on when you use <see cref="MapPrefab"/> so clients load it too.</summary>
	[Property, Sync( SyncFlags.FromHost )] public bool UseRoundMap { get; set; }

	/// <summary>
	/// Hold the round countdown (and round-over checks) while the round map is still loading.
	/// Off by default — existing games keep their exact timing. Turn on so a first-time map
	/// download on a slow connection doesn't quietly eat the round.
	/// </summary>
	[Property] public bool WaitForMapBeforeClock { get; set; }

	[Sync( SyncFlags.FromHost )] public LobbyState State { get; set; } = LobbyState.Lobby;
	[Sync( SyncFlags.FromHost )] public int ActiveModeIndex { get; set; }
	[Sync( SyncFlags.FromHost )] public bool MenuOpen { get; set; }
	[Sync( SyncFlags.FromHost )] public int TimeLeftSeconds { get; set; }
	[Sync( SyncFlags.FromHost )] public string StatusMessage { get; set; } = "";
	[Sync( SyncFlags.FromHost )] public string Banner { get; set; } = "";
	[Sync( SyncFlags.FromHost )] public int EventPulse { get; set; }

	/// <summary>Local flag: a client opened the menu to suggest a mode to the host.</summary>
	public bool SuggestMenuOpen { get; set; }

	float _timeRemaining;
	float _restartAt;
	TimeUntil _startGrace;
	GameObject _mapInstance = null!;
	ModelRenderer _floorRenderer = null!;
	bool _floorResolved;

	List<ILobbyAgent> _agents = new();
	public IReadOnlyList<ILobbyAgent> Agents => _agents;
	int LiveCount => _agents.Count;

	IReadOnlyList<IGameMode> _modes = Array.Empty<IGameMode>();
	public IReadOnlyList<IGameMode> Modes => _modes ?? ResolveModes();
	public IGameMode ActiveMode => Modes.Count > 0 ? Modes[ Math.Clamp( ActiveModeIndex, 0, Modes.Count - 1 ) ] : null;

	IReadOnlyList<IGameMode> ResolveModes()
	{
		try
		{
			var catalog = Scene.GetAllComponents<IGameModeCatalog>().FirstOrDefault();
			_modes = catalog?.Modes ?? (IReadOnlyList<IGameMode>)Array.Empty<IGameMode>();
		}
		catch
		{
			_modes = Array.Empty<IGameMode>();
		}
		return _modes;
	}

	protected override void OnAwake() => Current = this;
	protected override void OnDestroy() { if ( Current == this ) Current = null; }

	bool _roundLive;

	/// <summary>
	/// True while a round is running. The RPC flag flips instantly for connected peers; OR-ing the synced
	/// State also catches players who join mid-round. Drive lobby-vs-round UI off this, not State.
	/// </summary>
	public bool RoundLive => _roundLive || State == LobbyState.Active;
	// HOST-ONLY. `_roundLive` is the single flag that decides whether a round is running at all —
	// it gates spawning, the clock, scoring and the entire HUD. As a plain broadcast, ANY client
	// could call it and end (or start) the round for everybody. The RPC docs are explicit that an
	// unflagged broadcast is callable by anyone.
	[Rpc.Broadcast( NetFlags.HostOnly )] public void RpcSetRoundLive( bool live ) => _roundLive = live;
	void SetRoundLive( bool live ) { if ( Networking.IsActive ) RpcSetRoundLive( live ); else _roundLive = live; }

	/// <summary>
	/// True when the round map exists AND its content has finished loading. The old check passed
	/// the moment the root object was created, which on big workshop maps is seconds (or a first
	/// download) before any geometry exists — loading screens dropped early and consumers saw the
	/// lobby standing inside a half-streamed map. Weak hardware feels this most, so gate on
	/// <see cref="MapInstance.IsLoaded"/> whenever the root carries a MapInstance.
	/// </summary>
	public bool MapReady
	{
		get
		{
			if ( !_mapInstance.IsValid() ) return false;
			try
			{
				var mi = _mapInstance.Components.Get<MapInstance>() ?? _mapInstance.Components.GetInChildren<MapInstance>();
				return mi is null || mi.IsLoaded;
			}
			catch
			{
				return true; // never let a reflection hiccup hold the round hostage
			}
		}
	}

	TimeUntil _nextScan;
	void RefreshAgents()
	{
		if ( _nextScan > 0f && _agents.Count > 0 ) return;
		_nextScan = 0.2f;
		try
		{
			var fresh = new List<ILobbyAgent>();
			foreach ( var a in Scene.GetAllComponents<ILobbyAgent>() )
				if ( a.IsValid() ) fresh.Add( a );
			_agents = fresh;
		}
		catch
		{
			// Scene changed during the scan; keep the previous list and retry next frame.
		}
	}

	/// <summary>A spawn-point position spread by index. False until the map collision has loaded, so retry.</summary>
	public bool TryRoundSpawn( int i, out Vector3 pos )
	{
		pos = default;
		List<SpawnPoint> spawns;
		try { spawns = Scene.GetAllComponents<SpawnPoint>().Where( s => s.IsValid() ).ToList(); }
		catch { return false; }
		if ( spawns.Count == 0 ) return false;
		var sp = spawns[ ((i % spawns.Count) + spawns.Count) % spawns.Count ];
		pos = sp.WorldPosition + Vector3.Up * 20f;
		return true;
	}
	public Vector3 RoundSpawnPoint( int i ) => TryRoundSpawn( i, out var p ) ? p : Vector3.Up * 300f;

	/// <summary>
	/// Adopt a pre-created map root ("warm loading"): a consumer may start loading the round map
	/// early — during a map vote, behind an opaque screen — and hand it over here. The director
	/// then treats it exactly like a map it created itself: <see cref="MapReady"/> tracks its
	/// load, and the normal lifecycle owns its teardown. No-op if a map instance already exists.
	/// </summary>
	public void AdoptMapInstance( GameObject root )
	{
		if ( _mapInstance.IsValid() || !root.IsValid() ) return;
		_mapInstance = root;
	}

	void SyncRoundMap()
	{
		if ( !UseRoundMap ) return;
		if ( !RoundLive || _mapInstance.IsValid() ) return;

		// Create once. Toggling or destroying a MapInstance cancels its async load.
		if ( !string.IsNullOrWhiteSpace( MapIdent ) )
		{
			// Workshop package path: build the map root here instead of needing a prefab per map.
			var go = Scene.CreateObject();
			go.Name = $"Round Map ({MapIdent})";
			var mi = go.Components.Create<MapInstance>();
			mi.MapName = MapIdent;
			_mapInstance = go;
		}
		else if ( MapPrefab is not null )
		{
			_mapInstance = MapPrefab.Clone( Vector3.Zero );
		}
	}

	void SyncLobbyFloor()
	{
		if ( !_floorResolved )
		{
			_floorRenderer = LobbyFloor?.Components.Get<ModelRenderer>();
			_floorResolved = true;
		}
		if ( _floorRenderer is not null )
		{
			bool show = !MapReady;
			if ( _floorRenderer.Enabled != show ) _floorRenderer.Enabled = show;
		}
	}

	public void RequestModeMenu()
	{
		bool host = !Networking.IsActive || Networking.IsHost;
		if ( host ) { if ( MenuOpen ) RequestCloseMenu(); else RequestOpenMenu(); }
		else SuggestMenuOpen = !SuggestMenuOpen;
	}
	public void RequestOpenMenu() { if ( !Networking.IsActive ) OpenMenu(); else RpcOpenMenu(); }
	// The inner `!IsHost return` guarded the RECEIVER, not the sender — every non-host peer ignored
	// the call, so a client invoking this still executed it ON THE HOST, which is the machine whose
	// state actually matters. NetFlags.HostOnly stops it being sent in the first place; the inner
	// check stays as belt-and-braces.
	[Rpc.Broadcast( NetFlags.HostOnly )] public void RpcOpenMenu() { if ( Networking.IsActive && !Networking.IsHost ) return; OpenMenu(); }
	void OpenMenu() { MenuOpen = true; State = LobbyState.Lobby; }

	public void RequestCloseMenu() { SuggestMenuOpen = false; if ( !Networking.IsActive ) CloseMenu(); else RpcCloseMenu(); }
	[Rpc.Broadcast( NetFlags.HostOnly )] public void RpcCloseMenu() { if ( Networking.IsActive && !Networking.IsHost ) return; CloseMenu(); }
	void CloseMenu() { MenuOpen = false; State = LobbyState.Lobby; Banner = ""; }

	/// <summary>Called by the HUD when a mode is clicked. The host starts it; a client suggests it.</summary>
	public void PickMode( int index )
	{
		bool host = !Networking.IsActive || Networking.IsHost;
		if ( host ) ChooseMode( index );
		else { SuggestMenuOpen = false; SuggestMode( index ); }
	}
	public void ChooseMode( int index ) { MenuOpen = false; Banner = ""; StartRound( index ); }

	public string ChatLine { get; private set; } = "";
	TimeUntil _chatUntil;
	public bool ChatVisible => !string.IsNullOrEmpty( ChatLine ) && _chatUntil > 0f;
	public void SuggestMode( int index )
	{
		string who = Connection.Local?.DisplayName ?? "Someone";
		string mode = (index >= 0 && index < Modes.Count) ? Modes[index].DisplayName : "a mode";
		RpcSuggest( who, mode );
	}
	// NOT host-only: a suggestion is a CLIENT telling the room it wants a mode — that's the whole
	// feature, so gating it to the host would delete it. But it writes a string straight onto every
	// peer's HUD from an unverified sender, which is a broadcast text channel with no bounds.
	// Clamp both fields and take the name from the connection rather than the payload.
	[Rpc.Broadcast]
	public void RpcSuggest( string who, string mode )
	{
		try { if ( Rpc.Caller is not null ) who = Rpc.Caller.DisplayName; } catch { }
		who = string.IsNullOrWhiteSpace( who ) ? "Someone" : who;
		if ( who.Length > 48 ) who = who[..48];
		mode = string.IsNullOrWhiteSpace( mode ) ? "a mode" : mode;
		if ( mode.Length > 48 ) mode = mode[..48];

		ChatLine = $"{who} suggested starting {mode}";
		_chatUntil = 6f;
	}

	protected override void OnUpdate()
	{
		RefreshAgents();
		SyncRoundMap();
		SyncLobbyFloor();

		// Only the host drives round state; clients render the synced values.
		if ( Networking.IsActive && !Networking.IsHost ) return;

		if ( MenuOpen ) { StatusMessage = "Select a game mode"; return; }

		switch ( State )
		{
			case LobbyState.Lobby:
				StatusMessage = "Waiting in the lobby";
				break;

			case LobbyState.Active:
				if ( WaitForMapBeforeClock && UseRoundMap && !MapReady ) break; // map still loading — clock holds
				_timeRemaining -= Time.Delta;
				int secs = (int)MathF.Ceiling( _timeRemaining );
				if ( secs != TimeLeftSeconds ) TimeLeftSeconds = secs;
				bool graceOver = _startGrace <= 0f;
				bool earlyOver = graceOver && (LiveCount < MinPlayers || (ActiveMode?.IsRoundOver( this, _agents ) ?? false));
				if ( _timeRemaining <= 0f || earlyOver ) EndRound();
				break;

			case LobbyState.Ended:
				if ( Time.Now >= _restartAt )
				{
					Banner = "";
					State = LobbyState.Lobby;
					SetRoundLive( false );
				}
				break;
		}
	}

	/// <summary>Begin a round with the given mode index. Host only.</summary>
	public void StartRound( int modeIndex )
	{
		if ( Modes.Count == 0 ) return;
		ActiveModeIndex = Math.Clamp( modeIndex, 0, Modes.Count - 1 );

		var live = _agents.Where( a => a.IsValid() ).ToList();
		if ( live.Count < 1 ) { State = LobbyState.Lobby; return; }

		State = LobbyState.Active;
		SetRoundLive( true );
		_startGrace = 1.5f;
		_timeRemaining = RoundDuration;
		TimeLeftSeconds = (int)RoundDuration;

		foreach ( var a in live ) a.ResetForRound();
		ActiveMode?.OnRoundStart( this, live );

		StatusMessage = "";
		Banner = ActiveMode?.DisplayName ?? "";
	}

	void EndRound()
	{
		State = LobbyState.Ended;
		_timeRemaining = 0f;
		TimeLeftSeconds = 0;
		_restartAt = Time.Now + RestartDelay;
		StatusMessage = ActiveMode?.ResultText( this, _agents ) ?? "Round over!";
		Banner = "ROUND OVER";
	}

	/// <summary>Bump <see cref="EventPulse"/> to flash every HUD once.</summary>
	public void PulseEvent() => EventPulse++;
}
