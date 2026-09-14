namespace C12ProjetoCiv.Concurrency;

/// <summary>
/// Contadores da mina pertencentes a uma civilização. Cada instância é escrita somente
/// pelo worker que atualiza essa civilização e lida pela thread principal entre ciclos,
/// portanto não precisa de sincronização.
/// </summary>
public sealed class MineExtractionStats
{
    public long Attempts;
    public long UnitsExtracted;
    public long Contentions;
    public long LockWaitTicks;
    public long DefeatedMiners;
    public long BattleDeaths;
    public long LootLost;
    public long LootReceived;
}
