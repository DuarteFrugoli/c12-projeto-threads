using System.Globalization;
using System.Numerics;
using C12ProjetoCiv.Core;
using C12ProjetoCiv.Entities;
using C12ProjetoCiv.Metrics;
using C12ProjetoCiv.Simulation;
using Raylib_cs;

namespace C12ProjetoCiv.Rendering;

public sealed class Renderer
{
    private static readonly CultureInfo DisplayCulture = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly Color Background = new(17, 20, 28, 255);
    private static readonly Color Panel = new(27, 32, 43, 255);
    private static readonly Color Text = new(232, 235, 242, 255);
    private static readonly Color MutedText = new(164, 172, 190, 255);
    private static readonly Color ResourceColor = new(118, 126, 143, 255);
    private static readonly Color CarriedResourceColor = new(250, 250, 252, 255);
    private static readonly Color BaseColor = new(250, 250, 252, 255);
    private static readonly Color ActiveButton = new(52, 117, 214, 255);
    private static readonly Color InactiveButton = new(54, 61, 77, 255);
    private static readonly Color Warning = new(255, 116, 92, 255);
    private static readonly Color Good = new(83, 210, 132, 255);
    private DisplayMetrics _displayMetrics;
    private double _nextMetricsRefreshAtSeconds;
    private double _lastElapsedSeconds = -1;
    private bool _displayMetricsReady;

    public void Draw(
        SimulationState state,
        SimulationConfig config,
        PerformanceMetrics metrics,
        PopulationGrowthController growthController,
        SimulationCoordinator coordinator,
        double elapsedSeconds,
        bool paused,
        bool stressMode,
        bool fullscreen)
    {
        UpdateDisplayMetrics(metrics, config, elapsedSeconds);
        Raylib.ClearBackground(Background);
        Camera2D camera = UiLayout.CreateVirtualCamera(config);
        Raylib.BeginMode2D(camera);
        DrawHeader(
            state,
            config,
            metrics,
            growthController,
            coordinator.WorkerCount,
            coordinator.ThreadsControlledByProject,
            elapsedSeconds,
            paused,
            stressMode,
            fullscreen);

        foreach (Civilization civilization in state.Civilizations)
        {
            DrawCivilization(
                civilization,
                config,
                coordinator.GetWorkerNumberForCivilization(civilization.Id),
                coordinator.GetManagedThreadIdForCivilization(civilization.Id));
        }

        Raylib.EndMode2D();
    }

