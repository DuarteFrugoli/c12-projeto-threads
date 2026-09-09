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
    private static readonly Color ResourceColor = new(238, 238, 244, 255);
    private static readonly Color BaseColor = new(250, 250, 252, 255);
    private static readonly Color ActiveButton = new(52, 117, 214, 255);
    private static readonly Color InactiveButton = new(54, 61, 77, 255);
    private static readonly Color Warning = new(255, 116, 92, 255);
    private static readonly Color Good = new(83, 210, 132, 255);

    public void Draw(
        SimulationState state,
        SimulationConfig config,
        PerformanceMetrics metrics,
        PopulationGrowthController growthController,
        int workerCount,
        int controlledThreadCount,
        double elapsedSeconds,
        bool paused,
        bool stressMode)
    {
        Raylib.ClearBackground(Background);
        DrawHeader(
            state,
            config,
            metrics,
            growthController,
            workerCount,
            controlledThreadCount,
            elapsedSeconds,
            paused,
            stressMode);

        foreach (Civilization civilization in state.Civilizations)
        {
            DrawCivilization(civilization, config);
        }
    }

    private static void DrawHeader(
        SimulationState state,
        SimulationConfig config,
        PerformanceMetrics metrics,
        PopulationGrowthController growthController,
        int workerCount,
        int controlledThreadCount,
        double elapsedSeconds,
        bool paused,
        bool stressMode)
    {
        Raylib.DrawRectangle(0, 0, config.WindowWidth, config.HeaderHeight, Panel);
        Raylib.DrawText("SIMULAÇÃO DE CIVILIZAÇÕES | MULTITHREADING", 20, 12, 23, Text);

        string elapsed = TimeSpan.FromSeconds(elapsedSeconds).ToString(@"mm\:ss\.fff", DisplayCulture);
        Raylib.DrawText($"Tempo real: {elapsed}", 20, 43, 18, Text);
        Raylib.DrawText(
            $"FPS: {metrics.AverageFps:F1} (ilimitado)   Frame: {metrics.AverageFrameMilliseconds:F2} ms",
            245,
            43,
            18,
            Text);
        Raylib.DrawText(
            $"Simulação: {metrics.LastSimulationMilliseconds:F2} ms   " +
            $"p50/p95: {metrics.SimulationP50Milliseconds:F2}/{metrics.SimulationP95Milliseconds:F2} ms",
            650,
            43,
            18,
            Text);

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
            $"Render: {metrics.LastRenderMilliseconds:F2} ms   Updates/s: {metrics.UpdatesPerSecond:F1}",
            825,
            72,
            17,
            Text);

        string growthText = GetGrowthText(growthController.State, stressMode);
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

        Raylib.DrawText("Atalhos: 1/2/4, Espaço, R, S", 770, 133, 17, MutedText);
    }

    private static void DrawCivilization(Civilization civilization, SimulationConfig config)
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
            $"Civilização {civilization.Name}  |  Pop: {civilization.Agents.Count:N0}  |  " +
            $"Recursos: {civilization.StoredResources:N0}",
            (int)territory.X + 12,
            (int)territory.Y + 9,
            17,
            Text);

        foreach (ResourceNode resource in civilization.ResourceNodes)
        {
            Raylib.DrawRectangle(
                (int)resource.Position.X - 2,
                (int)resource.Position.Y - 2,
                5,
                5,
                ResourceColor);
        }

        Vector2 basePosition = civilization.BasePosition;
        Raylib.DrawTriangle(
            new Vector2(basePosition.X, basePosition.Y - 12),
            new Vector2(basePosition.X - 12, basePosition.Y + 10),
            new Vector2(basePosition.X + 12, basePosition.Y + 10),
            BaseColor);

        foreach (Agent agent in civilization.Agents)
        {
            int diameter = (int)MathF.Ceiling(config.AgentRadius * 2f);
            Raylib.DrawRectangle(
                (int)agent.Position.X - (diameter / 2),
                (int)agent.Position.Y - (diameter / 2),
                diameter,
                diameter,
                accent);

            if (agent.IsCarryingResource)
            {
                Raylib.DrawPixel((int)agent.Position.X, (int)agent.Position.Y, ResourceColor);
            }
        }
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

    private static string GetGrowthText(PopulationGrowthState state, bool stressMode)
    {
        return state switch
        {
            PopulationGrowthState.Enabled => stressMode ? "ATIVO — STRESS" : "ATIVO",
            PopulationGrowthState.BlockedByLowFps => "PAUSADO — FPS <= 30",
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
}
