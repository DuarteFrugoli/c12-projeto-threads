using System.Diagnostics;
using C12ProjetoCiv.Concurrency;
using C12ProjetoCiv.Core;
using C12ProjetoCiv.Entities;

namespace C12ProjetoCiv.Metrics;

/// <summary>
/// Converte os contadores acumulados da mina e do árbitro em taxas por segundo.
/// Lido e escrito somente pela thread principal, entre ciclos.
/// </summary>
public sealed class MineMetrics
{
    private const double SampleIntervalSeconds = 0.5;

    private double _lastSampleAtSeconds = double.NaN;
    private long _lastUnits;
    private long _lastAttempts;
    private long _lastContentions;
    private long _lastLockWaitTicks;
    private long _lastReports;
    private long _anomalyBaseline;

    public double UnitsPerSecond { get; private set; }
    public double AttemptsPerSecond { get; private set; }
    public double ContentionsPerSecond { get; private set; }
    public double LockWaitMillisecondsPerSecond { get; private set; }
    public double ReportsPerSecond { get; private set; }
    public long RaceAnomalySinceModeChange { get; private set; }
    public long TotalBattleDeaths { get; private set; }

    public void Sample(SimulationState state, ConflictArbiter? arbiter, double elapsedSeconds)
    {
        if (state.Mine is null)
        {
            return;
        }

        MineExtractionStats totals = Sum(state);
        long reports = arbiter?.ReportsProcessed ?? 0;
        RaceAnomalySinceModeChange = state.MineRaceAnomaly - _anomalyBaseline;
        TotalBattleDeaths = totals.BattleDeaths;

        if (double.IsNaN(_lastSampleAtSeconds) || elapsedSeconds < _lastSampleAtSeconds)
        {
            Store(totals, reports, elapsedSeconds);
            return;
        }

        double interval = elapsedSeconds - _lastSampleAtSeconds;

        if (interval < SampleIntervalSeconds)
        {
            return;
        }

        UnitsPerSecond = (totals.UnitsExtracted - _lastUnits) / interval;
        AttemptsPerSecond = (totals.Attempts - _lastAttempts) / interval;
        ContentionsPerSecond = (totals.Contentions - _lastContentions) / interval;
        LockWaitMillisecondsPerSecond =
            (totals.LockWaitTicks - _lastLockWaitTicks) * 1_000.0 / Stopwatch.Frequency / interval;
        ReportsPerSecond = (reports - _lastReports) / interval;
        Store(totals, reports, elapsedSeconds);
    }

    /// <summary>Recomeça a contagem de unidades duplicadas ao trocar a sincronização.</summary>
    public void ResetAnomalyBaseline(SimulationState state)
    {
        _anomalyBaseline = state.MineRaceAnomaly;
        RaceAnomalySinceModeChange = 0;
    }

    public void Reset()
    {
        _lastSampleAtSeconds = double.NaN;
        _anomalyBaseline = 0;
        UnitsPerSecond = 0;
        AttemptsPerSecond = 0;
        ContentionsPerSecond = 0;
        LockWaitMillisecondsPerSecond = 0;
        ReportsPerSecond = 0;
        RaceAnomalySinceModeChange = 0;
        TotalBattleDeaths = 0;
    }

    private void Store(MineExtractionStats totals, long reports, double elapsedSeconds)
    {
        _lastSampleAtSeconds = elapsedSeconds;
        _lastUnits = totals.UnitsExtracted;
        _lastAttempts = totals.Attempts;
        _lastContentions = totals.Contentions;
        _lastLockWaitTicks = totals.LockWaitTicks;
        _lastReports = reports;
    }

    private static MineExtractionStats Sum(SimulationState state)
    {
        MineExtractionStats totals = new();

        foreach (Civilization civilization in state.Civilizations)
        {
            totals.Attempts += civilization.MineStats.Attempts;
            totals.UnitsExtracted += civilization.MineStats.UnitsExtracted;
            totals.Contentions += civilization.MineStats.Contentions;
            totals.LockWaitTicks += civilization.MineStats.LockWaitTicks;
            totals.BattleDeaths += civilization.MineStats.BattleDeaths;
        }

        return totals;
    }
}
