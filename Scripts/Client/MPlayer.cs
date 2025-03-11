using Godot;
using Common;

public partial class MPlayer : CharacterBody3D
{
	private CharacterBody3D head;
	private Node3D blockHighlight, kierunek;
	public bool Enabled = false;
	private string[] actions = new string[6];
	private byte id;
	public override void _Ready()
	{
		head = GetNode<CharacterBody3D>("head");
		blockHighlight = GetNode<Node3D>("head/camera/testerro");
		kierunek = GetNode<Node3D>("head/camera/kierunek");
		actions[0] = Name + "_left"; actions[1] = Name + "_right";
		actions[2] = Name + "_forward"; actions[3] = Name + "_backward";
		actions[4] = Name + "_down"; actions[5] = Name + "_up";
		string s = Name;
		id = byte.Parse(s.Split('p')[1]);
	}
	public const float Speed = 5.0f;
	public void Connect()
	{
		Node3D origin = GetNode<Node3D>("../origin");
		float distance = Game.graphicDistance + Game.viewDistance * Game.voxelsInChunk;
		switch (Name)
		{
			case "p0": origin.Position = new Vector3(distance, 0, distance); break;
			case "p1": origin.Position = new Vector3(-distance, 0, distance); break;
			case "p2": origin.Position = new Vector3(distance, 0, -distance); break;
			case "p3": origin.Position = new Vector3(-distance, 0, -distance); break;
		}
		//world = new World(origin, main.ip.Text, Int32.Parse(main.port.Text));
		//position = new DVector(0,0,0);
		//Game.world.AddPlayer(this, id);
		Enabled = true;
	}

	public override void _PhysicsProcess(double delta)
	{
		if (Game.world == null || !Enabled)
			return;
		//Game.world.Update();
		Vector3 velocity = Velocity;
		Vector2 inputDir = Input.GetVector(actions[0], actions[1], actions[2], actions[3]);
		Vector3 direction = (Transform.basis * new Vector3(inputDir.x, 0, inputDir.y)).Normalized();
		float upDown = 0;
		if(Input.IsActionPressed(actions[4]))
				upDown = -1;
		if(Input.IsActionPressed(actions[5]))
			upDown = 1;
		if (Name == "p0")
		{
			
			Rect2 window = GetViewport().GetVisibleRect();
			Vector2 mouseDelta = GetViewport().GetMousePosition() - (window.Size / 2);
			GetViewport().WarpMouse(window.Size / 2);
			//GD.Print(mouseDelta.x);
			RotateY((float)(-mouseDelta.x * delta * 0.1f));
			head.RotateX((float)(-mouseDelta.y * delta * 0.1f));
			Vector3 blockPosition = Game.world.CursorPosition((kierunek.GlobalPosition - GlobalPosition).Normalized(), id);
			if (blockPosition.x == -0.5f)
			{
				blockHighlight.Visible = false;
			}
			else
			{
				blockHighlight.Visible = true;
				blockHighlight.GlobalPosition = blockPosition + new Vector3(0.51f, 0.51f, 0.51f);
				blockHighlight.GlobalRotation = Vector3.Zero;
			}
		}
		direction.y = upDown;
		if (direction != Vector3.Zero)
		{
			velocity.x = direction.x * Speed;
			velocity.y = direction.y * Speed;
			velocity.z = direction.z * Speed;
		}
		else
		{
			velocity.x = Mathf.MoveToward(Velocity.x, 0, Speed);
			velocity.y = Mathf.MoveToward(Velocity.y, 0, Speed);
			velocity.z = Mathf.MoveToward(Velocity.z, 0, Speed);
		}
		
		if(velocity != Vector3.Zero)
			Game.world.MovePlayer(velocity, id);
	}
}