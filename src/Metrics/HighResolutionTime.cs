using System.Diagnostics;

namespace C12ProjetoCiv.Metrics;

public static class HighResolutionTime
{
    public static double NowSeconds => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;

    public static double ElapsedMilliseconds(long startedAtTimestamp)
    {
        return Stopwatch.GetElapsedTime(startedAtTimestamp).TotalMilliseconds;
    }
}
