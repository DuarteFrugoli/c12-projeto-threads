using System.Numerics;
using C12ProjetoCiv.Core;
using C12ProjetoCiv.Simulation;

namespace C12ProjetoCiv.Entities;

public sealed class Civilization
{
    private readonly Random _random;
    private double _nextNaturalSpawnAtSeconds;
    private double _nextStressSpawnAtSeconds;

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

        for (int index = 0; index < populationAtCycleStart; index++)
        {
            Agent agent = Agents[index];
            agent.TargetResourceIndex = FindNearestResource(agent.Position);

            Vector2 resourcePosition = agent.TargetResourceIndex >= 0
                ? ResourceNodes[agent.TargetResourceIndex].Position
                : default;

            if (agent.Update(
                    resourcePosition,
                    BasePosition,
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

    private int FindNearestResource(Vector2 position)
    {
        int nearestIndex = 0;
        float nearestDistanceSquared = float.MaxValue;

        for (int index = 0; index < ResourceNodes.Length; index++)
        {
            Vector2 displacement = ResourceNodes[index].Position - position;
            float distanceSquared = displacement.LengthSquared();

            if (distanceSquared < nearestDistanceSquared)
            {
                nearestDistanceSquared = distanceSquared;
                nearestIndex = index;
            }
        }

        return nearestIndex;
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
        float angle = (float)(_random.NextDouble() * Math.Tau);
        float radius = (float)(_random.NextDouble() * 8.0);
        Vector2 jitter = new(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius);
        Agents.Add(new Agent(Id, BasePosition + jitter));
    }
}
