namespace C12ProjetoCiv.Simulation;

/// <summary>
/// Informações do worker que está atualizando uma civilização no ciclo atual.
/// </summary>
public readonly record struct WorkerContext(int WorkerNumber, CancellationToken CancellationToken);
