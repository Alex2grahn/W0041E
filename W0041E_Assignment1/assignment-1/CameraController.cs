using Godot;
using System;

public partial class CameraController : Node3D
{



    public override void _Ready()
    {
        GD.Print("Ready");
    }


    public override void _Process(double delta)
    {
        GD.Print("test");
        GD.Print(Input.GetJoyAxis(0, JoyAxis.LeftX));
    }


}
