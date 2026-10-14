namespace C12ProjetoCiv.Simulation;

/// <summary>
/// Algoritmo usado por cada ponto de recurso para escolher o próximo agente da fila.
/// A coleta é não preemptiva: um agente admitido termina antes de liberar a vaga.
/// </summary>
public enum ResourceSchedulingMode
{
    Fcfs,
    Priority,
}

public static class ResourceSchedulingModeExtensions
{
    public static ResourceSchedulingMode Next(this ResourceSchedulingMode mode)
    {
        return mode switch
        {
            ResourceSchedulingMode.Fcfs => ResourceSchedulingMode.Priority,
            ResourceSchedulingMode.Priority => ResourceSchedulingMode.Fcfs,
            _ => ResourceSchedulingMode.Priority,
        };
    }

    public static string DisplayName(this ResourceSchedulingMode mode)
    {
        return mode switch
        {
            ResourceSchedulingMode.Fcfs => "FCFS (ordem de chegada)",
            ResourceSchedulingMode.Priority => "Prioridade com aging",
            _ => mode.ToString(),
        };
    }

    public static string ShortName(this ResourceSchedulingMode mode)
    {
        return mode switch
        {
            ResourceSchedulingMode.Fcfs => "FCFS",
            ResourceSchedulingMode.Priority => "PRIORIDADE",
            _ => mode.ToString().ToUpperInvariant(),
        };
    }
}
