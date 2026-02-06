using Godot;
using System;
using System.Threading.Tasks;

public partial class PlayerController : CharacterBody3D
{
	public enum AccelMode
	{
		Immediate = 1,
		Linear = 2,
		EaseFromStandstill = 3
	}

	[ExportCategory("References")]
	[Export] public Node3D Gfx;
	[Export] public Camera3D Camera;
	[Export] public CollisionShape3D CollisionLower;
	[Export] public CollisionShape3D CollisionUpper;

	[ExportCategory("Movement")]
	[Export] public float MaxSpeed = 7.5f;

	// Mode 2: linear accel/decel
	[Export] public float LinearAccel = 18.0f;
	[Export] public float LinearDecel = 32.0f; // "fast constant decel" (fig 7.16 feel)

	// Mode 3: ease accel (from standstill), constant decel
	[Export] public float EaseK = 10.0f;       // higher = snappier ease
	[Export] public float EaseStandstillThreshold = 0.2f;
	[Export] public float EaseDecel = 26.0f;

	[ExportCategory("Jump")]
	[Export] public float JumpVelocity = 6.5f;
	[Export] public float JumpAnticipationDelay = 0.06f; // "slight waiting time" (tune for feel)
	[Export] public float SquashDuration = 0.06f;
	[Export] public float StretchDuration = 0.08f;

	// Hover / hang-time
	[Export] public float Gravity = 18.0f;         // you can also read ProjectSettings gravity
	[Export] public float FallGravityMult = 1.6f;  // faster fall = snappier feel
	[Export] public float HoverTime = 0.12f;       // adjustable hang-time window
	[Export] public float HoverGravityMult = 0.25f; // reduced gravity while holding jump in hover window

	[ExportCategory("Coyote Time")]
	[Export] public float CoyoteDuration = 0.12f;

	[ExportCategory("Duck")]
	[Export] public float DuckSpeedMult = 0.55f;
	[Export] public float DuckSphereDrop = 0.65f; // how much the upper sphere moves downward
	[Export] public float DuckTweenTime = 0.08f;

	[ExportCategory("GFX Aim")]
    [Export] public float AimTurnSpeed = 14.0f;
    [Export] public float AimMinSpeed = 0.1f;

    

	public AccelMode Mode = AccelMode.Immediate;

	private float _coyoteTimer = 0f;
	private float _hoverTimer = 0f;

	private bool _jumpRequested = false;
	private bool _isJumpingRoutine = false;

	private bool _isDucking = false;
	private Vector3 _upperSphereStartPos;
	private Vector3 _gfxStartScale;

	public override void _Ready()
	{
		if (Gfx == null) Gfx = GetNode<Node3D>("GFX");
		if (Camera == null) Camera = GetNode<Camera3D>("CameraRig/SpringArm3D/Camera3D");
		if (CollisionLower == null) CollisionLower = GetNode<CollisionShape3D>("CollisionLower");
		if (CollisionUpper == null) CollisionUpper = GetNode<CollisionShape3D>("CollisionUpper");

		_upperSphereStartPos = CollisionUpper.Position;
		_gfxStartScale = Gfx.Scale;
	}

	public override void _Input(InputEvent e)
	{
		// Optional: mouse camera orbit can be implemented here if you want.
	}



