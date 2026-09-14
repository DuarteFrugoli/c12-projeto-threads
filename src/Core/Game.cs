using System.Diagnostics;
using C12ProjetoCiv.Concurrency;
using C12ProjetoCiv.Diagnostics;
using C12ProjetoCiv.Metrics;
using C12ProjetoCiv.Rendering;
using C12ProjetoCiv.Simulation;
using Raylib_cs;

namespace C12ProjetoCiv.Core;

public sealed class Game : IDisposable
{
    private readonly SimulationConfig _config;
    private readonly Renderer _renderer = new();
    private readonly SimulationClock _clock = new();
    private readonly PerformanceMetrics _metrics;
    private readonly PopulationGrowthController _growthController;
    private readonly MineMetrics _mineMetrics = new();
    private readonly ThreadTimeline _mainTimeline = new("Principal");
    private readonly ScalingBenchmark _scalingBenchmark;
    private readonly List<ThreadTimeline> _timelines = new();
    private SimulationState _state;
    private SimulationCoordinator _coordinator;
    private ConflictArbiter? _arbiter;
    private bool _paused;
    private bool _stressMode;
    private bool _scalingViewOpen;
    private bool _pausedBeforeScaling;
    private bool _disposed;
    private bool _windowInitialized;

    public Game(SimulationConfig config, WorkerMode initialMode = WorkerMode.One)
    {
        _config = config;
        _config.Validate();
        _state = SimulationState.Create(config);
        _arbiter = CreateArbiter(_state);
        _coordinator = CreateCoordinator(_state, (int)initialMode);
        _metrics = new PerformanceMetrics(config);
        _growthController = new PopulationGrowthController(config);
        _scalingBenchmark = new ScalingBenchmark(config);
        RebuildTimelineList();
    }

    public PerformanceMetrics Metrics => _metrics;
    public int WorkerCount => _coordinator.WorkerCount;

