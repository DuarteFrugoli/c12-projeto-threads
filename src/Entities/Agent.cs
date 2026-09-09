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

    /// <summary>
    /// Atualiza o agente e retorna true quando uma unidade é depositada na base.
    /// </summary>
    public bool Update(
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
                if (elapsedSeconds - CollectionStartedAtSeconds >= config.CollectionDurationSeconds)
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
                IsCarryingResource = false;
                CompletedTrips++;
                State = AgentState.Searching;
                return true;

            default:
                throw new InvalidOperationException($"Estado de agente desconhecido: {State}.");
        }

        return false;
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
