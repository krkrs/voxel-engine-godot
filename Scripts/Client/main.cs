using Godot;
using System;
using System.Collections.Generic;
using Common;

public partial class main : Node3D
{
	private GameServer gameServer;
	public static Material material { get; private set; }
	public static TextEdit ip, port;
	private RichTextLabel localPlayer;
	private GridContainer gridContainer;
	List<MPlayer> players = new List<MPlayer>();
	private CanvasItem main_menu, player1Screen, player2Screen, player3Screen, player4Screen;
	public override void _PhysicsProcess(double delta)
	{
		foreach (OneFrameUpdate item in Game.updates)
			item.Update();
	}
	public override void _Ready()
	{
		material = GetNode<MeshInstance3D>("/root/main/material").MaterialOverride;
		gridContainer = GetNode<GridContainer>("GridContainer");
		player1Screen = gridContainer.GetNode<CanvasItem>("screen_player0");
		player2Screen = gridContainer.GetNode<CanvasItem>("screen_player1");
		player3Screen = gridContainer.GetNode<CanvasItem>("screen_player2");
		player4Screen = gridContainer.GetNode<CanvasItem>("screen_player3");
		ip = player1Screen.GetNode<TextEdit>("SubViewport/UI/main_menu/dnsip");
		port = player1Screen.GetNode<TextEdit>("SubViewport/UI/main_menu/port");
		localPlayer = player1Screen.GetNode<RichTextLabel>("SubViewport/UI/main_menu/local_players");
		main_menu = player1Screen.GetNode<CanvasItem>("SubViewport/UI/main_menu");
		Game.Read();
	}
	private void MainMenu()
	{
		main_menu.Visible = !main_menu.Visible;
	}
	private void _on_offline_pressed()
	{
		RunGame(1);
	}
	private void _on_run_server_pressed()
	{
		RunServer();
	}
	private void _on_split_screen_pressed()
	{
		RunGame(Int32.Parse(localPlayer.Text));	
	}
	private void RunGame(int i)
	{
		Mouse(false);
		RunServer();
		GetViewport().WarpMouse(GetViewport().GetVisibleRect().Size / 2);
		main_menu.Visible = false;
		ManageViewports(i);
	}
	private void AddPlayer(int i)
	{
		MPlayer mPlayer = new MPlayer();
		switch (i)
		{
			case 1: mPlayer = player1Screen.GetNode<MPlayer>("SubViewport/p0"); break;
			case 2: mPlayer = player2Screen.GetNode<MPlayer>("SubViewport/p1"); break;
			case 3: mPlayer = player3Screen.GetNode<MPlayer>("SubViewport/p2"); break;
			case 4: mPlayer = player4Screen.GetNode<MPlayer>("SubViewport/p3"); break;
		}
		players.Add(mPlayer);
		mPlayer.Connect();
	}
	private void RunServer()
	{
		if (gameServer == null)
			gameServer = new GameServer(Int32.Parse(port.Text));
	}
	private void Mouse(bool visible)
	{
		if (visible)
			Input.MouseMode = Input.MouseModeEnum.Visible;
		else
			Input.MouseMode = Input.MouseModeEnum.ConfinedHidden;
	}
	private void ManageViewports(int amount)
	{
		Node3D origin = this;
		switch(amount)
		{
			case 1:
			gridContainer.Columns = 1;
			player1Screen.Visible = true;
			player2Screen.Visible = false;
			player3Screen.Visible = false;
			player4Screen.Visible = false;
			AddPlayer(1);
			break;
			case 2:
			gridContainer.Columns = 2;
			player1Screen.Visible = true;
			player2Screen.Visible = true;
			player3Screen.Visible = false;
			player4Screen.Visible = false;
			AddPlayer(1); AddPlayer(2);
			break;
			case 3:
			gridContainer.Columns = 2;
			player1Screen.Visible = true;
			player2Screen.Visible = true;
			player3Screen.Visible = true;
			player4Screen.Visible = false;
			AddPlayer(1); AddPlayer(2); AddPlayer(3);
			break;
			case 4:
			gridContainer.Columns = 2;
			player1Screen.Visible = true;
			player2Screen.Visible = true;
			player3Screen.Visible = true;
			player4Screen.Visible = true;
			AddPlayer(1); AddPlayer(2); AddPlayer(3); AddPlayer(4);
			break;
		}
		Game.world = new World(origin, ip.Text, Int32.Parse(port.Text), players.ToArray());
	}
	private void _on_slider_split_screen_value_changed(float count)
	{
		localPlayer.Text = count.ToString();
	}
}
