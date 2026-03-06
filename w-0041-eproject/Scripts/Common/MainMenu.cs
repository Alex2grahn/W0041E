using Godot;

public partial class MainMenu : Control
{
	public override void _Ready()
	{
		// Koppla knappar via nodnamn
		GetNode<Button>("CenterContainer/VBoxContainer/ButtonLab1")
			.Pressed += OnLab1Pressed;

		GetNode<Button>("CenterContainer/VBoxContainer/ButtonLab2")
			.Pressed += OnLab2Pressed;

		GetNode<Button>("CenterContainer/VBoxContainer/ButtonLab3")
			.Pressed += OnLab3Pressed;

		GetNode<Button>("CenterContainer/VBoxContainer/ButtonQuit")
			.Pressed += OnQuitPressed;
	}

	private void OnLab1Pressed()
	{
		GetTree().ChangeSceneToFile(
            "res://Scenes/Lab1_Controller/Lab1.tscn"
		);
	}

	private void OnLab2Pressed()
	{
		GetTree().ChangeSceneToFile(
            "res://Scenes/Lab2_CameraContext/Lab2.tscn"
		);
	}

	private void OnLab3Pressed()
	{
		GetTree().ChangeSceneToFile(
            "res://Scenes/Lab3_Pong/PongMain.tscn"
		);
	}

	private void OnQuitPressed()
	{
		GetTree().Quit();
	}
}
