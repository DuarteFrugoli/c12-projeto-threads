using System.Numerics;
using C12ProjetoCiv.Concurrency;
using C12ProjetoCiv.Core;
using C12ProjetoCiv.Simulation;

namespace C12ProjetoCiv.Entities;

public sealed class Civilization
{
    private const int MineSlotHashSalt = 2_000_003;
    private const int BattleHashSalt = 3_000_017;
    private const float MineSlotInnerRadius = 34f;
    private const float MineSlotRadiusRange = 40f;

    private readonly Random _random;
    private readonly ContestedMine? _mine;
    private readonly Vector2 _mineEntranceDirection;
    private long _lastSeenControlGeneration;
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
        SimulationConfig config,
        ContestedMine? mine,
        Vector2 mineEntranceDirection)
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
        _mine = mine;
        _mineEntranceDirection = mineEntranceDirection;
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

    /// <summary>Escrito somente pelo worker desta civilização.</summary>
    public MineExtractionStats MineStats { get; } = new();

    /// <summary>Agentes extraindo na mina no último ciclo.</summary>
    public int MinersAtMine { get; private set; }

    public void Update(SimulationFrameCommand command, SimulationConfig config, WorkerContext worker)
    {
        int populationAtCycleStart = Agents.Count;
        int deliveredUnits = 0;
        int minersAtMine = 0;
        int extractedFromMine = 0;
        RebuildResourceOccupancy();
        CollectLoot();
        MineControl? battleResult = ReadNewBattleResult();
        int deathsAllowed = battleResult is null ? 0 : GetAllowedBattleDeaths(config);
        int deaths = 0;

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

            if (agent.IsMiningAtMine)
            {
                minersAtMine++;

                if (battleResult is not null)
                {
                    if (ApplyBattleToAgent(agent, battleResult, ref deathsAllowed))
                    {
                        deaths++;
                        continue;
                    }
                }
                else
                {
                    extractedFromMine += MineWithAgent(agent, command, config);
                }
            }

            Vector2 resourcePosition = agent.IsMineTrip
                ? GetMineSlot(agent.Id)
                : agent.TargetResourceIndex >= 0
                    ? ResourceNodes[agent.TargetResourceIndex].Position
                    : default;

            deliveredUnits += agent.Update(
                resourcePosition,
                agent.DepositPosition,
                command.DeltaSeconds,
                command.ElapsedSeconds,
                config);
        }

        if (deaths > 0)
        {
            Agents.RemoveAll(static agent => agent.DiedInBattle);
            MineStats.BattleDeaths += deaths;
        }

        StoredResources += deliveredUnits;
        MinersAtMine = minersAtMine;
        SendMineReport(command, worker, minersAtMine, extractedFromMine);

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

    /// <summary>
    /// Leitura sem bloqueio: o árbitro troca a referência inteira de forma atômica.
    /// Retorna a decisão somente na primeira vez que esta civilização a enxerga.
    /// </summary>
    private MineControl? ReadNewBattleResult()
    {
        if (_mine is null)
        {
            return null;
        }

        MineControl control = _mine.Control;

        if (control.Generation == _lastSeenControlGeneration)
        {
            return null;
        }

        _lastSeenControlGeneration = control.Generation;
        return control.WasContested ? control : null;
    }

    /// <summary>
    /// Depois de uma disputa todos deixam a mina. A vencedora leva a carga; as derrotadas
    /// entregam a carga como saque e podem perder o agente. Retorna true se o agente morreu.
    /// </summary>
    private bool ApplyBattleToAgent(Agent agent, MineControl battle, ref int deathsAllowed)
    {
        int winner = battle.ControllerCivilizationId;

        if (winner == Id)
        {
            agent.LeaveMine();
            return false;
        }

        if (agent.MineLoad > 0)
        {
            _mine!.SendLoot(winner, agent.MineLoad);
            MineStats.LootLost += agent.MineLoad;
            agent.DropMineLoad();
        }

        MineStats.DefeatedMiners++;
        agent.LeaveMine();

        if (deathsAllowed <= 0 ||
            GetPreference(agent.Id, (int)battle.Generation, BattleHashSalt) >= battle.CasualtyRateFor(Id))
        {
            return false;
        }

        agent.MarkDiedInBattle();
        deathsAllowed--;
        return true;
    }

    /// <summary>
    /// Limita as baixas de uma batalha a uma fração da população e preserva um mínimo,
    /// para que nenhuma civilização seja eliminada.
    /// </summary>
    private int GetAllowedBattleDeaths(SimulationConfig config)
    {
        int byFraction = (int)(Agents.Count * config.MineBattleMaxLossFraction);
        int aboveMinimum = Agents.Count - config.MineBattleMinimumPopulation;
        return Math.Max(0, Math.Min(byFraction, aboveMinimum));
    }

    private void CollectLoot()
    {
        if (_mine is null)
        {
            return;
        }

        long loot = _mine.CollectLoot(Id);
        StoredResources += loot;
        MineStats.LootReceived += loot;
    }

    private int MineWithAgent(
        Agent agent,
        SimulationFrameCommand command,
        SimulationConfig config)
    {
        ContestedMine mine = _mine!;
        int extracted = 0;
        agent.MineExtractionProgress = Math.Min(
            agent.MineExtractionProgress + (config.MineExtractionsPerSecond * command.DeltaSeconds),
            config.MineExtractionsPerSecond);

        while (agent.MineExtractionProgress >= 1 && agent.MineLoad < config.MineCarryCapacity)
        {
            agent.MineExtractionProgress -= 1;
            int units = mine.Extract(MineStats);

            if (units == 0)
            {
                agent.MineExtractionProgress = 0;
                break;
            }

            agent.AddMineLoad(units);
            extracted += units;
        }

        if (agent.MineLoad >= config.MineCarryCapacity ||
            command.ElapsedSeconds - agent.CollectionStartedAtSeconds >= config.MineMaxWaitSeconds)
        {
            agent.LeaveMine();
        }

        return extracted;
    }

    private void SendMineReport(
        SimulationFrameCommand command,
        WorkerContext worker,
        int minersAtMine,
        int extractedFromMine)
    {
        BoundedBuffer<MinePresenceReport>? reports = _mine?.Reports;

        if (reports is null || command.DeltaSeconds <= 0)
        {
            return;
        }

        // Produtor: pode bloquear se a thread árbitro estiver atrasada e o buffer encher.
        reports.TryAdd(
            new MinePresenceReport(
                Id,
                worker.WorkerNumber,
                command.ElapsedSeconds,
                command.DeltaSeconds,
                minersAtMine,
                extractedFromMine,
                Agents.Count),
            worker.CancellationToken);
    }

    private Vector2 GetMineSlot(int agentId)
    {
        if (_mine is null)
        {
            return BasePosition;
        }

        float angleNoise = GetPreference(agentId, 0, MineSlotHashSalt);
        float radiusNoise = GetPreference(agentId, 1, MineSlotHashSalt);
        float angle = 0.12f + (angleNoise * ((MathF.PI / 2f) - 0.24f));
        float radius = MineSlotInnerRadius + (radiusNoise * MineSlotRadiusRange);

        return _mine.Position + new Vector2(
            _mineEntranceDirection.X * MathF.Cos(angle) * radius,
            _mineEntranceDirection.Y * MathF.Sin(angle) * radius);
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
        // Cada agente vai à mina a cada N viagens, escalonado pelo identificador. Um sorteio
        // aqui favorecia sempre as mesmas civilizações; uma regra fixa dá a mesma proporção a todas.
        agent.IsMineTrip = _mine is not null &&
            (agent.Id + agent.CompletedTrips) % config.MineTripInterval == 0;

        if (agent.IsMineTrip)
        {
            agent.TargetResourceIndex = -1;
            agent.NextResourceIndex = resourceIndex;
            return;
        }

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
