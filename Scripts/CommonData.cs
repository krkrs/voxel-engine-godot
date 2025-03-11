using System;
using System.IO;
using System.IO.Compression;
using Godot;
using System.Collections.Generic;
using System.Text.Json;

namespace Common
{
	[System.Serializable]
	public class BlockType //0 - air false false, 1 - dirt, true, false
	{
		public string blockName;
		public bool isOpaque;
		public bool connectFaces;
		public int backFaceTexture;
		public int frontFaceTexture;
		public int topFaceTexture;
		public int bottomFaceTexture;
		public int leftFaceTexture;
		public int rightFaceTexture;
		public BlockType(string name, bool isOpaque, bool connectFaces)
		{
			blockName = name; this.isOpaque = isOpaque; this.connectFaces = connectFaces;
		}
		// Back, Front, Top, Bottom, Left, Right
		public int GetTextureID(int faceIndex)
		{
			switch (faceIndex)
			{
				case 0:
					return backFaceTexture;
				case 1:
					return frontFaceTexture;
				case 2:
					return topFaceTexture;
				case 3:
					return bottomFaceTexture;
				case 4:
					return leftFaceTexture;
				case 5:
					return rightFaceTexture;
				default:
					GD.Print("Error in GetTextureID; invalid face index");
					return 0;
			}
		}
	}
	public struct Coord
	{
		public int x, y, z;
		public Coord(int x, int y, int z)
		{
			this.x = x;
			this.y = y;
			this.z = z;
		}
		public void Set(Coord coord)
		{
			x = coord.x;
			y = coord.y;
			z = coord.z;
		}
		public bool Equals(Coord other)
		{
			if (other.x != x || other.y != y || other.z != z)
				return false;
			else
				return true;
		}
		public static Coord operator- (Coord a, Coord b)
		{
			a.x -= b.x; a.y -= b.y; a.z -= b.z; return a;
		}
		public static Coord operator- (Coord a, Vector3 b)
		{
			a.x -= (int)b.x; a.y -= (int)b.y; a.z -= (int)b.z; return a;
		}
		public static Coord operator+ (Coord a, Vector3 b)
		{
			a.x += (int)b.x; a.y += (int)b.y; a.z += (int)b.z; return a;
		}
		public static Coord operator- (Coord a, int b)
		{
			a.x -= b; a.y -= b; a.z -= b; return a;
		}
		public static Coord operator- (Coord a, DVector b)
		{
			a.x -= (int)b.x; a.y -= (int)b.y; a.z -= (int)b.z; return a;
		}
		public static Coord operator* (Coord a, int b)
		{
			a.x *= b; a.y *= b; a.z *= b; return a;
		}
		public Vector3 ToVector3()
		{
			return new Vector3(x, y, z);
		}
	}
	public struct DVector
	{
		/*If there are any issues with precision
		  remember... there is also decimal ( ͡° ͜ʖ ͡°)*/
		public double x, y, z;
		public DVector(double x, double y, double z)
		{
			this.x = x; this.y = y; this.z = z;
		}
		public static DVector FromVector(Vector3 pos)
		{
			return new DVector(pos.x, pos.y, pos.z);
		}
		public bool Equals(DVector dVector)
		{
			if (dVector.x == x && dVector.y == y && dVector.z == z)
				return true;
			else
				return false;
		}
		public Vector3 ToVector3()
		{
			return new Vector3((float)x, (float)y, (float)z);
		}
		public static Coord GetCoord(DVector pos)
		{
			return new Coord((int)Math.Floor(pos.x / Game.voxelsInChunk), (int)Math.Floor(pos.y / Game.voxelsInChunk), (int)Math.Floor(pos.z / Game.voxelsInChunk));
		}
		public DVector Floor()
		{
			x = Math.Floor(x); y = Math.Floor(y); z = Math.Floor(z); return this;
		}
		public static DVector operator+ (DVector a, DVector b)
		{
			a.x += b.x; a.y += b.y; a.z += b.z; return a;
		}
		public static DVector operator- (DVector a, DVector b)
		{
			a.x -= b.x; a.y -= b.y; a.z -= b.z; return a;
		}
		public static DVector operator- (DVector a, int b)
		{
			a.x -= b; a.y -= b; a.z -= b; return a;
		}
		public static DVector operator* (DVector a, double b)
		{
			a.x *= b; a.y *= b; a.z *= b; return a;
		}
		public static DVector operator/ (DVector a, double b)
		{
			a.x /= b; a.y /= b; a.z /= b; return a;
		}
	}
	public enum Packet
	{
		Settings,
		WorldInitData,
		StateUpdate,
		Move,
		ChunkData,
		ChunkRequest,
		Entity
	}
	public enum Job
	{
		Suspended,
		Make,
		CreateMesh,
		Skip
	}
	public class Message
	{
		private byte[] message;
		private int pointer;
		public void NewMessage(Packet packetType, int size)
		{
			message = new byte[size];
			message[0] = (byte)packetType;
			pointer = 1;
		}
		public byte[] Compress()
		{
			MemoryStream output = new MemoryStream();
			using (DeflateStream dstream = new DeflateStream(output, System.IO.Compression.CompressionLevel.Optimal))
			{
				dstream.Write(message, 0, message.Length);
			}
			return output.ToArray();
		}
		public static byte[] Decompress(byte[] data)
		{
			MemoryStream input = new MemoryStream(data);
			MemoryStream output = new MemoryStream();
			using (DeflateStream dstream = new DeflateStream(input, CompressionMode.Decompress))
			{
				dstream.CopyTo(output);
			}
			return output.ToArray();
		}
		public Packet Type(byte[] array)
		{
			pointer = 1;
			message = array;
			return (Packet)array[0];
		}
		public bool Is(Packet packet)
		{
			if (message[0] == (byte)packet)
				return true;
			else
				return false;
		}
		#region Read
		public ushort ReadByte()
		{
			byte value = message[pointer];
			pointer++;
			return value;
		}
		public ushort ReadUShort()
		{
			ushort value = BitConverter.ToUInt16(message, pointer);
			pointer += 2;
			return value;
		}
		public int ReadInt()
		{
			int value = BitConverter.ToInt32(message, pointer);
			pointer += 4;
			return value;
		}
		public double ReadDouble()
		{
			double value = BitConverter.ToDouble(message, pointer);
			pointer += 8;
			return value;
		}
		public DVector ReadDVector()
		{
			DVector value = new DVector(BitConverter.ToDouble(message, pointer),
										BitConverter.ToDouble(message, pointer + 8),
										BitConverter.ToDouble(message, pointer + 16));
			pointer += 24;
			return value;
		}
		public Guid ReadGuid()
		{
			byte[] t = new byte[16];
			Buffer.BlockCopy(message, pointer, t, 0, 16);
			pointer += 16;
			return new Guid(t);
		}
		#endregion
		#region Write
		public void WriteBuffer(ushort[] value, int srcOffset, int count)
		{
			Buffer.BlockCopy(value, srcOffset, message, pointer, count);
			pointer += count;
		}
		public void WriteByte(byte value)
		{
			message[pointer] = value;
			pointer++;
		}
		public void WriteUShort(short value)
		{
			byte[] tmp = BitConverter.GetBytes(value);
			message[pointer] = tmp[0];
			message[pointer + 1] = tmp[1];
			pointer += 2;
		}
		public void WriteInt(int value)
		{
			byte[] tmp = BitConverter.GetBytes(value);
			message[pointer] = tmp[0];
			message[pointer + 1] = tmp[1];
			message[pointer + 2] = tmp[2];
			message[pointer + 3] = tmp[3];
			pointer += 4;
		}
		public void WriteDouble(double value)
		{
			byte[] tmp = BitConverter.GetBytes(value);
			message[pointer] = tmp[0];
			message[pointer + 1] = tmp[1];
			message[pointer + 2] = tmp[2];
			message[pointer + 3] = tmp[3];
			message[pointer + 4] = tmp[4];
			message[pointer + 5] = tmp[5];
			message[pointer + 6] = tmp[6];
			message[pointer + 7] = tmp[7];
			pointer += 8;
		}
		public void WriteDVector(DVector value)
		{
			WriteDouble(value.x);
			WriteDouble(value.y);
			WriteDouble(value.z);
		}
		public void WriteGuid(Guid value)
		{
			Buffer.BlockCopy(value.ToByteArray(), 0, message, pointer, 16);
			pointer += 16;
		}
		#endregion
	}
	public class OneFrameUpdate
	{
		public virtual void Update(){}
	}
	public static class ThreadAllocation
	{
		public static uint ServerThreads = (uint)((System.Environment.ProcessorCount - 2) );//* 0.2);
		public static uint GameThreads = (uint)((System.Environment.ProcessorCount - 2) );//* 0.8);
		//public static uint GameThreads = (uint)((System.Environment.ProcessorCount - 2) * 0.5);
	}
	public class Data
	{
		public int worldSize { get; set; }
		public int viewDistance { get; set; }
		public int voxelsInChunk { get; set; }
	}
 	public static class Game
	{
		public static List<Common.OneFrameUpdate> updates = new List<Common.OneFrameUpdate>();
		public static World world;
		public static int worldSize;
		public static int viewDistance;
		public static int voxelsInChunk;
		public static readonly int graphicDistance = 100;
		public static readonly int frequency = 50;
		public static void Save()
		{
			var data = new Data
			{
				worldSize = Game.worldSize,
				viewDistance = Game.viewDistance,
				voxelsInChunk = Game.voxelsInChunk
			};
			System.IO.File.WriteAllText("./ustawienia", JsonSerializer.Serialize(data));
		}
		public static void Read()
		{
			var data = JsonSerializer.Deserialize<Data>(System.IO.File.ReadAllText("./ustawienia"));
			Game.worldSize = data.worldSize;
			Game.viewDistance = data.viewDistance;
			Game.voxelsInChunk = data.voxelsInChunk;
		}
	}
}
