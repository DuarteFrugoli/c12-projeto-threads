using C12ProjetoCiv.Concurrency;

namespace C12ProjetoCiv.Core;

public sealed record SimulationConfig
{
    public int WindowWidth { get; init; } = 1600;
    public int WindowHeight { get; init; } = 900;
    public int HeaderHeight { get; init; } = 175;
    public int SidePanelWidth { get; init; } = 400;
    public string WindowTitle { get; init; } = "Simulação de Civilizações — Multithreading";

    public int Seed { get; init; } = 12062026;
    public int CivilizationCount { get; init; } = 4;
    public int InitialPopulationPerCivilization { get; init; } = 10;
    public int ResourceNodesPerCivilization { get; init; } = 768;
    public int MaxPopulationPerCivilization { get; init; } = 7_000;

    public int AgentCost { get; init; } = 10;
    public int ResourceUnitsPerDelivery { get; init; } = 1;
    public int MaxAgentsPerResourceRoute { get; init; } = 10;
    public float ResourceOccupancyWeight { get; init; } = 2f;
    public float ResourceDistanceWeight { get; init; } = 0.35f;
    public float ResourcePreferenceWeight { get; init; } = 1f;
    public float AgentSpeedPixelsPerSecond { get; init; } = 80f;
    public float AgentRadius { get; init; } = 3f;
    public float ArrivalDistance { get; init; } = 2f;
    public int BaseDropOffColumns { get; init; } = 16;
    public int BaseDropOffRows { get; init; } = 5;
    public float BaseDropOffWidth { get; init; } = 104f;
    public float BaseDropOffHeight { get; init; } = 36f;
    public double CollectionDurationSeconds { get; init; } = 0.25;
    public double NaturalSpawnIntervalSeconds { get; init; } = 0.05;
    public double StressSpawnIntervalSeconds { get; init; } = 0.05;
    public int StressSpawnBatchPerCivilization { get; init; } = 2;

    public bool ContestedMineEnabled { get; init; } = true;
    public MineSyncMode MineSyncMode { get; init; } = MineSyncMode.Lock;
    public int MineTripInterval { get; init; } = 5;
    public int MineCapacity { get; init; } = 40_000;
    public double MineRegenerationPerSecond { get; init; } = 5_000;
    public double MineExtractionsPerSecond { get; init; } = 8;
    public int MineCarryCapacity { get; init; } = 6;
    public double MineMaxWaitSeconds { get; init; } = 2.5;
    public int MineYieldComputationIterations { get; init; } = 2_000;
    public bool MineBattlesEnabled { get; init; } = true;
    public double MineBattleCasualtyRate { get; init; } = 0.5;
    public double MineBattleMaxLossFraction { get; init; } = 0.05;
    public double MineBattleProtectionRatio { get; init; } = 0.5;
    public int MineBattleMinimumPopulation { get; init; } = 10;
    public double ConflictRoundSeconds { get; init; } = 2.0;
    public int ConflictBufferCapacity { get; init; } = 64;

    public double TimelineHistorySeconds { get; init; } = 1.0;

    public double FpsWindowSeconds { get; init; } = 1.0;
    public double LowFpsThreshold { get; init; } = 10.0;
    public double RecoveryFpsThreshold { get; init; } = 15.0;
    public double RecoveryDurationSeconds { get; init; } = 2.0;
    public double WorkerChangeWarmupSeconds { get; init; } = 2.0;
    public double MetricsDisplayRefreshSeconds { get; init; } = 0.5;
    public int MetricHistoryCapacity { get; init; } = 600;

    public int MaxPopulationTotal => CivilizationCount * MaxPopulationPerCivilization;
    public int WorldWidth => WindowWidth - SidePanelWidth;

    public void Validate()
    {
        if (CivilizationCount != 4)
        {
            throw new InvalidOperationException("Esta demonstração exige exatamente quatro civilizações.");
        }

        if (WindowWidth <= SidePanelWidth || SidePanelWidth < 0 || WindowHeight <= HeaderHeight)
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

        if (MaxAgentsPerResourceRoute <= 0 ||
            ResourceOccupancyWeight <= 0 ||
            ResourceDistanceWeight < 0 ||
            ResourcePreferenceWeight < 0)
        {
            throw new InvalidOperationException("A configuração de distribuição dos recursos é inválida.");
        }

        if (BaseDropOffColumns <= 0 || BaseDropOffRows <= 0 ||
            BaseDropOffWidth <= 0 || BaseDropOffHeight <= 0)
        {
            throw new InvalidOperationException("A área de depósito da base é inválida.");
        }

        if (MineTripInterval <= 0 ||
            MineBattleCasualtyRate < 0 || MineBattleCasualtyRate > 1 ||
            MineBattleMaxLossFraction < 0 || MineBattleMaxLossFraction > 1 ||
            MineBattleProtectionRatio < 0 || MineBattleProtectionRatio > 1 ||
            MineBattleMinimumPopulation < 0 ||
            MineCapacity <= 0 ||
            MineRegenerationPerSecond < 0 ||
            MineExtractionsPerSecond <= 0 ||
            MineCarryCapacity <= 0 ||
            MineMaxWaitSeconds <= 0 ||
            MineYieldComputationIterations < 0 ||
            ConflictRoundSeconds <= 0 ||
            ConflictBufferCapacity <= 0)
        {
            throw new InvalidOperationException("A configuração da mina central é inválida.");
        }

        if (LowFpsThreshold <= 0 || RecoveryFpsThreshold <= LowFpsThreshold)
        {
            throw new InvalidOperationException("Os limites de FPS precisam possuir histerese válida.");
        }

        if (MetricHistoryCapacity <= 0 ||
            FpsWindowSeconds <= 0 ||
            MetricsDisplayRefreshSeconds <= 0 ||
            TimelineHistorySeconds <= 0)
        {
            throw new InvalidOperationException("A configuração das métricas é inválida.");
        }
    }
}
