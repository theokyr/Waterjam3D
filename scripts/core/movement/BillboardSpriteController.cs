using Godot;
using System;

public partial class BillboardSpriteController : Node3D
{
	[Export] public string IdleTexturePath { get; set; } = "res://resources/sprites/jamoulis/Idle/master_animation-new_idle_slim.png";
	[Export] public string RunTexturePath { get; set; } = "res://resources/sprites/jamoulis/Run/master_animation-new_run_slim.png";
	[Export] public string AirDescendTexturePath { get; set; } = "res://resources/sprites/jamoulis/Jump/master_animation-decend.png";
	[Export] public string JumpStartTexturePath { get; set; } = "res://resources/sprites/jamoulis/Jump/master_animation-new_jump_slim_wip.png";
	[Export] public string LandingTexturePath { get; set; } = "res://resources/sprites/jamoulis/Roll/master_animation-roll_recover.png";

	[Export] public int IdleFrames { get; set; } = 8;
	[Export] public int RunFrames { get; set; } = 8;
	[Export] public int IdleFPS { get; set; } = 6;
	[Export] public int RunFPS { get; set; } = 10;
	[Export] public int AirDescendFrames { get; set; } = 4; // user: descend=4
	[Export] public int AirDescendFPS { get; set; } = 10;
	[Export] public int JumpStartFrames { get; set; } = 8; // user: jump=8
	[Export] public int JumpStartFPS { get; set; } = 10;
	[Export] public float JumpStartDuration { get; set; } = 0.18f;
	[Export] public int LandingFrames { get; set; } = 8;
	[Export] public int LandingFPS { get; set; } = 10;
	[Export] public float LandingDuration { get; set; } = 0.18f;
	[Export] public bool AutoDetectFrames { get; set; } = false; // use explicit counts by default
	[Export] public float TargetHeightMeters { get; set; } = 1.6f;
	[Export] public bool MaintainBottomOnGround { get; set; } = true;
	[Export] public bool UseColliderGroundAlignment { get; set; } = false;
	[Export] public float GroundLocalY { get; set; } = 0.0f; // set to 0.2 to lift bottom

	private Sprite3D _sprite;
	private CompressedTexture2D _idleTex;
	private CompressedTexture2D _runTex;
	private CompressedTexture2D _airDescendTex;
	private CompressedTexture2D _jumpStartTex;
	private CompressedTexture2D _landingTex;
	private CharacterBody3D _character;
	private float _frameTimer;
	private int _currentFrame;
	private string _state = "idle";
	private float _stateTimer;
	private bool _wasOnFloor;
	private int _currentTotalFrames;

	// Animation intents from controller (read via metadata)
	private const string MetaAnimDash = "anim_dash";
	private const string MetaAnimSlide = "anim_slide";
	private const string MetaAnimWallJumpTime = "anim_wall_jump_time";
	[Export] public float WallJumpHoldTime { get; set; } = 0.25f;

	public override void _Ready()
	{
		_sprite = GetNodeOrNull<Sprite3D>("Sprite3D");
		if (_sprite == null)
		{
			_sprite = new Sprite3D();
			_sprite.Billboard = BaseMaterial3D.BillboardModeEnum.Enabled;
			_sprite.Modulate = Colors.White;
			_sprite.Shaded = false;
			_sprite.TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest;
			AddChild(_sprite);
		}

		_character = GetParent() as CharacterBody3D ?? GetOwner() as CharacterBody3D;

		_idleTex = GD.Load<CompressedTexture2D>(IdleTexturePath);
		_runTex = GD.Load<CompressedTexture2D>(RunTexturePath);
		_airDescendTex = GD.Load<CompressedTexture2D>(AirDescendTexturePath);
		_jumpStartTex = GD.Load<CompressedTexture2D>(JumpStartTexturePath);
		_landingTex = GD.Load<CompressedTexture2D>(LandingTexturePath);

		ApplyState("idle");
	}

	public override void _Process(double delta)
	{
		UpdateStateFromVelocity((float)delta);
		Animate((float)delta);
		FaceCamera();
	}