    private void DrawHeader(
        SimulationState state,
        SimulationConfig config,
        PerformanceMetrics metrics,
        PopulationGrowthController growthController,
        int workerCount,
        int controlledThreadCount,
        double elapsedSeconds,
        bool paused,
        bool stressMode,
        bool fullscreen)
    {
        Raylib.DrawRectangle(0, 0, config.WindowWidth, config.HeaderHeight, Panel);
        Raylib.DrawText("SIMULAÇÃO DE CIVILIZAÇÕES | MULTITHREADING", 20, 12, 23, Text);

        string elapsed = TimeSpan.FromSeconds(elapsedSeconds).ToString(@"mm\:ss\.fff", DisplayCulture);
        Raylib.DrawText($"Tempo real: {elapsed}", 20, 43, 18, Text);
        DrawMetric("FPS", FormatMetric(_displayMetrics.Fps, "F1"), 230, 43);
        DrawMetric("Frame", FormatMetric(_displayMetrics.FrameMilliseconds, "F2", " ms"), 350, 43);
        DrawMetric("Sim.", FormatMetric(_displayMetrics.SimulationMilliseconds, "F2", " ms"), 555, 43);
        DrawMetric("Render", FormatMetric(_displayMetrics.RenderMilliseconds, "F2", " ms"), 790, 43);
        DrawMetric("Updates/s", FormatMetric(_displayMetrics.UpdatesPerSecond, "F1"), 1_000, 43);

        Raylib.DrawText(
            $"População: {state.TotalPopulation:N0}/{config.MaxPopulationTotal:N0}",
            20,
            72,
            18,
            Text);
        Raylib.DrawText(
            $"Workers: {workerCount} | Threads: {controlledThreadCount} " +
            $"(1 principal + {workerCount} sim.)",
            300,
            72,
            17,
            Text);
        Raylib.DrawText(
            $"p50/p95: {FormatMetric(_displayMetrics.SimulationP50Milliseconds, "F2")}/" +
            $"{FormatMetric(_displayMetrics.SimulationP95Milliseconds, "F2")} ms",
            825,
            72,
            17,
            Text);

        string growthText = GetGrowthText(
            growthController.State,
            stressMode,
            config.LowFpsThreshold);
        Color growthColor = growthController.State == PopulationGrowthState.Enabled ? Good : Warning;
        Raylib.DrawText($"Crescimento: {growthText}", 20, 99, 18, growthColor);

        if (!metrics.IsWarmupComplete)
        {
            Raylib.DrawText(
                $"Aquecendo métricas: {metrics.WarmupRemainingSeconds:F1}s",
                620,
                99,
                18,
                MutedText);
        }
        else if (growthController.State == PopulationGrowthState.BlockedByLowFps)
        {
            Raylib.DrawText(
                $"Retoma com FPS >= {config.RecoveryFpsThreshold:F0} por " +
                $"{config.RecoveryDurationSeconds:F0}s",
                620,
                99,
                18,
                Warning);
        }

        DrawButton(UiLayout.OneWorkerButton, "1 worker", workerCount == 1);
        DrawButton(UiLayout.TwoWorkersButton, "2 workers", workerCount == 2);
        DrawButton(UiLayout.FourWorkersButton, "4 workers", workerCount == 4);
        DrawButton(UiLayout.PauseButton, paused ? "Continuar" : "Pausar", paused);
        DrawButton(UiLayout.RestartButton, "Reiniciar", false);
        DrawButton(UiLayout.StressButton, stressMode ? "Stress: ON" : "Stress Test", stressMode);
        DrawButton(
            UiLayout.FullscreenButton,
            fullscreen ? "Modo janela" : "Tela cheia",
            fullscreen);

        Raylib.DrawText("Atalhos: 1/2/4, Espaço, R, S, F11", 895, 133, 16, MutedText);
    }

    private static void DrawCivilization(
        Civilization civilization,
        SimulationConfig config,
        int workerNumber,
        int managedThreadId)
    {
        RgbColor theme = civilization.Color;
        Color fill = new(
            (byte)(theme.R / 8),
            (byte)(theme.G / 8),
            (byte)(theme.B / 8),
            (byte)255);
        Color accent = ToRaylib(theme);
        FloatRectangle territory = civilization.Territory;

        Raylib.DrawRectangle(
            (int)territory.X,
            (int)territory.Y,
            (int)territory.Width,
            (int)territory.Height,
            fill);
        Raylib.DrawRectangleLinesEx(ToRaylibRectangle(territory), 2f, accent);
        Raylib.DrawText(
            $"Civilização {civilization.Name}  |  Worker {workerNumber} (Thread ID {managedThreadId})  |  " +
            $"Pop: {civilization.Agents.Count:N0}  |  " +
            $"Recursos: {civilization.StoredResources:N0}",
            (int)territory.X + 12,
            (int)territory.Y + 9,
            17,
            Text);

        foreach (ResourceNode resource in civilization.ResourceNodes)
        {
            Raylib.DrawRectangle(
                (int)resource.Position.X - 1,
                (int)resource.Position.Y - 1,
                2,
                2,
                ResourceColor);
        }

        Vector2 basePosition = civilization.BasePosition;
        Rectangle baseArea = new(
            basePosition.X - (config.BaseDropOffWidth / 2f),
            basePosition.Y - (config.BaseDropOffHeight / 2f),
            config.BaseDropOffWidth,
            config.BaseDropOffHeight);
        Color baseAreaFill = new(theme.R, theme.G, theme.B, (byte)55);
        Raylib.DrawRectangleRec(baseArea, baseAreaFill);
        Raylib.DrawRectangleLinesEx(baseArea, 1f, accent);
        Raylib.DrawTriangle(
            new Vector2(basePosition.X, basePosition.Y - 9),
            new Vector2(basePosition.X - 9, basePosition.Y + 7),
            new Vector2(basePosition.X + 9, basePosition.Y + 7),
            BaseColor);

        int diameter = GetAgentDiameter(civilization.Agents.Count, config.AgentRadius);

        foreach (Agent agent in civilization.Agents)
        {
            Raylib.DrawRectangle(
                (int)agent.Position.X - (diameter / 2),
                (int)agent.Position.Y - (diameter / 2),
                diameter,
                diameter,
                accent);

            if (agent.IsCarryingResource)
            {
                Raylib.DrawPixel(
                    (int)agent.Position.X,
                    (int)agent.Position.Y,
                    CarriedResourceColor);
            }
        }
    }

