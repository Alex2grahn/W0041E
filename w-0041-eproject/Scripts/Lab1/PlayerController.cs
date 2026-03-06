using Godot;

public partial class PlayerController : CharacterBody3D
{
    public enum AccelMode { Immediate = 1, Linear = 2, EaseFromStandstill = 3 }

    [ExportCategory("Refs")]
    [Export] public Node3D Gfx;
    [Export] public Camera3D Camera;
    [Export] public CollisionShape3D CollisionLower;
    [Export] public CollisionShape3D CollisionUpper;

    [ExportCategory("Move")]
    [Export] public float MaxSpeed = 7.5f;

    [ExportCategory("Mode 2: Linear")]
    [Export] public float LinearAccel = 18.0f;
    [Export] public float LinearDecel = 30.0f;

    [ExportCategory("Mode 3: Ease From Standstill")]
    [Export] public float EaseTimeToMax = 0.22f;
    private float _easeT = 0f;

    [ExportCategory("Jump/Gravity")]
    [Export] public float Gravity = 22.0f;
    [Export] public float JumpSpeed = 8.5f;
    [Export] public float JumpAnticipation = 0.08f;

    [ExportCategory("Hang Time")]
    [Export] public float HangGravityScale = 0.35f;
    [Export] public float HangApexThreshold = 1.2f;

    [ExportCategory("Coyote Time")]
    [Export] public float CoyoteTime = 0.12f;
    private float _coyoteTimer = 0f;

    [ExportCategory("Jump Buffer")]
    [Export] public float JumpBufferTime = 0.12f;
    private bool _jumpQueued = false;
    private float _jumpQueueTimer = 0f;

    [ExportCategory("Duck")]
    [Export] public float DuckSpeedMultiplier = 0.55f;
    [Export] public float DuckGfxScaleY = 0.55f;
    [Export] public float UpperSphereDuckOffsetY = -0.6f;

    [ExportCategory("Gfx Aim")]
    [Export] public float AimTurnSpeed = 10.0f;

    [ExportCategory("Mode State")]
    [Export] public AccelMode CurrentMode = AccelMode.Immediate;

    private Vector3 _gfxStandScale;
    private Vector3 _upperSphereStandPos;

