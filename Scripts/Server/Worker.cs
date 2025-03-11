using System;
using System.Threading;
using System.Collections.Concurrent;
using Common;

namespace Server
{
	public class Worker
	{
		private ConcurrentQueue<Worker> readyWorkers;
		private System.Threading.Thread thread;
		private AutoResetEvent trigger = new AutoResetEvent(false);
		public Job job { get; private set; }
		public Chunk chunk { get; private set; }
		private bool loop = true;
		public Worker(ConcurrentQueue<Worker> readyWorkers)
		{
			this.readyWorkers = readyWorkers;
			thread = new System.Threading.Thread(Loop);
			thread.Start();
		}
		private void Loop()
		{
			while (loop)
			{
				trigger.WaitOne();
				if (job == Job.Make)
				{
					if (!ExistOnDrive(chunk.chunkCoord))
					{
						chunk.Initialize();
						readyWorkers.Enqueue(this);
					}
				}
			}
		}
		private bool ExistOnDrive(Coord coord)
		{
			return false;
		}
		public void Trigger(ChunkJob chunkJob)
		{
			job = chunkJob.job;
			chunk = chunkJob.chunk;
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
