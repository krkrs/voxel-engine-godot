using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using Common;
using Godot;

namespace Client
{
	public class Entity
	{
		public DVector pos;
		public Guid guid;
		public Entity(DVector pos, Guid guid)
		{
			this.pos = pos;
			this.guid = guid;
		}
	}
	public class Pool
	{
		private ConcurrentQueue<Entity> entities = new ConcurrentQueue<Entity>();
		private int size;
		public Pool(int size = 10)
		{
			this.size = size;
			for (int i = 0; i < size; i++)
				entities.Enqueue(CreateEntity());
		}
		private Entity CreateEntity()
		{
			Entity entity = new Entity(new DVector(0,0,0), new Guid());
			return entity;
		}
		public Entity GetEntity(Guid guid)
		{
			if (entities.Count == 0)
				return CreateEntity();
			
			entities.TryDequeue(out Entity entity);
			entity.guid = guid;
			return entity;
		}
		public void ReturnEntity(Entity entity)
		{
			entities.Enqueue(entity);
		}
	}
	public struct ChunkJob
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
		private ConcurrentQueue<Chunk> trash;
		private ConcurrentQueue<Coord> chunksToCreate;
		public Dictionary<Guid, Entity> entities = new Dictionary<Guid, Entity>();
		private Chunk[,,] chunks, chunks1;
		private Coord offset, newOffset;
		private Coord lastPlayerChunkCoord = new Coord(0, 0, 0);
		private Coord playerChunkCoord = new Coord(0, 0, 0);
		private MPlayer player;
		public DVector position { get; private set; } = new DVector(0,0,0);
		public float xAngle {private set; get;} = 0;
		public float yAngle {private set; get;} = 0;
		private int tableRepetitions, tableSize;
		public byte id {private set; get;}
		public Player(MPlayer player, ConcurrentQueue<Chunk> trash, ConcurrentQueue<Coord> chunksToCreate, byte id)
		{
			this.trash = trash;
			this.chunksToCreate = chunksToCreate;
			this.player = player;
			this.id = id;
			int range = Game.viewDistance * 2;
			chunks = new Chunk[range, range, range];
			chunks1 = new Chunk[range, range, range];
			position = new DVector(Game.worldSize / 2, Game.worldSize / 2, Game.worldSize / 2);
			playerChunkCoord = DVector.GetCoord(position);
			lastPlayerChunkCoord = new Coord(-1, -1, -1);
			offset = GetOffset(playerChunkCoord);
			newOffset = GetOffset(playerChunkCoord);
			tableRepetitions = Game.viewDistance * 2;
			tableSize = tableRepetitions - 1;
		}
		public bool Check()
		{
			playerChunkCoord = DVector.GetCoord(position);
			if (!playerChunkCoord.Equals(lastPlayerChunkCoord))
			{
				newOffset = GetOffset(playerChunkCoord);
				ChangeTables();
				lastPlayerChunkCoord = DVector.GetCoord(position);
				return false;
			}
			return true;
		}
		public void RequestMissing()
		{
			lock (chunks)
				for (int x = 0; x < tableRepetitions; x++)
				for (int y = 0; y < tableRepetitions; y++)
				for (int z = 0; z < tableRepetitions; z++)
				if (chunks[x, y, z] == null)
					chunksToCreate.Enqueue(new Coord(x + newOffset.x, y + newOffset.y, z + newOffset.z));
		}
		public void ChangeTables()
		{
			lock (chunks)
			{
				for (int x = 0; x < tableRepetitions; x++)
				for (int y = 0; y < tableRepetitions; y++)
				for (int z = 0; z < tableRepetitions; z++)
				{
					if (chunks[x, y, z] != null && !IsInOffset(chunks[x, y, z].chunkCoord))
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
			}
			offset.Set(newOffset);
		}
		public bool IsInOffset(Coord coord)
		{
			Coord result = coord - newOffset;
			if (result.x < 0 || result.y < 0 || result.z < 0 ||
				result.x > tableSize || result.y > tableSize || result.z > tableSize)
				return false;
			else
				return true;
		}
		private DVector GetGraphicOffset()
		{
			return (position / Game.graphicDistance).Floor();
		}
		private Coord GetOffset(Coord playerChunkCoord)
		{
			return playerChunkCoord - Game.viewDistance;
		}
		public void MovePlayer(DVector pos, float xAngle, float yAngle, bool angle = true)
		{
			position += pos;
			if (angle)
			{
				this.xAngle = xAngle;
				this.yAngle = yAngle;
			}
		}
		public void AdjustPosition(DVector graphicOffset)
		{
			player.Position = ToGrapgic(position);
		}
		public Vector3 CursorPosition(Vector3 direction)
		{
			return ToGrapgic(Raycast(position, direction));
		}
		public DVector Raycast(DVector origin, Vector3 direction, float radius = 8f)
		{
			double x = Math.Floor(origin.x);
			double y = Math.Floor(origin.y);
			double z = Math.Floor(origin.z);
			float dx = direction.x;
			float dy = direction.y;
			float dz = direction.z;
			int stepX = Signum(dx);
			int stepY = Signum(dy);
			int stepZ = Signum(dz);
			double tMaxX = Inboud(origin.x, dx);
			double tMaxY = Inboud(origin.y, dy);
			double tMaxZ = Inboud(origin.z, dz);
			float tDeltaX = stepX / dx;
			float tDeltaY = stepY / dy;
			float tDeltaZ = stepZ / dz;
			if (dx == 0 && dy == 0 && dz == 0)
			{
				return new DVector(0, 0, 0);
			}
			radius /= (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
			while (stepX > 0 ? x < Game.worldSize * Game.voxelsInChunk : x >= 0 &&
				   stepY > 0 ? y < Game.worldSize * Game.voxelsInChunk : y >= 0 &&
				   stepZ > 0 ? z < Game.worldSize * Game.voxelsInChunk : z >= 0)
			{
				if (tMaxX < tMaxY)
				{
					if (tMaxX < tMaxZ)
					{
						if (tMaxX > radius) break;
						x += stepX;
						tMaxX += tDeltaX;
					}
					else
					{
						if (tMaxZ > radius) break;
						z += stepZ;
						tMaxZ += tDeltaZ;
					}
				}
				else
				{
					if (tMaxY < tMaxZ)
					{
						if (tMaxY > radius) break;
						y += stepY;
						tMaxY += tDeltaY;
					}
					else
					{
						if (tMaxZ > radius) break;
						z += stepZ;
						tMaxZ += tDeltaZ;
					}
				}
				Vector3 b = BlockInChunk(new DVector(x, y, z));
				Chunk chunk = GetChunk(CoordFromBlock(new DVector(x, y, z)));
				if (chunk != null && chunk.ReadLock())
				{
					if (chunk.voxelMap != null)
					{
						if (chunk.voxelMap[FlatIndex((int)b.x, (int)b.y, (int)b.z)] > 0)
						{
							chunk.ReadUnlock();
							return new DVector(x, y, z);
						}
					}
					chunk.ReadUnlock();
				}
			}
			return new DVector(-1, -1, -1);
		}
		public int FlatIndex(int x, int y, int z)
		{
			return (x * Game.voxelsInChunk * Game.voxelsInChunk) + y * Game.voxelsInChunk + z;
		}
		private static double Inboud(double s, double ds)
		{
			if (ds < 0)
				return Inboud(-s, -ds);
			else
			{
				s = Mod(s, 1);
				return (1 - s) / ds;
			}
		}
		private static int Signum(float x)
		{
			return x > 0 ? 1 : x < 0 ? -1 : 0;
		}
		public static double Mod(double value, double modulus)
		{
			return (value % modulus + modulus) % modulus;
		}
		public static Coord CoordFromBlock(DVector pos)
		{
			return new Coord((int)Math.Floor(pos.x / Game.voxelsInChunk),
							(int)Math.Floor(pos.y / Game.voxelsInChunk),
							(int)Math.Floor(pos.z / Game.voxelsInChunk));
		}
		public static Vector3 BlockInChunk(DVector pos)
		{
			return (pos - (pos / Game.voxelsInChunk).Floor() * Game.voxelsInChunk).ToVector3();
		}
		public Vector3 ToGrapgic(DVector dVector)
		{
			return (dVector - World.graphicOffset * Game.graphicDistance).ToVector3();
		}
		public Chunk GetChunk(Coord coord)
		{
			Coord result = coord - newOffset;
			return chunks[result.x, result.y, result.z];
		}
		public Chunk[] GetChunksForMeshSafe(Coord coord)
		{
			lock (chunks)
			{
				Chunk[] mChunks = new Chunk[6];
				for (int i = 0; i < 6; i++)
				{
					Coord result = coord + Voxel.faceChecks[i] - newOffset;
					if (IsInOffset(coord + Voxel.faceChecks[i]) &&
						chunks[result.x, result.y, result.z] != null &&
						chunks[result.x, result.y, result.z].ReadLock())
						mChunks[i] = chunks[result.x, result.y, result.z];
				}
				return mChunks;
			}
		}
		public Chunk[] GetChunksForMesh(Coord coord)
		{
			lock (chunks)
			{
				Chunk[] mChunks = new Chunk[6];
				for (int i = 0; i < 6; i++)
				{
					Coord result = coord + Voxel.faceChecks[i] - newOffset;
					if (IsInOffset(coord + Voxel.faceChecks[i]) && chunks[result.x, result.y, result.z] != null)
						mChunks[i] = chunks[result.x, result.y, result.z];
				}
				return mChunks;
			}
		}
		public void SetChunk(Chunk chunk, Coord coord)
		{
			Coord result = coord - newOffset;
			chunks[result.x, result.y, result.z] = chunk;
		}
	}
}
