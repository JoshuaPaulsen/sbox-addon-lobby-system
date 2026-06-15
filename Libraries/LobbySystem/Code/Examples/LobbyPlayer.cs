using Sandbox;
using Sandbox.Citizen;

namespace LobbySystem.Examples;

/// <summary>
/// Self-contained networked third-person player: WASD with run and jump, a third-person camera created on
/// demand, Citizen animation and owner-synced facing, plus <see cref="ILobbyAgent"/> so the lobby can name,
/// spawn, place and reset it. Built on the standard s&amp;box Citizen; copy or extend it for your own game.
/// </summary>
public sealed class LobbyPlayer : Component, ILobbyAgent
{
	[Property] public float WalkSpeed { get; set; } = 130f;
	[Property] public float RunSpeed { get; set; } = 260f;
	[Property] public float JumpForce { get; set; } = 320f;
	[Property] public GameObject Head { get; set; }
	[Property] public GameObject Body { get; set; }
	[Property] public SkinnedModelRenderer Renderer { get; set; }

	[Sync( SyncFlags.FromHost )] public bool IsBot { get; set; }
	[Sync( SyncFlags.FromHost )] public string DisplayName { get; set; } = "Player";
	[Sync] public float LookYaw { get; set; }

	CharacterController _cc;
	CitizenAnimationHelper _anim;
	CameraComponent _cam;
	Vector3 _wish;
	Vector3 _lastPos;
	Vector3 _proxyVel;
	float _jumpUntil;

	bool _dressed;
	TimeUntil _nextDressTry;

	Vector3 _wanderDir = Vector3.Forward;
	float _wanderTime;

	// True on the peer that simulates this pawn: the owner, or anyone in single-player.
	bool Controlled => !Networking.IsActive || !IsProxy;

	public void InitAgent( bool isBot, string displayName ) { IsBot = isBot; DisplayName = displayName; }
	public void ResetForRound() { if ( _cc is not null ) _cc.Velocity = Vector3.Zero; }

	[Rpc.Broadcast]
	public void TeleportTo( Vector3 position )
	{
		WorldPosition = position;
		if ( _cc is not null ) _cc.Velocity = Vector3.Zero;
	}

	protected override void OnStart()
	{
		_cc = Components.Get<CharacterController>();
		Body ??= GameObject.Children.FirstOrDefault( c => c.Name == "Body" );
		Head ??= GameObject.Children.FirstOrDefault( c => c.Name == "Head" );
		Renderer ??= Body?.Components.Get<SkinnedModelRenderer>();
		if ( Renderer is not null ) _anim = Renderer.Components.GetOrCreate<CitizenAnimationHelper>();
		_lastPos = WorldPosition;
	}

	protected override void OnUpdate()
	{
		TryDressFromAvatar();

		if ( !Controlled )
		{
			_proxyVel = Time.Delta > 0f ? (WorldPosition - _lastPos) / Time.Delta : Vector3.Zero;
			_lastPos = WorldPosition;
			if ( Head is not null ) Head.WorldRotation = Rotation.FromYaw( LookYaw );
			Animate();
			return;
		}

		if ( !IsBot )
		{
			UpdateLookAndCamera();
			if ( Input.Pressed( "Jump" ) ) _jumpUntil = Time.Now + 0.2f;
		}
		Animate();
	}

	protected override void OnFixedUpdate()
	{
		if ( _cc is null || !Controlled ) return;
		_wish = IsBot ? WanderWish() : InputWish();
		Move();
	}

	Vector3 InputWish()
	{
		if ( Head is null ) return Vector3.Zero;
		var yaw = Head.WorldRotation.Angles().WithPitch( 0 ).ToRotation();
		var move = Input.AnalogMove;
		float speed = Input.Down( "Run" ) ? RunSpeed : WalkSpeed;
		return yaw * new Vector3( move.x, move.y, 0f ) * speed;
	}

	// Bots stroll around and turn back when they drift too far from the centre.
	Vector3 WanderWish()
	{
		_wanderTime -= Time.Delta;
		if ( _wanderTime <= 0f )
		{
			_wanderDir = Rotation.FromYaw( Game.Random.Float( 0f, 360f ) ).Forward;
			_wanderTime = Game.Random.Float( 2f, 4f );
		}

		var flat = WorldPosition.WithZ( 0 );
		if ( flat.Length > 700f ) _wanderDir = (-flat).Normal;
		return _wanderDir.WithZ( 0 ).Normal * WalkSpeed;
	}

