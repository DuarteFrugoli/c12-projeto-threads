namespace C12ProjetoCiv.Simulation;

public enum WorkerMode
{
    One = 1,
    Two = 2,
    Four = 4,
}

public static class WorkerModeExtensions
{
    public static WorkerMode FromCount(int workerCount)
    {
        return workerCount switch
        {
            1 => WorkerMode.One,
            2 => WorkerMode.Two,
            4 => WorkerMode.Four,
            _ => throw new ArgumentOutOfRangeException(
                nameof(workerCount),
                "Somente 1, 2 ou 4 workers são permitidos."),
        };
    }
}