    public override void _Ready()
    {
        if (Gfx != null) _gfxStandScale = Gfx.Scale;
        if (CollisionUpper != null) _upperSphereStandPos = CollisionUpper.Position;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsActionPressed("mode_1")) CurrentMode = AccelMode.Immediate;
        if (e.IsActionPressed("mode_2")) CurrentMode = AccelMode.Linear;
        if (e.IsActionPressed("mode_3")) CurrentMode = AccelMode.EaseFromStandstill;
    }

    public override void _PhysicsProcess(double deltaD)
    {
        float dt = (float)deltaD;
        var input = GetNode<InputManager>("/root/InputManager");

        UpdateCoyote(dt);

        bool duckHeld = input.DuckHeld();
        ApplyDuck(dt, duckHeld);

        Vector2 moveStick = input.GetMoveStick();
        Vector3 wishDir = GetCameraRelativeMove(moveStick);

        float speedMul = duckHeld ? DuckSpeedMultiplier : 1.0f;
        Vector3 desiredVel = wishDir * (MaxSpeed * speedMul);

        ApplyHorizontalVelocity(dt, desiredVel);
        ApplyGravity(dt);
        HandleJump(dt, input.JumpPressed());

        MoveAndSlide();

        AimGfxTowardsVelocity(dt);
    }

    private void UpdateCoyote(float dt)
    {
        if (IsOnFloor()) _coyoteTimer = CoyoteTime;
        else _coyoteTimer = Mathf.Max(0, _coyoteTimer - dt);
    }

    private void ApplyDuck(float dt, bool duckHeld)
    {
        if (Gfx == null || CollisionUpper == null) return;

        Vector3 targetScale = _gfxStandScale;
        if (duckHeld) targetScale.Y = DuckGfxScaleY;

        float s = 1.0f - Mathf.Exp(-18f * dt);
        Gfx.Scale = Gfx.Scale.Lerp(targetScale, s);

        Vector3 upperTarget = _upperSphereStandPos;
        if (duckHeld) upperTarget.Y += UpperSphereDuckOffsetY;

        CollisionUpper.Position = CollisionUpper.Position.Lerp(upperTarget, s);
    }

    private Vector3 GetCameraRelativeMove(Vector2 stick)
    {
        if (stick.LengthSquared() < 0.0001f) return Vector3.Zero;

        if (Camera == null)
        {
            Vector3 d = new Vector3(stick.X, 0, stick.Y);
            return d.Normalized();
        }

        Vector3 camF = -Camera.GlobalTransform.Basis.Z;
        Vector3 camR = Camera.GlobalTransform.Basis.X;

        camF.Y = 0; camR.Y = 0;
        camF = camF.Normalized();
        camR = camR.Normalized();

        return (camR * stick.X + camF * stick.Y).Normalized();
    }

    private void ApplyHorizontalVelocity(float dt, Vector3 desired)
    {
        Vector3 v = Velocity;
        Vector3 currentH = new Vector3(v.X, 0, v.Z);
        Vector3 desiredH = new Vector3(desired.X, 0, desired.Z);

        Vector3 newH = CurrentMode switch
        {
            AccelMode.Immediate => ModeImmediate(desiredH),
            AccelMode.Linear => ModeLinear(dt, currentH, desiredH),
            AccelMode.EaseFromStandstill => ModeEaseFromStandstill(dt, currentH, desiredH),
            _ => currentH
        };

        Velocity = new Vector3(newH.X, v.Y, newH.Z);
    }

    private Vector3 ModeImmediate(Vector3 desiredH)
    {
        _easeT = desiredH.LengthSquared() > 0 ? 1f : 0f;
        return desiredH;
    }

    private Vector3 ModeLinear(float dt, Vector3 currentH, Vector3 desiredH)
    {
        float rate = desiredH.LengthSquared() > 0.0001f ? LinearAccel : LinearDecel;
        _easeT = desiredH.LengthSquared() > 0 ? 1f : 0f;
        return currentH.MoveToward(desiredH, rate * dt);
    }

    private Vector3 ModeEaseFromStandstill(float dt, Vector3 currentH, Vector3 desiredH)
    {
        bool wantsMove = desiredH.LengthSquared() > 0.0001f;
        bool isStandstill = currentH.Length() < 0.2f;

        if (isStandstill && wantsMove)
        {
            _easeT = Mathf.Min(1f, _easeT + dt / Mathf.Max(0.001f, EaseTimeToMax));
            float eased = _easeT * _easeT * (3f - 2f * _easeT); // smoothstep
            return desiredH * eased;
        }

        _easeT = wantsMove ? 1f : 0f;
        float rate = wantsMove ? LinearAccel : LinearDecel;
        return currentH.MoveToward(desiredH, rate * dt);
    }

    private void ApplyGravity(float dt)
    {
        if (IsOnFloor())
        {
            if (Velocity.Y < 0) Velocity = new Vector3(Velocity.X, -1f, Velocity.Z);
            return;
        }

        float g = Gravity;

        if (Mathf.Abs(Velocity.Y) < HangApexThreshold)
            g *= HangGravityScale;

        Velocity += Vector3.Down * g * dt;
    }

    private void HandleJump(float dt, bool jumpHeld)
    {
        if (jumpHeld && !_jumpQueued)
        {
            _jumpQueued = true;
            _jumpQueueTimer = JumpBufferTime;
        }

        if (_jumpQueued)
        {
            _jumpQueueTimer -= dt;
            if (_jumpQueueTimer <= 0) _jumpQueued = false;
        }

        bool canJump = IsOnFloor() || _coyoteTimer > 0f;

        if (_jumpQueued && canJump)
        {
            _jumpQueued = false;
            _coyoteTimer = 0f;
            StartAnticipatedJump();
        }
    }

    private async void StartAnticipatedJump()
    {
        if (Gfx == null) return;

        var tween = CreateTween();
        tween.TweenProperty(Gfx, "scale",
            new Vector3(_gfxStandScale.X, _gfxStandScale.Y * 0.85f, _gfxStandScale.Z),
            0.06f);

        await ToSignal(GetTree().CreateTimer(JumpAnticipation), SceneTreeTimer.SignalName.Timeout);

        Velocity = new Vector3(Velocity.X, JumpSpeed, Velocity.Z);

        var tween2 = CreateTween();
        tween2.TweenProperty(Gfx, "scale",
            new Vector3(_gfxStandScale.X, _gfxStandScale.Y * 1.12f, _gfxStandScale.Z),
            0.08f);
        tween2.TweenProperty(Gfx, "scale", _gfxStandScale, 0.10f);
    }

    private void AimGfxTowardsVelocity(float dt)
    {
        if (Gfx == null) return;

        Vector3 v = Velocity;
        v.Y = 0;
        if (v.LengthSquared() < 0.05f) return;

        Vector3 forward = -Gfx.GlobalTransform.Basis.Z;
        Vector3 target = v.Normalized();

        float t = 1.0f - Mathf.Exp(-AimTurnSpeed * dt);
        Vector3 newForward = forward.Slerp(target, t);

        Gfx.LookAt(Gfx.GlobalPosition + newForward, Vector3.Up);
    }
}