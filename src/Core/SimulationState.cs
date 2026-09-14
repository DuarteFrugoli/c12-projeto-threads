using System.Numerics;
using C12ProjetoCiv.Concurrency;
using C12ProjetoCiv.Entities;

namespace C12ProjetoCiv.Core;

public sealed class SimulationState
{
    private SimulationState(Civilization[] civilizations, ContestedMine? mine)
    {
        Civilizations = civilizations;
        Mine = mine;
    }

    public Civilization[] Civilizations { get; }
    public ContestedMine? Mine { get; }

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

    /// <summary>
    /// Diferença entre o estoque real e o estoque esperado pela contabilidade de cada
    /// civilização. Um valor positivo são unidades duplicadas por atualizações perdidas.
    /// Deve ser lido somente entre ciclos, com os workers aguardando.
    /// </summary>
    public long MineRaceAnomaly
    {
        get
        {
            if (Mine is null)
            {
                return 0;
            }

            long extracted = 0;

            foreach (Civilization civilization in Civilizations)
            {
                extracted += civilization.MineStats.UnitsExtracted;
            }

            long expectedStock = Mine.InitialStock + Mine.TotalRegenerated - extracted;
            return Mine.Stock - expectedStock;
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

        float territoryWidth = config.WorldWidth / 2f;
        float territoryHeight = (config.WindowHeight - config.HeaderHeight) / 2f;
        Vector2 minePosition = new(territoryWidth, config.HeaderHeight + territoryHeight);
        ContestedMine? mine = config.ContestedMineEnabled
            ? new ContestedMine(minePosition, config)
            : null;

        for (int index = 0; index < config.CivilizationCount; index++)
        {
            int column = index % 2;
            int row = index / 2;
            FloatRectangle territory = new(
                column * territoryWidth,
                config.HeaderHeight + (row * territoryHeight),
                territoryWidth,
                territoryHeight);

            // A linha de baixo é espelhada verticalmente: todas as bases ficam à mesma
            // distância da mina central, e nenhuma civilização leva vantagem na disputa.
            bool mirrored = row == 1;
            ResourceNode[] nodes = new ResourceNode[relativeResources.Length];

            for (int resourceIndex = 0; resourceIndex < nodes.Length; resourceIndex++)
            {
                Vector2 relative = relativeResources[resourceIndex];
                float relativeY = mirrored ? 1f - relative.Y : relative.Y;
                nodes[resourceIndex] = new ResourceNode(territory.FromRelative(relative.X, relativeY));
            }

            Vector2 basePosition = territory.FromRelative(0.5f, mirrored ? 0.22f : 0.78f);
            Vector2 mineEntranceDirection = new(column == 0 ? -1f : 1f, row == 0 ? -1f : 1f);
            civilizations[index] = new Civilization(
                index,
                names[index],
                colors[index],
                territory,
                basePosition,
                nodes,
                config.Seed + (index * 7_919),
                initialPopulation,
                config,
                mine,
                mineEntranceDirection);
        }

        return new SimulationState(civilizations, mine);
    }

    /// <summary>
    /// Cria uma carga fixa com muitas civilizações pequenas e sem mina, usada para medir
    /// a escalabilidade com mais threads do que as quatro civilizações da demonstração.
    /// </summary>
    public static Civilization[] CreateScalingWorkload(
        SimulationConfig config,
        int civilizationCount,
        int populationPerCivilization)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(civilizationCount);
        SimulationConfig workloadConfig = config with
        {
            ContestedMineEnabled = false,
            MaxPopulationPerCivilization = Math.Max(
                populationPerCivilization,
                config.MaxPopulationPerCivilization),
        };
        Vector2[] relativeResources = CreateRelativeResourceLayout(workloadConfig);
        FloatRectangle territory = new(
            0,
            config.HeaderHeight,
            config.WorldWidth / 2f,
            (config.WindowHeight - config.HeaderHeight) / 2f);
        ResourceNode[] nodes = relativeResources
            .Select(relative => new ResourceNode(territory.FromRelative(relative.X, relative.Y)))
            .ToArray();
        Vector2 basePosition = territory.FromRelative(0.5f, 0.78f);
        Civilization[] civilizations = new Civilization[civilizationCount];

        for (int index = 0; index < civilizationCount; index++)
        {
            civilizations[index] = new Civilization(
                index,
                $"W{index}",
                new RgbColor(128, 128, 128),
                territory,
                basePosition,
                nodes,
                config.Seed + (index * 7_919),
                populationPerCivilization,
                workloadConfig,
                mine: null,
                mineEntranceDirection: Vector2.Zero);
        }

        return civilizations;
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
