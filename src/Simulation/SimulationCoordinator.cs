using System.Diagnostics;
using C12ProjetoCiv.Concurrency;
using C12ProjetoCiv.Core;
using C12ProjetoCiv.Entities;
using C12ProjetoCiv.Metrics;

namespace C12ProjetoCiv.Simulation;

/// <summary>
/// Mantém workers persistentes e executa duas fases de barreira por ciclo:
/// liberação do trabalho e conclusão do trabalho.
/// </summary>
public sealed class SimulationCoordinator : IDisposable
{
    private readonly Civilization[] _civilizations;
    private readonly SimulationConfig _config;
    private readonly ContestedMine? _mine;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Barrier _barrier;
    private readonly Thread[] _threads;
    private readonly int[] _firstCivilizationOfWorker;
    private readonly int[] _civilizationLimitOfWorker;
    private readonly int[] _workerOfCivilization;
    private readonly ThreadTimeline[]? _workerTimelines;
    private SimulationFrameCommand _currentCommand;
    private Exception? _workerException;
    private bool _disposed;

    public SimulationCoordinator(
        SimulationState state,
        SimulationConfig config,
        WorkerMode mode,
        bool recordTimeline = false)
        : this(state.Civilizations, config, (int)mode, state.Mine, recordTimeline)
    {
    }

    public SimulationCoordinator(
        Civilization[] civilizations,
        SimulationConfig config,
        int workerCount,
        ContestedMine? mine = null,
        bool recordTimeline = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(workerCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(workerCount, civilizations.Length);

        _civilizations = civilizations;
        _config = config;
        _mine = mine;
        WorkerCount = workerCount;
        ThreadsControlledByProject = WorkerCount + 1;
        _barrier = new Barrier(WorkerCount + 1);
        _threads = new Thread[WorkerCount];
        _firstCivilizationOfWorker = new int[WorkerCount];
        _civilizationLimitOfWorker = new int[WorkerCount];
        _workerOfCivilization = new int[civilizations.Length];
        _workerTimelines = recordTimeline ? new ThreadTimeline[WorkerCount] : null;
        PartitionCivilizations();

        for (int workerIndex = 0; workerIndex < WorkerCount; workerIndex++)
        {
            int capturedIndex = workerIndex;
            Thread thread = new(() => WorkerLoop(capturedIndex))
            {
                IsBackground = true,
                Name = $"Simulation Worker {workerIndex + 1}",
            };

            if (_workerTimelines is not null)
            {
                _workerTimelines[workerIndex] = new ThreadTimeline($"Worker {workerIndex + 1}");
            }

            _threads[workerIndex] = thread;
            thread.Start();
        }
    }

    public int WorkerCount { get; }
    public int ThreadsControlledByProject { get; }
    public IReadOnlyList<ThreadTimeline> WorkerTimelines =>
        _workerTimelines ?? Array.Empty<ThreadTimeline>();

    public int GetWorkerNumberForCivilization(int civilizationId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(civilizationId);

        if (civilizationId >= _civilizations.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(civilizationId));
        }

        return _workerOfCivilization[civilizationId] + 1;
    }

    public int GetManagedThreadIdForCivilization(int civilizationId)
    {
        int workerIndex = GetWorkerNumberForCivilization(civilizationId) - 1;
        return _threads[workerIndex].ManagedThreadId;
    }

    public int GetManagedThreadIdForWorker(int workerNumber)
    {
        return _threads[workerNumber - 1].ManagedThreadId;
    }

    public void ExecuteFrame(SimulationFrameCommand command)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ThrowIfWorkerFailed();
        _currentCommand = command;

        // Fase sequencial: somente a thread principal regenera a mina, com os workers parados.
        _mine?.Regenerate(command.DeltaSeconds);

        try
        {
            _barrier.SignalAndWait(_cancellation.Token);
            _barrier.SignalAndWait(_cancellation.Token);
        }
        catch (OperationCanceledException) when (_workerException is not null)
        {
            ThrowIfWorkerFailed();
            throw;
        }

        ThrowIfWorkerFailed();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cancellation.Cancel();

        foreach (Thread thread in _threads)
        {
            if (!thread.Join(TimeSpan.FromSeconds(2)))
            {
                throw new InvalidOperationException(
                    $"O worker '{thread.Name}' não encerrou dentro do tempo esperado.");
            }
        }

        _barrier.Dispose();
        _cancellation.Dispose();
    }

    /// <summary>
    /// Divide as civilizações em blocos contíguos. Quando a divisão não é exata,
    /// os primeiros workers recebem uma civilização a mais.
    /// </summary>
    private void PartitionCivilizations()
    {
        int baseCount = _civilizations.Length / WorkerCount;
        int remainder = _civilizations.Length % WorkerCount;
        int next = 0;

        for (int workerIndex = 0; workerIndex < WorkerCount; workerIndex++)
        {
            int count = baseCount + (workerIndex < remainder ? 1 : 0);
            _firstCivilizationOfWorker[workerIndex] = next;
            _civilizationLimitOfWorker[workerIndex] = next + count;

            for (int civilizationIndex = next; civilizationIndex < next + count; civilizationIndex++)
            {
                _workerOfCivilization[civilizationIndex] = workerIndex;
            }

            next += count;
        }
    }

    private void WorkerLoop(int workerIndex)
    {
        int firstCivilization = _firstCivilizationOfWorker[workerIndex];
        int civilizationLimit = _civilizationLimitOfWorker[workerIndex];
        ThreadTimeline? timeline = _workerTimelines?[workerIndex];
        WorkerContext context = new(workerIndex + 1, _cancellation.Token);

        try
        {
            while (true)
            {
                _barrier.SignalAndWait(_cancellation.Token);
                SimulationFrameCommand command = _currentCommand;

                for (int civilizationIndex = firstCivilization;
                     civilizationIndex < civilizationLimit;
                     civilizationIndex++)
                {
                    Civilization civilization = _civilizations[civilizationIndex];
                    long startedAt = Stopwatch.GetTimestamp();
                    civilization.Update(command, _config, context);
                    timeline?.Record(
                        startedAt,
                        Stopwatch.GetTimestamp(),
                        ThreadTimeline.KindForCivilization(civilization.Id));
                }

                _barrier.SignalAndWait(_cancellation.Token);
            }
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
            // Encerramento normal.
        }
        catch (Exception exception)
        {
            Interlocked.CompareExchange(ref _workerException, exception, null);
            _cancellation.Cancel();
        }
    }

    private void ThrowIfWorkerFailed()
    {
        Exception? exception = Volatile.Read(ref _workerException);

        if (exception is not null)
        {
            throw new InvalidOperationException(
                "Um worker de simulação falhou. Consulte a exceção interna.",
                exception);
        }
    }
}
