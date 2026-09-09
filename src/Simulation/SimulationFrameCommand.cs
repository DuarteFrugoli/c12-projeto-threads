namespace C12ProjetoCiv.Simulation;

public readonly record struct SimulationFrameCommand(
    double DeltaSeconds,
    double ElapsedSeconds,
    bool AllowPopulationGrowth,
    bool StressModeEnabled);
