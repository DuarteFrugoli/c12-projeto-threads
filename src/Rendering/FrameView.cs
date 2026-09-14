using C12ProjetoCiv.Concurrency;
using C12ProjetoCiv.Core;
using C12ProjetoCiv.Diagnostics;
using C12ProjetoCiv.Metrics;
using C12ProjetoCiv.Simulation;

namespace C12ProjetoCiv.Rendering;

/// <summary>
/// Tudo o que a thread principal precisa para desenhar um frame.
/// </summary>
public readonly record struct FrameView(
    SimulationState State,
    SimulationConfig Config,
    PerformanceMetrics Metrics,
    PopulationGrowthController GrowthController,
    SimulationCoordinator Coordinator,
    ConflictArbiter? Arbiter,
    MineMetrics MineMetrics,
    IReadOnlyList<ThreadTimeline> Timelines,
    ScalingBenchmark? ScalingView,
    double ElapsedSeconds,
    bool Paused,
    bool StressMode,
    bool Fullscreen);
