using Godot;

public partial class ModeHud : CanvasLayer
{
    private PlayerController _player;
    private Label _modeLabel;

    public override void _Ready()
    {
        _modeLabel = GetNode<Label>("ModeLabel");
        _player = GetNode<PlayerController>("../World/Player");
    }

    public override void _Process(double delta)
    {
        if (_player == null || _modeLabel == null) return;

        float speed = new Vector2(_player.Velocity.X, _player.Velocity.Z).Length();

        _modeLabel.Text =
            $"Mode: {(int)_player.CurrentMode} ({_player.CurrentMode})\n" +
            $"1=Immediate  2=Linear  3=Ease\n" +
            $"Speed: {speed:0.0}";
    }
}