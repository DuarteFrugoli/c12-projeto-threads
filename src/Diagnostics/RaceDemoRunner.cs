using System.Diagnostics;
using C12ProjetoCiv.Concurrency;
using C12ProjetoCiv.Core;
using C12ProjetoCiv.Entities;
using C12ProjetoCiv.Simulation;

namespace C12ProjetoCiv.Diagnostics;

/// <summary>
/// Executa a mina central sem janela em cada modo de sincronização e mostra o que
/// acontece com o estoque compartilhado com 1 e 4 workers.
/// </summary>
public static class RaceDemoRunner
{
    private const double FixedDeltaSeconds = 1.0 / 30.0;
    private const int WarmupFrames = 150;

    public static int Run(string[] args)
    {
        int populationPerCivilization = ParsePositiveInteger(args, 1, 3_000);
        int measuredFrames = ParsePositiveInteger(args, 2, 300);
        SimulationConfig baseConfig = new()
        {
            InitialPopulationPerCivilization = Math.Min(populationPerCivilization, 7_000),
        };

        Console.WriteLine("Demonstração da região crítica da mina central");
        Console.WriteLine($"Processadores lógicos: {Environment.ProcessorCount}");
        Console.WriteLine(
            $"População: {baseConfig.InitialPopulationPerCivilization:N0} por civilização | " +
            $"aquecimento: {WarmupFrames} ciclos | medição: {measuredFrames} ciclos");
        Console.WriteLine();
        Console.WriteLine(
            "Sincronização        | Workers | extraído  | duplicado (race) | contenção | espera lock | ms/ciclo");

        foreach (MineSyncMode syncMode in Enum.GetValues<MineSyncMode>())
        {
            foreach (int workers in new[] { 1, 4 })
            {
                SimulationConfig config = baseConfig with { MineSyncMode = syncMode };
                RaceDemoResult result = Measure(config, workers, measuredFrames);
                Console.WriteLine(
                    $"{syncMode.DisplayName(),-20} | {workers,7} | {result.Extracted,9:N0} | " +
                    $"{result.Anomaly,16:N0} | {result.Contentions,9:N0} | " +
                    $"{result.LockWaitMilliseconds,8:F1} ms | {result.MillisecondsPerCycle,8:F2}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("'duplicado' > 0 significa atualizações perdidas: duas threads leram o mesmo estoque");
        Console.WriteLine("e gravaram por cima uma da outra.");
        return 0;
    }

    private static RaceDemoResult Measure(SimulationConfig config, int workerCount, int measuredFrames)
    {
        SimulationState state = SimulationState.Create(config);
        using SimulationCoordinator coordinator = new(
            state.Civilizations,
            config,
            workerCount,
            state.Mine);
        double elapsed = 0;

        RunFrames(coordinator, WarmupFrames, ref elapsed);
        long extractedBefore = SumStats(state, stats => stats.UnitsExtracted);
        long contentionsBefore = SumStats(state, stats => stats.Contentions);
        long waitTicksBefore = SumStats(state, stats => stats.LockWaitTicks);
        long anomalyBefore = state.MineRaceAnomaly;
        long startedAt = Stopwatch.GetTimestamp();

        RunFrames(coordinator, measuredFrames, ref elapsed);

        double totalMilliseconds = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
        return new RaceDemoResult(
            SumStats(state, stats => stats.UnitsExtracted) - extractedBefore,
            state.MineRaceAnomaly - anomalyBefore,
            SumStats(state, stats => stats.Contentions) - contentionsBefore,
            (SumStats(state, stats => stats.LockWaitTicks) - waitTicksBefore) * 1_000.0 / Stopwatch.Frequency,
            totalMilliseconds / measuredFrames);
    }

    private static void RunFrames(SimulationCoordinator coordinator, int frames, ref double elapsed)
    {
        for (int frame = 0; frame < frames; frame++)
        {
            elapsed += FixedDeltaSeconds;
            coordinator.ExecuteFrame(new SimulationFrameCommand(
                FixedDeltaSeconds,
                elapsed,
                AllowPopulationGrowth: false,
                StressModeEnabled: false));
        }
    }

    private static long SumStats(SimulationState state, Func<MineExtractionStats, long> selector)
    {
        long total = 0;

        foreach (Civilization civilization in state.Civilizations)
        {
            total += selector(civilization.MineStats);
        }

        return total;
    }

    private static int ParsePositiveInteger(string[] args, int index, int fallback)
    {
        if (args.Length <= index || !int.TryParse(args[index], out int parsed) || parsed <= 0)
        {
            return fallback;
        }

        return parsed;
    }

    private readonly record struct RaceDemoResult(
        long Extracted,
        long Anomaly,
        long Contentions,
        double LockWaitMilliseconds,
        double MillisecondsPerCycle);
}
