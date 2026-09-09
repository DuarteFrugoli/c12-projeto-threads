namespace C12ProjetoCiv.Core;

public sealed record SimulationConfig
{
    public int WindowWidth { get; init; } = 1280;
    public int WindowHeight { get; init; } = 800;
    public int HeaderHeight { get; init; } = 175;
    public string WindowTitle { get; init; } = "Simulação de Civilizações — Multithreading";

    public int Seed { get; init; } = 12062026;
    public int CivilizationCount { get; init; } = 4;
    public int InitialPopulationPerCivilization { get; init; } = 10;
    public int ResourceNodesPerCivilization { get; init; } = 768;
    public int MaxPopulationPerCivilization { get; init; } = 5_000;

    public int AgentCost { get; init; } = 10;
    public int ResourceUnitsPerDelivery { get; init; } = 1;
    public float AgentSpeedPixelsPerSecond { get; init; } = 80f;
    public float AgentRadius { get; init; } = 3f;
    public float ArrivalDistance { get; init; } = 2f;
    public double CollectionDurationSeconds { get; init; } = 0.25;
    public double NaturalSpawnIntervalSeconds { get; init; } = 0.25;
    public double StressSpawnIntervalSeconds { get; init; } = 0.05;
    public int StressSpawnBatchPerCivilization { get; init; } = 2;

    public double FpsWindowSeconds { get; init; } = 1.0;
    public double LowFpsThreshold { get; init; } = 30.0;
    public double RecoveryFpsThreshold { get; init; } = 35.0;
    public double RecoveryDurationSeconds { get; init; } = 2.0;
    public double WorkerChangeWarmupSeconds { get; init; } = 2.0;
    public int MetricHistoryCapacity { get; init; } = 600;

    public int MaxPopulationTotal => CivilizationCount * MaxPopulationPerCivilization;

    public void Validate()
    {
        if (CivilizationCount != 4)
        {
            throw new InvalidOperationException("Esta demonstração exige exatamente quatro civilizações.");
        }

        if (WindowWidth <= 0 || WindowHeight <= HeaderHeight)
        {
            throw new InvalidOperationException("As dimensões da janela são inválidas.");
        }

        if (InitialPopulationPerCivilization < 0 ||
            InitialPopulationPerCivilization > MaxPopulationPerCivilization)
        {
            throw new InvalidOperationException("A população inicial está fora do limite permitido.");
        }

        if (ResourceNodesPerCivilization <= 0)
        {
            throw new InvalidOperationException("Cada civilização precisa ter ao menos um ponto de recurso.");
        }

        if (LowFpsThreshold <= 0 || RecoveryFpsThreshold <= LowFpsThreshold)
        {
            throw new InvalidOperationException("Os limites de FPS precisam possuir histerese válida.");
        }

        if (MetricHistoryCapacity <= 0 || FpsWindowSeconds <= 0)
        {
            throw new InvalidOperationException("A configuração das métricas é inválida.");
        }
    }
}
