using System;
using Common;

namespace Server
{
	public class Chunk
	{
		public Coord chunkCoord { get; private set; }
		public ushort[] voxelMap;
		//public List<Entity> entities = new List<Entity>();
		private object locker = false;
		private bool isLocked = false;
		public Chunk()
		{
			chunkCoord = new Coord(-1, -1, -1);
			voxelMap = new ushort[Game.voxelsInChunk * Game.voxelsInChunk * Game.voxelsInChunk];
		}
		public int FlatIndex(int x, int y, int z)
		{
			return (x * Game.voxelsInChunk * Game.voxelsInChunk) + y * Game.voxelsInChunk + z;
		}
		public void Initialize()
		{
			for (int x = 0; x < Game.voxelsInChunk; x++)
			for (int y = 0; y < Game.voxelsInChunk; y++)
			for (int z = 0; z < Game.voxelsInChunk; z++)
				if ((chunkCoord.y * Game.voxelsInChunk) + y > Math.Floor(FastNoiseLiteStatic.GetNoise(((chunkCoord.x * Game.voxelsInChunk) + x) + 0.1f, ((chunkCoord.z * Game.voxelsInChunk) + z)) * 10) + Game.worldSize / 2 + 0.1f)
					voxelMap[FlatIndex(x, y, z)] = 0;
				else
					voxelMap[FlatIndex(x, y, z)] = 1;
		}
		/*
		public static bool Get3DPerlin(Vector3 position, float offset, float scale, float threshold)
		{
			return FastNoiseLiteStatic.GetNoise(position.x * scale, position.y * scale, position.z * scale) > threshold;
		}
		*/
		public bool IsLocked()
		{
			lock (locker)
			{
				if (isLocked)
					return true;
				else
					return false;
			}
		}
		public bool Lock()
		{
			lock (locker)
			{
				if (isLocked)
					return false;
				else
				{
					isLocked = true;
					return true;
				}
			}
		}
		public void UnLock()
		{
			lock (locker)
			{
				isLocked = false;
			}
		}
		public void Move(Coord coord)
		{
			chunkCoord = coord;
		}
	}
}
