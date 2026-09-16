namespace C12ProjetoCiv.Concurrency;

/// <summary>
/// Mensagem enviada por um worker ao árbitro ao final da atualização de uma civilização.
/// </summary>
public readonly record struct MinePresenceReport(
    int CivilizationId,
    int WorkerNumber,
    double ElapsedSeconds,
    double DeltaSeconds,
    int MinersAtMine,
    int UnitsExtracted,
    int Population);

/// <summary>
/// Decisão imutável publicada pelo árbitro. Os workers apenas leem a referência atual,
/// e o árbitro nunca altera uma decisão depois de publicá-la.
/// </summary>
public sealed record MineControl(
    long Generation,
    int ControllerCivilizationId,
    int Round,
    bool WasContested,
    double[] CasualtyRates)
{
    public static MineControl Initial { get; } = new(0, -1, -1, false, []);

    public double CasualtyRateFor(int civilizationId)
    {
        return civilizationId < CasualtyRates.Length ? CasualtyRates[civilizationId] : 0;
    }
}

public sealed record BattleRecord(
    int Round,
    int WinnerCivilizationId,
    double[] AverageMiners,
    bool[] RetreatedWithoutLosses,
    int ParticipantCount);
