namespace C12ProjetoCiv.Concurrency;

/// <summary>
/// Estratégia usada pelos workers para alterar o estoque compartilhado da mina central.
/// </summary>
public enum MineSyncMode
{
    /// <summary>Lê, calcula e grava sem proteção: permite condição de corrida.</summary>
    Unsynchronized,

    /// <summary>Exclusão mútua com <c>lock</c> (Monitor) em volta da região crítica.</summary>
    Lock,

    /// <summary>Otimista: calcula fora e confirma com <c>Interlocked.CompareExchange</c>.</summary>
    Interlocked,
}

public static class MineSyncModeExtensions
{
    public static MineSyncMode Next(this MineSyncMode mode)
    {
        return mode switch
        {
            MineSyncMode.Unsynchronized => MineSyncMode.Lock,
            MineSyncMode.Lock => MineSyncMode.Interlocked,
            _ => MineSyncMode.Unsynchronized,
        };
    }

    public static string DisplayName(this MineSyncMode mode)
    {
        return mode switch
        {
            MineSyncMode.Unsynchronized => "Sem sincronização",
            MineSyncMode.Lock => "lock (Monitor)",
            MineSyncMode.Interlocked => "Interlocked (CAS)",
            _ => mode.ToString(),
        };
    }

    public static string ShortName(this MineSyncMode mode)
    {
        return mode switch
        {
            MineSyncMode.Unsynchronized => "Sem sync",
            MineSyncMode.Lock => "lock",
            MineSyncMode.Interlocked => "Interlocked",
            _ => mode.ToString(),
        };
    }
}
