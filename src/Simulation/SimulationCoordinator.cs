using C12ProjetoCiv.Core;
using C12ProjetoCiv.Entities;

namespace C12ProjetoCiv.Simulation;

/// <summary>
/// Mantém workers persistentes e executa duas fases de barreira por ciclo:
/// liberação do trabalho e conclusão do trabalho.
/// </summary>
public sealed class SimulationCoordinator : IDisposable
{
    private readonly SimulationState _state;
    private readonly SimulationConfig _config;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Barrier _barrier;
    private readonly Thread[] _threads;
    private SimulationFrameCommand _currentCommand;
    private Exception? _workerException;
    private bool _disposed;

    public SimulationCoordinator(
        SimulationState state,
        SimulationConfig config,
        WorkerMode mode)
    {
        _state = state;
        _config = config;
        Mode = mode;
        WorkerCount = (int)mode;
        ThreadsControlledByProject = WorkerCount + 1;
        _barrier = new Barrier(WorkerCount + 1);
        _threads = new Thread[WorkerCount];

        for (int workerIndex = 0; workerIndex < WorkerCount; workerIndex++)
        {
            int capturedIndex = workerIndex;
            Thread thread = new(() => WorkerLoop(capturedIndex))
            {
                IsBackground = true,
                Name = $"Simulation Worker {workerIndex + 1}",
            };

            _threads[workerIndex] = thread;
            thread.Start();
        }
    }

    public WorkerMode Mode { get; }
    public int WorkerCount { get; }
    public int ThreadsControlledByProject { get; }

    public void ExecuteFrame(SimulationFrameCommand command)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ThrowIfWorkerFailed();
        _currentCommand = command;

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

    private void WorkerLoop(int workerIndex)
    {
        int civilizationsPerWorker = _state.Civilizations.Length / WorkerCount;
        int firstCivilization = workerIndex * civilizationsPerWorker;
        int civilizationLimit = firstCivilization + civilizationsPerWorker;

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
                    Civilization civilization = _state.Civilizations[civilizationIndex];
                    civilization.Update(command, _config);
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
