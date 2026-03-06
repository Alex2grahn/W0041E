using Godot;

public partial class CameraRig : Node3D
{
    [ExportCategory("References")]
    [Export] public CharacterBody3D Target;

    [ExportCategory("Follow")]
    [Export] public float FollowSharpness = 8.0f;

    [ExportCategory("Orbit")]
    [Export] public float OrbitSpeed = 2.8f;           // känsla
    [Export] public float MinPitchDeg = -20f;
    [Export] public float MaxPitchDeg = 60f;

    [ExportCategory("Auto Align")]
    [Export] public float AutoAlignSpeed = 2.0f;       // yaw lerp speed
    [Export] public float AutoAlignDeadzone = 0.10f;   // om spelaren knappt rör sig

    [ExportCategory("Obstacle Avoidance")]
    [Export] public float AvoidPadding = 0.25f;        // avstånd från vägg
    [Export] public float SidePushStrength = 0.8f;     // sidotryck för left/right rays
    [Export] public float UpPushStrength = 0.6f;       // om du vill lägga upp-rays senare
    [Export] public float MaxCameraDistance = 5.0f;    // matcha Camera3D local Z

    // Internal state
    private float _yaw = 0f;
    private float _pitch = 0.2f; // rad
    private HintArea _activeHint = null;
    private float _defaultFov = 65f;

    public override void _Ready()
    {
        var cam = GetCamera();
        _defaultFov = cam.Fov;

        // init yaw från nuvarande rotation
        _yaw = Rotation.Y;
        _pitch = GetPivot().Rotation.X;
    }

    public override void _Process(double deltaD)
    {
        float dt = (float)deltaD;
        if (Target == null) return;

        if (_activeHint == null)
        {
            UpdateOrbitAndAlign(dt);
            UpdateFollow(dt);
            AvoidObstacles(dt);
            UpdateFovBackToDefault(dt);
        }
        else
        {
            UpdateHintOverride(dt);
        }
    }

    // --- Public API called by HintArea ---
    public void EnterHint(HintArea hint) => _activeHint = hint;

    public void ExitHint(HintArea hint)
    {
        if (_activeHint == hint) _activeHint = null;
    }

    // --- Core camera behaviours ---
    private void UpdateOrbitAndAlign(float dt)
    {
        var input = GetNode<InputManager>("/root/InputManager");
        Vector2 look = input.GetLookStick();

        bool playerOrbiting = look.LengthSquared() > 0.001f;

        if (playerOrbiting)
        {
            _yaw -= look.X * OrbitSpeed * dt * 3.0f;
            _pitch -= look.Y * OrbitSpeed * dt * 3.0f;
        }
        else
        {
            Vector3 vel = Target.Velocity;
            vel.Y = 0;

            if (vel.Length() > AutoAlignDeadzone)
            {
                float targetYaw = Mathf.Atan2(vel.X, vel.Z);
                _yaw = Mathf.LerpAngle(_yaw, targetYaw, AutoAlignSpeed * dt);
            }
        }

        float minP = Mathf.DegToRad(MinPitchDeg);
        float maxP = Mathf.DegToRad(MaxPitchDeg);
        _pitch = Mathf.Clamp(_pitch, minP, maxP);

        // Apply rotations (yaw on rig, pitch on pivot)
        Rotation = new Vector3(0, _yaw, 0);
        GetPivot().Rotation = new Vector3(_pitch, 0, 0);
    }

    private void UpdateFollow(float dt)
    {
        Vector3 desired = Target.GlobalPosition;
        float t = 1.0f - Mathf.Exp(-FollowSharpness * dt);
        GlobalPosition = GlobalPosition.Lerp(desired, t);
    }

    private void AvoidObstacles(float dt)
    {
        // Rays are children of Target (player), per assignment
        var cam = GetCamera();

        var rayCenter = Target.GetNode<RayCast3D>("RayCenter");
        var rayLeft = Target.GetNode<RayCast3D>("RayLeft");
        var rayRight = Target.GetNode<RayCast3D>("RayRight");

        Vector3 playerPos = Target.GlobalPosition;
        Vector3 camPos = cam.GlobalPosition;

        // Direction from player -> camera
        Vector3 dir = camPos - playerPos;
        float dist = dir.Length();

        if (dist < 0.001f) return;

        // Clamp to max distance (so rays don't go too far)
        float maxDist = Mathf.Min(dist, MaxCameraDistance);
        Vector3 dirNorm = dir / dist;
        Vector3 targetVec = dirNorm * maxDist;

        // CENTER ray: pull camera in if wall blocks view
        rayCenter.TargetPosition = targetVec;
        rayCenter.ForceRaycastUpdate();

        if (rayCenter.IsColliding())
        {
            Vector3 hit = rayCenter.GetCollisionPoint();
            // Move rig towards hit point, but keep some padding
            Vector3 safePos = hit + (playerPos - hit).Normalized() * AvoidPadding;
            GlobalPosition = GlobalPosition.Lerp(safePos, 1f - Mathf.Exp(-20f * dt));
            return; // if center hits, prioritize pulling in
        }

        // Whiskers: push sideways if close to wall edges
        // We shoot slight left/right offset rays by offsetting their *origin* in local space:
        // easiest: place RayLeft/Right nodes at small X offsets under Player in editor (±0.25)
        rayLeft.TargetPosition = targetVec;
        rayRight.TargetPosition = targetVec;
        rayLeft.ForceRaycastUpdate();
        rayRight.ForceRaycastUpdate();

        Vector3 sidePush = Vector3.Zero;

        if (rayLeft.IsColliding())
        {
            // push to the right (relative to camera yaw)
            sidePush += Transform.Basis.X * SidePushStrength;
        }
        if (rayRight.IsColliding())
        {
            // push to the left
            sidePush -= Transform.Basis.X * SidePushStrength;
        }

        if (sidePush != Vector3.Zero)
        {
            GlobalPosition = GlobalPosition.Lerp(GlobalPosition + sidePush, 1f - Mathf.Exp(-12f * dt));
        }
    }

    private void UpdateHintOverride(float dt)
    {
        if (_activeHint == null) return;

        var cam = GetCamera();

        // Smooth move rig to marker position
        Vector3 targetPos = _activeHint.HintCamPos.GlobalPosition;
        GlobalPosition = GlobalPosition.Lerp(targetPos, 1f - Mathf.Exp(-10f * dt));

        // Smooth look-at
        Vector3 lookAt = _activeHint.HintLookAt.GlobalPosition;

        // Build a target basis by looking at point
        Transform3D t = GlobalTransform;
        t = t.LookingAt(lookAt, Vector3.Up);
        GlobalTransform = GlobalTransform.InterpolateWith(t, 1f - Mathf.Exp(-8f * dt));

        // Smooth FOV
        cam.Fov = Mathf.Lerp(cam.Fov, _activeHint.HintFov, 1f - Mathf.Exp(-6f * dt));
    }

    private void UpdateFovBackToDefault(float dt)
    {
        var cam = GetCamera();
        cam.Fov = Mathf.Lerp(cam.Fov, _defaultFov, 1f - Mathf.Exp(-6f * dt));
    }

    // --- Helpers ---
    private Node3D GetPivot() => GetNode<Node3D>("CameraPivot");
    private Camera3D GetCamera() => GetNode<Camera3D>("CameraPivot/Camera3D");
}