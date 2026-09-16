using System.Numerics;
using C12ProjetoCiv.Concurrency;
using C12ProjetoCiv.Core;
using C12ProjetoCiv.Entities;
using C12ProjetoCiv.Metrics;
using C12ProjetoCiv.Simulation;

namespace C12ProjetoCiv.Diagnostics;

public static class SelfTestRunner
{
    public static int Run()
    {
        List<(string Name, Action Test)> tests =
        [
            ("recursos permanecem inesgotáveis", ResourcesRemainUnchanged),
            ("agentes usam rotas e depósitos distribuídos", AgentsUseDistributedRoutes),
            ("1 e 4 workers produzem o mesmo estado", WorkerModesAreDeterministic),
            ("parâmetros padrão da apresentação", DefaultPresentationParametersAreCorrect),
            ("bloqueio e recuperação do crescimento", GrowthControllerUsesHysteresis),
            ("crescimento natural depende das entregas", NaturalGrowthUsesDeliveries),
            ("Stress Test respeita bloqueio e limite", StressTestRespectsSafetyAndLimit),
            ("contagem educacional de threads", ControlledThreadCountIsCorrect),
            ("trocas repetidas preservam a população", RepeatedWorkerChangesPreservePopulation),
            ("cronômetro pausa em tempo real", RealClockPauses),
            ("mina com lock preserva o estoque", () => MinePreservesStock(MineSyncMode.Lock, WorkerMode.Four)),
            ("mina com Interlocked preserva o estoque", () => MinePreservesStock(MineSyncMode.Interlocked, WorkerMode.Four)),
            ("mina sem sincronização é correta com 1 worker", () => MinePreservesStock(MineSyncMode.Unsynchronized, WorkerMode.One)),
            ("buffer limitado entrega todos os itens", BoundedBufferDeliversEveryItem),
            ("árbitro entrega a mina a quem tem mais agentes", ArbiterAwardsMineToLargestPresence),
            ("batalhas causam baixas e saque sem eliminar", () => BattlesAffectPopulations(battlesEnabled: true)),
            ("batalhas desligadas não matam agentes", () => BattlesAffectPopulations(battlesEnabled: false)),
            ("linha do tempo registra cada civilização", TimelineRecordsCivilizationsPerWorker),
            ("coordenador divide carga entre N workers", CoordinatorPartitionsAnyWorkerCount),
        ];

        int failures = 0;

        foreach ((string name, Action test) in tests)
        {
            try
            {
                test();
                Console.WriteLine($"[OK] {name}");
            }
            catch (Exception exception)
            {
                failures++;
                Console.Error.WriteLine($"[FALHA] {name}: {exception.Message}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"{tests.Count - failures}/{tests.Count} testes passaram.");
        return failures == 0 ? 0 : 1;
    }

    private static void ResourcesRemainUnchanged()
    {
        SimulationConfig config = new()
        {
            InitialPopulationPerCivilization = 20,
            ResourceNodesPerCivilization = 32,
        };
        SimulationState state = SimulationState.Create(config);
        int[] originalCounts = state.Civilizations
            .Select(civilization => civilization.ResourceNodes.Length)
            .ToArray();

        RunFrames(state, config, WorkerMode.Four, frameCount: 1_000);

        for (int index = 0; index < state.Civilizations.Length; index++)
        {
            Assert(
                state.Civilizations[index].ResourceNodes.Length == originalCounts[index],
                $"A civilização {index} alterou sua quantidade de recursos.");
        }
    }

    private static void WorkerModesAreDeterministic()
    {
        // A mina compartilhada torna a ordem das extrações dependente do escalonamento.
        // O determinismo vale para a parte sem estado compartilhado.
        SimulationConfig config = new()
        {
            InitialPopulationPerCivilization = 30,
            ResourceNodesPerCivilization = 48,
            ContestedMineEnabled = false,
        };
        SimulationState sequential = SimulationState.Create(config);
        SimulationState parallel = SimulationState.Create(config);

        RunFrames(sequential, config, WorkerMode.One, frameCount: 400);
        RunFrames(parallel, config, WorkerMode.Four, frameCount: 400);

        for (int civilizationIndex = 0;
             civilizationIndex < sequential.Civilizations.Length;
             civilizationIndex++)
        {
            Civilization expected = sequential.Civilizations[civilizationIndex];
            Civilization actual = parallel.Civilizations[civilizationIndex];
            Assert(expected.StoredResources == actual.StoredResources, "Recursos armazenados divergiram.");
            Assert(expected.Agents.Count == actual.Agents.Count, "Populações divergiram.");

            for (int agentIndex = 0; agentIndex < expected.Agents.Count; agentIndex++)
            {
                Agent expectedAgent = expected.Agents[agentIndex];
                Agent actualAgent = actual.Agents[agentIndex];
                Assert(expectedAgent.State == actualAgent.State, "Estados de agentes divergiram.");
                Assert(
                    expectedAgent.TargetResourceIndex == actualAgent.TargetResourceIndex,
                    "Destinos de agentes divergiram.");
                Assert(
                    expectedAgent.DepositPosition == actualAgent.DepositPosition,
                    "Pontos de depósito de agentes divergiram.");
                Assert(
                    Math.Abs(expectedAgent.Position.X - actualAgent.Position.X) < 0.0001f &&
                    Math.Abs(expectedAgent.Position.Y - actualAgent.Position.Y) < 0.0001f,
                    "Posições de agentes divergiram.");
            }
        }
    }

    private static void AgentsUseDistributedRoutes()
    {
        SimulationConfig config = new()
        {
            InitialPopulationPerCivilization = 200,
            ResourceNodesPerCivilization = 64,
            MaxPopulationPerCivilization = 500,
        };
        SimulationState state = SimulationState.Create(config);

        RunFrames(state, config, WorkerMode.Four, frameCount: 1);

        foreach (Civilization civilization in state.Civilizations)
        {
            // Viagens para a mina central não ocupam rotas de recursos próprios.
            Agent[] resourceAgents = civilization.Agents
                .Where(agent => !agent.IsMineTrip)
                .ToArray();
            int distinctResources = resourceAgents
                .Select(agent => agent.TargetResourceIndex)
                .Distinct()
                .Count();
            int busiestRoute = resourceAgents
                .GroupBy(agent => agent.TargetResourceIndex)
                .Max(group => group.Count());
            int distinctDeposits = civilization.Agents
                .Select(agent => agent.DepositPosition)
                .Distinct()
                .Count();

            Assert(
                distinctResources == config.ResourceNodesPerCivilization,
                "Os agentes não foram distribuídos entre todos os recursos disponíveis.");
            Assert(
                busiestRoute <= config.MaxAgentsPerResourceRoute,
                "Uma rota recebeu mais agentes do que sua capacidade configurada.");
            Assert(
                distinctDeposits == config.BaseDropOffColumns * config.BaseDropOffRows,
                "A área da base não utilizou todos os pontos de depósito.");
        }
    }

    private static void GrowthControllerUsesHysteresis()
    {
        SimulationConfig config = new();
        PopulationGrowthController controller = new(config);

        controller.Update(
            averageFps: 10,
            fpsWindowReady: true,
            deltaSeconds: 0.016,
            pausedByUser: false,
            benchmarkMode: false,
            populationLimitReached: false);
        Assert(
            controller.State == PopulationGrowthState.BlockedByLowFps,
            "O crescimento não foi bloqueado em 10 FPS.");

        for (int index = 0; index < 130; index++)
        {
            controller.Update(
                averageFps: 15,
                fpsWindowReady: true,
                deltaSeconds: 1.0 / 60.0,
                pausedByUser: false,
                benchmarkMode: false,
                populationLimitReached: false);
        }

        Assert(
            controller.State == PopulationGrowthState.Enabled,
            "O crescimento não foi liberado após a recuperação esperada.");
    }

    private static void DefaultPresentationParametersAreCorrect()
    {
        SimulationConfig config = new();

        Assert(
            Math.Abs(config.NaturalSpawnIntervalSeconds - 0.05) < 0.000_001,
            "O intervalo padrão de nascimento deveria ser 0,05 segundo.");
        Assert(
            config.MaxPopulationPerCivilization == 7_000 &&
            config.MaxPopulationTotal == 28_000,
            "O limite padrão deveria ser 7.000 por civilização e 28.000 no total.");
        Assert(
            config.ResourceNodesPerCivilization * config.MaxAgentsPerResourceRoute >=
            config.MaxPopulationPerCivilization,
            "As rotas configuradas não comportam a população máxima.");
        Assert(
            Math.Abs(config.LowFpsThreshold - 10.0) < 0.000_001,
            "O bloqueio padrão deveria acontecer em 10 FPS.");
        Assert(
            Math.Abs(config.RecoveryFpsThreshold - 15.0) < 0.000_001,
            "A recuperação padrão deveria acontecer em 15 FPS.");
    }

    private static void ControlledThreadCountIsCorrect()
    {
        SimulationConfig config = new();
        SimulationState state = SimulationState.Create(config);

        using (SimulationCoordinator one = new(state, config, WorkerMode.One))
        {
            Assert(one.ThreadsControlledByProject == 2, "O modo 1 deveria informar 2 threads.");
            Assert(
                state.Civilizations.All(civilization =>
                    one.GetWorkerNumberForCivilization(civilization.Id) == 1),
                "O worker único deveria controlar todas as civilizações.");
        }

        using SimulationCoordinator four = new(state, config, WorkerMode.Four);
        Assert(four.ThreadsControlledByProject == 5, "O modo 4 deveria informar 5 threads.");
        Assert(
            state.Civilizations.All(civilization =>
                four.GetWorkerNumberForCivilization(civilization.Id) == civilization.Id + 1),
            "O modo 4 deveria atribuir uma civilização a cada worker.");
        Assert(
            state.Civilizations
                .Select(civilization => four.GetManagedThreadIdForCivilization(civilization.Id))
                .Distinct()
                .Count() == 4,
            "Cada worker deveria possuir uma thread gerenciada distinta.");
    }

    private static void NaturalGrowthUsesDeliveries()
    {
        SimulationConfig config = new()
        {
            InitialPopulationPerCivilization = 20,
            ResourceNodesPerCivilization = 16,
            AgentSpeedPixelsPerSecond = 1_000,
            CollectionDurationSeconds = 0,
            NaturalSpawnIntervalSeconds = 0.01,
            AgentCost = 2,
            MaxPopulationPerCivilization = 50,
        };
        SimulationState state = SimulationState.Create(config);

        RunFrames(
            state,
            config,
            WorkerMode.Four,
            frameCount: 180,
            allowPopulationGrowth: true);

        Assert(
            state.Civilizations.All(civilization => civilization.Agents.Count > 20),
            "As civilizações não transformaram entregas em novos agentes.");
    }

    private static void StressTestRespectsSafetyAndLimit()
    {
        SimulationConfig config = new()
        {
            InitialPopulationPerCivilization = 4,
            ResourceNodesPerCivilization = 8,
            MaxPopulationPerCivilization = 12,
            StressSpawnBatchPerCivilization = 5,
            StressSpawnIntervalSeconds = 0.01,
        };
        SimulationState state = SimulationState.Create(config);

        RunFrames(
            state,
            config,
            WorkerMode.Four,
            frameCount: 20,
            allowPopulationGrowth: false,
            stressMode: true);
        Assert(
            state.Civilizations.All(civilization => civilization.Agents.Count == 4),
            "O Stress Test ignorou o bloqueio de crescimento.");

        RunFrames(
            state,
            config,
            WorkerMode.Four,
            frameCount: 20,
            allowPopulationGrowth: true,
            stressMode: true);
        Assert(
            state.Civilizations.All(civilization => civilization.Agents.Count == 12),
            "O Stress Test não respeitou o limite por civilização.");
        Assert(
            state.TotalPopulation == config.MaxPopulationTotal,
            "O Stress Test não respeitou o limite populacional total.");
    }

    private static void RealClockPauses()
    {
        SimulationClock clock = new();
        Thread.Sleep(20);
        clock.Pause();
        double pausedAt = clock.ElapsedSeconds;
        Thread.Sleep(20);
        double afterWaiting = clock.ElapsedSeconds;

        Assert(pausedAt >= 0.01, "O cronômetro não avançou em tempo real.");
        Assert(
            Math.Abs(afterWaiting - pausedAt) < 0.005,
            "O cronômetro continuou avançando durante a pausa.");
    }

    private static void RepeatedWorkerChangesPreservePopulation()
    {
        SimulationConfig config = new()
        {
            InitialPopulationPerCivilization = 25,
            ResourceNodesPerCivilization = 24,
        };
        SimulationState state = SimulationState.Create(config);
        int expectedPopulation = state.TotalPopulation;
        WorkerMode[] modes =
        [
            WorkerMode.One,
            WorkerMode.Four,
            WorkerMode.Two,
            WorkerMode.One,
            WorkerMode.Four,
        ];
        const double delta = 1.0 / 60.0;
        double elapsed = 0;

        foreach (WorkerMode mode in modes)
        {
            using SimulationCoordinator coordinator = new(state, config, mode);

            for (int frame = 0; frame < 25; frame++)
            {
                elapsed += delta;
                coordinator.ExecuteFrame(new SimulationFrameCommand(
                    delta,
                    elapsed,
                    AllowPopulationGrowth: false,
                    StressModeEnabled: false));
            }

            Assert(
                state.TotalPopulation == expectedPopulation,
                $"A população mudou indevidamente após usar {mode} worker(s).");
        }
    }

    private static void MinePreservesStock(MineSyncMode syncMode, WorkerMode workerMode)
    {
        SimulationConfig config = new()
        {
            InitialPopulationPerCivilization = 400,
            ResourceNodesPerCivilization = 64,
            MineSyncMode = syncMode,
            MineTripInterval = 2,
            MineCapacity = 2_000,
            MineRegenerationPerSecond = 3_000,
        };
        SimulationState state = SimulationState.Create(config);

        RunFrames(state, config, workerMode, frameCount: 600);

        long extracted = state.Civilizations.Sum(civilization => civilization.MineStats.UnitsExtracted);
        Assert(extracted > 0, "Nenhum agente extraiu da mina.");
        Assert(
            state.MineRaceAnomaly == 0,
            $"O estoque divergiu da contabilidade em {state.MineRaceAnomaly} unidade(s).");
        Assert(state.Mine!.Stock >= 0, "O estoque da mina ficou negativo.");
    }

    private static void BattlesAffectPopulations(bool battlesEnabled)
    {
        SimulationConfig config = new()
        {
            InitialPopulationPerCivilization = 300,
            ResourceNodesPerCivilization = 32,
            MineTripInterval = 1,
            MineBattlesEnabled = battlesEnabled,
            MineBattleMaxLossFraction = 0.2,
            ConflictRoundSeconds = 0.5,
        };
        SimulationState state = SimulationState.Create(config);
        int initialPopulation = state.TotalPopulation;
        using ConflictArbiter arbiter = new(state.Mine!, config);
        using SimulationCoordinator coordinator = new(state, config, WorkerMode.Four);
        const double delta = 1.0 / 30.0;
        double elapsed = 0;

        for (int frame = 0; frame < 900; frame++)
        {
            elapsed += delta;
            coordinator.ExecuteFrame(new SimulationFrameCommand(delta, elapsed, false, false));

            // Dá tempo ao árbitro: as decisões chegam aos workers de forma assíncrona.
            if (frame % 15 == 0)
            {
                Thread.Sleep(1);
            }
        }

        long deaths = state.Civilizations.Sum(civilization => civilization.MineStats.BattleDeaths);
        long loot = state.Civilizations.Sum(civilization => civilization.MineStats.LootReceived);

        if (!battlesEnabled)
        {
            Assert(deaths == 0 && arbiter.Battles == 0, "Com as batalhas desligadas ninguém deveria morrer.");
            Assert(state.TotalPopulation == initialPopulation, "A população mudou sem batalhas.");
            return;
        }

        Assert(arbiter.Battles > 0, "Nenhuma batalha foi decidida.");
        Assert(deaths > 0, "As batalhas não causaram baixas.");
        Assert(loot > 0, "As batalhas não entregaram saque à vencedora.");
        Assert(state.TotalPopulation == initialPopulation - deaths, "A população não bate com as baixas.");
        Assert(
            state.Civilizations.All(civilization =>
                civilization.Agents.Count >= config.MineBattleMinimumPopulation),
            "Uma civilização ficou abaixo da população mínima.");
    }

    private static void BoundedBufferDeliversEveryItem()
    {
        const int producers = 4;
        const int itemsPerProducer = 5_000;
        using BoundedBuffer<int> buffer = new(capacity: 8);
        using CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(10));
        long consumedSum = 0;
        int consumedCount = 0;

        Thread consumer = new(() =>
        {
            for (int index = 0; index < producers * itemsPerProducer; index++)
            {
                Assert(buffer.TryTake(out int item, cancellation.Token), "O consumidor não recebeu um item.");
                consumedSum += item;
                consumedCount++;
            }
        });
        Thread[] producerThreads = Enumerable.Range(0, producers)
            .Select(_ => new Thread(() =>
            {
                for (int item = 1; item <= itemsPerProducer; item++)
                {
                    buffer.TryAdd(item, cancellation.Token);
                }
            }))
            .ToArray();

        consumer.Start();

        foreach (Thread producer in producerThreads)
        {
            producer.Start();
        }

        foreach (Thread producer in producerThreads)
        {
            producer.Join();
        }

        Assert(consumer.Join(TimeSpan.FromSeconds(10)), "O consumidor não terminou.");
        long expectedSum = producers * ((long)itemsPerProducer * (itemsPerProducer + 1) / 2);
        Assert(consumedCount == producers * itemsPerProducer, "Itens foram perdidos no buffer.");
        Assert(consumedSum == expectedSum, "Itens foram corrompidos no buffer.");
        Assert(buffer.ProducerBlocks > 0, "O buffer pequeno deveria ter bloqueado produtores.");
    }

    private static void ArbiterAwardsMineToLargestPresence()
    {
        SimulationConfig config = new() { ConflictRoundSeconds = 1.0 };
        ContestedMine mine = new(Vector2.Zero, config);
        using ConflictArbiter arbiter = new(mine, config);
        BoundedBuffer<MinePresenceReport> reports = mine.Reports!;
        int[] minersPerCivilization = [3, 12, 0, 7];

        for (int civilizationId = 0; civilizationId < 4; civilizationId++)
        {
            reports.TryAdd(
                new MinePresenceReport(civilizationId, 1, 0.5, 0.1, minersPerCivilization[civilizationId], 0, 100),
                CancellationToken.None);
        }

        reports.TryAdd(new MinePresenceReport(0, 1, 1.2, 0.1, 0, 0, 100), CancellationToken.None);
        DateTime deadline = DateTime.UtcNow.AddSeconds(2);

        while (mine.Control.Generation == 0 && DateTime.UtcNow < deadline)
        {
            Thread.Yield();
        }

        MineControl control = mine.Control;
        Assert(control.Generation == 1, "O árbitro não publicou uma decisão.");
        Assert(control.ControllerCivilizationId == 1, "A civilização com mais agentes deveria vencer.");
        Assert(control.WasContested, "A rodada com vários participantes deveria contar como disputa.");
        Assert(arbiter.Battles == 1 && arbiter.GetVictories(1) == 1, "A vitória não foi contabilizada.");
    }

    private static void TimelineRecordsCivilizationsPerWorker()
    {
        SimulationConfig config = new()
        {
            InitialPopulationPerCivilization = 10,
            ResourceNodesPerCivilization = 16,
        };
        SimulationState state = SimulationState.Create(config);
        using SimulationCoordinator coordinator = new(state, config, WorkerMode.One, recordTimeline: true);

        coordinator.ExecuteFrame(new SimulationFrameCommand(1.0 / 60.0, 1.0 / 60.0, false, false));

        ThreadTimeline timeline = coordinator.WorkerTimelines.Single();
        List<TimelineSegment> segments = new();
        timeline.CopySegments(long.MinValue, long.MaxValue, segments);
        Assert(
            segments.Select(segment => segment.Kind).SequenceEqual(
            [
                TimelineSegmentKind.CivilizationA,
                TimelineSegmentKind.CivilizationB,
                TimelineSegmentKind.CivilizationC,
                TimelineSegmentKind.CivilizationD,
            ]),
            "O worker único deveria registrar as quatro civilizações em ordem.");
        Assert(segments.All(segment => segment.EndTimestamp >= segment.StartTimestamp), "Segmento com duração negativa.");
    }

    private static void CoordinatorPartitionsAnyWorkerCount()
    {
        SimulationConfig config = new() { ResourceNodesPerCivilization = 8 };
        Civilization[] workload = SimulationState.CreateScalingWorkload(config, 64, 2);

        foreach (int workers in new[] { 1, 7, 10, 64 })
        {
            using SimulationCoordinator coordinator = new(workload, config, workers);
            int[] perWorker = workload
                .GroupBy(civilization => coordinator.GetWorkerNumberForCivilization(civilization.Id))
                .Select(group => group.Count())
                .ToArray();

            Assert(perWorker.Length == workers, $"Nem todos os {workers} workers receberam trabalho.");
            Assert(perWorker.Sum() == workload.Length, "Alguma civilização ficou sem worker.");
            Assert(perWorker.Max() - perWorker.Min() <= 1, "A divisão entre workers ficou desbalanceada.");

            coordinator.ExecuteFrame(new SimulationFrameCommand(1.0 / 60.0, 1.0 / 60.0, false, false));
        }
    }

    private static void RunFrames(
        SimulationState state,
        SimulationConfig config,
        WorkerMode mode,
        int frameCount,
        bool allowPopulationGrowth = false,
        bool stressMode = false)
    {
        using SimulationCoordinator coordinator = new(state, config, mode);
        const double delta = 1.0 / 60.0;
        double elapsed = 0;

        for (int frame = 0; frame < frameCount; frame++)
        {
            elapsed += delta;
            coordinator.ExecuteFrame(new SimulationFrameCommand(
                delta,
                elapsed,
                AllowPopulationGrowth: allowPopulationGrowth,
                StressModeEnabled: stressMode));
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
