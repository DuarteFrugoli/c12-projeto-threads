using System.Numerics;
using C12ProjetoCiv.Simulation;

namespace C12ProjetoCiv.Entities;

public readonly record struct ResourceNodeStatistics(
    int WaitingCount,
    int ActiveCollectors,
    long Admissions,
    double TotalWaitSeconds);

/// <summary>
/// Ponto renovável com capacidade limitada de coleta. O estoque não acaba, mas o
/// <see cref="SemaphoreSlim"/> limita quantos agentes podem coletar simultaneamente.
/// Os demais aguardam numa fila escolhida por FCFS ou por prioridade com aging.
/// </summary>
public sealed class ResourceNode : IDisposable
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _collectorSlots;
    private readonly List<WaitingAgent> _waiting = new();
    private readonly HashSet<int> _queuedAgentIds = new();
    private readonly HashSet<int> _collectingAgentIds = new();
    private long _nextArrivalSequence;
    private long _admissions;
    private double _totalWaitSeconds;
    private bool _disposed;

    public ResourceNode(Vector2 position, int collectionCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(collectionCapacity);
        Position = position;
        CollectionCapacity = collectionCapacity;
        _collectorSlots = new SemaphoreSlim(collectionCapacity, collectionCapacity);
    }

    public Vector2 Position { get; }
    public int CollectionCapacity { get; }

    public int WaitingCount
    {
        get
        {
            lock (_gate)
            {
                return _waiting.Count;
            }
        }
    }

    public int ActiveCollectors
    {
        get
        {
            lock (_gate)
            {
                return _collectingAgentIds.Count;
            }
        }
    }

    public long Admissions
    {
        get
        {
            lock (_gate)
            {
                return _admissions;
            }
        }
    }

    public double TotalWaitSeconds
    {
        get
        {
            lock (_gate)
            {
                return _totalWaitSeconds;
            }
        }
    }

    public ResourceNodeStatistics GetStatistics()
    {
        lock (_gate)
        {
            return new ResourceNodeStatistics(
                _waiting.Count,
                _collectingAgentIds.Count,
                _admissions,
                _totalWaitSeconds);
        }
    }

    /// <summary>
    /// Coloca o agente na fila uma única vez. Retorna false se ele já estiver esperando
    /// ou coletando neste ponto.
    /// </summary>
    public bool Enqueue(Agent agent, double elapsedSeconds)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_gate)
        {
            if (_collectingAgentIds.Contains(agent.Id) || !_queuedAgentIds.Add(agent.Id))
            {
                return false;
            }

            _waiting.Add(new WaitingAgent(
                agent,
                Math.Max(0, elapsedSeconds),
                _nextArrivalSequence++));
            return true;
        }
    }

    /// <summary>
    /// Admite tantos agentes quanto o semáforo permitir. A espera é não bloqueante para
    /// que uma fila cheia não pare o worker responsável por toda a civilização.
    /// </summary>
    public int AdmitWaiting(
        ResourceSchedulingMode mode,
        double elapsedSeconds,
        double agingIntervalSeconds,
        int maximumAgentLevel)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(agingIntervalSeconds);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumAgentLevel);
        int admitted = 0;

        lock (_gate)
        {
            while (_waiting.Count > 0 && _collectorSlots.Wait(0))
            {
                int selectedIndex = SelectNextIndex(
                    mode,
                    elapsedSeconds,
                    agingIntervalSeconds,
                    maximumAgentLevel);
                WaitingAgent selected = _waiting[selectedIndex];
                _waiting.RemoveAt(selectedIndex);
                _queuedAgentIds.Remove(selected.Agent.Id);

                if (!_collectingAgentIds.Add(selected.Agent.Id))
                {
                    _collectorSlots.Release();
                    throw new InvalidOperationException(
                        $"O agente {selected.Agent.Id} já estava coletando neste recurso.");
                }

                double waited = Math.Max(0, elapsedSeconds - selected.ArrivedAtSeconds);
                _totalWaitSeconds += waited;
                _admissions++;
                admitted++;
                selected.Agent.BeginCollecting(elapsedSeconds);
            }
        }

        return admitted;
    }

    /// <summary>Libera a vaga do semáforo quando a coleta termina.</summary>
    public void Release(int agentId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_gate)
        {
            if (!_collectingAgentIds.Remove(agentId))
            {
                throw new InvalidOperationException(
                    $"O agente {agentId} tentou liberar um recurso que não ocupava.");
            }

            _collectorSlots.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _collectorSlots.Dispose();
    }

    private int SelectNextIndex(
        ResourceSchedulingMode mode,
        double elapsedSeconds,
        double agingIntervalSeconds,
        int maximumAgentLevel)
    {
        if (mode == ResourceSchedulingMode.Fcfs)
        {
            return 0;
        }

        int selectedIndex = 0;
        int selectedPriority = GetEffectivePriority(
            _waiting[0],
            elapsedSeconds,
            agingIntervalSeconds,
            maximumAgentLevel);

        for (int index = 1; index < _waiting.Count; index++)
        {
            WaitingAgent candidate = _waiting[index];
            int candidatePriority = GetEffectivePriority(
                candidate,
                elapsedSeconds,
                agingIntervalSeconds,
                maximumAgentLevel);
            WaitingAgent selected = _waiting[selectedIndex];

            if (candidatePriority > selectedPriority ||
                (candidatePriority == selectedPriority &&
                 candidate.ArrivalSequence < selected.ArrivalSequence))
            {
                selectedIndex = index;
                selectedPriority = candidatePriority;
            }
        }

        return selectedIndex;
    }

    private static int GetEffectivePriority(
        WaitingAgent waiting,
        double elapsedSeconds,
        double agingIntervalSeconds,
        int maximumAgentLevel)
    {
        double waitSeconds = Math.Max(0, elapsedSeconds - waiting.ArrivedAtSeconds);
        int agingBonus = (int)Math.Floor(waitSeconds / agingIntervalSeconds);
        return Math.Min(maximumAgentLevel, waiting.Agent.Level + agingBonus);
    }

    private readonly record struct WaitingAgent(
        Agent Agent,
        double ArrivedAtSeconds,
        long ArrivalSequence);
}
