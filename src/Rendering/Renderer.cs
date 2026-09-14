using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using C12ProjetoCiv.Concurrency;
using C12ProjetoCiv.Core;
using C12ProjetoCiv.Diagnostics;
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
    private static readonly Color PanelBorder = new(54, 61, 77, 255);
    private static readonly Color Text = new(232, 235, 242, 255);
    private static readonly Color MutedText = new(164, 172, 190, 255);
    private static readonly Color ResourceColor = new(118, 126, 143, 255);
    private static readonly Color CarriedResourceColor = new(250, 250, 252, 255);
    private static readonly Color BaseColor = new(250, 250, 252, 255);
    private static readonly Color ActiveButton = new(52, 117, 214, 255);
    private static readonly Color InactiveButton = new(54, 61, 77, 255);
    private static readonly Color DangerButton = new(176, 64, 48, 255);
    private static readonly Color Warning = new(255, 116, 92, 255);
    private static readonly Color Good = new(83, 210, 132, 255);
    private static readonly Color TimelineTrack = new(20, 23, 31, 255);
    private static readonly Color WaitingColor = new(62, 70, 90, 255);
    private static readonly Color RenderingColor = new(205, 209, 219, 255);
    private static readonly Color ArbitrationColor = new(190, 125, 255, 255);
    private static readonly Color NeutralMine = new(140, 146, 160, 255);
    private static readonly double[] TimelineWindowOptionsMs = [5, 10, 25, 50, 100, 250, 500, 1_000];

    private readonly List<TimelineSegment> _segmentBuffer = new(512);
    private readonly List<double> _busyFractions = new();
    private DisplayMetrics _displayMetrics;
    private double _nextMetricsRefreshAtSeconds;
    private double _lastElapsedSeconds = -1;
    private bool _displayMetricsReady;
    private double _timelineWindowMs = 50;
    private double _nextTimelineRefreshAtSeconds;
    private long _lastSeenBattles;
    private double _battleFlashUntilSeconds;

    public void Draw(FrameView view)
    {
        UpdateDisplayMetrics(view.Metrics, view.Config, view.ElapsedSeconds);
        Raylib.ClearBackground(Background);
        Camera2D camera = UiLayout.CreateVirtualCamera(view.Config);
        Raylib.BeginMode2D(camera);
        DrawHeader(view);

        if (view.ScalingView is not null)
        {
            DrawScalingView(view.ScalingView, view.Config);
            Raylib.EndMode2D();
            return;
        }

        foreach (Civilization civilization in view.State.Civilizations)
        {
            DrawCivilization(
                civilization,
                view.Config,
                view.Coordinator.GetWorkerNumberForCivilization(civilization.Id),
                view.Coordinator.GetManagedThreadIdForCivilization(civilization.Id));
        }

        if (view.State.Mine is not null)
        {
            DrawMine(view.State, view.Arbiter);
        }

        DrawSidePanel(view);
        Raylib.EndMode2D();
    }

    private void DrawHeader(FrameView view)
    {
        SimulationConfig config = view.Config;
        int workerCount = view.Coordinator.WorkerCount;
        Raylib.DrawRectangle(0, 0, config.WindowWidth, config.HeaderHeight, Panel);
        Raylib.DrawText("SIMULAÇÃO DE CIVILIZAÇÕES | MULTITHREADING", 20, 12, 23, Text);

        string elapsed = TimeSpan.FromSeconds(view.ElapsedSeconds).ToString(@"mm\:ss\.fff", DisplayCulture);
        Raylib.DrawText($"Tempo real: {elapsed}", 20, 43, 18, Text);
        DrawMetric("FPS", FormatMetric(_displayMetrics.Fps, "F1"), 230, 43);
        DrawMetric("Frame", FormatMetric(_displayMetrics.FrameMilliseconds, "F2", " ms"), 350, 43);
        DrawMetric("Sim.", FormatMetric(_displayMetrics.SimulationMilliseconds, "F2", " ms"), 555, 43);
        DrawMetric("Render", FormatMetric(_displayMetrics.RenderMilliseconds, "F2", " ms"), 790, 43);
        DrawMetric("Updates/s", FormatMetric(_displayMetrics.UpdatesPerSecond, "F1"), 1_000, 43);

        Raylib.DrawText(
            $"População: {Format(view.State.TotalPopulation, "N0")}/{Format(config.MaxPopulationTotal, "N0")}",
            20,
            72,
            18,
            Text);

        string threadText = view.Arbiter is null
            ? $"Workers: {workerCount} | Threads: {workerCount + 1} (1 principal + {workerCount} sim.)"
            : $"Workers: {workerCount} | Threads: {workerCount + 2} " +
              $"(1 principal + {workerCount} sim. + 1 árbitro)";
        Raylib.DrawText(threadText, 300, 72, 17, Text);
        Raylib.DrawText(
            $"p50/p95: {FormatMetric(_displayMetrics.SimulationP50Milliseconds, "F2")}/" +
            $"{FormatMetric(_displayMetrics.SimulationP95Milliseconds, "F2")} ms",
            1_000,
            72,
            17,
            Text);

        string growthText = GetGrowthText(
            view.GrowthController.State,
            view.StressMode,
            config.LowFpsThreshold);
        Color growthColor = view.GrowthController.State == PopulationGrowthState.Enabled ? Good : Warning;
        Raylib.DrawText($"Crescimento: {growthText}", 20, 99, 18, growthColor);

        if (!view.Metrics.IsWarmupComplete)
        {
            Raylib.DrawText(
                $"Aquecendo métricas: {Format(view.Metrics.WarmupRemainingSeconds, "F1")}s",
                620,
                99,
                18,
                MutedText);
        }
        else if (view.GrowthController.State == PopulationGrowthState.BlockedByLowFps)
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
        DrawButton(UiLayout.PauseButton, view.Paused ? "Continuar" : "Pausar", view.Paused);
        DrawButton(UiLayout.RestartButton, "Reiniciar", false);
        DrawButton(UiLayout.StressButton, view.StressMode ? "Stress: ON" : "Stress Test", view.StressMode);
        DrawButton(
            UiLayout.FullscreenButton,
            view.Fullscreen ? "Modo janela" : "Tela cheia",
            view.Fullscreen);

        if (view.State.Mine is ContestedMine mine)
        {
            Color mineButtonColor = mine.SyncMode == MineSyncMode.Unsynchronized ? DangerButton : ActiveButton;
            DrawButton(UiLayout.MineSyncButton, $"Mina: {mine.SyncMode.ShortName()}", mineButtonColor);
        }

        if (view.State.Mine is ContestedMine battleMine)
        {
            DrawButton(
                UiLayout.BattlesButton,
                battleMine.BattlesEnabled ? "Batalhas: ON" : "Batalhas: OFF",
                battleMine.BattlesEnabled);
        }

        DrawButton(UiLayout.ScalingButton, "Escalabilidade", view.ScalingView is not null);
        Raylib.DrawText("Atalhos: 1/2/4, Espaço, R, S, M, B, E, F11", 1_000, 99, 16, MutedText);
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

        // O rótulo fica na borda oposta à mina central, para não cobrir a disputa.
        bool bottomRow = civilization.BasePosition.Y < territory.Center.Y;
        int labelY = bottomRow
            ? (int)(territory.Y + territory.Height) - 46
            : (int)territory.Y + 9;
        Raylib.DrawText(
            $"Civilização {civilization.Name}  |  Worker {workerNumber} (Thread ID {managedThreadId})",
            (int)territory.X + 12,
            labelY,
            17,
            Text);
        Raylib.DrawText(
            $"Pop: {Format(civilization.Agents.Count, "N0")}  |  " +
            $"Recursos: {Format(civilization.StoredResources, "N0")}  |  " +
            $"Na mina: {Format(civilization.MinersAtMine, "N0")}  |  " +
            $"Mortos em batalha: {Format(civilization.MineStats.BattleDeaths, "N0")}",
            (int)territory.X + 12,
            labelY + 20,
            15,
            MutedText);
    }

    private void DrawMine(SimulationState state, ConflictArbiter? arbiter)
    {
        ContestedMine mine = state.Mine!;
        MineControl control = mine.Control;
        Color controllerColor = control.ControllerCivilizationId >= 0
            ? ToRaylib(state.Civilizations[control.ControllerCivilizationId].Color)
            : NeutralMine;
        double now = HighResolutionTime.NowSeconds;
        long battles = arbiter?.Battles ?? 0;

        if (battles != _lastSeenBattles)
        {
            _lastSeenBattles = battles;
            _battleFlashUntilSeconds = now + 0.7;
        }

        if (now < _battleFlashUntilSeconds)
        {
            float progress = 1f - (float)((_battleFlashUntilSeconds - now) / 0.7);
            Raylib.DrawCircleLinesV(mine.Position, 26f + (progress * 30f), Warning);
            Raylib.DrawCircleLinesV(mine.Position, 25f + (progress * 30f), Warning);
        }

        Raylib.DrawPoly(mine.Position, 4, 24f, 0f, Background);
        Raylib.DrawPoly(mine.Position, 4, 20f, 0f, controllerColor);
        Raylib.DrawPolyLinesEx(mine.Position, 4, 24f, 0f, 2f, Text);

        float fill = Math.Clamp(mine.Stock / (float)mine.Capacity, 0f, 1f);
        Rectangle bar = new(mine.Position.X - 36, mine.Position.Y + 30, 72, 6);
        Raylib.DrawRectangleRec(bar, Background);
        Raylib.DrawRectangleRec(bar with { Width = bar.Width * fill }, Good);
        Raylib.DrawRectangleLinesEx(bar, 1f, Text);
    }

    private void DrawSidePanel(FrameView view)
    {
        SimulationConfig config = view.Config;
        int panelX = config.WorldWidth;
        int left = panelX + 16;
        Raylib.DrawRectangle(
            panelX,
            config.HeaderHeight,
            config.SidePanelWidth,
            config.WindowHeight - config.HeaderHeight,
            Panel);
        Raylib.DrawLine(panelX, config.HeaderHeight, panelX, config.WindowHeight, PanelBorder);

        int y = config.HeaderHeight + 12;
        y = DrawTimelineSection(view, left, y);
        y = DrawMineSection(view, left, y + 14);
        DrawArbiterSection(view, left, y + 14);
    }

    private int DrawTimelineSection(FrameView view, int left, int y)
    {
        IReadOnlyList<ThreadTimeline> timelines = view.Timelines;
        const int labelWidth = 86;
        const int barWidth = 238;
        const int rowHeight = 24;
        int barX = left + labelWidth;

        long now = view.Paused
            ? timelines.Max(timeline => timeline.LatestTimestamp)
            : Stopwatch.GetTimestamp();
        RefreshTimelineSummary(view, now);
        long windowTicks = (long)(_timelineWindowMs / 1_000.0 * Stopwatch.Frequency);
        long from = now - windowTicks;

        Raylib.DrawText("LINHA DO TEMPO DAS THREADS", left, y, 18, Text);
        y += 24;
        Raylib.DrawText(
            $"Últimos {Format(_timelineWindowMs, "N0")} ms  |  cor = civilização processada",
            left,
            y,
            14,
            MutedText);
        y += 24;

        for (int index = 0; index < timelines.Count; index++)
        {
            ThreadTimeline timeline = timelines[index];
            Raylib.DrawText(timeline.Label, left, y + 2, 15, Text);
            Raylib.DrawRectangle(barX, y, barWidth, 17, TimelineTrack);
            timeline.CopySegments(from, now, _segmentBuffer);

            foreach (TimelineSegment segment in _segmentBuffer)
            {
                float startX = barX + (barWidth * Math.Clamp((segment.StartTimestamp - from) / (float)windowTicks, 0f, 1f));
                float endX = barX + (barWidth * Math.Clamp((segment.EndTimestamp - from) / (float)windowTicks, 0f, 1f));
                Raylib.DrawRectangle(
                    (int)startX,
                    y + (segment.Kind == TimelineSegmentKind.WaitingForWorkers ? 5 : 0),
                    Math.Max(1, (int)(endX - startX)),
                    segment.Kind == TimelineSegmentKind.WaitingForWorkers ? 7 : 17,
                    GetSegmentColor(segment.Kind, view.State));
            }

            double busy = index < _busyFractions.Count ? _busyFractions[index] : 0;
            Raylib.DrawText(Format(busy, "P0"), barX + barWidth + 8, y + 2, 14, busy > 0.85 ? Warning : MutedText);
            y += rowHeight;
        }

        y += 2;
        int legendX = left;

        for (int civilizationId = 0; civilizationId < view.State.Civilizations.Length; civilizationId++)
        {
            Civilization civilization = view.State.Civilizations[civilizationId];
            legendX = DrawLegendItem(legendX, y, ToRaylib(civilization.Color), civilization.Name);
        }

        legendX = DrawLegendItem(legendX, y, RenderingColor, "Render");
        legendX = DrawLegendItem(legendX, y, WaitingColor, "Espera");
        DrawLegendItem(legendX, y, ArbitrationColor, "Árbitro");
        y += 20;
        Raylib.DrawText("% = tempo ocupado no último segundo", left, y, 13, MutedText);
        return y + 16;
    }

    private int DrawMineSection(FrameView view, int left, int y)
    {
        ContestedMine? mine = view.State.Mine;
        MineMetrics metrics = view.MineMetrics;
        Raylib.DrawLine(left, y - 7, left + 368, y - 7, PanelBorder);
        Raylib.DrawText("MINA CENTRAL  |  REGIÃO CRÍTICA", left, y, 18, Text);
        y += 24;

        if (mine is null)
        {
            Raylib.DrawText("Mina desativada na configuração.", left, y, 15, MutedText);
            return y + 20;
        }

        bool unsafeMode = mine.SyncMode == MineSyncMode.Unsynchronized;
        Raylib.DrawText("Sincronização:", left, y, 16, MutedText);
        Raylib.DrawText(mine.SyncMode.DisplayName(), left + 120, y, 16, unsafeMode ? Warning : Good);
        Raylib.DrawText("[M] troca", left + 300, y, 14, MutedText);
        y += 22;

        Raylib.DrawText(
            $"Estoque: {Format(mine.Stock, "N0")} / {Format(mine.Capacity, "N0")}",
            left,
            y,
            15,
            Text);
        float fill = Math.Clamp(mine.Stock / (float)mine.Capacity, 0f, 1f);
        Raylib.DrawRectangle(left + 220, y + 3, 148, 10, TimelineTrack);
        Raylib.DrawRectangle(left + 220, y + 3, (int)(148 * fill), 10, Good);
        y += 21;

        Raylib.DrawText(
            $"Extraído: {Format(metrics.UnitsPerSecond, "N0")}/s  |  " +
            $"acessos: {Format(metrics.AttemptsPerSecond, "N0")}/s",
            left,
            y,
            15,
            Text);
        y += 21;

        long anomaly = metrics.RaceAnomalySinceModeChange;
        Raylib.DrawText("Unidades duplicadas (race):", left, y, 15, Text);
        Raylib.DrawText(Format(anomaly, "N0"), left + 215, y - 1, 17, anomaly != 0 ? Warning : Good);
        y += 21;

        string contentionText = mine.SyncMode switch
        {
            MineSyncMode.Lock =>
                $"Contenção: {Format(metrics.ContentionsPerSecond, "N0")}/s  |  " +
                $"espera: {Format(metrics.LockWaitMillisecondsPerSecond, "F1")} ms/s",
            MineSyncMode.Interlocked =>
                $"Retentativas do CAS: {Format(metrics.ContentionsPerSecond, "N0")}/s",
            _ => "Sem proteção: leituras e escritas se sobrepõem",
        };
        Raylib.DrawText(contentionText, left, y, 15, unsafeMode ? Warning : MutedText);
        return y + 20;
    }

    private static void DrawArbiterSection(FrameView view, int left, int y)
    {
        ConflictArbiter? arbiter = view.Arbiter;
        Raylib.DrawLine(left, y - 7, left + 368, y - 7, PanelBorder);
        Raylib.DrawText("THREAD ÁRBITRO  |  BATALHAS", left, y, 18, Text);
        y += 24;

        if (arbiter is null || view.State.Mine is null)
        {
            Raylib.DrawText("Árbitro desativado.", left, y, 15, MutedText);
            return;
        }

        bool battlesEnabled = view.State.Mine.BattlesEnabled;
        Raylib.DrawText("Batalhas:", left, y, 16, MutedText);
        Raylib.DrawText(battlesEnabled ? "LIGADAS" : "DESLIGADAS", left + 80, y, 16, battlesEnabled ? Good : Warning);
        Raylib.DrawText("[B] troca", left + 300, y, 14, MutedText);
        y += 21;

        Raylib.DrawText(
            $"Thread ID {arbiter.ManagedThreadId}  |  relatórios: {Format(view.MineMetrics.ReportsPerSecond, "N0")}/s",
            left,
            y,
            15,
            Text);
        y += 19;
        Raylib.DrawText(
            $"Buffer: {arbiter.QueueLength}/{arbiter.QueueCapacity}  |  " +
            $"produtores bloqueados: {Format(arbiter.ProducerBlocks, "N0")}",
            left,
            y,
            15,
            MutedText);
        y += 21;

        if (!battlesEnabled)
        {
            Raylib.DrawText("Mina compartilhada sem disputas: populações", left, y, 15, MutedText);
            y += 19;
            Raylib.DrawText("equilibradas para comparar 1 e 4 workers.", left, y, 15, MutedText);
            y += 23;
        }

        int x = left;
        x += DrawTextAt($"Batalhas: {Format(arbiter.Battles, "N0")}  |  Vitórias:", x, y, 15, Text) + 6;

        foreach (Civilization civilization in view.State.Civilizations)
        {
            x += DrawTextAt(
                $"{civilization.Name} {arbiter.GetVictories(civilization.Id)}",
                x,
                y,
                15,
                ToRaylib(civilization.Color)) + 8;
        }

        y += 20;
        DrawPerCivilizationLine(view.State, left, y, "Mortos:", civilization => civilization.MineStats.BattleDeaths);
        y += 20;
        DrawPerCivilizationLine(view.State, left, y, "Saque recebido:", civilization => civilization.MineStats.LootReceived);
        y += 24;

        Raylib.DrawText("Últimas batalhas (média de agentes na mina):", left, y, 14, MutedText);
        y += 19;

        foreach (BattleRecord battle in arbiter.GetRecentBattles())
        {
            Civilization winner = view.State.Civilizations[battle.WinnerCivilizationId];
            x = left;
            x += DrawTextAt($"R{battle.Round}", x, y, 15, MutedText) + 8;
            x += DrawTextAt($"{winner.Name} venceu", x, y, 15, ToRaylib(winner.Color)) + 10;

            foreach (Civilization civilization in view.State.Civilizations)
            {
                double miners = battle.AverageMiners[civilization.Id];
                Color color = miners > 0 ? ToRaylib(civilization.Color) : MutedText;
                string retreat = battle.RetreatedWithoutLosses[civilization.Id] ? "r" : "";
                x += DrawTextAt($"{civilization.Name}:{Format(miners, "N0")}{retreat}", x, y, 15, color) + 8;
            }

            y += 19;
        }

        Raylib.DrawText("r = recuou sem baixas (menos da metade da vencedora)", left, y + 2, 13, MutedText);
    }

    private static void DrawPerCivilizationLine(
        SimulationState state,
        int x,
        int y,
        string label,
        Func<Civilization, long> selector)
    {
        x += DrawTextAt(label, x, y, 15, MutedText) + 8;

        foreach (Civilization civilization in state.Civilizations)
        {
            x += DrawTextAt(
                $"{civilization.Name} {Format(selector(civilization), "N0")}",
                x,
                y,
                15,
                ToRaylib(civilization.Color)) + 10;
        }
    }

    private static void DrawScalingView(ScalingBenchmark benchmark, SimulationConfig config)
    {
        Raylib.DrawRectangle(
            0,
            config.HeaderHeight,
            config.WindowWidth,
            config.WindowHeight - config.HeaderHeight,
            Background);

        int top = config.HeaderHeight;
        Raylib.DrawText("ESCALABILIDADE: E SE HOUVER MAIS THREADS QUE NÚCLEOS?", 40, top + 18, 24, Text);
        Raylib.DrawText(
            $"Mesma carga em todas as medições: {ScalingBenchmark.WorkloadCivilizations} civilizações x " +
            $"{benchmark.PopulationPerCivilization} agentes = {Format(benchmark.TotalAgents, "N0")} agentes  |  " +
            $"Núcleos lógicos deste computador: {benchmark.LogicalProcessors}",
            40,
            top + 50,
            16,
            MutedText);

        string status = benchmark.NextThreadCount is int next
            ? $"Medindo {next} threads... ({benchmark.Results.Count + 1}/{benchmark.ThreadCounts.Count})"
            : "Medição concluída.  [Enter] medir novamente   [E] voltar à simulação";
        Raylib.DrawText(status, 40, top + 74, 17, benchmark.IsComplete ? Good : Warning);

        DrawScalingChart(benchmark, new Rectangle(110, top + 125, 860, 520));
        DrawScalingTable(benchmark, 1_030, top + 118);
    }

    private static void DrawScalingChart(ScalingBenchmark benchmark, Rectangle plot)
    {
        IReadOnlyList<int> counts = benchmark.ThreadCounts;
        IReadOnlyList<ScalingResult> results = benchmark.Results;
        double maxMeasured = results.Count == 0 ? 2 : results.Max(result => result.Speedup);
        int yMax = Math.Max(4, (int)Math.Ceiling(maxMeasured * 1.3));
        int gridStep = yMax > 10 ? 2 : 1;

        float XFor(int index) => plot.X + (plot.Width * index / (counts.Count - 1f));
        float YFor(double speedup) => plot.Y + plot.Height - (float)(plot.Height * speedup / yMax);

        Raylib.DrawRectangleRec(plot, Panel);

        for (int value = 0; value <= yMax; value += gridStep)
        {
            float y = YFor(value);
            Raylib.DrawLineV(new Vector2(plot.X, y), new Vector2(plot.X + plot.Width, y), PanelBorder);
            string label = $"{value}x";
            Raylib.DrawText(label, (int)plot.X - 12 - Raylib.MeasureText(label, 16), (int)y - 8, 16, MutedText);
        }

        for (int index = 0; index < counts.Count; index++)
        {
            string label = counts[index].ToString(DisplayCulture);
            int width = Raylib.MeasureText(label, 16);
            Raylib.DrawText(label, (int)XFor(index) - (width / 2), (int)(plot.Y + plot.Height) + 10, 16, Text);
        }

        Raylib.DrawText("threads", (int)(plot.X + (plot.Width / 2)) - 30, (int)(plot.Y + plot.Height) + 34, 17, MutedText);
        Raylib.DrawText("speedup (vezes mais rápido que 1 thread)", (int)plot.X, (int)plot.Y - 24, 16, MutedText);

        int coresIndex = counts.ToList().IndexOf(benchmark.LogicalProcessors);

        if (coresIndex >= 0)
        {
            float x = XFor(coresIndex);
            DrawDashedLine(new Vector2(x, plot.Y), new Vector2(x, plot.Y + plot.Height), Warning);
            Raylib.DrawText($"núcleos lógicos ({benchmark.LogicalProcessors})", (int)x + 6, (int)plot.Y + 8, 16, Warning);
        }

        // Ideal: N threads = N vezes mais rápido. Desenhado até sair do topo do gráfico.
        Vector2 idealEnd = new(XFor(0), YFor(counts[0]));

        for (int index = 1; index < counts.Count; index++)
        {
            if (counts[index - 1] >= yMax)
            {
                break;
            }

            Vector2 start = new(XFor(index - 1), YFor(counts[index - 1]));
            Vector2 end = new(XFor(index), YFor(counts[index]));

            if (counts[index] > yMax)
            {
                float t = (yMax - counts[index - 1]) / (float)(counts[index] - counts[index - 1]);
                end = Vector2.Lerp(start, end, t);
            }

            DrawDashedLine(start, end, MutedText);
            idealEnd = end;
        }

        Raylib.DrawText("ideal (linear)", (int)idealEnd.X + 10, (int)idealEnd.Y + 6, 15, MutedText);

        for (int index = 0; index < results.Count; index++)
        {
            Vector2 point = new(XFor(index), YFor(results[index].Speedup));

            if (index > 0)
            {
                Vector2 previous = new(XFor(index - 1), YFor(results[index - 1].Speedup));
                Raylib.DrawLineEx(previous, point, 3f, Good);
            }
        }

        for (int index = 0; index < results.Count; index++)
        {
            Vector2 point = new(XFor(index), YFor(results[index].Speedup));
            Raylib.DrawCircleV(point, 6f, Good);
            Raylib.DrawCircleLinesV(point, 6f, Text);
            string label = $"{Format(results[index].Speedup, "F1")}x";
            int width = Raylib.MeasureText(label, 15);
            Raylib.DrawText(label, (int)point.X - (width / 2), (int)point.Y - 26, 15, Text);
        }
    }

    private static void DrawScalingTable(ScalingBenchmark benchmark, int x, int y)
    {
        int[] columns = [x, x + 90, x + 220, x + 330];
        Raylib.DrawText("Threads", columns[0], y, 16, MutedText);
        Raylib.DrawText("ms/ciclo", columns[1], y, 16, MutedText);
        Raylib.DrawText("Speedup", columns[2], y, 16, MutedText);
        Raylib.DrawText("Eficiência", columns[3], y, 16, MutedText);
        y += 26;

        ScalingResult? best = benchmark.Results.Count == 0
            ? null
            : benchmark.Results.MaxBy(result => result.Speedup);

        for (int index = 0; index < benchmark.ThreadCounts.Count; index++)
        {
            int threads = benchmark.ThreadCounts[index];
            bool measured = index < benchmark.Results.Count;
            Color color = measured && best?.ThreadCount == threads ? Good : Text;
            string marker = threads == benchmark.LogicalProcessors ? " *" : "";
            Raylib.DrawText($"{threads}{marker}", columns[0], y, 16, threads == benchmark.LogicalProcessors ? Warning : color);

            if (measured)
            {
                ScalingResult result = benchmark.Results[index];
                Raylib.DrawText(Format(result.MedianMilliseconds, "F2"), columns[1], y, 16, color);
                Raylib.DrawText($"{Format(result.Speedup, "F2")}x", columns[2], y, 16, color);
                Raylib.DrawText(Format(result.Efficiency, "P0"), columns[3], y, 16, color);
            }
            else
            {
                Raylib.DrawText("--", columns[1], y, 16, MutedText);
            }

            y += 24;
        }

        y += 10;
        Raylib.DrawText("* núcleos lógicos deste computador", x, y, 14, Warning);
        y += 26;
        Raylib.DrawText("Speedup = tempo com 1 thread / tempo com N", x, y, 15, MutedText);
        y += 20;
        Raylib.DrawText("Eficiência = speedup / N", x, y, 15, MutedText);
        y += 30;

        if (!benchmark.IsComplete || best is not ScalingResult bestResult)
        {
            return;
        }

        ScalingResult last = benchmark.Results[^1];
        Raylib.DrawText(
            $"Melhor: {Format(bestResult.Speedup, "F2")}x com {bestResult.ThreadCount} threads",
            x,
            y,
            17,
            Good);
        y += 24;
        Raylib.DrawText(
            $"Com {last.ThreadCount} threads: {Format(last.Speedup, "F2")}x " +
            $"(eficiência {Format(last.Efficiency, "P0")})",
            x,
            y,
            17,
            Text);
        y += 32;
        Raylib.DrawText("Depois dos núcleos, threads extras disputam a", x, y, 15, MutedText);
        y += 19;
        Raylib.DrawText("CPU: trocas de contexto e sincronização", x, y, 15, MutedText);
        y += 19;
        Raylib.DrawText("consomem o ganho, e o tempo volta a subir.", x, y, 15, MutedText);
    }

    private void RefreshTimelineSummary(FrameView view, long now)
    {
        bool rowsChanged = _busyFractions.Count != view.Timelines.Count;

        bool clockWasReset =
            view.ElapsedSeconds + view.Config.MetricsDisplayRefreshSeconds < _nextTimelineRefreshAtSeconds;

        if (!rowsChanged && !clockWasReset && view.ElapsedSeconds < _nextTimelineRefreshAtSeconds)
        {
            return;
        }

        if (!view.Paused || rowsChanged)
        {
            long historyTicks = (long)(view.Config.TimelineHistorySeconds * Stopwatch.Frequency);
            _busyFractions.Clear();

            foreach (ThreadTimeline timeline in view.Timelines)
            {
                _busyFractions.Add(timeline.BusyFraction(now - historyTicks, now));
            }

            double frameMs = view.Metrics.AverageFrameMilliseconds;

            if (frameMs > 0)
            {
                double target = frameMs * 6;
                _timelineWindowMs = TimelineWindowOptionsMs.FirstOrDefault(
                    option => option >= target,
                    TimelineWindowOptionsMs[^1]);
            }
        }

        _nextTimelineRefreshAtSeconds = view.ElapsedSeconds + view.Config.MetricsDisplayRefreshSeconds;
    }

    private static Color GetSegmentColor(TimelineSegmentKind kind, SimulationState state)
    {
        return kind switch
        {
            TimelineSegmentKind.WaitingForWorkers => WaitingColor,
            TimelineSegmentKind.Rendering => RenderingColor,
            TimelineSegmentKind.Arbitration => ArbitrationColor,
            _ => ToRaylib(state.Civilizations[(int)kind % state.Civilizations.Length].Color),
        };
    }

    private static int DrawLegendItem(int x, int y, Color color, string label)
    {
        Raylib.DrawRectangle(x, y + 3, 10, 10, color);
        Raylib.DrawText(label, x + 14, y, 14, MutedText);
        return x + 14 + Raylib.MeasureText(label, 14) + 12;
    }

    private static int DrawTextAt(string text, int x, int y, int fontSize, Color color)
    {
        Raylib.DrawText(text, x, y, fontSize, color);
        return Raylib.MeasureText(text, fontSize);
    }

    private static void DrawDashedLine(Vector2 start, Vector2 end, Color color)
    {
        const float dash = 8f;
        const float gap = 6f;
        float length = Vector2.Distance(start, end);

        if (length <= 0)
        {
            return;
        }

        Vector2 direction = (end - start) / length;

        for (float offset = 0; offset < length; offset += dash + gap)
        {
            Vector2 dashStart = start + (direction * offset);
            Vector2 dashEnd = start + (direction * MathF.Min(offset + dash, length));
            Raylib.DrawLineEx(dashStart, dashEnd, 2f, color);
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

    private static string Format(double value, string format)
    {
        return value.ToString(format, DisplayCulture);
    }

    private static string Format(long value, string format)
    {
        return value.ToString(format, DisplayCulture);
    }

    private static void DrawMetric(string label, string value, int x, int y)
    {
        Raylib.DrawText($"{label}:", x, y, 16, MutedText);
        Raylib.DrawText(value, x + Raylib.MeasureText($"{label}: ", 16), y, 18, Text);
    }

    private static void DrawButton(FloatRectangle rectangle, string label, bool active)
    {
        DrawButton(rectangle, label, active ? ActiveButton : InactiveButton);
    }

    private static void DrawButton(FloatRectangle rectangle, string label, Color background)
    {
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
