using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Collections.Concurrent;
using Common;
using Server;
using Godot;

public class GameServer
{
	#region Server Data
	private bool loop = true;
	private System.Threading.Thread serverThread;
	public BlockType[] blocks { get; private set; }
	#endregion
	#region Chunk management
	private ConcurrentQueue<Chunk> trash = new ConcurrentQueue<Chunk>();
	private ConcurrentQueue<Coord> chunksToCreate = new ConcurrentQueue<Coord>();
	public ConcurrentQueue<ChunkJob> chunkJobs = new ConcurrentQueue<ChunkJob>();
	private Queue<Chunk> chunkPool = new Queue<Chunk>();
	private List<Player> players = new List<Player>();
	private ConcurrentQueue<Player> playersToRemove = new ConcurrentQueue<Player>();
	private int reducePool = 0;
	#endregion
	#region Threading
	private ConcurrentQueue<Worker> freeWorkers = new ConcurrentQueue<Worker>();
	private ConcurrentQueue<Worker> readyWorkers = new ConcurrentQueue<Worker>();
	#endregion
	#region Telepathy
	Telepathy.Server server = new Telepathy.Server(65535);
	private ConcurrentQueue<ServerPacket> packets = new ConcurrentQueue<ServerPacket>();
	private Message msg = new Message();
	private Message msgR = new Message();
	#endregion
	#region Tick system
	private Stopwatch time = new Stopwatch();
	#endregion
	public GameServer(int port)
	{
		server.OnConnected = (connectionId) => OnConnected(connectionId);
		server.OnData = (connectionId, message) => packets.Enqueue(new ServerPacket(connectionId, Message.Decompress(message.Array)));
		server.OnDisconnected = (connectionId) => OnDisconnected(connectionId);
		server.Start(port);
		serverThread = new System.Threading.Thread(Update);
		serverThread.Start();
		#region Threading setup
		int workerCount = (int)Math.Clamp(System.Environment.ProcessorCount, 1, System.Environment.ProcessorCount - 2);
		for (int i = 0; i < workerCount; i++)
			freeWorkers.Enqueue(new Worker(readyWorkers));
		#endregion
		time.Start();
	}
	private void Update()
	{
		while (loop)
		{
			//Obsługa pakietów
			ProcessPackets();
			//Usuwanie graczy
			int tmpCount = playersToRemove.Count;
			for (int i = 0; i < tmpCount; i++)
			{
				if (!playersToRemove.TryDequeue(out Player player))
					continue;

				player.Destroy();
				reducePool += (int)Math.Floor(Math.Pow(player.viewDistance * 2, 3));
			}
			//Rozdzielanie zadań
			tmpCount = readyWorkers.Count;
			for (int i = 0; i < tmpCount; i++)
			{
				if (!readyWorkers.TryDequeue(out Worker worker))
					continue;

				if (worker.job == Job.Make)
				{
					worker.chunk.UnLock();
					freeWorkers.Enqueue(worker);
					continue;
				}
				if (worker.job == Job.Suspended)
				{
					worker.chunk.UnLock();
					freeWorkers.Enqueue(worker);
					continue;
				}
			}
			//Zwaracanie chunków
			tmpCount = trash.Count;
			for (int i = 0; i < tmpCount; i++)
			{
				if (!trash.TryDequeue(out Chunk chunk))
					continue;
				
				TrashChunk(chunk);
			}
			//Tworzenie chunków
			tmpCount = chunksToCreate.Count;
			for (int i = 0; i < tmpCount; i++)
			{
				if (!chunksToCreate.TryDequeue(out Coord coord))
					continue;

				MoveChunk(coord);
			}
			//Odbieranie zadań
			tmpCount = freeWorkers.Count;
			for (int i = 0; i < tmpCount; i++)
			{
				if (!freeWorkers.TryDequeue(out Worker worker))
					continue;
				if (!chunkJobs.TryDequeue(out ChunkJob chunkJob))
				{
					freeWorkers.Enqueue(worker);
					continue;
				}

				if (chunkJob.job == Job.Make)
				{
					worker.Trigger(chunkJob);
				}
				else
				{
					chunkJobs.Enqueue(chunkJob);
					freeWorkers.Enqueue(worker);
					GD.Print("Update err");
				}
			}
			//Zadania poszczególnych graczy
			for (int i = 0; i < players.Count; i++)
				players[i].Check();
			
			server.Tick(400);
			Tick();
		}
	}
	#region Entities

