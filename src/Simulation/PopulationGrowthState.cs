namespace C12ProjetoCiv.Simulation;

public enum PopulationGrowthState
{
    Enabled,
    BlockedByLowFps,
    BlockedByPopulationLimit,
    PausedByUser,
    DisabledForBenchmark,
}
