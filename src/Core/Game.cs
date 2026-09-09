using System.Diagnostics;
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
    private SimulationState _state;
    private SimulationCoordinator _coordinator;
    private bool _paused;
    private bool _stressMode;
    private bool _disposed;
    private bool _windowInitialized;

    public Game(SimulationConfig config, WorkerMode initialMode = WorkerMode.One)
    {
        _config = config;
        _config.Validate();
        _state = SimulationState.Create(config);
        _coordinator = new SimulationCoordinator(_state, config, initialMode);
        _metrics = new PerformanceMetrics(config);
        _growthController = new PopulationGrowthController(config);
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

        Raylib.InitWindow(_config.WindowWidth, _config.WindowHeight, _config.WindowTitle);
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
                _metrics.RecordSimulation(
                    HighResolutionTime.ElapsedMilliseconds(simulationStartedAt));
                simulationWasUpdated = true;
            }

            long renderStartedAt = Stopwatch.GetTimestamp();
            Raylib.BeginDrawing();
            _renderer.Draw(
                _state,
                _config,
                _metrics,
                _growthController,
                _coordinator,
                _clock.ElapsedSeconds,
                _paused,
                _stressMode,
                Raylib.IsWindowFullscreen());
            Raylib.EndDrawing();

            double renderMilliseconds = HighResolutionTime.ElapsedMilliseconds(renderStartedAt);
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

        if (_windowInitialized)
        {
            Raylib.CloseWindow();
            _windowInitialized = false;
        }
    }

    private bool ApplyUiCommand(UiCommand command)
    {
        bool resetFrameMeasurement = false;

        if (command.TogglePause)
        {
            _paused = !_paused;

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
            ChangeWorkerMode(WorkerModeExtensions.FromCount(workerCount));
            resetFrameMeasurement = true;
        }

        if (command.ToggleStressMode)
        {
            _stressMode = !_stressMode;
        }

        if (command.ToggleFullscreenMode)
        {
            Raylib.ToggleFullscreen();
            ResetMetricsAndDelta();
            resetFrameMeasurement = true;
        }

        return resetFrameMeasurement;
    }

    private void ChangeWorkerMode(WorkerMode mode)
    {
        _coordinator.Dispose();
        _coordinator = new SimulationCoordinator(_state, _config, mode);
        ResetMetricsAndDelta();
    }

    private void RestartSimulation()
    {
        WorkerMode currentMode = _coordinator.Mode;
        _coordinator.Dispose();
        _state = SimulationState.Create(_config);
        _coordinator = new SimulationCoordinator(_state, _config, currentMode);
        _stressMode = false;
        _growthController.Reset();
        _clock.Reset(startImmediately: !_paused);
        _metrics.Reset(HighResolutionTime.NowSeconds);
    }

    private void ResetMetricsAndDelta()
    {
        _metrics.Reset(HighResolutionTime.NowSeconds);
        _clock.ResetDeltaSample();
    }
}
