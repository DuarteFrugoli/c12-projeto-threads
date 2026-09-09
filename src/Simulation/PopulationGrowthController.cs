using C12ProjetoCiv.Core;

namespace C12ProjetoCiv.Simulation;

public sealed class PopulationGrowthController
{
    private readonly SimulationConfig _config;
    private bool _blockedByLowFps;
    private double _recoverySeconds;

    public PopulationGrowthController(SimulationConfig config)
    {
        _config = config;
    }

    public PopulationGrowthState State { get; private set; } = PopulationGrowthState.Enabled;
    public bool CanGrow => State == PopulationGrowthState.Enabled;
    public double RecoveryProgress => Math.Clamp(
        _recoverySeconds / _config.RecoveryDurationSeconds,
        0,
        1);

    public void Update(
        double averageFps,
        bool fpsWindowReady,
        double deltaSeconds,
        bool pausedByUser,
        bool benchmarkMode,
        bool populationLimitReached)
    {
        if (!pausedByUser && !benchmarkMode && fpsWindowReady)
        {
            UpdateLowFpsBlock(averageFps, deltaSeconds);
        }

        State = pausedByUser
            ? PopulationGrowthState.PausedByUser
            : benchmarkMode
                ? PopulationGrowthState.DisabledForBenchmark
                : populationLimitReached
                    ? PopulationGrowthState.BlockedByPopulationLimit
                    : _blockedByLowFps
                        ? PopulationGrowthState.BlockedByLowFps
                        : PopulationGrowthState.Enabled;
    }

    public void Reset()
    {
        _blockedByLowFps = false;
        _recoverySeconds = 0;
        State = PopulationGrowthState.Enabled;
    }

    private void UpdateLowFpsBlock(double averageFps, double deltaSeconds)
    {
        if (averageFps <= _config.LowFpsThreshold)
        {
            _blockedByLowFps = true;
            _recoverySeconds = 0;
            return;
        }

        if (!_blockedByLowFps)
        {
            return;
        }

        if (averageFps < _config.RecoveryFpsThreshold)
        {
            _recoverySeconds = 0;
            return;
        }

        _recoverySeconds += Math.Max(0, deltaSeconds);

        if (_recoverySeconds >= _config.RecoveryDurationSeconds)
        {
            _blockedByLowFps = false;
            _recoverySeconds = 0;
        }
    }
}
