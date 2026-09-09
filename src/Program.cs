using System.Text;
using C12ProjetoCiv.Core;
using C12ProjetoCiv.Diagnostics;

namespace C12ProjetoCiv;

public static class Program
{
    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        try
        {
            if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
            {
                return SelfTestRunner.Run();
            }

            if (args.Length > 0 &&
                args[0].Equals("--benchmark", StringComparison.OrdinalIgnoreCase))
            {
                return BenchmarkRunner.RunQuick(args);
            }

            if (args.Length > 0 &&
                args[0].Equals("--benchmark-final", StringComparison.OrdinalIgnoreCase))
            {
                return BenchmarkRunner.RunPresentation(args);
            }

            if (args.Contains("--smoke-test", StringComparer.OrdinalIgnoreCase))
            {
                int populationPerCivilization = ParseBoundedIntegerArgument(args, 1, 10, 5_000);
                int workerCount = ParseWorkerCountArgument(args, 2);
                SimulationConfig smokeConfig = new()
                {
                    InitialPopulationPerCivilization = populationPerCivilization,
                };
                using Game smokeGame = new(
                    smokeConfig,
                    Simulation.WorkerModeExtensions.FromCount(workerCount));
                string artifactDirectory = Path.Combine(Environment.CurrentDirectory, "artifacts");
                Directory.CreateDirectory(artifactDirectory);
                string relativeScreenshotPath = Path.Combine(
                    "artifacts",
                    $"smoke-test-{populationPerCivilization}-agents-{workerCount}-workers.png");
                string absoluteScreenshotPath = Path.Combine(
                    Environment.CurrentDirectory,
                    relativeScreenshotPath);
                smokeGame.Run(
                    frameLimit: 120,
                    hiddenWindow: true,
                    screenshotPath: relativeScreenshotPath);

                if (!File.Exists(absoluteScreenshotPath))
                {
                    throw new IOException("A Raylib não produziu a captura esperada do smoke test.");
                }

                Console.WriteLine(
                    $"Smoke test gráfico concluído: 120 frames e captura em '{absoluteScreenshotPath}'.");
                Console.WriteLine(
                    $"Resultado: {smokeGame.Metrics.AverageFps:F1} FPS | " +
                    $"Simulação {smokeGame.Metrics.AverageSimulationMilliseconds:F2} ms | " +
                    $"Render {smokeGame.Metrics.AverageRenderMilliseconds:F2} ms");
            }
            else
            {
                SimulationConfig config = new();
                using Game game = new(config);
                game.Run();
            }

            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("A aplicação foi encerrada por um erro:");
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static int ParseBoundedIntegerArgument(
        string[] args,
        int index,
        int fallback,
        int maximum)
    {
        if (args.Length <= index ||
            !int.TryParse(args[index], out int parsed) ||
            parsed <= 0)
        {
            return fallback;
        }

        return Math.Min(parsed, maximum);
    }

    private static int ParseWorkerCountArgument(string[] args, int index)
    {
        if (args.Length <= index || !int.TryParse(args[index], out int parsed))
        {
            return 1;
        }

        return parsed is 1 or 2 or 4 ? parsed : 1;
    }
}
