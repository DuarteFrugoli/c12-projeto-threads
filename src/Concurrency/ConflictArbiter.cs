using System.Diagnostics;
using C12ProjetoCiv.Core;
using C12ProjetoCiv.Metrics;

namespace C12ProjetoCiv.Concurrency;

/// <summary>
/// Thread consumidora que processa os relatórios de presença enviados pelos workers.
/// A cada rodada de tempo real, a civilização com mais agentes na mina vence a disputa:
/// todos deixam a mina, as derrotadas entregam a carga como saque e perdem parte dos
/// agentes. Os workers nunca esperam por essa decisão: eles apenas leem a última
/// <see cref="MineControl"/> publicada.
/// </summary>
public sealed class ConflictArbiter : IDisposable
{
    private const int RecentBattleCapacity = 4;

    private readonly ContestedMine _mine;
    private readonly BoundedBuffer<MinePresenceReport> _reports;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Thread _thread;
    private readonly double _roundSeconds;
    private readonly double _casualtyRate;
    private readonly double _protectionRatio;
    private readonly int[] _latestPopulation;
    private readonly double[] _roundPresence;
    private readonly int[] _victories;
    private readonly object _historyGate = new();
    private readonly Queue<BattleRecord> _recentBattles = new(RecentBattleCapacity);
    private int _currentRound = -1;
    private long _generation;
    private long _reportsProcessed;
    private long _battles;
    private Exception? _failure;
    private bool _disposed;

    public ConflictArbiter(ContestedMine mine, SimulationConfig config)
    {
        _mine = mine;
        _roundSeconds = config.ConflictRoundSeconds;
        _casualtyRate = config.MineBattleCasualtyRate;
        _protectionRatio = config.MineBattleProtectionRatio;
        _latestPopulation = new int[config.CivilizationCount];
        _roundPresence = new double[config.CivilizationCount];
        _victories = new int[config.CivilizationCount];
        _reports = new BoundedBuffer<MinePresenceReport>(config.ConflictBufferCapacity);
        Timeline = new ThreadTimeline("Árbitro");
        _thread = new Thread(ArbiterLoop)
        {
            IsBackground = true,
            Name = "Conflict Arbiter",
        };

        _mine.AttachReports(_reports);
        _thread.Start();
    }

    public ThreadTimeline Timeline { get; }
    public int ManagedThreadId => _thread.ManagedThreadId;
    public long ReportsProcessed => Interlocked.Read(ref _reportsProcessed);
    public long Battles => Interlocked.Read(ref _battles);
    public int QueueLength => _reports.Count;
    public int QueueCapacity => _reports.Capacity;
    public long ProducerBlocks => _reports.ProducerBlocks;

    public int GetVictories(int civilizationId)
    {
        return Volatile.Read(ref _victories[civilizationId]);
    }

    public BattleRecord[] GetRecentBattles()
    {
        lock (_historyGate)
        {
            return _recentBattles.Reverse().ToArray();
        }
    }

    public void ThrowIfFailed()
    {
        Exception? failure = Volatile.Read(ref _failure);

        if (failure is not null)
        {
            throw new InvalidOperationException(
                "A thread árbitro falhou. Consulte a exceção interna.",
                failure);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _mine.DetachReports();
        _cancellation.Cancel();
        _reports.Close();

        if (!_thread.Join(TimeSpan.FromSeconds(2)))
        {
            throw new InvalidOperationException("A thread árbitro não encerrou dentro do tempo esperado.");
        }

        _reports.Dispose();
        _cancellation.Dispose();
    }

    private void ArbiterLoop()
    {
        try
        {
            while (_reports.TryTake(out MinePresenceReport report, _cancellation.Token))
            {
                long startedAt = Stopwatch.GetTimestamp();
                Process(report);
                Interlocked.Increment(ref _reportsProcessed);
                Timeline.Record(startedAt, Stopwatch.GetTimestamp(), TimelineSegmentKind.Arbitration);
            }
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
            // Encerramento normal.
        }
        catch (Exception exception)
        {
            Volatile.Write(ref _failure, exception);
            _mine.DetachReports();
            _reports.Close();
        }
    }

    private void Process(MinePresenceReport report)
    {
        int round = (int)Math.Floor(report.ElapsedSeconds / _roundSeconds);

        if (_currentRound < 0)
        {
            _currentRound = round;
        }
        else if (round > _currentRound)
        {
            ResolveRound();
            Array.Clear(_roundPresence);
            _currentRound = round;
        }

        // Relatórios atrasados da rodada anterior entram na rodada atual.
        _roundPresence[report.CivilizationId] += report.MinersAtMine * report.DeltaSeconds;
        _latestPopulation[report.CivilizationId] = report.Population;
    }

    private void ResolveRound()
    {
        if (!_mine.BattlesEnabled)
        {
            return;
        }

        int participants = 0;
        int winner = -1;

        for (int civilizationId = 0; civilizationId < _roundPresence.Length; civilizationId++)
        {
            if (_roundPresence[civilizationId] <= 0)
            {
                continue;
            }

            participants++;

            if (winner < 0 || _roundPresence[civilizationId] > _roundPresence[winner])
            {
                winner = civilizationId;
            }
        }

        if (participants == 0)
        {
            return;
        }

        bool contested = participants >= 2;

        if (!contested)
        {
            return;
        }

        // Civilizações com menos da metade da população da vencedora recuam sem baixas,
        // para que a maior não elimine as outras.
        double[] casualtyRates = new double[_roundPresence.Length];
        bool[] retreated = new bool[_roundPresence.Length];

        for (int civilizationId = 0; civilizationId < _roundPresence.Length; civilizationId++)
        {
            if (civilizationId == winner || _roundPresence[civilizationId] <= 0)
            {
                continue;
            }

            bool muchSmaller =
                _latestPopulation[civilizationId] < _latestPopulation[winner] * _protectionRatio;
            retreated[civilizationId] = muchSmaller;
            casualtyRates[civilizationId] = muchSmaller ? 0 : _casualtyRate;
        }

        _generation++;
        _mine.PublishControl(new MineControl(_generation, winner, _currentRound, contested, casualtyRates));

        Interlocked.Increment(ref _battles);
        Interlocked.Increment(ref _victories[winner]);
        double[] averageMiners = _roundPresence
            .Select(presence => presence / _roundSeconds)
            .ToArray();

        lock (_historyGate)
        {
            if (_recentBattles.Count == RecentBattleCapacity)
            {
                _recentBattles.Dequeue();
            }

            _recentBattles.Enqueue(new BattleRecord(_currentRound, winner, averageMiners, retreated, participants));
        }
    }
}
