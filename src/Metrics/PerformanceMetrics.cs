using C12ProjetoCiv.Core;

namespace C12ProjetoCiv.Metrics;

public sealed class PerformanceMetrics
{
    private readonly SimulationConfig _config;
    private readonly Queue<FrameSample> _frames = new(2_048);
    private readonly Queue<double> _updates = new(2_048);
    private readonly RollingValueWindow _simulationTimes;
    private readonly RollingValueWindow _renderTimes;
    private double _frameDurationSumSeconds;
    private double _resetAtSeconds;
    private double _lastTimestampSeconds;
    private double _nextSummaryAtSeconds;

    public PerformanceMetrics(SimulationConfig config)
    {
        _config = config;
        _simulationTimes = new RollingValueWindow(config.MetricHistoryCapacity);
        _renderTimes = new RollingValueWindow(config.MetricHistoryCapacity);
        Reset(HighResolutionTime.NowSeconds);
    }

    public double AverageFps { get; private set; }
    public double AverageFrameMilliseconds { get; private set; }
    public double UpdatesPerSecond { get; private set; }
    public double LastSimulationMilliseconds { get; private set; }
    public double AverageSimulationMilliseconds => _simulationTimes.Average;
    public double SimulationP50Milliseconds { get; private set; }
    public double SimulationP95Milliseconds { get; private set; }
    public double LastRenderMilliseconds { get; private set; }
    public double AverageRenderMilliseconds => _renderTimes.Average;

    public bool IsFpsWindowReady =>
        _lastTimestampSeconds - _resetAtSeconds >= _config.FpsWindowSeconds;

    public bool IsWarmupComplete =>
        _lastTimestampSeconds - _resetAtSeconds >= _config.WorkerChangeWarmupSeconds;

    public double WarmupRemainingSeconds => Math.Max(
        0,
        _config.WorkerChangeWarmupSeconds - (_lastTimestampSeconds - _resetAtSeconds));

    public void RecordSimulation(double milliseconds)
    {
        LastSimulationMilliseconds = milliseconds;
        _simulationTimes.Add(milliseconds);
    }

    public void RecordFrame(
        double completedAtSeconds,
        double frameDurationSeconds,
        double renderMilliseconds,
        bool simulationWasUpdated)
    {
        _lastTimestampSeconds = completedAtSeconds;
        LastRenderMilliseconds = renderMilliseconds;
        _renderTimes.Add(renderMilliseconds);
        _frames.Enqueue(new FrameSample(completedAtSeconds, frameDurationSeconds));
        _frameDurationSumSeconds += frameDurationSeconds;

        if (simulationWasUpdated)
        {
            _updates.Enqueue(completedAtSeconds);
        }

        PruneOldSamples(completedAtSeconds);
        RecalculateRates(completedAtSeconds);

        if (completedAtSeconds >= _nextSummaryAtSeconds)
        {
            SimulationP50Milliseconds = _simulationTimes.CalculatePercentile(0.50);
            SimulationP95Milliseconds = _simulationTimes.CalculatePercentile(0.95);
            _nextSummaryAtSeconds = completedAtSeconds + 0.25;
        }
    }

    public void Reset(double nowSeconds)
    {
        _frames.Clear();
        _updates.Clear();
        _simulationTimes.Clear();
        _renderTimes.Clear();
        _frameDurationSumSeconds = 0;
        _resetAtSeconds = nowSeconds;
        _lastTimestampSeconds = nowSeconds;
        _nextSummaryAtSeconds = nowSeconds;
        AverageFps = 0;
        AverageFrameMilliseconds = 0;
        UpdatesPerSecond = 0;
        LastSimulationMilliseconds = 0;
        SimulationP50Milliseconds = 0;
        SimulationP95Milliseconds = 0;
        LastRenderMilliseconds = 0;
    }

    private void PruneOldSamples(double nowSeconds)
    {
        double cutoff = nowSeconds - _config.FpsWindowSeconds;

        while (_frames.Count > 0 && _frames.Peek().CompletedAtSeconds < cutoff)
        {
            _frameDurationSumSeconds -= _frames.Dequeue().DurationSeconds;
        }

        while (_updates.Count > 0 && _updates.Peek() < cutoff)
        {
            _updates.Dequeue();
        }
    }

    private void RecalculateRates(double nowSeconds)
    {
        double observedSeconds = Math.Min(
            _config.FpsWindowSeconds,
            Math.Max(0.000_001, nowSeconds - _resetAtSeconds));

        AverageFps = _frames.Count / observedSeconds;
        AverageFrameMilliseconds = _frames.Count == 0
            ? 0
            : (_frameDurationSumSeconds / _frames.Count) * 1_000.0;
        UpdatesPerSecond = _updates.Count / observedSeconds;
    }

    private readonly record struct FrameSample(double CompletedAtSeconds, double DurationSeconds);
}