	void Move()
	{
		var g = Scene.PhysicsWorld.Gravity;
		if ( _cc.IsOnGround )
		{
			_cc.Velocity = _cc.Velocity.WithZ( 0 );
			_cc.Accelerate( _wish );
			_cc.ApplyFriction( 5f );
			if ( Time.Now <= _jumpUntil ) { _cc.Punch( Vector3.Up * JumpForce ); _jumpUntil = 0f; }
		}
		else
		{
			_cc.Velocity += g * Time.Delta * 0.5f;
			_cc.Accelerate( _wish.ClampLength( 60f ) );
			_cc.ApplyFriction( 0.1f );
		}

		_cc.Move();

		if ( _cc.IsOnGround ) _cc.Velocity = _cc.Velocity.WithZ( 0 );
		else _cc.Velocity += g * Time.Delta * 0.5f;
	}

	void UpdateLookAndCamera()
	{
		if ( Head is null ) return;
		var look = Input.AnalogLook;
		var a = Head.WorldRotation.Angles();
		a.pitch = MathX.Clamp( a.pitch + look.pitch, -89f, 89f );
		a.yaw += look.yaw;
		a.roll = 0f;
		Head.WorldRotation = a.ToRotation();
		LookYaw = a.yaw;

		if ( !_cam.IsValid() )
		{
			_cam = Scene.GetAllComponents<CameraComponent>().FirstOrDefault();
			if ( _cam is null )
			{
				var go = Scene.CreateObject();
				go.Name = "Player Camera";
				_cam = go.Components.Create<CameraComponent>();
			}
		}

		var rot = Head.WorldRotation;
		_cam.WorldPosition = WorldPosition + rot.Backward * 150f + rot.Right * 28f + Vector3.Up * 50f;
		_cam.WorldRotation = rot;
	}

	void Animate()
	{
		if ( _anim is null || _cc is null ) return;
		var vel = Controlled ? _cc.Velocity : _proxyVel;
		_anim.WithVelocity( vel );
		_anim.WithWishVelocity( Controlled ? _wish : _proxyVel );
		_anim.IsGrounded = Controlled ? _cc.IsOnGround : MathF.Abs( _proxyVel.z ) < 50f;
		if ( !IsBot && Head is not null )
			_anim.WithLook( Head.WorldRotation.Forward, 1f, 0.75f, 0.5f );
		FaceMovement( vel );
	}

	void FaceMovement( Vector3 velocity )
	{
		if ( Body is null ) return;

		Rotation target;
		if ( !IsBot && Head is not null )
		{
			target = Rotation.FromYaw( Head.WorldRotation.Yaw() );
		}
		else
		{
			var flat = velocity.WithZ( 0 );
			target = flat.Length > 20f ? Rotation.LookAt( flat, Vector3.Up ) : Body.WorldRotation;
		}

		Body.WorldRotation = Rotation.Lerp( Body.WorldRotation, target, Time.Delta * 10f );
	}

	// Dress the Citizen in the player's own s&box avatar. Runs on every peer and retries until ownership has
	// replicated. Bots keep the plain model.
	void TryDressFromAvatar()
	{
		if ( _dressed || Renderer is null || _nextDressTry > 0f ) return;
		_nextDressTry = 0.25f;
		if ( IsBot ) { _dressed = true; return; }

		ClothingContainer clothing;
		try
		{
			var owner = Network.Owner;
			if ( owner is not null )
			{
				var json = owner.GetUserData( "avatar" );
				if ( string.IsNullOrEmpty( json ) ) return;
				clothing = new ClothingContainer();
				clothing.Deserialize( json );
			}
			else if ( !Networking.IsActive )
			{
				clothing = ClothingContainer.CreateFromLocalUser();
			}
			else return;
		}
		catch { _dressed = true; return; }

		_dressed = true;
		ApplyClothingAsync( clothing );
	}

	async void ApplyClothingAsync( ClothingContainer clothing )
	{
		try { await clothing.ApplyAsync( Renderer, System.Threading.CancellationToken.None ); }
		catch { }
	}
}