	private void UpdateStateFromVelocity(float dt)
	{
		if (_character == null)
			return;
		var vel = _character.Velocity;
		var horizontal = new Vector2(vel.X, vel.Z).Length();
		var moving = horizontal > 0.1f;
		var onFloor = _character.IsOnFloor();
		// Edge triggers for jump/land one-shots
		if (_wasOnFloor && !onFloor)
		{
			// Jumped
			ApplyState("jump_start");
			_stateTimer = 0f;
		}
		else if (!_wasOnFloor && onFloor)
		{
			// Landed
			ApplyState("land");
			_stateTimer = 0f;
		}
		_wasOnFloor = onFloor;

		// Advance timer for one-shots
		_stateTimer += dt;

		// While in one-shot, hold until duration, then transition
		if (_state == "jump_start")
		{
			if (_stateTimer >= JumpStartDuration)
			{
				ApplyState("air_descend");
			}
			return;
		}
		if (_state == "land")
		{
			if (_stateTimer >= LandingDuration)
			{
				var baseState = moving ? "run" : "idle";
				ApplyState(baseState);
			}
			return;
		}

		// Priority: dash > slide > wall jump flash > airborne/run/idle
		if (GetMetaFlag(MetaAnimDash))
		{
			ApplyState("dash");
			return;
		}
		if (GetMetaFlag(MetaAnimSlide))
		{
			ApplyState("slide");
			return;
		}
		if (TryShowWallJumpFlash())
		{
			return; // transient state handled
		}

		var nextState = IsAirborne() ? "air_descend" : (moving ? "run" : "idle");
		if (nextState != _state)
		{
			ApplyState(nextState);
		}
	}

	private bool IsAirborne()
	{
		if (_character == null) return false;
		return !_character.IsOnFloor();
	}

	private void ApplyState(string newState)
	{
		_state = newState;
		_currentFrame = 0;
		_frameTimer = 0;
		if (_state == "jump_start")
		{
			_sprite.Texture = _jumpStartTex ?? _idleTex;
			ApplyFramesFor(_sprite.Texture, JumpStartFrames, 1);
			_sprite.Frame = 0;
		}
		else if (_state == "land")
		{
			_sprite.Texture = _landingTex ?? _idleTex;
			ApplyFramesFor(_sprite.Texture, LandingFrames, 1);
			_sprite.Frame = 0;
		}
		else if (_state == "air_descend")
		{
			_sprite.Texture = _airDescendTex ?? _idleTex;
			ApplyFramesFor(_sprite.Texture, AirDescendFrames, 1);
			_sprite.Frame = 0;
		}
		else if (_state == "dash")
		{
			_sprite.Texture = _runTex; // placeholder until dedicated dash sheet is provided
			ApplyFramesFor(_sprite.Texture, RunFrames, 1);
			_sprite.Frame = 0;
		}
		else if (_state == "slide")
		{
			_sprite.Texture = _runTex; // placeholder until slide sheet is provided
			ApplyFramesFor(_sprite.Texture, RunFrames, 1);
			_sprite.Frame = 0;
		}
		else if (_state == "run")
		{
			_sprite.Texture = _runTex;
			ApplyFramesFor(_sprite.Texture, RunFrames, 1);
			_sprite.Frame = 0;
		}
		else
		{
			_sprite.Texture = _idleTex;
			ApplyFramesFor(_sprite.Texture, IdleFrames, 1);
			_sprite.Frame = 0;
		}
		_sprite.FlipH = false;
		ApplySizing();
		_sprite.Billboard = BaseMaterial3D.BillboardModeEnum.Enabled;
	}

	private void ApplySizing()
	{
		if (_sprite.Texture == null)
			return;
		// One frame height in pixels (we use Vframes=1 here)
		var frameHeightPx = (float)_sprite.Texture.GetHeight() / MathF.Max(1, _sprite.Vframes);
		if (frameHeightPx <= 0)
			return;
		// PixelSize scales sprite crisply without blur (we set TextureFilter=Nearest)
		var desiredPixelSize = TargetHeightMeters / frameHeightPx;
		_sprite.PixelSize = desiredPixelSize;
		// Keep base aligned to collider bottom when requested
		float yOffset;
		if (MaintainBottomOnGround && UseColliderGroundAlignment && TryComputeColliderGroundLocalY(out var groundLocalY))
		{
			// Place sprite so that its bottom rests on collider ground plane, with user offset
			yOffset = groundLocalY + GroundLocalY + TargetHeightMeters * 0.5f;
		}
		else
		{
			// Bottom at configurable local Y for predictable placement
			yOffset = MaintainBottomOnGround ? GroundLocalY + TargetHeightMeters * 0.5f : 0.0f;
		}
		_sprite.Transform = new Transform3D(Basis.Identity, new Vector3(0, yOffset, 0));
		_sprite.Visible = true;
	}

