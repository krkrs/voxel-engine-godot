using System;
using System.Threading;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Common;
using Godot;

namespace Client 
{
	public class Worker
	{
		#region Thread
		private System.Threading.Thread thread;
		private AutoResetEvent trigger = new AutoResetEvent(false);
		#endregion
		#region Chunk
		public Job job { get; private set; }
		public Chunk chunk { get; private set; }
		private ushort[] voxelMap;
		#endregion
		#region Mesh
		private BlockType[] blocks;
		private World world;
		private ConcurrentQueue<Worker> readyWorkers;
		public bool applyMesh { get; private set; } = false;
		private SurfaceTool st = new SurfaceTool();
		#endregion
		#region Cache
		private int intX, intY, intZ;
		private Chunk[] chunks = new Chunk[6];
		private bool loop = true;
		#endregion
		public Worker(World world, ConcurrentQueue<Worker> workers)
		{
			st.Begin(Mesh.PrimitiveType.Triangles);
			blocks = world.blocks;
			this.world = world;
			this.readyWorkers = workers;
			thread = new System.Threading.Thread(Loop);
			thread.Start();
		}
		private void Loop()
		{
			while (loop)
			{
				trigger.WaitOne();
				if (job == Job.CreateMesh)
				{
					CreateMeshData();
					readyWorkers.Enqueue(this);
					continue;
				}
				if (job == Job.Make)
				{
					if (!ExistOnDrive(chunk.chunkCoord))
					{
						chunk.lastModTime = Clock.ElapsedMilliseconds();
						chunk.Initialize();
						CreateMeshData();
						readyWorkers.Enqueue(this);
					}
					continue;
				}
			}
		}
		public int FlatIndex(int x, int y, int z)
		{
			return (x * Game.voxelsInChunk * Game.voxelsInChunk) + y * Game.voxelsInChunk + z;
		}
		void CreateMeshData()
		{
			chunks = world.GetChunkForMeshSafe(chunk.chunkCoord);
			applyMesh = false;
			foreach (Chunk chunk in chunks)
				if (chunk == null)
				{
					ReadUnlock();
					return;
				}
			int vertexIndex = 0;
			if ((chunk.lastMeshTime < chunk.lastModTime || chunk.lastMeshTime < chunks[0].lastModTime ||
				 chunk.lastMeshTime < chunks[1].lastModTime || chunk.lastMeshTime < chunks[2].lastModTime ||
				 chunk.lastMeshTime < chunks[3].lastModTime || chunk.lastMeshTime < chunks[4].lastModTime ||
				 chunk.lastMeshTime < chunks[5].lastModTime))
			{
				chunk.lastMeshTime = Clock.ElapsedMilliseconds();
				int opaqueTrianglesCounter = 0;
				int transparentTrianglesCounter = 0;
				
				//st.Clear();
				st.Begin(Mesh.PrimitiveType.Triangles);
				st.SetMaterial(main.material);
				for (int x = 0; x < Game.voxelsInChunk; x++)
				for (int y = 0; y < Game.voxelsInChunk; y++)
				for (int z = 0; z < Game.voxelsInChunk; z++)
				{
					if (voxelMap[FlatIndex(x,y,z)] == 0)
						continue;

					for (int p = 0; p < 6; p++)
					{
						//If can source -> If !IsOpaque -> If !ConnectFaces -> If !IsExcluded -> If current IsOpaque -> Draw opaque
						//      \/               \/               \/                 \/                   \/
						//    Nothing          Nothing          Nothing            Nothing         Draw transparent
						Vector3 pos = new Vector3(x, y, z);
						if (IsOpaque(pos + Voxel.faceChecks[p]))
							continue;

						st.SetColor(new Color(1,0,1));
						st.AddVertex(pos + Voxel.voxelVerts[Voxel.voxelTris[p, 0]]);
						st.SetColor(new Color(0, 1, 0));
						st.AddVertex(pos + Voxel.voxelVerts[Voxel.voxelTris[p, 1]]);
						st.SetColor(new Color(1,0,1));
						st.AddVertex(pos + Voxel.voxelVerts[Voxel.voxelTris[p, 2]]);
						st.SetColor(new Color(0, 1, 0));
						st.AddVertex(pos + Voxel.voxelVerts[Voxel.voxelTris[p, 3]]);
						
						/*int textureID = blocks[voxelMap[FlatIndex(pos.x, pos.y, pos.z, Game.voxelsInChunk)]].GetTextureID(p);
						float floatY = textureID / Voxel.TextureAtlasSizeInBlocks;
						float floatX = textureID - (floatY * Voxel.TextureAtlasSizeInBlocks);
						floatX *= Voxel.NormalizedBlockTextureSize;
						floatY *= Voxel.NormalizedBlockTextureSize;
						uvsArr[vertexIndex] = new Vector2(floatX, floatY);
						uvsArr[vertexIndex + 1] = new Vector2(floatX, floatY + Voxel.NormalizedBlockTextureSize);
						uvsArr[vertexIndex + 2] = new Vector2(floatX + Voxel.NormalizedBlockTextureSize, floatY);
						uvsArr[vertexIndex + 3] = new Vector2(floatX + Voxel.NormalizedBlockTextureSize, floatY + Voxel.NormalizedBlockTextureSize);*/
						if (IsOpaque(pos))
						{
							// org order 0 1 2 2 1 3
							st.AddIndex(vertexIndex + 2);
							st.AddIndex(vertexIndex + 1);
							st.AddIndex(vertexIndex);
							st.AddIndex(vertexIndex + 3);
							st.AddIndex(vertexIndex + 1);
							st.AddIndex(vertexIndex + 2);

							/*opTrArr[opaqueTrianglesCounter] = vertexIndex;
							opTrArr[opaqueTrianglesCounter + 1] = vertexIndex + 1;
							opTrArr[opaqueTrianglesCounter + 2] = vertexIndex + 2;
							opTrArr[opaqueTrianglesCounter + 3] = vertexIndex + 2;
							opTrArr[opaqueTrianglesCounter + 4] = vertexIndex + 1;
							opTrArr[opaqueTrianglesCounter + 5] = vertexIndex + 3;*/

							opaqueTrianglesCounter += 6;
						}
						else
						{/*
							trTrArr[transparentTrianglesCounter] = vertexIndex;
							trTrArr[transparentTrianglesCounter + 1] = vertexIndex + 1;
							trTrArr[transparentTrianglesCounter + 2] = vertexIndex + 2;
							trTrArr[transparentTrianglesCounter + 3] = vertexIndex + 2;
							trTrArr[transparentTrianglesCounter + 4] = vertexIndex + 1;
							trTrArr[transparentTrianglesCounter + 5] = vertexIndex + 3;
							transparentTrianglesCounter += 6;*/
						}
						vertexIndex += 4;
					}
				}
				if (opaqueTrianglesCounter > 0)
				{
					st.GenerateNormals();
					chunk.mesh.Mesh = st.Commit();
					applyMesh = true;
				}
				if (transparentTrianglesCounter > 0)
				{
					//trTrArr = new int[transparentTrianglesCounter];
					//Array.Copy(trTrArr, trTrOut, transparentTrianglesCounter);
					applyMesh = true;
				}
			}
			ReadUnlock();
		}
		private void ReadUnlock()
		{
			for (int j = 0; j < 6; j++)
				if (chunks[j] != null)
					chunks[j].ReadUnlock();
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private bool IsOpaque(Vector3 pos)
		{
			intX = (int)pos.x; intY = (int)pos.y; intZ = (int)pos.z;
			if (intX > -1 && intX < Game.voxelsInChunk &&
				intY > -1 && intY < Game.voxelsInChunk &&
				intZ > -1 && intZ < Game.voxelsInChunk)
			return blocks[voxelMap[FlatIndex(intX,intY,intZ)]].isOpaque;

			if (intZ < 0 && chunks[0] != null)
				return blocks[chunks[0].voxelMap[FlatIndex(intX, intY, Game.voxelsInChunk - 1)]].isOpaque;
			else if (intZ == Game.voxelsInChunk && chunks[1] != null)
				return blocks[chunks[1].voxelMap[FlatIndex(intX,intY,0)]].isOpaque;
			else if (intX < 0 && chunks[4] != null)
				return blocks[chunks[4].voxelMap[FlatIndex(Game.voxelsInChunk - 1,intY,intZ)]].isOpaque;
			else if (intX == Game.voxelsInChunk && chunks[5] != null)
				return blocks[chunks[5].voxelMap[FlatIndex(0,intY,intZ)]].isOpaque;
			else if (intY < 0 && chunks[3] != null)
				return blocks[chunks[3].voxelMap[FlatIndex(intX,Game.voxelsInChunk - 1,intZ)]].isOpaque;
			else if (intY == Game.voxelsInChunk && chunks[2] != null)
				return blocks[chunks[2].voxelMap[FlatIndex(intX,0,intZ)]].isOpaque;
			else
				return false;
			
		}
		private bool ExistOnDrive(Coord coord)
		{
			return false;
		}
		public void Trigger(ChunkJob chunkJob)
		{
			job = chunkJob.job;
			chunk = chunkJob.chunk;
			voxelMap = chunk.voxelMap;
			trigger.Set();
		}
		public void Abort()
		{
			loop = false;
			job = Job.Skip;
			trigger.Set();
		}
	}
}