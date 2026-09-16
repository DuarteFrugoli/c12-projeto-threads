using System.Diagnostics;
using System.Numerics;
using C12ProjetoCiv.Core;

namespace C12ProjetoCiv.Concurrency;

/// <summary>
/// Mina central compartilhada pelas quatro civilizações. O estoque é o único dado da
/// simulação alterado por vários workers ao mesmo tempo, por isso é a região crítica
/// usada para demonstrar condição de corrida, exclusão mútua e operações atômicas.
/// </summary>
public sealed class ContestedMine
{
    private readonly object _gate = new();
    private readonly int _yieldComputationIterations;
    private readonly double _regenerationPerSecond;
    private readonly long[] _pendingLoot;
    private long _stock;
    private volatile bool _battlesEnabled;
    private double _pendingRegeneration;
    private MineControl _control = MineControl.Initial;
    private BoundedBuffer<MinePresenceReport>? _reports;

    public ContestedMine(Vector2 position, SimulationConfig config)
    {
        Position = position;
        Capacity = config.MineCapacity;
        InitialStock = config.MineCapacity;
        SyncMode = config.MineSyncMode;
        _stock = InitialStock;
        _yieldComputationIterations = config.MineYieldComputationIterations;
        _regenerationPerSecond = config.MineRegenerationPerSecond;
        _pendingLoot = new long[config.CivilizationCount];
        _battlesEnabled = config.MineBattlesEnabled;
    }

    public Vector2 Position { get; }
    public long Capacity { get; }
    public long InitialStock { get; }

    /// <summary>Alterado somente pela thread principal, enquanto os workers aguardam.</summary>
    public MineSyncMode SyncMode { get; set; }

    public long Stock => Interlocked.Read(ref _stock);

    /// <summary>Alterado pela thread principal e lido pelo árbitro, por isso é volátil.</summary>
    public bool BattlesEnabled
    {
        get => _battlesEnabled;
        set => _battlesEnabled = value;
    }

    /// <summary>Escrito somente pela thread principal, enquanto os workers aguardam.</summary>
    public long TotalRegenerated { get; private set; }

    public MineControl Control => Volatile.Read(ref _control);
    public BoundedBuffer<MinePresenceReport>? Reports => Volatile.Read(ref _reports);

    public void AttachReports(BoundedBuffer<MinePresenceReport> reports)
    {
        Volatile.Write(ref _reports, reports);
    }

    public void DetachReports()
    {
        Volatile.Write(ref _reports, null);
    }

    public void PublishControl(MineControl control)
    {
        Volatile.Write(ref _control, control);
    }

    /// <summary>
    /// Chamado pelo worker de uma civilização derrotada: entrega a carga à vencedora.
    /// A vencedora pode estar sendo atualizada por outro worker neste mesmo instante,
    /// então a transferência passa por uma caixa de mensagens atômica.
    /// </summary>
    public void SendLoot(int winnerCivilizationId, long units)
    {
        Interlocked.Add(ref _pendingLoot[winnerCivilizationId], units);
    }

    /// <summary>Chamado pelo worker da própria civilização para receber o saque pendente.</summary>
    public long CollectLoot(int civilizationId)
    {
        return Interlocked.Exchange(ref _pendingLoot[civilizationId], 0);
    }

    /// <summary>
    /// Fase sequencial executada pela thread principal antes de liberar os workers.
    /// </summary>
    public void Regenerate(double deltaSeconds)
    {
        _pendingRegeneration += _regenerationPerSecond * Math.Max(0, deltaSeconds);
        long units = (long)_pendingRegeneration;

        if (units <= 0)
        {
            return;
        }

        _pendingRegeneration -= units;
        long current = Interlocked.Read(ref _stock);
        long added = Math.Clamp(Capacity - current, 0, units);

        if (added > 0)
        {
            Interlocked.Add(ref _stock, added);
            TotalRegenerated += added;
        }
    }

    /// <summary>
    /// Executado pelos workers. Retorna quantas unidades o agente conseguiu extrair.
    /// </summary>
    public int Extract(MineExtractionStats stats)
    {
        stats.Attempts++;
        int extracted = SyncMode switch
        {
            MineSyncMode.Unsynchronized => ExtractUnsynchronized(),
            MineSyncMode.Lock => ExtractWithLock(stats),
            MineSyncMode.Interlocked => ExtractWithCompareExchange(stats),
            _ => throw new InvalidOperationException($"Modo de sincronização desconhecido: {SyncMode}."),
        };
        stats.UnitsExtracted += extracted;
        return extracted;
    }

    private int ExtractUnsynchronized()
    {
        // 1. lê o valor compartilhado
        long observed = _stock;

        // 2. calcula usando uma cópia que pode ficar desatualizada
        int yield = ComputeYield(observed);

        if (yield == 0)
        {
            return 0;
        }

        // 3. grava por cima: se outra thread gravou entre 1 e 3, a extração dela é perdida
        _stock = observed - yield;
        return yield;
    }

    private int ExtractWithLock(MineExtractionStats stats)
    {
        if (!Monitor.TryEnter(_gate))
        {
            stats.Contentions++;
            long waitStartedAt = Stopwatch.GetTimestamp();
            Monitor.Enter(_gate);
            stats.LockWaitTicks += Stopwatch.GetTimestamp() - waitStartedAt;
        }

        try
        {
            long observed = _stock;
            int yield = ComputeYield(observed);
            _stock = observed - yield;
            return yield;
        }
        finally
        {
            Monitor.Exit(_gate);
        }
    }

    private int ExtractWithCompareExchange(MineExtractionStats stats)
    {
        while (true)
        {
            long observed = Interlocked.Read(ref _stock);
            int yield = ComputeYield(observed);

            if (yield == 0)
            {
                return 0;
            }

            // Grava somente se ninguém alterou o estoque desde a leitura.
            if (Interlocked.CompareExchange(ref _stock, observed - yield, observed) == observed)
            {
                return yield;
            }

            stats.Contentions++;
        }
    }

    /// <summary>
    /// Calcula o rendimento a partir do estoque lido. O trabalho fica entre a leitura e a
    /// escrita, como em código real que valida ou transforma um valor antes de gravá-lo.
    /// O custo é o mesmo em todos os modos de sincronização.
    /// </summary>
    private int ComputeYield(long observedStock)
    {
        if (observedStock <= 0)
        {
            return 0;
        }

        uint hash = unchecked((uint)observedStock * 0x9E3779B9u) | 1u;

        for (int iteration = 0; iteration < _yieldComputationIterations; iteration++)
        {
            hash ^= hash << 13;
            hash ^= hash >> 17;
            hash ^= hash << 5;
        }

        long yield = 1 + (hash % 3);
        return (int)Math.Min(yield, observedStock);
    }
}