    private static int GetAgentDiameter(int population, float configuredRadius)
    {
        if (population >= 3_000)
        {
            return 2;
        }

        if (population >= 1_000)
        {
            return 3;
        }

        if (population >= 250)
        {
            return 4;
        }

        return Math.Max(2, (int)MathF.Ceiling(configuredRadius * 2f));
    }

    private void UpdateDisplayMetrics(
        PerformanceMetrics metrics,
        SimulationConfig config,
        double elapsedSeconds)
    {
        if (!metrics.IsFpsWindowReady)
        {
            _displayMetricsReady = false;
            _nextMetricsRefreshAtSeconds = elapsedSeconds;
            _lastElapsedSeconds = elapsedSeconds;
            return;
        }

        bool clockWasReset = elapsedSeconds < _lastElapsedSeconds;

        if (!_displayMetricsReady ||
            clockWasReset ||
            elapsedSeconds >= _nextMetricsRefreshAtSeconds)
        {
            _displayMetrics = new DisplayMetrics(
                metrics.AverageFps,
                metrics.AverageFrameMilliseconds,
                metrics.AverageSimulationMilliseconds,
                metrics.AverageRenderMilliseconds,
                metrics.UpdatesPerSecond,
                metrics.SimulationP50Milliseconds,
                metrics.SimulationP95Milliseconds);
            _displayMetricsReady = true;
            _nextMetricsRefreshAtSeconds =
                elapsedSeconds + config.MetricsDisplayRefreshSeconds;
        }

        _lastElapsedSeconds = elapsedSeconds;
    }

    private string FormatMetric(double value, string format, string suffix = "")
    {
        return _displayMetricsReady
            ? value.ToString(format, DisplayCulture) + suffix
            : "--";
    }

    private static void DrawMetric(string label, string value, int x, int y)
    {
        Raylib.DrawText($"{label}:", x, y, 16, MutedText);
        Raylib.DrawText(value, x + Raylib.MeasureText($"{label}: ", 16), y, 18, Text);
    }

    private static void DrawButton(FloatRectangle rectangle, string label, bool active)
    {
        Color background = active ? ActiveButton : InactiveButton;
        Raylib.DrawRectangleRec(ToRaylibRectangle(rectangle), background);
        Raylib.DrawRectangleLinesEx(ToRaylibRectangle(rectangle), 1f, MutedText);
        int textWidth = Raylib.MeasureText(label, 17);
        int textX = (int)(rectangle.X + ((rectangle.Width - textWidth) / 2f));
        int textY = (int)(rectangle.Y + ((rectangle.Height - 17) / 2f));
        Raylib.DrawText(label, textX, textY, 17, Text);
    }

    private static string GetGrowthText(
        PopulationGrowthState state,
        bool stressMode,
        double lowFpsThreshold)
    {
        return state switch
        {
            PopulationGrowthState.Enabled => stressMode ? "ATIVO — STRESS" : "ATIVO",
            PopulationGrowthState.BlockedByLowFps =>
                $"PAUSADO — FPS <= {lowFpsThreshold:F0}",
            PopulationGrowthState.BlockedByPopulationLimit => "LIMITE POPULACIONAL",
            PopulationGrowthState.PausedByUser => "PAUSADO PELO USUÁRIO",
            PopulationGrowthState.DisabledForBenchmark => "DESATIVADO NO BENCHMARK",
            _ => state.ToString(),
        };
    }

    private static Color ToRaylib(RgbColor color)
    {
        return new Color(color.R, color.G, color.B, color.A);
    }

    private static Rectangle ToRaylibRectangle(FloatRectangle rectangle)
    {
        return new Rectangle(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
    }

    private readonly record struct DisplayMetrics(
        double Fps,
        double FrameMilliseconds,
        double SimulationMilliseconds,
        double RenderMilliseconds,
        double UpdatesPerSecond,
        double SimulationP50Milliseconds,
        double SimulationP95Milliseconds);
}
