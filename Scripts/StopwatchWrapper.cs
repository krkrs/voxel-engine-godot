using System.Diagnostics;

public static class Timer
{
	public static Stopwatch stopwatch = new Stopwatch();
	public static void Start()
	{
		stopwatch.Start();
	}
	public static void Stop()
	{
		stopwatch.Stop();
	}
	public static void Restart()
	{
		stopwatch.Restart();
	}
	public static int ElapsedMilliseconds()
	{
		return (int)stopwatch.ElapsedMilliseconds;
	}
}
public static class Clock
{
	private static Stopwatch stopwatch = new Stopwatch();
	public static void Start()
	{
		stopwatch.Start();
	}
	public static long ElapsedMilliseconds()
	{
		return stopwatch.ElapsedMilliseconds;
	}
}
