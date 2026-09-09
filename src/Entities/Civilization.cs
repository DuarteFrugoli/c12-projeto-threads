using System.Numerics;
using C12ProjetoCiv.Core;
using C12ProjetoCiv.Simulation;

namespace C12ProjetoCiv.Entities;

public sealed class Civilization
{
    private readonly Random _random;
    private readonly int[] _resourceOccupancy;
    private readonly int _baseDropOffColumns;
    private readonly int _baseDropOffRows;
    private readonly float _baseDropOffWidth;
    private readonly float _baseDropOffHeight;
    private readonly int _baseDropOffSlotStep;
    private double _nextNaturalSpawnAtSeconds;
    private double _nextStressSpawnAtSeconds;
    private int _nextAgentId;

    public Civilization(
        int id,
        string name,
        RgbColor color,
        FloatRectangle territory,
        Vector2 basePosition,
        ResourceNode[] resourceNodes,
        int randomSeed,
        int initialPopulation,
        SimulationConfig config)
    {
        Id = id;
        Name = name;
        Color = color;
        Territory = territory;
        BasePosition = basePosition;
        ResourceNodes = resourceNodes;
        _resourceOccupancy = new int[resourceNodes.Length];
        _baseDropOffColumns = config.BaseDropOffColumns;
        _baseDropOffRows = config.BaseDropOffRows;
        _baseDropOffWidth = config.BaseDropOffWidth;
        _baseDropOffHeight = config.BaseDropOffHeight;
        _baseDropOffSlotStep = FindCoprimeStep(_baseDropOffColumns * _baseDropOffRows);
        _random = new Random(randomSeed);
        Agents = new List<Agent>(config.MaxPopulationPerCivilization);

        for (int index = 0; index < initialPopulation; index++)
        {
            SpawnAgent();
        }
    }

    public int Id { get; }
    public string Name { get; }
    public RgbColor Color { get; }
    public FloatRectangle Territory { get; }
    public Vector2 BasePosition { get; }
    public ResourceNode[] ResourceNodes { get; }
    public List<Agent> Agents { get; }
    public long StoredResources { get; private set; }

    public void Update(SimulationFrameCommand command, SimulationConfig config)
    {
        int populationAtCycleStart = Agents.Count;
        int deliveredUnits = 0;
        RebuildResourceOccupancy();

        for (int index = 0; index < populationAtCycleStart; index++)
        {
            Agent agent = Agents[index];

            if (agent.State == AgentState.Searching)
            {
                AssignResourceForCurrentTrip(agent, config);
            }
            else
            {
                agent.NextResourceIndex = FindBestResource(
                    agent.Position,
                    agent.Id,
                    agent.CompletedTrips + 1,
                    agent.NextResourceIndex,
                    config);
            }

            Vector2 resourcePosition = agent.TargetResourceIndex >= 0
                ? ResourceNodes[agent.TargetResourceIndex].Position
                : default;

            if (agent.Update(
                    resourcePosition,
                    agent.DepositPosition,
                    command.DeltaSeconds,
                    command.ElapsedSeconds,
                    config))
            {
                deliveredUnits += config.ResourceUnitsPerDelivery;
            }
        }

        StoredResources += deliveredUnits;

        if (!command.AllowPopulationGrowth || Agents.Count >= config.MaxPopulationPerCivilization)
        {
            return;
        }

        if (command.StressModeEnabled)
        {
            TryStressSpawn(command.ElapsedSeconds, config);
        }
        else
        {
            TryNaturalSpawn(command.ElapsedSeconds, config);
        }
    }

    private void RebuildResourceOccupancy()
    {
        Array.Clear(_resourceOccupancy);

        foreach (Agent agent in Agents)
        {
            if (agent.TargetResourceIndex >= 0)
            {
                _resourceOccupancy[agent.TargetResourceIndex]++;
            }
        }
    }

    private void AssignResourceForCurrentTrip(Agent agent, SimulationConfig config)
    {
        if (agent.TargetResourceIndex >= 0)
        {
            _resourceOccupancy[agent.TargetResourceIndex]--;
        }

        int resourceIndex = FindBestResource(
            agent.Position,
            agent.Id,
            agent.CompletedTrips,
            agent.NextResourceIndex,
            config);
        agent.TargetResourceIndex = resourceIndex;
        agent.NextResourceIndex = -1;
        _resourceOccupancy[resourceIndex]++;
    }

