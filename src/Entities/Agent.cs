using System.Numerics;
using C12ProjetoCiv.Core;

namespace C12ProjetoCiv.Entities;

public sealed class Agent
{
    public Agent(
        int id,
        int civilizationId,
        Vector2 position,
        Vector2 depositPosition)
    {
        Id = id;
        CivilizationId = civilizationId;
        Position = position;
        DepositPosition = depositPosition;
    }

    public int Id { get; }
    public int CivilizationId { get; }
    public Vector2 Position { get; private set; }
    public Vector2 DepositPosition { get; }
    public AgentState State { get; set; } = AgentState.Searching;
    public int TargetResourceIndex { get; set; } = -1;
    public int NextResourceIndex { get; set; } = -1;
    public int CompletedTrips { get; private set; }
    public bool IsCarryingResource { get; private set; }
    public double CollectionStartedAtSeconds { get; private set; }

    /// <summary>Indica que a viagem atual vai para a mina central, e não para um recurso próprio.</summary>
    public bool IsMineTrip { get; set; }
    public int MineLoad { get; private set; }
    public double MineExtractionProgress { get; set; }
    public bool IsMiningAtMine => IsMineTrip && State == AgentState.Collecting;
    public bool DiedInBattle { get; private set; }

    public void MarkDiedInBattle()
    {
        DiedInBattle = true;
    }

    public void AddMineLoad(int units)
    {
        MineLoad += units;
    }

    /// <summary>Perde o que extraiu nesta viagem, por ter sido derrotado numa disputa.</summary>
    public void DropMineLoad()
    {
        MineLoad = 0;
    }

    /// <summary>Sai da mina com o que conseguiu extrair, seja por carga completa ou disputa.</summary>
    public void LeaveMine()
    {
        IsCarryingResource = MineLoad > 0;
        MineExtractionProgress = 0;
        State = AgentState.ReturningToBase;
    }

    /// <summary>
    /// Atualiza o agente e retorna quantas unidades foram depositadas na base neste ciclo.
    /// </summary>
    public int Update(
        Vector2 resourcePosition,
        Vector2 depositPosition,
        double deltaSeconds,
        double elapsedSeconds,
        SimulationConfig config)
    {
        switch (State)
        {
            case AgentState.Searching:
                State = AgentState.MovingToResource;
                goto case AgentState.MovingToResource;

            case AgentState.MovingToResource:
                if (MoveTowards(resourcePosition, deltaSeconds, config))
                {
                    State = AgentState.Collecting;
                    CollectionStartedAtSeconds = elapsedSeconds;
                }

                break;

            case AgentState.Collecting:
                // Na mina, quem decide a saída é a civilização, conforme a extração.
                if (!IsMineTrip &&
                    elapsedSeconds - CollectionStartedAtSeconds >= config.CollectionDurationSeconds)
                {
                    IsCarryingResource = true;
                    State = AgentState.ReturningToBase;
                }

                break;

            case AgentState.ReturningToBase:
                if (MoveTowards(depositPosition, deltaSeconds, config))
                {
                    State = AgentState.Depositing;
                }

                break;

            case AgentState.Depositing:
                int deliveredUnits = IsMineTrip ? MineLoad : config.ResourceUnitsPerDelivery;
                IsCarryingResource = false;
                IsMineTrip = false;
                MineLoad = 0;
                CompletedTrips++;
                State = AgentState.Searching;
                return deliveredUnits;

            default:
                throw new InvalidOperationException($"Estado de agente desconhecido: {State}.");
        }

        return 0;
    }

    private bool MoveTowards(Vector2 destination, double deltaSeconds, SimulationConfig config)
    {
        Vector2 displacement = destination - Position;
        float distanceSquared = displacement.LengthSquared();
        float arrivalSquared = config.ArrivalDistance * config.ArrivalDistance;

        if (distanceSquared <= arrivalSquared)
        {
            Position = destination;
            return true;
        }

        float distance = MathF.Sqrt(distanceSquared);
        float step = config.AgentSpeedPixelsPerSecond * (float)Math.Max(0, deltaSeconds);

        if (step + config.ArrivalDistance >= distance)
        {
            Position = destination;
            return true;
        }

        Position += displacement * (step / distance);
        return false;
    }
}