	public override void _PhysicsProcess(double deltaD)
	{


		float delta = (float)deltaD;

		HandleModeSwap();
		UpdateCoyote(delta);

		if (Input.IsActionJustPressed("jump"))
			_jumpRequested = true;

		TryConsumeJumpRequest();
		

		HandleDuck(delta);

		// Gravity & vertical handling
		ApplyVerticalPhysics(delta);

		// Horizontal move (camera-relative)
		Vector2 input = ReadMoveInput();
		Vector3 desiredDir = CameraRelativeDirection(input);
		float speedMult = _isDucking ? DuckSpeedMult : 1.0f;
		Vector3 desiredVel = desiredDir * (MaxSpeed * speedMult);

		ApplyHorizontalVelocity(desiredVel, delta);

		// Move & slide uses Velocity on CharacterBody3D
		MoveAndSlide();

		// Aim mesh towards current horizontal velocity (GFX only)
		AimGfxToVelocity(delta);

	}
private void AimGfxToVelocity(float delta)
{
    if (Gfx == null)
        return;

    // Endast horisontell velocity
    Vector3 horiz = new Vector3(Velocity.X, 0f, Velocity.Z);
    float speed = horiz.Length();

    if (speed < AimMinSpeed)
        return;

    Vector3 dir = horiz / speed;

    Transform3D current = Gfx.GlobalTransform;
    Transform3D target =
        current.LookingAt(current.Origin + dir, Vector3.Up);

    float t = 1.0f - Mathf.Exp(-AimTurnSpeed * delta);

    Quaternion a = current.Basis.GetRotationQuaternion();
    Quaternion b = target.Basis.GetRotationQuaternion();
    Quaternion q = a.Slerp(b, t);

    Gfx.GlobalTransform =
        new Transform3D(new Basis(q), current.Origin);
}


	private void HandleModeSwap()
	{
		if (Input.IsActionJustPressed("mode_1")) Mode = AccelMode.Immediate;
		if (Input.IsActionJustPressed("mode_2")) Mode = AccelMode.Linear;
		if (Input.IsActionJustPressed("mode_3")) Mode = AccelMode.EaseFromStandstill;
	}

	private Vector2 ReadMoveInput()
	{
		float x = Input.GetActionStrength("move_right") - Input.GetActionStrength("move_left");
		float y = Input.GetActionStrength("move_forward") - Input.GetActionStrength("move_back");
		Vector2 v = new Vector2(x, y);
		return  v;
	}

	private Vector3 CameraRelativeDirection(Vector2 input)
	{
		if (input == Vector2.Zero) return Vector3.Zero;

		// Forward = -Z in Godot. We flatten on Y to keep horizontal motion.
		Vector3 camForward = -Camera.GlobalTransform.Basis.Z;
		camForward.Y = 0;
		camForward = camForward.Normalized();

		Vector3 camRight = Camera.GlobalTransform.Basis.X;
		camRight.Y = 0;
		camRight = camRight.Normalized();

		Vector3 dir = (camRight * input.X + camForward * input.Y);
		return dir.Length() > 0 ? dir.Normalized() : Vector3.Zero;
	}

	private void ApplyHorizontalVelocity(Vector3 desiredVel, float delta)
	{
		Vector3 v = Velocity;
		Vector3 horiz = new Vector3(v.X, 0, v.Z);

		switch (Mode)
		{
			case AccelMode.Immediate:
				// Immediate accel + immediate stop
				horiz = desiredVel;
				break;

			case AccelMode.Linear:
				{
					float rate = (desiredVel.Length() > 0.001f) ? LinearAccel : LinearDecel;
					horiz = horiz.MoveToward(desiredVel, rate * delta);
				}
				break;

			case AccelMode.EaseFromStandstill:
				{
					if (desiredVel.Length() > 0.001f)
					{
						// Ease acceleration only when basically standstill
						if (horiz.Length() < EaseStandstillThreshold)
						{
							float t = 1.0f - Mathf.Exp(-EaseK * delta); // exponential smoothing
							horiz = horiz.Lerp(desiredVel, t);
						}
						else
						{
							// Once moving: you may choose your own modulation (allowed by spec)
							horiz = horiz.MoveToward(desiredVel, LinearAccel * delta);
						}
					}
					else
					{
						// Constant decel
						horiz = horiz.MoveToward(Vector3.Zero, EaseDecel * delta);
					}
				}
				break;
		}

		Velocity = new Vector3(horiz.X, Velocity.Y, horiz.Z);
	}