    private int FindBestResource(
        Vector2 position,
        int agentId,
        int tripNumber,
        int preferredIndex,
        SimulationConfig config)
    {
        int bestAvailableIndex = -1;
        int bestFallbackIndex = 0;
        float bestAvailableScore = float.MaxValue;
        float bestFallbackScore = float.MaxValue;
        float territoryDiagonalSquared =
            (Territory.Width * Territory.Width) + (Territory.Height * Territory.Height);

        for (int index = 0; index < ResourceNodes.Length; index++)
        {
            Vector2 displacement = ResourceNodes[index].Position - position;
            float normalizedDistance = displacement.LengthSquared() / territoryDiagonalSquared;
            float preference = GetPreference(agentId, tripNumber, index);
            float preferredBonus = index == preferredIndex ? 0.25f : 0f;
            float score =
                (_resourceOccupancy[index] * config.ResourceOccupancyWeight) +
                (normalizedDistance * config.ResourceDistanceWeight) +
                (preference * config.ResourcePreferenceWeight) -
                preferredBonus;

            if (score < bestFallbackScore)
            {
                bestFallbackScore = score;
                bestFallbackIndex = index;
            }

            if (_resourceOccupancy[index] < config.MaxAgentsPerResourceRoute &&
                score < bestAvailableScore)
            {
                bestAvailableScore = score;
                bestAvailableIndex = index;
            }
        }

        return bestAvailableIndex >= 0 ? bestAvailableIndex : bestFallbackIndex;
    }

    private float GetPreference(int agentId, int tripNumber, int resourceIndex)
    {
        uint value = unchecked(
            ((uint)(Id + 1) * 0x9E3779B9u) ^
            ((uint)(agentId + 1) * 0x85EBCA6Bu) ^
            ((uint)(tripNumber + 1) * 0xC2B2AE35u) ^
            ((uint)(resourceIndex + 1) * 0x27D4EB2Fu));
        value ^= value >> 16;
        value *= 0x7FEB352Du;
        value ^= value >> 15;
        value *= 0x846CA68Bu;
        value ^= value >> 16;
        return (value & 0x00FF_FFFFu) / 16_777_216f;
    }

    private void TryNaturalSpawn(double elapsedSeconds, SimulationConfig config)
    {
        if (elapsedSeconds < _nextNaturalSpawnAtSeconds)
        {
            return;
        }

        _nextNaturalSpawnAtSeconds = elapsedSeconds + config.NaturalSpawnIntervalSeconds;

        if (StoredResources < config.AgentCost)
        {
            return;
        }

        StoredResources -= config.AgentCost;
        SpawnAgent();
    }

    private void TryStressSpawn(double elapsedSeconds, SimulationConfig config)
    {
        if (elapsedSeconds < _nextStressSpawnAtSeconds)
        {
            return;
        }

        _nextStressSpawnAtSeconds = elapsedSeconds + config.StressSpawnIntervalSeconds;
        int availableSlots = config.MaxPopulationPerCivilization - Agents.Count;
        int spawnCount = Math.Min(config.StressSpawnBatchPerCivilization, availableSlots);

        for (int index = 0; index < spawnCount; index++)
        {
            SpawnAgent();
        }
    }

    private void SpawnAgent()
    {
        int agentId = _nextAgentId++;
        Vector2 depositPosition = CreateDepositPosition(agentId);
        float angle = (float)(_random.NextDouble() * Math.Tau);
        float radius = (float)(_random.NextDouble() * 2.0);
        Vector2 jitter = new(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius);
        Agents.Add(new Agent(agentId, Id, depositPosition + jitter, depositPosition));
    }

    private Vector2 CreateDepositPosition(int agentId)
    {
        int slotCount = _baseDropOffColumns * _baseDropOffRows;
        int slot = (agentId * _baseDropOffSlotStep) % slotCount;
        int column = slot % _baseDropOffColumns;
        int row = slot / _baseDropOffColumns;
        float relativeX = (column + 0.5f) / _baseDropOffColumns;
        float relativeY = (row + 0.5f) / _baseDropOffRows;

        return new Vector2(
            BasePosition.X + ((relativeX - 0.5f) * _baseDropOffWidth),
            BasePosition.Y + ((relativeY - 0.5f) * _baseDropOffHeight));
    }

    private static int FindCoprimeStep(int slotCount)
    {
        int step = Math.Max(1, (slotCount / 2) - 1);

        while (GreatestCommonDivisor(step, slotCount) != 1)
        {
            step--;
        }

        return step;
    }

    private static int GreatestCommonDivisor(int left, int right)
    {
        while (right != 0)
        {
            (left, right) = (right, left % right);
        }

        return left;
    }
}
