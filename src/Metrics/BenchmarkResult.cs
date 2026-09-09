namespace C12ProjetoCiv.Metrics;

public readonly record struct BenchmarkResult(
    int WorkerCount,
    double MedianMilliseconds,
    double P95Milliseconds,
    double Speedup,
    int SampleCount);
