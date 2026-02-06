using Godot;
using System;

public partial class PlayerController : CharacterBody3D
{
    [ExportCategory("Movement")]
    [Export] public float MaxSpeed = 6.5f;
    [Export] public float Accel = 18f;
    [Export] public float AirAccel = 6f;
    [Export] public float JumpVelocity = 6.5f;
    [Export] public float Gravity = 18f;

    // Läser kameran denna för auto-align
    public Vector3 LastMoveDirWorld { get; private set; } = Vector3.Forward;

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;

        ApplyGravity(dt);
        HandleMove(dt);
        HandleJump();

        MoveAndSlide();
    }

    private void ApplyGravity(float dt)
    {
        if (!IsOnFloor())
            Velocity += Vector3.Down * Gravity * dt;
    }

    private void HandleMove(float dt)
    {
        Vector2 input = GetMoveInput();
        Vector3 desired = new Vector3(input.X, 0f, input.Y);

        // Convert to world space using player's yaw
        desired = GlobalTransform.Basis * desired;
        desired.Y = 0f;
        desired = desired.Normalized();

        if (desired.Length() > 0.001f)
            LastMoveDirWorld = desired;

        float a = IsOnFloor() ? Accel : AirAccel;

        Vector3 horizVel = Velocity;
        horizVel.Y = 0f;

        Vector3 targetVel = desired * MaxSpeed;
        horizVel = horizVel.MoveToward(targetVel, a * dt);

        Velocity = new Vector3(horizVel.X, Velocity.Y, horizVel.Z);

        // (valfritt) rotera spelaren mot rörelseriktning
        RotateTowards(desired, dt);
    }

    private void RotateTowards(Vector3 desired, float dt)
    {
        if (desired.Length() < 0.001f) return;

        float targetYaw = Mathf.Atan2(desired.X, desired.Z);
        Vector3 rot = Rotation;
        rot.Y = Mathf.LerpAngle(rot.Y, targetYaw, 10f * dt);
        Rotation = rot;
    }

    private void HandleJump()
    {
        if (IsOnFloor() && Input.IsActionJustPressed("jump"))
            Velocity = new Vector3(Velocity.X, JumpVelocity, Velocity.Z);
    }

    private Vector2 GetMoveInput()
    {
        // Godot: forward/back ligger ofta på Y
        // Vi vill: input.Y = forward(+), back(-) i vår logik? Vi löser med mapping:
        // move_forward bör ge -Y i Input.get_vector, så vi inverterar vid behov.
        Vector2 v = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
        // Godot ger: upp = -Y. Vi vill att forward blir +Z i vår world-konvention.
        return new Vector2(v.X, -v.Y);
    }
}
