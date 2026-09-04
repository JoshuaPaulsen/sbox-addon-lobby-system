namespace LobbySystem;

/// <summary>
/// In-world button that opens the mode menu for the host, or a local suggestion menu for a client. It needs
/// a ModelRenderer to be visible and is hidden while a round is live. When the local player is within
/// <see cref="UseRange"/> and presses Use, the menu opens.
/// </summary>
public sealed class LobbyModeButton : Component
{
	[Property] public float UseRange { get; set; } = 130f;
	[Property] public bool GlowWhenInRange { get; set; } = true;
	[Property] public Color IdleTint { get; set; } = new Color( 0.85f, 0.4f, 0.15f );
	[Property] public Color ActiveTint { get; set; } = new Color( 1f, 0.85f, 0.3f );

	ModelRenderer _renderer = null!;
	ILobbyAgent _me;

	protected override void OnStart()
	{
		_renderer = Components.Get<ModelRenderer>() ?? Components.GetInChildren<ModelRenderer>();
		if ( _renderer is not null ) _renderer.Tint = IdleTint;
	}

	protected override void OnUpdate()
	{
		var dir = LobbyDirector.Current;
		bool inLobby = dir is null || !dir.RoundLive;
		if ( _renderer is not null && _renderer.Enabled != inLobby )
			_renderer.Enabled = inLobby;
		if ( !inLobby ) return;

		var me = LocalPlayer();
		bool inRange = me is not null && WorldPosition.Distance( me.WorldPosition ) <= UseRange;

		if ( GlowWhenInRange && _renderer is not null )
			_renderer.Tint = inRange ? ActiveTint : IdleTint;

		if ( inRange && Input.Pressed( "Use" ) )
			dir?.RequestModeMenu();
	}

	ILobbyAgent LocalPlayer()
	{
		if ( _me is not null && _me.IsValid() && !_me.IsBot && !_me.IsProxy ) return _me;
		try { _me = Scene.GetAllComponents<ILobbyAgent>().FirstOrDefault( c => c.IsValid() && !c.IsBot && !c.IsProxy ); }
		catch { _me = null; }
		return _me;
	}
}
