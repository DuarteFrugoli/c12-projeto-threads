using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using C12ProjetoCiv.Core;
using C12ProjetoCiv.Entities;
using C12ProjetoCiv.Simulation;

namespace C12ProjetoCiv.Diagnostics;

public readonly record struct ScalingResult(
    int ThreadCount,
    double MedianMilliseconds,
    double Speedup,
    double Efficiency,
    int SampleCount);

/// <summary>
/// Mede a mesma carga fixa com quantidades crescentes de threads, inclusive acima do
/// número de núcleos lógicos, para mostrar onde o ganho estagna.
/// Cada chamada de <see cref="MeasureNext"/> mede uma quantidade, permitindo que a
/// janela desenhe o progresso entre as etapas.
/// </summary>
public sealed class ScalingBenchmark
{
    public const int WorkloadCivilizations = 64;
    public const int DefaultPopulationPerCivilization = 120;

    private const double FixedDeltaSeconds = 1.0 / 60.0;
    private const int WarmupCycles = 5;
    private const int MinimumSamples = 12;
    private const double MinimumMeasurementSeconds = 0.35;

    private readonly SimulationConfig _config;
    private readonly List<ScalingResult> _results = new();

    public ScalingBenchmark(
        SimulationConfig config,
        int populationPerCivilization = DefaultPopulationPerCivilization)
    {
        _config = config;
        PopulationPerCivilization = populationPerCivilization;
        ThreadCounts = CreateThreadCounts(Environment.ProcessorCount);
    }

    public int PopulationPerCivilization { get; }
    public int TotalAgents => PopulationPerCivilization * WorkloadCivilizations;
    public int LogicalProcessors => Environment.ProcessorCount;
    public IReadOnlyList<int> ThreadCounts { get; }
    public IReadOnlyList<ScalingResult> Results => _results;
    public bool IsComplete => _results.Count == ThreadCounts.Count;
    public int? NextThreadCount => IsComplete ? null : ThreadCounts[_results.Count];

    public void Reset()
    {
        _results.Clear();
    }

    public ScalingResult MeasureNext()
    {
        if (IsComplete)
        {
            throw new InvalidOperationException("Todas as quantidades de threads já foram medidas.");
        }

        int threadCount = ThreadCounts[_results.Count];
        double median = MeasureMedian(threadCount);
        double baseline = _results.Count == 0 ? median : _results[0].MedianMilliseconds;
        double speedup = baseline / median;
        ScalingResult result = new(
            threadCount,
            median,
            speedup,
            speedup / threadCount,
            MinimumSamples);
        _results.Add(result);
        return result;
    }

    public static int RunCli(string[] args)
    {
        int population = args.Length > 1 && int.TryParse(args[1], out int parsed) && parsed > 0
            ? parsed
            : DefaultPopulationPerCivilization;
        ScalingBenchmark benchmark = new(new SimulationConfig(), population);

        Console.WriteLine("Escalabilidade: mesma carga com mais threads");
        Console.WriteLine($"SO: {RuntimeInformation.OSDescription}");
        Console.WriteLine($"Processadores lógicos disponíveis: {benchmark.LogicalProcessors}");
        Console.WriteLine(
            $"Carga fixa: {WorkloadCivilizations} civilizações x {population:N0} agentes " +
            $"({benchmark.TotalAgents:N0} agentes)");
        Console.WriteLine();
        Console.WriteLine("Threads | mediana (ms) | speedup | eficiência");

        while (!benchmark.IsComplete)
        {
            ScalingResult result = benchmark.MeasureNext();
            string marker = result.ThreadCount == benchmark.LogicalProcessors ? "  <- núcleos lógicos" : "";
            Console.WriteLine(
                $"{result.ThreadCount,7} | {result.MedianMilliseconds,12:F3} | " +
                $"{result.Speedup,6:F2}x | {result.Efficiency,9:P0}{marker}");
        }

        string artifactDirectory = Path.Combine(Environment.CurrentDirectory, "artifacts");
        Directory.CreateDirectory(artifactDirectory);
        string path = Path.Combine(artifactDirectory, "scaling.csv");
        List<string> lines = ["logical_processors,total_agents,threads,median_ms,speedup,efficiency"];
        lines.AddRange(benchmark.Results.Select(result => string.Join(
            ',',
            benchmark.LogicalProcessors,
            benchmark.TotalAgents,
            result.ThreadCount,
            result.MedianMilliseconds.ToString("F6", CultureInfo.InvariantCulture),
            result.Speedup.ToString("F6", CultureInfo.InvariantCulture),
            result.Efficiency.ToString("F6", CultureInfo.InvariantCulture))));
        File.WriteAllLines(path, lines);
        Console.WriteLine();
        Console.WriteLine($"CSV salvo em: {path}");
        return 0;
    }

    private double MeasureMedian(int threadCount)
    {
        Civilization[] workload = SimulationState.CreateScalingWorkload(
            _config,
            WorkloadCivilizations,
            PopulationPerCivilization);
        using SimulationCoordinator coordinator = new(workload, _config, threadCount);
        double elapsed = 0;

        for (int cycle = 0; cycle < WarmupCycles; cycle++)
        {
            RunCycle(coordinator, ref elapsed);
        }

        List<double> samples = new(capacity: 256);
        Stopwatch measurement = Stopwatch.StartNew();

        while (samples.Count < MinimumSamples ||
               measurement.Elapsed.TotalSeconds < MinimumMeasurementSeconds)
        {
            long startedAt = Stopwatch.GetTimestamp();
            RunCycle(coordinator, ref elapsed);
            samples.Add(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
        }

        samples.Sort();
        return samples[samples.Count / 2];
    }

    private static void RunCycle(SimulationCoordinator coordinator, ref double elapsed)
    {
        elapsed += FixedDeltaSeconds;
        coordinator.ExecuteFrame(new SimulationFrameCommand(
            FixedDeltaSeconds,
            elapsed,
            AllowPopulationGrowth: false,
            StressModeEnabled: false));
    }

    private static int[] CreateThreadCounts(int logicalProcessors)
    {
        int[] candidates = [1, 2, 3, 4, 6, 8, 12, 16, 24, 32, 48, 64, logicalProcessors];

        return candidates
            .Where(count => count >= 1 && count <= WorkloadCivilizations)
            .Distinct()
            .Order()
            .ToArray();
    }
}
