using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using C12ProjetoCiv.Core;
using C12ProjetoCiv.Metrics;
using C12ProjetoCiv.Simulation;

namespace C12ProjetoCiv.Diagnostics;

public static class BenchmarkRunner
{
    private const double PresentationWarmupSeconds = 3.0;

    public static int RunQuick(string[] args)
    {
        int populationPerCivilization = ParsePositiveInteger(args, 1, 1_000);
        int iterations = ParsePositiveInteger(args, 2, 240);
        SimulationConfig config = CreateBenchmarkConfig(populationPerCivilization);

        PrintEnvironment("Benchmark rápido por quantidade de ciclos", config);
        Console.WriteLine($"Iterações medidas: {iterations:N0}");
        Console.WriteLine();

        List<BenchmarkResult> results = MeasureAllModes(
            mode => MeasureIterations(config, mode, iterations));
        PrintResults(results);
        WriteCsv("benchmark-quick.csv", "quick", config, results);
        return 0;
    }

    public static int RunPresentation(string[] args)
    {
        int populationPerCivilization = ParsePositiveInteger(args, 1, 1_000);
        double measurementSeconds = ParsePositiveDouble(args, 2, 10.0);
        int repetitions = ParsePositiveInteger(args, 3, 3);
        SimulationConfig config = CreateBenchmarkConfig(populationPerCivilization);

        PrintEnvironment("Benchmark final para apresentação", config);
        Console.WriteLine($"Aquecimento por repetição: {PresentationWarmupSeconds:F0} s");
        Console.WriteLine($"Medição por repetição: {measurementSeconds:F1} s");
        Console.WriteLine($"Repetições: {repetitions}");
        Console.WriteLine();

        List<BenchmarkResult> results = MeasureAllModes(
            mode => MeasureForPresentation(
                config,
                mode,
                measurementSeconds,
                repetitions));
        PrintResults(results);
        WriteCsv("benchmark-presentation.csv", "presentation", config, results);
        return 0;
    }

    private static List<BenchmarkResult> MeasureAllModes(
        Func<WorkerMode, double[]> measureMode)
    {
        List<(WorkerMode Mode, double[] Samples)> measurements = new(3);

        foreach (WorkerMode mode in new[] { WorkerMode.One, WorkerMode.Two, WorkerMode.Four })
        {
            double[] samples = measureMode(mode);
            Array.Sort(samples);
            measurements.Add((mode, samples));
        }

        double baselineMedian = Percentile(measurements[0].Samples, 0.50);
        List<BenchmarkResult> results = new(measurements.Count);

        foreach ((WorkerMode mode, double[] samples) in measurements)
        {
            double median = Percentile(samples, 0.50);
            results.Add(new BenchmarkResult(
                (int)mode,
                median,
                Percentile(samples, 0.95),
                baselineMedian / median,
                samples.Length));
        }

        return results;
    }

    private static double[] MeasureIterations(
        SimulationConfig config,
        WorkerMode mode,
        int iterations)
    {
        SimulationState state = SimulationState.Create(config);
        using SimulationCoordinator coordinator = new(state, config, mode);
        Stopwatch realClock = Stopwatch.StartNew();
        double previousElapsed = realClock.Elapsed.TotalSeconds;

        RunIterations(
            coordinator,
            realClock,
            ref previousElapsed,
            iterationCount: 30,
            samples: null);

        double[] samples = new double[iterations];
        RunIterations(
            coordinator,
            realClock,
            ref previousElapsed,
            iterations,
            samples);
        return samples;
    }

    private static double[] MeasureForPresentation(
        SimulationConfig config,
        WorkerMode mode,
        double measurementSeconds,
        int repetitions)
    {
        List<double> combinedSamples = new(capacity: 16_384);

        for (int repetition = 1; repetition <= repetitions; repetition++)
        {
            Console.WriteLine(
                $"Medindo {(int)mode} worker(s), repetição {repetition}/{repetitions}...");
            SimulationState state = SimulationState.Create(config);
            using SimulationCoordinator coordinator = new(state, config, mode);
            Stopwatch realClock = Stopwatch.StartNew();
            double previousElapsed = realClock.Elapsed.TotalSeconds;

            RunForDuration(
                coordinator,
                realClock,
                ref previousElapsed,
                PresentationWarmupSeconds,
                samples: null);
            RunForDuration(
                coordinator,
                realClock,
                ref previousElapsed,
                measurementSeconds,
                combinedSamples);
        }

        return combinedSamples.ToArray();
    }

    private static void RunIterations(
        SimulationCoordinator coordinator,
        Stopwatch realClock,
        ref double previousElapsed,
        int iterationCount,
        double[]? samples)
    {
        for (int iteration = 0; iteration < iterationCount; iteration++)
        {
            double sample = ExecuteMeasuredCycle(coordinator, realClock, ref previousElapsed);

            if (samples is not null)
            {
                samples[iteration] = sample;
            }
        }
    }

