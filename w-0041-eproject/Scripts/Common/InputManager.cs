using Godot;

public partial class InputManager : Node
{
    [Export] public int DeviceId = 0;

    [Export] public float MoveDeadzone = 0.18f;
    [Export] public float LookDeadzone = 0.12f;

    [Export] public float LookSensitivity = 2.2f;

    public override void _Ready()
    {
        PickFirstConnectedJoypad();
        Input.JoyConnectionChanged += OnJoyConnectionChanged;
    }

    private void OnJoyConnectionChanged(long device, bool connected)
    {
        // När någon kopplas i/ur: välj första bästa igen
        PickFirstConnectedJoypad();
    }

    private void PickFirstConnectedJoypad()
    {
        var pads = Input.GetConnectedJoypads();
        if (pads.Count > 0)
        {
            DeviceId = pads[0];
            GD.Print($"[InputManager] Using DeviceId={DeviceId} Name='{Input.GetJoyName(DeviceId)}'");
        }
        else
        {
            GD.Print("[InputManager] No joypads connected.");
        }
    }

    public Vector2 GetMoveStick()
    {
        float x = Input.GetJoyAxis(DeviceId, JoyAxis.LeftX);
        float y = Input.GetJoyAxis(DeviceId, JoyAxis.LeftY);
        Vector2 v = new Vector2(x, -y); // upp blir +Y
        return ApplyDeadzone(v, MoveDeadzone);
    }

    public Vector2 GetLookStick()
    {
        float x = Input.GetJoyAxis(DeviceId, JoyAxis.RightX);
        float y = Input.GetJoyAxis(DeviceId, JoyAxis.RightY);
        Vector2 v = new Vector2(x, -y);
        return ApplyDeadzone(v, LookDeadzone);
    }

    public bool JumpPressed()
    {
        // A (Xbox) / Cross (PS ofta)
        return Input.IsJoyButtonPressed(DeviceId, JoyButton.A);
    }

    public bool DuckHeld()
    {
        // B (Xbox) / Circle (PS ofta)
        return Input.IsJoyButtonPressed(DeviceId, JoyButton.B);
    }

    private static Vector2 ApplyDeadzone(Vector2 v, float dz)
    {
        float len = v.Length();
        if (len < dz) return Vector2.Zero;

        float t = (len - dz) / (1.0f - dz);
        return v.Normalized() * Mathf.Clamp(t, 0, 1);
    }
}