	private void UpdateCoyote(float delta)
	{
		if (IsOnFloor())
			_coyoteTimer = CoyoteDuration;
		else
			_coyoteTimer = Mathf.Max(0, _coyoteTimer - delta);
	}

	private void ApplyVerticalPhysics(float delta)
	{
		float y = Velocity.Y;

		bool grounded = IsOnFloor();
		if (grounded && y < 0)
			y = 0;

		// Hover window starts when we jump (set in JumpRoutine)
		bool hoverActive = _hoverTimer > 0 && Input.IsActionPressed("jump") && y > 0;

		float gravityThisFrame = Gravity;
		if (!grounded)
		{
			if (y < 0)
				gravityThisFrame *= FallGravityMult;
			else if (hoverActive)
				gravityThisFrame *= HoverGravityMult;
		}

		if (!grounded)
			y -= gravityThisFrame * delta;

		if (_hoverTimer > 0)
			_hoverTimer = Mathf.Max(0, _hoverTimer - delta);

		Velocity = new Vector3(Velocity.X, y, Velocity.Z);
	}

	private void TryConsumeJumpRequest()
	{
		if (!_jumpRequested) return;

		bool canJump = IsOnFloor() || _coyoteTimer > 0;

		if (canJump && !_isJumpingRoutine)
		{
			_jumpRequested = false;
			_ = JumpRoutine(); // fire and forget
		}
		else if (!canJump)
		{
			// If you want jump buffering, you could keep request for a short time.
			_jumpRequested = false;
		}
	}

	private async Task JumpRoutine()
	{
		_isJumpingRoutine = true;

		// Anticipation delay (input-response feel)
		await ToSignal(GetTree().CreateTimer(JumpAnticipationDelay), SceneTreeTimer.SignalName.Timeout);

		// Squash down in Y
		var tween1 = CreateTween();
		tween1.TweenProperty(Gfx, "scale",
			new Vector3(_gfxStartScale.X, _gfxStartScale.Y * 0.7f, _gfxStartScale.Z),
			SquashDuration).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);

		await ToSignal(tween1, Tween.SignalName.Finished);

		// Jump impulse
		Velocity = new Vector3(Velocity.X, JumpVelocity, Velocity.Z);

		// Start hover window
		_hoverTimer = HoverTime;

		// Stretch up in Y (and a bit thinner in XZ for feel)
		var tween2 = CreateTween();
		tween2.TweenProperty(Gfx, "scale",
			new Vector3(_gfxStartScale.X * 0.92f, _gfxStartScale.Y * 1.15f, _gfxStartScale.Z * 0.92f),
			StretchDuration).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);

		await ToSignal(tween2, Tween.SignalName.Finished);

		// Return to normal scale
		var tween3 = CreateTween();
		tween3.TweenProperty(Gfx, "scale", _gfxStartScale, 0.10f)
			  .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);

		_isJumpingRoutine = false;
	}

	private void HandleDuck(float delta)
	{
		bool duckHeld = Input.IsActionPressed("duck");

		if (duckHeld && !_isDucking)
			StartDuck();
		else if (!duckHeld && _isDucking)
			EndDuck();
	}

	private void StartDuck()
	{
		_isDucking = true;

		// Move upper sphere down into lower sphere
		CollisionUpper.Position = _upperSphereStartPos + new Vector3(0, -DuckSphereDrop, 0);

		// Scale GFX in negative Y (as required)
		var tween = CreateTween();
		tween.TweenProperty(Gfx, "scale",
			new Vector3(_gfxStartScale.X, -Mathf.Abs(_gfxStartScale.Y) * 0.6f, _gfxStartScale.Z),
			DuckTweenTime).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
	}

	private void EndDuck()
	{
		_isDucking = false;

		CollisionUpper.Position = _upperSphereStartPos;

		var tween = CreateTween();
		tween.TweenProperty(Gfx, "scale", _gfxStartScale, DuckTweenTime)
			 .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
	}
}