    public void Run(
        int? frameLimit = null,
        bool hiddenWindow = false,
        string? screenshotPath = null)
    {
        if (hiddenWindow)
        {
            Raylib.SetConfigFlags(ConfigFlags.HiddenWindow);
        }

        // A área lógica (1600 x 900) é maior que muitas telas de notebook, e no macOS criar
        // uma janela maior que o monitor derruba o GLFW. Redimensionar depois quebra a área
        // de desenho em telas Retina, por isso a janela já nasce no tamanho final.
        // A câmera virtual escala o conteúdo; F11 amplia para a tela cheia.
        (int windowWidth, int windowHeight) = ScaleToFit(1_280, 720);
        Raylib.InitWindow(windowWidth, windowHeight, _config.WindowTitle);
        _windowInitialized = true;
        int renderedFrames = 0;

        while ((frameLimit is null || renderedFrames < frameLimit) && !Raylib.WindowShouldClose())
        {
            long frameStartedAt = Stopwatch.GetTimestamp();
            UiCommand uiCommand = UserInterface.ReadCommand(_config);
            bool resetFrameMeasurement = ApplyUiCommand(uiCommand);

            if (resetFrameMeasurement)
            {
                frameStartedAt = Stopwatch.GetTimestamp();
            }

            _arbiter?.ThrowIfFailed();
            double deltaSeconds = _clock.SampleDeltaSeconds();
            _growthController.Update(
                _metrics.AverageFps,
                _metrics.IsFpsWindowReady,
                deltaSeconds,
                _paused,
                benchmarkMode: false,
                _state.HasReachedPopulationLimit(_config));

            bool simulationWasUpdated = false;

            if (!_paused)
            {
                SimulationFrameCommand command = new(
                    deltaSeconds,
                    _clock.ElapsedSeconds,
                    _growthController.CanGrow,
                    _stressMode);
                long simulationStartedAt = Stopwatch.GetTimestamp();
                _coordinator.ExecuteFrame(command);
                long simulationCompletedAt = Stopwatch.GetTimestamp();
                _mainTimeline.Record(
                    simulationStartedAt,
                    simulationCompletedAt,
                    TimelineSegmentKind.WaitingForWorkers);
                _metrics.RecordSimulation(
                    Stopwatch.GetElapsedTime(simulationStartedAt, simulationCompletedAt).TotalMilliseconds);
                _mineMetrics.Sample(_state, _arbiter, _clock.ElapsedSeconds);
                simulationWasUpdated = true;
            }

            long renderStartedAt = Stopwatch.GetTimestamp();
            Raylib.BeginDrawing();
            _renderer.Draw(new FrameView(
                _state,
                _config,
                _metrics,
                _growthController,
                _coordinator,
                _arbiter,
                _mineMetrics,
                _timelines,
                _scalingViewOpen ? _scalingBenchmark : null,
                _clock.ElapsedSeconds,
                _paused,
                _stressMode,
                Raylib.IsWindowFullscreen()));
            Raylib.EndDrawing();

            long renderCompletedAt = Stopwatch.GetTimestamp();
            double renderMilliseconds = Stopwatch
                .GetElapsedTime(renderStartedAt, renderCompletedAt)
                .TotalMilliseconds;

            if (!_paused)
            {
                _mainTimeline.Record(renderStartedAt, renderCompletedAt, TimelineSegmentKind.Rendering);
            }

            long frameCompletedAt = Stopwatch.GetTimestamp();
            double frameDurationSeconds = Stopwatch
                .GetElapsedTime(frameStartedAt, frameCompletedAt)
                .TotalSeconds;

            _metrics.RecordFrame(
                (double)frameCompletedAt / Stopwatch.Frequency,
                frameDurationSeconds,
                renderMilliseconds,
                simulationWasUpdated);
            renderedFrames++;

            if (screenshotPath is not null && renderedFrames == frameLimit)
            {
                Raylib.TakeScreenshot(screenshotPath);
            }

            // Mede uma quantidade de threads por frame, depois de desenhar o progresso.
            if (_scalingViewOpen && !_scalingBenchmark.IsComplete)
            {
                _scalingBenchmark.MeasureNext();
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _coordinator.Dispose();
        _arbiter?.Dispose();

        if (_windowInitialized)
        {
            Raylib.CloseWindow();
            _windowInitialized = false;
        }
    }

    private bool ApplyUiCommand(UiCommand command)
    {
        bool resetFrameMeasurement = false;

        if (command.ToggleScalingView)
        {
            ToggleScalingView();
            resetFrameMeasurement = true;
        }

        if (_scalingViewOpen)
        {
            if (command.RerunScaling && _scalingBenchmark.IsComplete)
            {
                _scalingBenchmark.Reset();
            }

            // Enquanto a tela de escalabilidade está aberta, a simulação fica parada.
            return resetFrameMeasurement;
        }

        if (command.TogglePause)
        {
            SetPaused(!_paused);
            resetFrameMeasurement = true;
        }

        if (command.Restart)
        {
            RestartSimulation();
            resetFrameMeasurement = true;
        }

        if (command.RequestedWorkerCount is int workerCount &&
            workerCount != _coordinator.WorkerCount)
        {
            ChangeWorkerCount(workerCount);
            resetFrameMeasurement = true;
        }

        if (command.ToggleStressMode)
        {
            _stressMode = !_stressMode;
        }

        if (command.CycleMineSyncMode && _state.Mine is not null)
        {
            // Seguro: os workers estão parados na barreira entre ciclos.
            _state.Mine.SyncMode = _state.Mine.SyncMode.Next();
            _mineMetrics.ResetAnomalyBaseline(_state);
        }

        if (command.ToggleBattles && _state.Mine is not null)
        {
            _state.Mine.BattlesEnabled = !_state.Mine.BattlesEnabled;
        }

        if (command.ToggleFullscreenMode)
        {
            Raylib.ToggleFullscreen();
            ResetMetricsAndDelta();
            resetFrameMeasurement = true;
        }

        return resetFrameMeasurement;
    }

    private void ToggleScalingView()
    {
        _scalingViewOpen = !_scalingViewOpen;

        if (_scalingViewOpen)
        {
            _pausedBeforeScaling = _paused;
            SetPaused(true);

            if (_scalingBenchmark.IsComplete)
            {
                _scalingBenchmark.Reset();
            }

            return;
        }

        SetPaused(_pausedBeforeScaling);
        ResetMetricsAndDelta();
    }

    private void SetPaused(bool paused)
    {
        if (paused == _paused)
        {
            return;
        }

        _paused = paused;

        if (_paused)
        {
            _clock.Pause();
            _clock.ResetDeltaSample();
        }
        else
        {
            _clock.Resume();
            ResetMetricsAndDelta();
        }
    }

    private void ChangeWorkerCount(int workerCount)
    {
        _coordinator.Dispose();
        _coordinator = CreateCoordinator(_state, workerCount);
        RebuildTimelineList();
        ResetMetricsAndDelta();
    }

    private void RestartSimulation()
    {
        int currentWorkerCount = _coordinator.WorkerCount;
        MineSyncMode? currentSyncMode = _state.Mine?.SyncMode;
        bool battlesEnabled = _state.Mine?.BattlesEnabled ?? _config.MineBattlesEnabled;
        _coordinator.Dispose();
        _arbiter?.Dispose();
        _state = SimulationState.Create(_config);

        if (_state.Mine is not null && currentSyncMode is MineSyncMode syncMode)
        {
            _state.Mine.SyncMode = syncMode;
            _state.Mine.BattlesEnabled = battlesEnabled;
        }

        _arbiter = CreateArbiter(_state);
        _coordinator = CreateCoordinator(_state, currentWorkerCount);
        RebuildTimelineList();
        _stressMode = false;
        _growthController.Reset();
        _mineMetrics.Reset();
        _clock.Reset(startImmediately: !_paused);
        _metrics.Reset(HighResolutionTime.NowSeconds);
    }

    private SimulationCoordinator CreateCoordinator(SimulationState state, int workerCount)
    {
        return new SimulationCoordinator(
            state.Civilizations,
            _config,
            workerCount,
            state.Mine,
            recordTimeline: true);
    }

    private ConflictArbiter? CreateArbiter(SimulationState state)
    {
        return state.Mine is null ? null : new ConflictArbiter(state.Mine, _config);
    }

    private void RebuildTimelineList()
    {
        _timelines.Clear();
        _timelines.Add(_mainTimeline);
        _timelines.AddRange(_coordinator.WorkerTimelines);

        if (_arbiter is not null)
        {
            _timelines.Add(_arbiter.Timeline);
        }
    }

    private void ResetMetricsAndDelta()
    {
        _metrics.Reset(HighResolutionTime.NowSeconds);
        _clock.ResetDeltaSample();
    }

    private (int Width, int Height) ScaleToFit(int maxWidth, int maxHeight)
    {
        float scale = MathF.Min(
            1f,
            MathF.Min(maxWidth / (float)_config.WindowWidth, maxHeight / (float)_config.WindowHeight));
        return ((int)(_config.WindowWidth * scale), (int)(_config.WindowHeight * scale));
    }
}