    private static void RunForDuration(
        SimulationCoordinator coordinator,
        Stopwatch realClock,
        ref double previousElapsed,
        double durationSeconds,
        List<double>? samples)
    {
        Stopwatch duration = Stopwatch.StartNew();

        while (duration.Elapsed.TotalSeconds < durationSeconds)
        {
            double sample = ExecuteMeasuredCycle(coordinator, realClock, ref previousElapsed);
            samples?.Add(sample);
        }
    }

    private static double ExecuteMeasuredCycle(
        SimulationCoordinator coordinator,
        Stopwatch realClock,
        ref double previousElapsed)
    {
        double elapsed = realClock.Elapsed.TotalSeconds;
        double delta = Math.Max(0, elapsed - previousElapsed);
        previousElapsed = elapsed;
        long startedAt = Stopwatch.GetTimestamp();
        coordinator.ExecuteFrame(new SimulationFrameCommand(
            delta,
            elapsed,
            AllowPopulationGrowth: false,
            StressModeEnabled: false));
        return Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
    }

    private static void PrintEnvironment(string title, SimulationConfig config)
    {
        Console.WriteLine(title);
        Console.WriteLine($"SO: {RuntimeInformation.OSDescription}");
        Console.WriteLine($"Runtime: {RuntimeInformation.FrameworkDescription}");
        Console.WriteLine($"Arquitetura: {RuntimeInformation.ProcessArchitecture}");
        Console.WriteLine($"Processadores lógicos disponíveis: {Environment.ProcessorCount}");
        Console.WriteLine(
            $"População: {config.InitialPopulationPerCivilization:N0} por civilização " +
            $"({config.InitialPopulationPerCivilization * config.CivilizationCount:N0} total)");
        Console.WriteLine(
            $"Recursos inesgotáveis: {config.ResourceNodesPerCivilization:N0} pontos por civilização");
    }

    private static void PrintResults(IReadOnlyList<BenchmarkResult> results)
    {
        Console.WriteLine("Workers | mediana (ms) | p95 (ms) | speedup | amostras");

        foreach (BenchmarkResult result in results)
        {
            Console.WriteLine(
                $"{result.WorkerCount,7} | " +
                $"{result.MedianMilliseconds,12:F3} | " +
                $"{result.P95Milliseconds,8:F3} | " +
                $"{result.Speedup,6:F2}x | " +
                $"{result.SampleCount,8:N0}");
        }
    }

    private static void WriteCsv(
        string fileName,
        string benchmarkType,
        SimulationConfig config,
        IReadOnlyList<BenchmarkResult> results)
    {
        string artifactDirectory = Path.Combine(Environment.CurrentDirectory, "artifacts");
        Directory.CreateDirectory(artifactDirectory);
        string path = Path.Combine(artifactDirectory, fileName);
        List<string> lines =
        [
            "benchmark_type,os,runtime,architecture,logical_processors,population_per_civilization," +
            "total_population,resource_nodes_per_civilization,workers,median_ms,p95_ms,speedup,samples",
        ];

        foreach (BenchmarkResult result in results)
        {
            lines.Add(string.Join(
                ',',
                EscapeCsv(benchmarkType),
                EscapeCsv(RuntimeInformation.OSDescription),
                EscapeCsv(RuntimeInformation.FrameworkDescription),
                RuntimeInformation.ProcessArchitecture,
                Environment.ProcessorCount,
                config.InitialPopulationPerCivilization,
                config.InitialPopulationPerCivilization * config.CivilizationCount,
                config.ResourceNodesPerCivilization,
                result.WorkerCount,
                result.MedianMilliseconds.ToString("F6", CultureInfo.InvariantCulture),
                result.P95Milliseconds.ToString("F6", CultureInfo.InvariantCulture),
                result.Speedup.ToString("F6", CultureInfo.InvariantCulture),
                result.SampleCount));
        }

        File.WriteAllLines(path, lines);
        Console.WriteLine();
        Console.WriteLine($"CSV salvo em: {path}");
    }

    private static string EscapeCsv(string value)
    {
        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static SimulationConfig CreateBenchmarkConfig(int requestedPopulation)
    {
        return new SimulationConfig
        {
            InitialPopulationPerCivilization = Math.Min(requestedPopulation, 5_000),
        };
    }

    private static double Percentile(double[] sortedValues, double percentile)
    {
        int index = (int)Math.Ceiling(sortedValues.Length * percentile) - 1;
        return sortedValues[Math.Clamp(index, 0, sortedValues.Length - 1)];
    }

    private static int ParsePositiveInteger(string[] args, int index, int fallback)
    {
        if (args.Length <= index || !int.TryParse(args[index], out int parsed) || parsed <= 0)
        {
            return fallback;
        }

        return parsed;
    }

    private static double ParsePositiveDouble(string[] args, int index, double fallback)
    {
        if (args.Length <= index ||
            !double.TryParse(args[index], NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ||
            parsed <= 0)
        {
            return fallback;
        }

        return parsed;
    }
}