	#endregion
	#region Chunk manipulation
	private void MoveChunk(Coord coord)
	{
		if (chunkPool.Count == 0) // && IsInOffset(coord))// && coord.player.chunks[coord.x - coord.player.offset.x][coord.y - coord.player.offset.y][coord.z - coord.player.offset.z] == null)
			return;

		Chunk chunk = CheckInPlayers(coord);// = chunkPool.Dequeue();
		if (chunk != null)
		{
			for (int i = 0; i < players.Count; i++)
				if (players[i].IsInOffset(coord))
					players[i].SetChunk(chunk, coord);
		}
		else if (IsInAnyOffset(coord))
		{
			chunk = chunkPool.Dequeue();
			chunk.Move(coord);
			chunk.Lock();
			for (int i = 0; i < players.Count; i++)
				if (players[i].IsInOffset(coord))
					players[i].SetChunk(chunk, coord);
			chunkJobs.Enqueue(new ChunkJob(Job.Make, chunk));
		}
	}
	private void TrashChunk(Chunk chunk)
	{
		if (chunk.IsLocked())
		{
			trash.Enqueue(chunk);
			return;
		}
		
		Chunk chunk1 = CheckInPlayers(chunk.chunkCoord);
		if (chunk1 == null)
		{
			chunkPool.Enqueue(chunk);
		}
	}
	private void ExtendPool(int viewDistance)
	{
		int amount = (int)Math.Floor(Math.Pow(viewDistance * 2, 3));
		for (int i = 0; i < amount; i++)
			chunkPool.Enqueue(new Chunk());
	}
	#endregion
	#region Network
	private void ProcessPackets()
	{
		int tmpCount = packets.Count;
		for (int i = 0; i < tmpCount; i++)
		{
			if (!packets.TryDequeue(out ServerPacket serverPacket))
				continue;
			
			Packet type = msgR.Type(serverPacket.data);
			if (type == Packet.Settings)
			{
				int ct = msgR.ReadByte();
				int viewDistance = msgR.ReadByte();
				for	(int j = 0; j < ct; j++)
					AddPlayer(viewDistance, serverPacket.connectionID, j);
				continue;
			}
			
			if (type == Packet.WorldInitData)
			{
				foreach (Player player in FindPlayers(serverPacket.connectionID))
				{
					msg.NewMessage(Packet.Move, 26);
					msg.WriteByte((byte)player.userID);
					msg.WriteDVector(player.GetPlayerPos());
					Send(serverPacket.connectionID, msg.Compress());
				}
				continue;
			}
			
			if (type == Packet.ChunkRequest)
			{
				List<Player> list = FindPlayers(serverPacket.connectionID);
				int max = msgR.ReadInt();
				Chunk chunk;
				Coord coord;
				for (int j = 5; j < max; j += 12)
				{
					coord = new Coord(msgR.ReadInt(), msgR.ReadInt(), msgR.ReadInt());
					foreach (Player player in list)
					{
						if (!player.IsInOffset(coord))
							continue;

						chunk = player.GetChunk(coord);
						if (chunk != null && chunk.Lock())
						{
							msg.NewMessage(Packet.ChunkData, Game.voxelsInChunk * Game.voxelsInChunk * Game.voxelsInChunk * 2 + 13);
							msg.WriteInt(chunk.chunkCoord.x);
							msg.WriteInt(chunk.chunkCoord.y);
							msg.WriteInt(chunk.chunkCoord.z);
							msg.WriteBuffer(chunk.voxelMap, 0, Game.voxelsInChunk * Game.voxelsInChunk * Game.voxelsInChunk * 2);
							Send(serverPacket.connectionID, msg.Compress());
							chunk.UnLock();
							break;
						}
					}
				}
				continue;
			}
			
			if (type == Packet.StateUpdate)
			{
				int id = msgR.ReadByte();
				Player player = FindPlayer(serverPacket.connectionID, id);
				if (player == null)
					continue;
				player.MovePlayer(new DVector(msgR.ReadDouble(), msgR.ReadDouble(), msgR.ReadDouble()), msgR.ReadDouble(), msgR.ReadDouble());
				for (int j = 0; j < players.Count; j++)
				{
					if(players[j].connectionID != player.connectionID && players[j].IsInOffset(player.GetPlayerCoord()))
					{
						msg.NewMessage(Packet.Entity, 56);
						msg.WriteDVector(player.GetPlayerPos());
						msg.WriteDouble(player.xAngle);
						msg.WriteDouble(player.yAngle);
						msg.WriteGuid(player.guid);
						Send(players[j].connectionID, msg.Compress());
					}
				}
				continue;
			}
		}
	}
	void AddPlayer(int viewDistance, int connectionID, int userID)
	{
		ExtendPool(viewDistance);
		players.Add(new Player(viewDistance, connectionID, userID, trash, chunksToCreate));
	}
	private void OnConnected(int connectionId)
	{
		Message message = new Message();
		message.NewMessage(Packet.Settings, 2);
		message.WriteByte((byte)Game.voxelsInChunk);
		Send(connectionId, message.Compress());
	}
	private void Send(int connectionId, byte[] data)
	{
		server.Send(connectionId, new ArraySegment<byte>(data));
	}
	private void OnDisconnected(int connectionID)
	{
		foreach (Player player in FindPlayers(connectionID))
			playersToRemove.Enqueue(player);
	}
	private Player FindPlayer(int connectionID, int userID)
	{
		for (int i = 0; i < players.Count; i++)
			if (players[i].connectionID == connectionID && players[i].userID == userID)
				return players[i];
		return null;
	}
	private List<Player> FindPlayers(int connectionID)
	{
		List<Player> list = new List<Player>();
		for (int i = 0; i < players.Count; i++)
			if (players[i].connectionID == connectionID)
				list.Add(players[i]);
		return list;
	}
	#endregion
	#region Tick system
	private void Tick()
	{
		time.Stop();
		int rTime = (int)(Game.frequency - (time.Elapsed.TotalMilliseconds * 1000));
		if (rTime < Game.frequency && rTime > 0)
			System.Threading.Thread.Sleep(rTime);
		time.Restart();
		time.Start();
	}
	#endregion
	private Chunk CheckInPlayers(Coord coord)
	{
		for (int i = 0; i < players.Count; i++)
		{
			if (players[i].IsInOffset(coord) && players[i].GetChunk(coord) != null)
			{
				return players[i].GetChunk(coord);
			}
		}
		return null;
	}
	private bool IsInAnyOffset(Coord coord)
	{
		for (int i = 0; i < players.Count; i++)
			if (players[i].IsInOffset(coord))
				return true;
		return false;
	}
	public void OnDisable()
	{
		if (server.Active)
			server.Stop();
		while (freeWorkers.Count > 0)
			if (freeWorkers.TryDequeue(out Worker worker))
				worker.Abort();
		while (readyWorkers.Count > 0)
			if (readyWorkers.TryDequeue(out Worker worker))
				worker.Abort();
		loop = false;
	}
	private Player IsInOffset(int i, Coord coord)
	{
		for (int j = 0; j < players.Count; j++)
			if (j != i)
				if (players[j].IsInOffset(coord))
					return players[j];
		return null;
	}
}
