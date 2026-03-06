using Godot;

public partial class HintArea : Area3D
{
    [ExportCategory("Hint Nodes (children of this Area)")]
    [Export] public Marker3D HintCamPos;
    [Export] public Marker3D HintLookAt;

    [ExportCategory("Hint Settings")]
    [Export] public float HintFov = 45f;
    [Export] public float EnterBlendSpeed = 3.5f;
    [Export] public float ExitBlendSpeed = 3.0f;

    [ExportCategory("Camera Rig Path")]
    [Export] public NodePath CameraRigPath = "../../../CameraRig";

    private CameraRig _rig;

    public override void _Ready()
    {
        if (!CameraRigPath.IsEmpty)
            _rig = GetNode<CameraRig>(CameraRigPath);

        BodyEntered += OnBodyEntered;
        BodyExited += OnBodyExited;
    }

    private void OnBodyEntered(Node3D body)
    {
        if (_rig == null) return;
        if (!body.IsInGroup("player")) return;

        _rig.EnterHint(this);
    }

    private void OnBodyExited(Node3D body)
    {
        if (_rig == null) return;
        if (!body.IsInGroup("player")) return;

        _rig.ExitHint(this);
    }
}