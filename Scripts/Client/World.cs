using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Common;
using Client;
using Godot;

public class World : Common.OneFrameUpdate
{
	#region References
	public BlockType[] blocks;
	#endregion
	#region Chunk management
	private ConcurrentQueue<Chunk> trash = new ConcurrentQueue<Chunk>();
	private ConcurrentQueue<Coord> chunksToCreate = new ConcurrentQueue<Coord>();
	private ConcurrentQueue<ChunkJob> chunkJobs = new ConcurrentQueue<ChunkJob>();
	private Queue<Chunk> chunkPool = new Queue<Chunk>();
	private List<Player> players = new List<Player>();
	private Chunk[] chunks = new Chunk[0];
	public static DVector graphicOffset { get; private set; }
	private Node parent;
	#endregion
	#region Threading
	private ConcurrentQueue<Worker> freeWorkers = new ConcurrentQueue<Worker>();
	private ConcurrentQueue<Worker> readyWorkers = new ConcurrentQueue<Worker>();
	private Worker[] workers;
	private string ip;
	private int port;
	#endregion
	#region Telepathy
	private Telepathy.Client client = new Telepathy.Client(65535);
	private ConcurrentQueue<byte[]> packets = new ConcurrentQueue<byte[]>();
	private Message message = new Message();
	private int connectionState = 0;
	private long clock = 0;
	private long requestTimeStamp = 0;
	private float coolDown = 0.2f;
	#endregion
	#region Time management
	private long time = DateTimeOffset.Now.ToUnixTimeSeconds();
	private float fpsTarget = 120;
	#endregion
	#region Entity management
	private Pool pool;
	private Queue<Entity> entities = new Queue<Entity>();
	#endregion
	public World(Node parent, string ip, int port, MPlayer[] mPlayers)
	{
		this.ip = ip;
		this.port = port;
		this.parent = parent;
		blocks = new BlockType[2];
		blocks[0] = new BlockType("air", false, false);
		blocks[1] = new BlockType("solid", true, false);
		client.OnConnected = () => GD.Print("Client Connected");
		client.OnData = (message) => packets.Enqueue(Message.Decompress(message.Array));
		client.OnDisconnected = () => GD.Print("Client Disconnected");
		Timer.Start();
		Game.updates.Add(this);
		for (int i = 0; i < mPlayers.Length; i++)
			players.Add(new Player(mPlayers[i], trash, chunksToCreate, (byte)i));
		int t = (int)(Math.Pow(Game.viewDistance * 2, 3) * players.Count);
		chunks = new Chunk[t];
		for (int i = 0; i < t; i++)
			chunkPool.Enqueue(chunks[i] = new Chunk(parent));
	}
	private void InitWorld()
	{
		#region Threading setup
		workers = new Worker[ThreadAllocation.GameThreads];
		Clock.Start();
		for (int i = 0; i < ThreadAllocation.GameThreads; i++)
			freeWorkers.Enqueue(workers[i] = new Worker(this, readyWorkers));
		#endregion
		#region Entity pool setup
		pool = new Pool();
		#endregion
		graphicOffset = new DVector(0, 0, 0);
	}
	public override void Update()
	{
		ConnectionManager();
		ProcessPackets();
		PositionCheck();
		//Odbieranie zadań
		ReceiveWork();
		//Zwracanie chunk�w
		TrashChunk();
		//Zapytanie o chunki
		RequestChunks();
		//Rozdzielenie zada�
		GiveWork();
		//Zadania poszczeg�lnych graczy
		GatherMissingChunks();
		//Przypisz byty do chunk�w
		AssignEntities();
		client.Tick(400);
		//Dodatkowe zadania
		while (Timer.ElapsedMilliseconds() < fpsTarget && chunkJobs.Count > 0)
		{
			ReceiveWork();
			GiveWork();
			TrashChunk();
		}
		Timer.Restart();
		//Debug();
	}
	#region IN-DEV
	private void Debug()
	{
		GD.Print("chunksToCreate ", chunksToCreate.Count);
		GD.Print("trash ", trash.Count);
		GD.Print("chunkJobs ", chunkJobs.Count);
		GD.Print("chunkPool ", chunkPool.Count);
		GD.Print("freeWorkers ", freeWorkers.Count);
		GD.Print("readyWorkers ", readyWorkers.Count);
		GD.Print("packets ", packets.Count);
	}
	private void PositionCheck()
	{
		DVector groupPos = new DVector(0,0,0);
		foreach (Player player in players)
			groupPos += player.position;
		groupPos /= players.Count;
		Shift((groupPos / Game.graphicDistance).Floor());
		foreach (Player player in players)
				player.AdjustPosition(graphicOffset);
		foreach (Player player in players)
		{
			message.NewMessage(Packet.StateUpdate, 42); //update all players todo
			message.WriteByte(player.id);
			message.WriteDVector(players[player.id].position);
			message.WriteDouble(player.xAngle);
			message.WriteDouble(player.yAngle);
			Send();
		}
	}
	private List<Player> PlayersInOffset(Coord offset)
	{
		List<Player> list = new List<Player>();
		foreach (Player player in players)
			if (player.IsInOffset(offset))
				list.Add(player);
		return list;
	}
	#endregion
	private void GatherMissingChunks()
	{
		if (!packets.IsEmpty)
			return;

		foreach (Player player in players)
			if (player.Check() && trash.Count == 0 && chunkPool.Count > 0 && packets.Count == 0 && chunksToCreate.Count == 0)
				player.RequestMissing();
	}
	private void RequestChunks()
	{
		int tmpCount = Mathf.Clamp(chunksToCreate.Count, 0, 5460);
		time = DateTimeOffset.Now.ToUnixTimeSeconds();
		if (time - requestTimeStamp > coolDown && connectionState == 2 && tmpCount > 0)
		{
			message.NewMessage(Packet.ChunkRequest, 6 + (tmpCount * 12));
			message.WriteInt(5 + (tmpCount * 12));
			for (int i = 0; i < tmpCount; i++)
			{
				if (chunksToCreate.TryDequeue(out Coord coord))
				{
					message.WriteInt(coord.x);
					message.WriteInt(coord.y);
					message.WriteInt(coord.z);
				}
			}
			Send();
			requestTimeStamp = time;
		}
	}
	#region Work
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ReceiveWork()
	{
		for (int i = 0; i < readyWorkers.Count; i++)
		{
			if (!readyWorkers.TryDequeue(out Worker worker))
				continue;

			if (worker.job == Job.Make)
			{
				if(worker.applyMesh)
					worker.chunk.SetMesh();
				worker.chunk.SetPos(graphicOffset);
				worker.chunk.WriteUnlock();
				Chunk[] uChunks = GetChunkForMesh(worker.chunk.chunkCoord);
				for (int j = 0; j < 6; j++)
					if (uChunks[j] != null)
						chunkJobs.Enqueue(new ChunkJob(Job.CreateMesh, uChunks[j]));
				freeWorkers.Enqueue(worker);
			}
			else if (worker.job == Job.CreateMesh)
			{
				if (worker.applyMesh)
					worker.chunk.SetMesh();
				worker.chunk.WriteUnlock();
				freeWorkers.Enqueue(worker);
			}
			else if (worker.job == Job.Suspended)
			{
				worker.chunk.WriteUnlock();
				freeWorkers.Enqueue(worker);
			}
		}
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void GiveWork()
	{
		int tmpCount = freeWorkers.Count;
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
			else if (chunkJob.job == Job.CreateMesh)
			{
				worker.Trigger(chunkJob);
			}
			else
			{
				chunkJobs.Enqueue(chunkJob);
				freeWorkers.Enqueue(worker);
				GD.Print("GiveWork err");
			}
		}
		
	}
	#endregion
	#region Entities
	private void AssignEntities()
	{
		int size = entities.Count;
		for (int i = 0; i < size; i++)
		{
			Entity entity = entities.Dequeue();
			Coord coord = DVector.GetCoord(entity.pos);

			foreach (Player player in players)
			{
				if (!player.IsInOffset(coord))
				{
					player.entities.Remove(entity.guid);
					pool.ReturnEntity(entity);
				}
				Chunk chunk = player.GetChunk(coord);
				if (chunk != null && chunk.WriteLock())
				{
					chunk.entities.Add(entity);
					chunk.WriteUnlock();
				}
				else
					entities.Enqueue(entity);
			}
		}
	}
	#endregion
	#region Chunk manipulation
	private void MoveChunk(Coord coord, byte[] data)
	{
		if (chunkPool.Count == 0) 
			return;
			
		Chunk chunk = CheckInPlayers(coord);
		if (chunk != null)
		{
			foreach (Player player in PlayersInOffset(coord))
				player.SetChunk(chunk, coord);
		}
		else if (IsInAnyOffset(coord))
		{
			chunk = chunkPool.Dequeue();
			chunk.Move(coord);
			chunk.WriteLock();
			chunk.data = data;
			foreach (Player player in PlayersInOffset(coord))
					player.SetChunk(chunk, coord);
			chunkJobs.Enqueue(new ChunkJob(Job.Make, chunk));
		}
	}
	private void TrashChunk()
	{
		int tmpCount = trash.Count;
		for (int i = 0; i < tmpCount; i++)
		{
			if (!trash.TryDequeue(out Chunk chunk))
				continue;
			if (chunk.IsLocked())
			{
				trash.Enqueue(chunk);
				continue;
			}
			
			Chunk chunk1 = CheckInPlayers(chunk.chunkCoord);
			if (chunk1 == null)
			{
				RemoveEntities(chunk);
				chunk.Hide();
				chunkPool.Enqueue(chunk);
			}
		}
	}
	private void RemoveEntities(Chunk chunk)
	{
		foreach (Entity entity in chunk.entities)
		{
			foreach (Player player in players)
				player.entities.Remove(entity.guid);
			pool.ReturnEntity(entity);
		}
		chunk.entities.Clear();
	}
	public void Shift(DVector _graphicOffset)
	{
		if (graphicOffset.Equals(_graphicOffset) || chunks == null)
			return;

		for (int i = 0; i < chunks.Length; i++)
			chunks[i].SetPos(_graphicOffset);

		foreach (Player player in players)
		{
			foreach (Entity entity in player.entities.Values)
			{
				//entity.transform.position += delta;
			}
		}

		graphicOffset = _graphicOffset;
		//Send();
	}
	public Chunk[] GetChunkForMeshSafe(Coord coord)
	{
		return players[0].GetChunksForMeshSafe(coord);
	}
	public Chunk[] GetChunkForMesh(Coord coord)
	{
		return players[0].GetChunksForMesh(coord);
	}
	#endregion
	#region Network
	private void Send()
	{
		client.Send(new ArraySegment<byte>(message.Compress()));
	}
	private void ConnectionManager()
	{
		if (connectionState == 0 && !client.Connected && time - clock > 1f)
		{
			client.Connect("localhost", 2137);
			clock = time;
		}
		else if (connectionState == 0 && client.Connected)
		{
			connectionState = 1;
			message.NewMessage(Packet.Settings, 3);
			message.WriteByte((byte)(players.Count));
			message.WriteByte((byte)Game.viewDistance);
			Send();
		}
	}
	private void ProcessPackets()
	{
		int tmp = packets.Count;
		for (int i = 0; i < tmp; i++)
		{
			if (!packets.TryDequeue(out byte[] array))
				continue;

			Packet type = message.Type(array);
			if (type == Packet.Settings)
			{
				InitWorld();
				message.NewMessage(Packet.WorldInitData, 1);
				Send();
				connectionState = 2;
				continue;
			}
			if (type == Packet.Move)
			{
				players[message.ReadByte()].MovePlayer(new DVector(message.ReadDouble(),
																   message.ReadDouble(),
																   message.ReadDouble()), 0, 0, false);
				continue;
			}
			if (type == Packet.ChunkData)
			{
				MoveChunk(new Coord(message.ReadInt(),
									message.ReadInt(),
									message.ReadInt()), array);
				continue;
			}
			if (type == Packet.Entity)
			{
				DVector pos = message.ReadDVector();
				double xAngle = message.ReadDouble();
				double yAngle = message.ReadDouble();
				Guid guid = message.ReadGuid();
				Coord coord = DVector.GetCoord(pos);
				foreach (Player player in PlayersInOffset(coord))
				{
					player.entities.TryGetValue(guid, out Entity entity);
					if (entity == null)
					{
						entity = pool.GetEntity(guid);  
						entity.pos = pos;
						/*entity.transform.position = players[0].ToGrapgic(pos);
						entity.transform.localRotation = Quaternion.Euler((float)yAngle, (float)xAngle, 0);
						entity.meshFilter.mesh = testMesh;
						entity.meshRenderer.enabled = true;*/
						player.entities.Add(entity.guid, entity);
						Chunk chunk = player.GetChunk(coord);
						if (chunk != null && chunk.WriteLock())
						{
							chunk.entities.Add(entity);
							chunk.WriteUnlock();
						}
						else
							entities.Enqueue(entity);
					}
					else
					{
						//entity.transform.position = players[0].ToGrapgic(pos);
						//entity.transform.localRotation = Quaternion.Euler((float)yAngle, (float)xAngle, 0);
					}
				}
				
				continue;
			}
		}
	}
	#endregion
	private Chunk CheckInPlayers(Coord coord)
	{
		foreach (Player player in players)
			if (player.IsInOffset(coord) && player.GetChunk(coord) != null)
				return player.GetChunk(coord);
		return null;
	}
	private bool IsInAnyOffset(Coord coord)
	{
		foreach (Player player in players)
			if (player.IsInOffset(coord))
				return true;
		return false;
	}
	public void OnDisable()
	{
		if (client.Connected)
			client.Disconnect();
		for (int i = 0; i < workers.Length; i++)
			workers[i].Abort();
	}
	public void MovePlayer(Vector3 delta, int id)
	{
		players[id].MovePlayer(DVector.FromVector(delta), 0,0);
	}
	public Vector3 CursorPosition(Vector3 direction, int id)
	{
		return players[id].CursorPosition(direction);
	}
}