	private bool TryComputeColliderGroundLocalY(out float groundLocalY)
	{
		groundLocalY = 0f;
		if (_character == null)
			return false;
		CollisionShape3D col = _character.GetNodeOrNull<CollisionShape3D>("CollisionShape3D");
		if (col == null)
		{
			foreach (Node child in _character.GetChildren())
			{
				if (child is CollisionShape3D c)
				{
					col = c;
					break;
				}
			}
		}
		if (col == null || col.Shape == null)
			return false;
		float halfHeight = 0f;
		switch (col.Shape)
		{
			case CapsuleShape3D cap:
				halfHeight = (cap.Height + cap.Radius * 2f) * 0.5f;
				break;
			case BoxShape3D box:
				halfHeight = box.Size.Y * 0.5f;
				break;
			case CylinderShape3D cyl:
				halfHeight = cyl.Height * 0.5f;
				break;
			default:
				halfHeight = TargetHeightMeters * 0.5f;
				break;
		}
		// Collider bottom local Y = collider local center Y - halfHeight
		groundLocalY = col.Transform.Origin.Y - halfHeight;
		return true;
	}

	private void Animate(float dt)
	{
		int fps = _state switch
		{
			"jump_start" => JumpStartFPS,
			"land" => LandingFPS,
			"air_descend" => AirDescendFPS,
			"run" => RunFPS,
			_ => IdleFPS
		};
		int frames = _currentTotalFrames > 0 ? _currentTotalFrames : 1;
		if (fps <= 0 || frames <= 1 || _sprite.Texture == null)
			return;
		_frameTimer += dt;
		float frameTime = 1.0f / fps;
		while (_frameTimer >= frameTime)
		{
			_frameTimer -= frameTime;
			_currentFrame = (_currentFrame + 1) % frames;
			_sprite.Frame = _currentFrame;
		}
	}

	private void ApplyFramesFor(Texture2D tex, int hframesFallback, int vframesFallback)
	{
		int h = hframesFallback;
		int v = vframesFallback;
		if (AutoDetectFrames && tex != null)
		{
			var w = tex.GetWidth();
			var hgt = tex.GetHeight();
			int cell = Gcd(w, hgt);
			if (cell > 0)
			{
				int autoH = Math.Max(1, w / cell);
				int autoV = Math.Max(1, hgt / cell);
				// Prefer single row if clearly much wider
				if (autoH >= autoV)
				{
					h = autoH;
					v = autoV;
				}
			}
		}
		_sprite.Hframes = Math.Max(1, h);
		_sprite.Vframes = Math.Max(1, v);
		_currentTotalFrames = _sprite.Hframes * _sprite.Vframes;
	}

	private static int Gcd(int a, int b)
	{
		while (b != 0)
		{
			int t = b;
			b = a % b;
			a = t;
		}
		return a;
	}

	private bool GetMetaFlag(string key)
	{
		var owner = GetOwner() as Node ?? GetParent();
		if (owner == null) return false;
		if (!owner.HasMeta(key)) return false;
		return owner.GetMeta(key).AsBool();
	}

	private bool TryShowWallJumpFlash()
	{
		var owner = GetOwner() as Node ?? GetParent();
		if (owner == null) return false;
		if (!owner.HasMeta(MetaAnimWallJumpTime)) return false;
		var t = owner.GetMeta(MetaAnimWallJumpTime).AsSingle();
		var now = (float)Time.GetTicksMsec() * 0.001f;
		if (now - t <= WallJumpHoldTime)
		{
			ApplyState("jump_start"); // reuse jump flash
			return true;
		}
		return false;
	}

	private void FaceCamera()
	{
		var viewport = GetViewport();
		var cam = viewport?.GetCamera3D();
		if (cam == null || _sprite == null)
			return;
		_sprite.LookAt(cam.GlobalTransform.Origin, Vector3.Up);
	}
}


