using Godot;
using System;

;public partial class ThirdPersonCameraRig : Node3D
{
    [ExportCategory("References")]
    [Export] public PlayerController Player;
    [Export] public Node3D PlayerHead;   // "Head" node i player
    [Export] public Camera3D Cam;

    [ExportCategory("Follow (Smooth)")]
    [Export] public float FollowStiffness = 12f; // proportional feedback
    [Export] public float FollowDamping = 2f;    // lite stabilitet
    [Export] public Vector3 FollowOffset = new Vector3(0f, 0f, 0f); // extra om du vill

    [ExportCategory("Orbit")]
    [Export] public float OrbitDistance = 4.5f;
    [Export] public float OrbitHeight = 1.6f;
    [Export] public float OrbitSpeedX = 2.4f;
    [Export] public float OrbitSpeedY = 1.8f;
    [Export] public float PitchMinDeg = -25f;
    [Export] public float PitchMaxDeg = 60f;

    [ExportCategory("Auto Align")]
    [Export] public float AutoAlignDelay = 0.35f;  // tid utan look-input innan align börjar
    [Export] public float AutoAlignSpeed = 1.8f;   // hur snabbt yaw alignar

    [ExportCategory("Obstacle Avoidance (Whiskers)")]
    [Export] public float CameraRadius = 0.25f;     // margin från vägg
    [Export] public float WhiskerSide = 0.35f;      // sid-ray offset från player
    [Export] public float WhiskerUp = 0.35f;        // upp-ray offset från player
    [Export] public float GroundPushUp = 0.55f;     // hur mycket vi pushar upp nära mark
    [Export] public uint LargeObstacleMask = 1u << 0; // Layer 1 ONLY (WorldLarge)

    [ExportCategory("Hint Override")]
    [Export] public float HintBlendSpeed = 3.0f;

    // Orbit state
    private float _yaw;
    private float _pitch;

    // Follow smoothing state (proportional feedback-ish)
    private Vector3 _followVel;

    // Look input tracking for auto-align
    private float _timeSinceLookInput = 999f;

    // Hint override state
    private bool _hintActive;
    private Transform3D _hintTargetTransform;
    private float _hintTargetFov = 70f;

    public override void _Ready()
    {
        if (Cam == null) Cam = GetNodeOrNull<Camera3D>("Camera3D");
        // init yaw/pitch from current
        _yaw = Rotation.Y;
        _pitch = 0f;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        if (Player == null || PlayerHead == null || Cam == null) return;

        Vector3 anchor = PlayerHead.GlobalPosition + FollowOffset;

        ReadOrbitInput(dt);
        ApplyAutoAlign(dt);

        // 1) Smooth follow camera rig position (NOT fixed to player)
        SmoothFollow(anchor, dt);

        // 2) Compute desired camera target position from orbit state
        Vector3 desiredCamPos = ComputeDesiredCameraPosition(anchor);

        // 3) Whisker obstacle avoidance (rays from player to camera)
        Vector3 adjustedCamPos = ApplyWhiskerAvoidance(anchor, desiredCamPos);

        // 4) Apply hint override blending if inside hint area
        ApplyHintOverride(ref adjustedCamPos, dt);

        // 5) Set camera position + look at anchor
        Cam.GlobalPosition = adjustedCamPos;
        LookCameraAt(anchor, dt);
    }

    // -------------------------
    // INPUT
    // -------------------------
    private void ReadOrbitInput(float dt)
    {
        Vector2 look = GetLookInput();
        float lookMag = look.Length();

        if (lookMag > 0.001f)
        {
            _timeSinceLookInput = 0f;

            _yaw   -= look.X * OrbitSpeedX * dt;   // minus så det känns “rätt”
            _pitch -= look.Y * OrbitSpeedY * dt;

            float minRad = Mathf.DegToRad(PitchMinDeg);
            float maxRad = Mathf.DegToRad(PitchMaxDeg);
            _pitch = Mathf.Clamp(_pitch, minRad, maxRad);
        }
        else
        {
            _timeSinceLookInput += dt;
        }
    }

    private Vector2 GetLookInput()
    {
        // look_up should be -Y, look_down +Y in Input.get_vector => vi inverterar så upp blir +.
        Vector2 v = Input.GetVector("look_left", "look_right", "look_up", "look_down");
        return new Vector2(v.X, -v.Y);
    }

    // -------------------------
    // AUTO ALIGN
    // -------------------------
    private void ApplyAutoAlign(float dt)
    {
        if (_timeSinceLookInput < AutoAlignDelay) return;

        Vector3 dir = Player.LastMoveDirWorld;
        dir.Y = 0f;
        if (dir.Length() < 0.001f) return;

        float targetYaw = Mathf.Atan2(dir.X, dir.Z);

        // Smoothly move yaw toward movement direction
        _yaw = Mathf.LerpAngle(_yaw, targetYaw, AutoAlignSpeed * dt);
    }

    // -------------------------
    // FOLLOW SMOOTHING
    // -------------------------
    private void SmoothFollow(Vector3 targetPos, float dt)
    {
        // Simple PD-like follow: vel += (error*stiffness - vel*damping) * dt
        Vector3 error = targetPos - GlobalPosition;
        _followVel += (error * FollowStiffness - _followVel * FollowDamping) * dt;
        GlobalPosition += _followVel * dt;
    }

    // -------------------------
    // ORBIT POSITION
    // -------------------------
    private Vector3 ComputeDesiredCameraPosition(Vector3 anchor)
    {
        // Orbit direction from yaw/pitch
        Vector3 dir = new Vector3(
            Mathf.Sin(_yaw) * Mathf.Cos(_pitch),
            Mathf.Sin(_pitch),
            Mathf.Cos(_yaw) * Mathf.Cos(_pitch)
        );

        // place camera behind anchor
        Vector3 wanted = anchor - dir * OrbitDistance;
        wanted.Y += OrbitHeight; // extra lift
        return wanted;
    }

    // -------------------------
    // WHISKER OBSTACLE AVOIDANCE
    // -------------------------
    private Vector3 ApplyWhiskerAvoidance(Vector3 anchor, Vector3 desiredCamPos)
    {
        var space = GetWorld3D().DirectSpaceState;

        // We cast rays from player(anchor) toward desired camera
        Vector3 toCam = desiredCamPos - anchor;
        float dist = toCam.Length();
        if (dist < 0.001f) return desiredCamPos;

        Vector3 dir = toCam / dist;

        // Base hit (center ray) pushes camera inward if wall in between
        Vector3 bestPos = desiredCamPos;

        // 1) Center ray
        if (RayHit(space, anchor, desiredCamPos, out var hitCenter))
        {
            bestPos = hitCenter.Position - dir * CameraRadius;
        }

        // 2) Side whiskers (push sideways if close to wall)
        // offset start points around anchor (not from camera)
        Vector3 right = dir.Cross(Vector3.Up).Normalized();
        if (right.Length() < 0.001f) right = Vector3.Right;

        Vector3 up = Vector3.Up;

        Vector3 sidePush = Vector3.Zero;
        Vector3 upPush = Vector3.Zero;

        // Left / Right whiskers
        Vector3 startL = anchor - right * WhiskerSide;
        Vector3 startR = anchor + right * WhiskerSide;

        if (RayHit(space, startL, desiredCamPos - right * WhiskerSide, out _))
            sidePush += right * 0.35f; // push right
        if (RayHit(space, startR, desiredCamPos + right * WhiskerSide, out _))
            sidePush -= right * 0.35f; // push left

        // Up whisker (if ceiling-like close)
        Vector3 startU = anchor + up * WhiskerUp;
        if (RayHit(space, startU, desiredCamPos + up * WhiskerUp, out _))
            upPush -= up * 0.25f;

        // Ground push: if camera gets too close to ground, push up & inward
        // Cast down from desired cam pos to see how close to ground
        float groundCheck = 0.9f;
        if (RayHit(space, desiredCamPos, desiredCamPos + Vector3.Down * groundCheck, out var groundHit))
        {
            // push up a bit, and also a bit inward toward anchor
            Vector3 inward = (anchor - desiredCamPos);
            inward.Y = 0f;
            inward = inward.Normalized();
            bestPos += Vector3.Up * GroundPushUp * 0.35f;
            bestPos += inward * 0.25f;
        }

        // Apply pushes (then clamp via a final safety ray)
        bestPos += sidePush + upPush;

        // Final safety: ensure no wall between anchor and bestPos
        Vector3 toBest = bestPos - anchor;
        float bestDist = toBest.Length();
        if (bestDist > 0.001f)
        {
            Vector3 bestDir = toBest / bestDist;
            if (RayHit(space, anchor, bestPos, out var hitFinal))
                bestPos = hitFinal.Position - bestDir * CameraRadius;
        }

        return bestPos;
    }

    private bool RayHit(PhysicsDirectSpaceState3D space, Vector3 from, Vector3 to, out PhysicsRayQueryResult3D hit)
    {
        var query = PhysicsRayQueryParameters3D.Create(from, to);
        query.CollisionMask = LargeObstacleMask;   // IGNORE small obstacles (layer2)
        query.HitFromInside = false;
        query.CollideWithAreas = false;

        var res = space.IntersectRay(query);
        if (res.Count > 0)
        {
            hit = new PhysicsRayQueryResult3D(res);
            return true;
        }

        hit = default;
        return false;
    }

    // -------------------------
    // HINT OVERRIDE
    // -------------------------
    private void ApplyHintOverride(ref Vector3 camPos, float dt)
    {
        if (!_hintActive) return;

        // Blend camera position toward hint target position
        Vector3 hintPos = _hintTargetTransform.Origin;
        camPos = camPos.Lerp(hintPos, 1f - Mathf.Exp(-HintBlendSpeed * dt));

        // Blend FOV too
        Cam.Fov = Mathf.Lerp(Cam.Fov, _hintTargetFov, 1f - Mathf.Exp(-HintBlendSpeed * dt));
    }

    private void LookCameraAt(Vector3 lookAtPoint, float dt)
    {
        // simple smooth look-at: build target basis, slerp rotation
        Transform3D t = Cam.GlobalTransform;
        t = t.LookingAt(lookAtPoint, Vector3.Up);

        // Smooth rotation
        Basis current = Cam.GlobalTransform.Basis;
        Basis target = t.Basis;

        Quaternion cq = current.GetRotationQuaternion();
        Quaternion tq = target.GetRotationQuaternion();

        Quaternion rq = cq.Slerp(tq, 1f - Mathf.Exp(-10f * dt));
        Cam.GlobalTransform = new Transform3D(new Basis(rq), Cam.GlobalTransform.Origin);
    }

    // Called by HintArea
    public void SetHintOverride(bool active, Transform3D targetTransform, float targetFov)
    {
        _hintActive = active;
        _hintTargetTransform = targetTransform;
        _hintTargetFov = targetFov;

        if (!active)
        {
            // When exiting hint, smoothly return FOV to something reasonable
            _hintTargetFov = 70f;
        }
    }
}

