using Godot;
using System;

public partial class HintAreaCameraOverride : Area3D
{
    [ExportCategory("References")]
    [Export] public ThirdPersonCameraRig CameraRig;
    [Export] public Node3D HintCameraTarget;   // position/rotation för “cinematic”
    [Export] public float HintFov = 55f;

    public override void _Ready()
    {
        BodyEntered += OnBodyEntered;
        BodyExited += OnBodyExited;
    }

    private void OnBodyEntered(Node3D body)
    {
        if (CameraRig == null || HintCameraTarget == null) return;
        if (body is PlayerController)
        {
            CameraRig.SetHintOverride(true, HintCameraTarget.GlobalTransform, HintFov);
        }
    }

    private void OnBodyExited(Node3D body)
    {
        if (CameraRig == null || HintCameraTarget == null) return;
        if (body is PlayerController)
        {
            CameraRig.SetHintOverride(false, HintCameraTarget.GlobalTransform, 70f);
        }
    }
}
