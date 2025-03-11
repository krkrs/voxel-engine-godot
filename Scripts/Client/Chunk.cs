using System;
using System.Collections.Generic;
using Common;
using Godot;

namespace Client
{
	public class Chunk
	{
		public MeshInstance3D mesh = new MeshInstance3D();
		//public Transform transform;
		public ushort[] voxelMap;
		public List<Entity> entities = new List<Entity>();
		private object locker = false;
		private bool writeLock = false;
		private int readLock = 0;
		public byte[] data;
		public long lastMeshTime = 0;
		public long lastModTime = 0;

		public Coord chunkCoord { get; private set; } = new Coord(-1, -1, -1);
		public Chunk(Node parent)
		{
			parent.AddChild(mesh);
			voxelMap = new ushort[Game.voxelsInChunk * Game.voxelsInChunk * Game.voxelsInChunk];
		}
		public void SetMesh()
		{
			Show();
		}
		public void Initialize()
		{
			Buffer.BlockCopy(data, 13, voxelMap, 0, Game.voxelsInChunk * Game.voxelsInChunk * Game.voxelsInChunk * 2);
		}
		#region Locks
		public bool IsLocked()
		{
			lock (locker)
				if (writeLock || readLock > 0)
					return true;
				else
					return false;
		}
		public bool WriteLock()
		{
			lock (locker)
				if (writeLock)
					return false;
				else
				{
					writeLock = true;
					return true;
				}
		}
		public void WriteUnlock()
		{
			lock (locker)
				writeLock = false;
		}
		public bool ReadLock()
		{
			lock (locker)
				if (writeLock)
					return false;
				else
				{
					readLock++;
					return true;
				}
		}
		public void ReadUnlock()
		{
			lock (locker)
				readLock--;
		}
		#endregion
		public void SetPos(DVector graphicOffset)
		{
			mesh.Position = (chunkCoord * Game.voxelsInChunk - graphicOffset * Game.graphicDistance).ToVector3();
		}
		public void Move(Coord coord)
		{
			chunkCoord = coord;
		}
		public void Hide()
		{
			mesh.Visible = false;
		}
		public void Show()
		{
			mesh.Visible = true;
		}
	}
}