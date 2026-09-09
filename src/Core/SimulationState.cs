using System.Numerics;
using C12ProjetoCiv.Entities;

namespace C12ProjetoCiv.Core;

public sealed class SimulationState
{
    private SimulationState(Civilization[] civilizations)
    {
        Civilizations = civilizations;
    }

    public Civilization[] Civilizations { get; }

    public int TotalPopulation
    {
        get
        {
            int total = 0;

            foreach (Civilization civilization in Civilizations)
            {
                total += civilization.Agents.Count;
            }

            return total;
        }
    }

    public bool HasReachedPopulationLimit(SimulationConfig config)
    {
        if (TotalPopulation >= config.MaxPopulationTotal)
        {
            return true;
        }

        return Civilizations.All(civilization =>
            civilization.Agents.Count >= config.MaxPopulationPerCivilization);
    }

    public static SimulationState Create(
        SimulationConfig config,
        int? initialPopulationPerCivilization = null)
    {
        config.Validate();
        int initialPopulation = initialPopulationPerCivilization
            ?? config.InitialPopulationPerCivilization;

        if (initialPopulation < 0 || initialPopulation > config.MaxPopulationPerCivilization)
        {
            throw new ArgumentOutOfRangeException(
                nameof(initialPopulationPerCivilization),
                "A população inicial solicitada está fora do limite.");
        }

        Vector2[] relativeResources = CreateRelativeResourceLayout(config);
        Civilization[] civilizations = new Civilization[config.CivilizationCount];
        string[] names = ["A", "B", "C", "D"];
        RgbColor[] colors =
        [
            new RgbColor(64, 135, 255),
            new RgbColor(240, 82, 82),
            new RgbColor(67, 190, 115),
            new RgbColor(242, 196, 67),
        ];

        float territoryWidth = config.WindowWidth / 2f;
        float territoryHeight = (config.WindowHeight - config.HeaderHeight) / 2f;

        for (int index = 0; index < config.CivilizationCount; index++)
        {
            int column = index % 2;
            int row = index / 2;
            FloatRectangle territory = new(
                column * territoryWidth,
                config.HeaderHeight + (row * territoryHeight),
                territoryWidth,
                territoryHeight);

            ResourceNode[] nodes = new ResourceNode[relativeResources.Length];

            for (int resourceIndex = 0; resourceIndex < nodes.Length; resourceIndex++)
            {
                Vector2 relative = relativeResources[resourceIndex];
                nodes[resourceIndex] = new ResourceNode(
                    territory.FromRelative(relative.X, relative.Y));
            }

            Vector2 basePosition = territory.FromRelative(0.5f, 0.78f);
            civilizations[index] = new Civilization(
                index,
                names[index],
                colors[index],
                territory,
                basePosition,
                nodes,
                config.Seed + (index * 7_919),
                initialPopulation,
                config);
        }

        return new SimulationState(civilizations);
    }

    private static Vector2[] CreateRelativeResourceLayout(SimulationConfig config)
    {
        Random random = new(config.Seed);
        Vector2[] positions = new Vector2[config.ResourceNodesPerCivilization];

        for (int index = 0; index < positions.Length; index++)
        {
            float x = 0.08f + ((float)random.NextDouble() * 0.84f);
            float y = 0.14f + ((float)random.NextDouble() * 0.54f);
            positions[index] = new Vector2(x, y);
        }

        return positions;
    }
}
