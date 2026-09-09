using System.Diagnostics;

namespace C12ProjetoCiv.Simulation;

/// <summary>
/// Cronômetro real da execução ativa. Não representa um relógio fictício de jogo.
/// </summary>
public sealed class SimulationClock
{
    private readonly Stopwatch _activeStopwatch = new();
    private double _lastSampleSeconds;

    public SimulationClock()
    {
        Reset(startImmediately: true);
    }

    public bool IsRunning => _activeStopwatch.IsRunning;
    public double ElapsedSeconds => _activeStopwatch.Elapsed.TotalSeconds;

    public double SampleDeltaSeconds()
    {
        double current = ElapsedSeconds;

        if (!IsRunning)
        {
            _lastSampleSeconds = current;
            return 0;
        }

        double delta = Math.Max(0, current - _lastSampleSeconds);
        _lastSampleSeconds = current;
        return delta;
    }

    public void Pause()
    {
        if (!IsRunning)
        {
            return;
        }

        _activeStopwatch.Stop();
        _lastSampleSeconds = ElapsedSeconds;
    }

    public void Resume()
    {
        if (IsRunning)
        {
            return;
        }

        _lastSampleSeconds = ElapsedSeconds;
        _activeStopwatch.Start();
    }

    public void Reset(bool startImmediately)
    {
        _activeStopwatch.Reset();
        _lastSampleSeconds = 0;

        if (startImmediately)
        {
            _activeStopwatch.Start();
        }
    }

    public void ResetDeltaSample()
    {
        _lastSampleSeconds = ElapsedSeconds;
    }
}
