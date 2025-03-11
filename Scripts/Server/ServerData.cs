using System;
using System.Collections.Concurrent;
using Common;

namespace Server
{
	public class ServerPacket
	{
		public byte[] data;
		public int connectionID;
		public ServerPacket(int connectionID, byte[] data)
		{
			this.connectionID = connectionID;
			//this.data = new byte[data.Length];
			this.data = data;
			//Array.Copy(data, this.data, data.Length);
		}
	}
	public class ChunkJob
	{
		public Job job;
		public Chunk chunk;
		public ChunkJob(Job job, Chunk chunk)
		{
			this.job = job;
			this.chunk = chunk;
		}
	}
	public class Player
	{
		#region Variables
		private ConcurrentQueue<Chunk> trash;
		private ConcurrentQueue<Coord> chunksToCreate;
		public Guid guid { get; private set; } = new Guid();
		private Chunk[,,] chunks;
		private Chunk[,,] chunks1;
		private Coord offset;
		private Coord newOffset;
		private Coord lastPlayerCoord = new Coord(0, 0, 0);
		private Coord playerCoord = new Coord(0, 0, 0);
		private DVector position = new DVector(0, 0, 0);
		public double xAngle { get; private set; }
		public double yAngle { get; private set; }
		public int viewDistance { get; private set; }
		public int connectionID { get; private set; }
		public int userID { get; private set; }
		private int tableRepetitions;
		private int tableSize;
		#endregion
		public Player(int viewDistance, int connectionID, int userID, ConcurrentQueue<Chunk> trash, ConcurrentQueue<Coord> chunksToCreate)
		{
			guid = Guid.NewGuid();
			this.trash = trash;
			this.chunksToCreate = chunksToCreate;
			this.viewDistance = viewDistance;
			this.connectionID = connectionID;
			this.userID = userID;
			tableSize = (viewDistance * 2) - 1;
			int range = viewDistance * 2;
			chunks = new Chunk[range, range, range];
			chunks1 = new Chunk[range, range, range];
			position = new DVector(Game.worldSize / 2, Game.worldSize / 2, Game.worldSize / 2);
			playerCoord = DVector.GetCoord(position);
			lastPlayerCoord = new Coord(-1, -1, -1);
			offset = GetOffset(playerCoord);
			newOffset = GetOffset(playerCoord);
			tableRepetitions = viewDistance * 2;
			tableSize = tableRepetitions - 1;
		}
		public void Check()
		{
			playerCoord = DVector.GetCoord(position);
			if (!playerCoord.Equals(lastPlayerCoord))
			{
				newOffset = GetOffset(playerCoord);
				ChangeTables();
				lastPlayerCoord = DVector.GetCoord(position);
			}
		}
		public void Destroy()
		{
			int tmp = viewDistance * 2 - 1;
			for (int x = 0; x < tmp; x++)
			{
				for (int y = 0; y < tmp; y++)
				{
					for (int z = 0; z < tmp; z++)
					{
						if (chunks[x, y, z] != null)
							trash.Enqueue(chunks[x, y, z]);
					}
				}
			}
		}
		private void ChangeTables()
		{
			for (int x = 0; x < tableRepetitions; x++)
			for (int y = 0; y < tableRepetitions; y++)
			for (int z = 0; z < tableRepetitions; z++)
			{
				if (chunks[x, y, z] != null && !IsInOffset(chunks[x, y, z].chunkCoord)) //If is not in offset returns true
				{
					trash.Enqueue(chunks[x, y, z]);
					chunks[x, y, z] = null;
				}
				else if (chunks[x, y, z] != null)
				{
					chunks1[chunks[x, y, z].chunkCoord.x - newOffset.x,
							chunks[x, y, z].chunkCoord.y - newOffset.y,
							chunks[x, y, z].chunkCoord.z - newOffset.z] = chunks[x, y, z];
					chunks[x, y, z] = null;
				}
			}
			for (int x = 0; x < tableRepetitions; x++)
			for (int y = 0; y < tableRepetitions; y++)
			for (int z = 0; z < tableRepetitions; z++)
			{
				chunks[x, y, z] = chunks1[x, y, z];
				chunks1[x, y, z] = null;
				if (chunks[x, y, z] == null)
				{
					chunksToCreate.Enqueue(new Coord(x + newOffset.x, y + newOffset.y, z + newOffset.z));
				}
			}
			offset.Set(newOffset);
		}
		public bool IsInOffset(Coord coord)
		{
			if (coord.x - newOffset.x < 0 || coord.y - newOffset.y < 0 || coord.z - newOffset.z < 0 ||
				coord.x - newOffset.x > tableSize || coord.y - newOffset.y > tableSize || coord.z - newOffset.z > tableSize)
				return false;
			else
				return true;
		}
		private Coord GetOffset(Coord playerCoord)
		{
			return playerCoord - viewDistance;
		}
		public void MovePlayer(DVector pos, double xAngle, double yAngle, bool angle = true)
		{
			position = pos;
			if(angle)
			{
				this.xAngle = xAngle;
				this.yAngle = yAngle;
			}
		}
		public Coord GetPlayerCoord()
		{
			return playerCoord;
		}
		public DVector GetPlayerPos()
		{
			return position;
		}
		public Chunk GetChunk(Coord coord)
		{
			Coord result = coord - newOffset;
			return chunks[result.x, result.y, result.z];
		}
		public void SetChunk(Chunk chunk, Coord coord)
		{
			Coord result = coord - newOffset;
			chunks[result.x, result.y, result.z] = chunk;
		}
	}
}
