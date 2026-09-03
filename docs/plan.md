# Planejamento do Projeto — Simulação de Civilizações com Multithreading

## 1. Visão Geral

O projeto será uma **simulação gráfica de quatro civilizações**, desenvolvida com o objetivo principal de demonstrar de forma visual e mensurável os efeitos do uso de **multithreading** no desempenho de uma aplicação.

Cada civilização possuirá uma população de agentes que crescerá progressivamente durante a execução da simulação.

Esses agentes realizarão tarefas simples, porém computacionalmente repetitivas, como:

- procurar recursos;
- calcular distâncias;
- escolher destinos;
- se movimentar;
- coletar recursos;
- retornar para a base;
- atualizar seu estado.

Conforme a população aumenta, a quantidade de processamento necessária para atualizar a simulação também aumenta.

A aplicação permitirá comparar diferentes formas de processamento, principalmente:

- **Single-thread:** uma única thread de simulação processa as quatro civilizações;
- **Multi-thread:** cada civilização é processada por uma thread independente.

A renderização continuará sendo realizada pela **thread principal**.

---

## 2. Objetivo do Projeto

O principal objetivo é demonstrar como o uso de múltiplas threads pode melhorar o desempenho de uma aplicação quando existe uma carga de processamento que pode ser dividida entre tarefas independentes.

A aplicação deverá permitir observar:

- aumento da carga computacional conforme a população cresce;
- queda do FPS no modo single-thread;
- redução do tempo de processamento utilizando múltiplas threads;
- diferença entre utilizar 1, 2 e 4 threads;
- influência da quantidade de núcleos da CPU;
- custo da sincronização entre threads;
- situações em que adicionar novas threads deixa de produzir ganhos significativos.

A proposta não é desenvolver um jogo completo, mas utilizar elementos de jogo para tornar a demonstração de multithreading mais visual e interessante.

---

## 3. Tecnologias

### Linguagem

**C#**

O C# será utilizado como linguagem principal do projeto.

Principais motivos:

- suporte nativo a multithreading;
- API simples para criação e gerenciamento de threads;
- suporte a `Thread`, `Task`, `lock`, `Mutex`, `Semaphore`, `Barrier` e outras estruturas de sincronização;
- boa performance;
- facilidade de desenvolvimento;
- possibilidade de futuramente migrar o conceito para Unity, caso exista interesse.

---

### Plataforma

**.NET**

O projeto será desenvolvido utilizando o ecossistema .NET.

Principais namespaces que poderão ser utilizados:

- `System`
- `System.Collections.Generic`
- `System.Diagnostics`
- `System.Threading`
- `System.Threading.Tasks`

---

### Biblioteca Gráfica

**Raylib com Raylib-cs**

A Raylib será responsável pela parte gráfica da aplicação.

Será utilizada para:

- criar a janela;
- desenhar os agentes;
- desenhar recursos;
- desenhar as bases;
- desenhar as divisões entre civilizações;
- mostrar textos;
- mostrar FPS;
- criar botões;
- receber entradas de teclado e mouse;
- construir uma interface gráfica simples.

A Raylib **não será responsável pelo multithreading**.

Todo o gerenciamento das threads será feito utilizando os recursos do próprio C#/.NET.

---

## 4. Estrutura Visual da Simulação

A tela principal será dividida em quatro regiões.

Cada região representará uma civilização.

Exemplo:

    ┌─────────────────────┬─────────────────────┐
    │                     │                     │
    │   Civilização A     │   Civilização B     │
    │                     │                     │
    │    ● ● ● ●          │       ● ● ●         │
    │       BASE          │       BASE           │
    │                     │                     │
    ├─────────────────────┼─────────────────────┤
    │                     │                     │
    │   Civilização C     │   Civilização D     │
    │                     │                     │
    │      ● ● ●          │      ● ● ● ●        │
    │       BASE          │       BASE           │
    │                     │                     │
    └─────────────────────┴─────────────────────┘

Cada civilização poderá possuir uma cor diferente.

Por exemplo:

- Civilização A — azul;
- Civilização B — vermelho;
- Civilização C — verde;
- Civilização D — amarelo.

Os habitantes poderão ser representados por círculos simples para evitar que a renderização gráfica seja desnecessariamente complexa.

---

## 5. Estrutura de Cada Civilização

Cada civilização terá:

- uma base;
- uma lista de habitantes;
- recursos disponíveis no território;
- quantidade de recursos armazenados;
- limite populacional;
- cor própria;
- território próprio.

Inicialmente, as quatro civilizações não precisarão interagir entre si.

Isso permitirá que cada uma seja processada de forma praticamente independente, facilitando a implementação do multithreading.

---

## 6. Funcionamento dos Agentes

Cada agente poderá seguir um ciclo simples:

    Procurar recurso
          ↓
    Calcular distância
          ↓
    Escolher recurso
          ↓
    Mover até o recurso
          ↓
    Coletar
          ↓
    Retornar para a base
          ↓
    Entregar recurso
          ↓
    Procurar novo recurso

Cada agente possuirá informações como:

- posição X;
- posição Y;
- velocidade;
- estado atual;
- destino;
- recurso selecionado;
- quantidade carregada;
- civilização pertencente.

---

## 7. Estados dos Agentes

Os agentes poderão utilizar uma pequena máquina de estados.

Exemplo:

    Searching
       ↓
    MovingToResource
       ↓
    Collecting
       ↓
    ReturningToBase
       ↓
    Depositing
       ↓
    Searching

Essa estrutura mantém o comportamento simples, mas cria processamento suficiente para milhares de agentes.

---

## 8. Busca de Recursos

Para aumentar a carga de processamento, cada agente poderá procurar o recurso mais próximo.

Por exemplo:

    Para cada recurso disponível:
        calcular distância entre agente e recurso

    escolher recurso com menor distância

Se existirem muitos agentes e muitos recursos, esse processo poderá gerar uma quantidade significativa de cálculos.

Isso é útil para o projeto porque cria uma carga computacional que pode ser distribuída entre múltiplas threads.

---

## 9. Crescimento Populacional

Cada civilização começará com poucos habitantes.

Por exemplo:

    10 habitantes

Os habitantes coletarão recursos.

Quando a civilização acumular recursos suficientes:

    Recursos suficientes
            ↓
       Novo habitante
            ↓
      População aumenta
            ↓
       Mais processamento
            ↓
     Mais recursos coletados
            ↓
       População aumenta

Esse ciclo fará com que a própria simulação aumente progressivamente sua carga computacional.

---

## 10. Limite Populacional

Será implementado um limite para impedir que a simulação continue criando agentes indefinidamente.

Exemplo inicial:

    Máximo por civilização:
    5.000 habitantes

    Total:
    20.000 habitantes

Esse valor será ajustado durante os testes.

O objetivo é encontrar um valor que permita causar uma queda considerável de desempenho no modo single-thread sem provocar o travamento completo do computador.

---

## 11. Thread Principal

A thread principal será responsável principalmente por:

- criar a janela;
- receber input do usuário;
- desenhar a interface;
- desenhar as civilizações;
- desenhar os agentes;
- desenhar os recursos;
- mostrar métricas;
- controlar a execução geral da aplicação.

A renderização deverá permanecer na thread principal.

---

## 12. Modo Single-Thread

No modo single-thread haverá apenas **uma thread responsável pela lógica das quatro civilizações**.

Estrutura conceitual:

    Thread Principal
        │
        └── Renderização

    Worker Thread
        │
        ├── Atualiza Civilização A
        │
        ├── Atualiza Civilização B
        │
        ├── Atualiza Civilização C
        │
        └── Atualiza Civilização D

O processamento ocorre sequencialmente:

    Civilização A
         ↓
    Civilização B
         ↓
    Civilização C
         ↓
    Civilização D
         ↓
    Finaliza ciclo

Quanto maior a população, maior será o tempo necessário para completar esse ciclo.

---

## 13. Modo Multi-Thread

No modo multithread, cada civilização poderá possuir uma thread responsável pelo seu processamento.

Estrutura:

                    Thread Principal
                           │
                       Renderização
                           │
           ┌───────────────┼───────────────┐
           │               │               │
         Thread 1        Thread 2        Thread 3        Thread 4
           │               │               │               │
          Civ A           Civ B           Civ C           Civ D

As quatro civilizações poderão ser processadas simultaneamente.

Depois que todas terminarem o ciclo atual, a aplicação poderá iniciar a próxima atualização.

---

## 14. Modo com Duas Threads

Caso haja tempo, também será implementado um modo intermediário.

### 1 Thread

    Worker 1
    ├── Civ A
    ├── Civ B
    ├── Civ C
    └── Civ D

### 2 Threads

    Worker 1
    ├── Civ A
    └── Civ B

    Worker 2
    ├── Civ C
    └── Civ D

### 4 Threads

    Worker 1 → Civ A
    Worker 2 → Civ B
    Worker 3 → Civ C
    Worker 4 → Civ D

Isso permitirá comparar melhor o ganho de desempenho.

---

## 15. Modos Disponíveis na Interface

A interface poderá possuir botões como:

    [ 1 Thread ] [ 2 Threads ] [ 4 Threads ]

A troca poderá ser realizada durante a execução da simulação ou através de uma reinicialização da simulação.

Idealmente, a troca ocorrerá durante a execução para tornar a demonstração mais interessante.

---

## 16. Sincronização

Como múltiplas threads estarão processando dados simultaneamente, será necessário garantir que a thread de renderização não leia estruturas enquanto elas estão em um estado inconsistente.

Uma abordagem possível será utilizar ciclos de simulação sincronizados.

Exemplo:

    Estado atual
         ↓
    Workers começam processamento
         ↓
    Civ A terminada
    Civ B terminada
    Civ C terminada
    Civ D terminada
         ↓
    Sincronização
         ↓
    Estado pronto
         ↓
    Renderização

Recursos como `Barrier`, `lock` ou outras estruturas de sincronização poderão ser utilizados.

---

## 17. Separação entre Lógica e Renderização

A lógica da simulação deverá ser separada da parte gráfica.

As threads de simulação não deverão realizar chamadas de renderização.

Por exemplo:

### Threads de Simulação

Responsáveis por:

- movimentação;
- decisões;
- busca de recursos;
- coleta;
- criação de habitantes;
- atualização de estados.

### Thread Principal

Responsável por:

- desenhar;
- receber input;
- mostrar métricas;
- atualizar interface.

---

## 18. Estrutura de Classes

Estrutura inicial sugerida:

    src/
    │
    ├── Program.cs
    │
    ├── Core/
    │   ├── Game.cs
    │   ├── Simulation.cs
    │   └── SimulationMode.cs
    │
    ├── Entities/
    │   ├── Agent.cs
    │   ├── Civilization.cs
    │   ├── Resource.cs
    │   └── Base.cs
    │
    ├── Simulation/
    │   ├── SingleThreadSimulation.cs
    │   ├── MultiThreadSimulation.cs
    │   └── SimulationWorker.cs
    │
    ├── Rendering/
    │   ├── Renderer.cs
    │   └── UserInterface.cs
    │
    ├── Metrics/
    │   └── PerformanceMetrics.cs
    │
    └── Utils/
        └── Constants.cs

---

## 19. Classe Agent

Responsável por representar cada habitante.

Exemplo de informações armazenadas:

    Agent
    ├── Position
    ├── Velocity
    ├── State
    ├── Target
    ├── ResourceCarried
    └── CivilizationId

Métodos possíveis:

    Update()
    FindResource()
    Move()
    Collect()
    ReturnToBase()

---

## 20. Classe Civilization

Responsável pelos dados de uma civilização.

Estrutura:

    Civilization
    ├── Agents
    ├── Resources
    ├── Base
    ├── StoredResources
    ├── Population
    ├── PopulationLimit
    └── Color

Métodos:

    Update()
    SpawnAgent()
    AddResource()
    UpdateAgents()

---

## 21. Classe Simulation

Responsável pelas regras gerais da simulação.

Ela poderá controlar:

- início;
- pausa;
- reinício;
- velocidade da simulação;
- quantidade de civilizações;
- modo de processamento;
- limite populacional.

---

## 22. SingleThreadSimulation

Implementará a versão sequencial.

Exemplo conceitual:

    foreach civilization:
        civilization.Update()

Todas serão atualizadas pela mesma thread.

---

## 23. MultiThreadSimulation

Implementará a versão paralela.

Exemplo:

    Thread 1 → Civilization A
    Thread 2 → Civilization B
    Thread 3 → Civilization C
    Thread 4 → Civilization D

Após todas terminarem:

    Barrier
       ↓
    próximo ciclo

---

## 24. Renderer

Responsável exclusivamente pela parte visual.

Deverá desenhar:

- fundo;
- divisões das civilizações;
- agentes;
- recursos;
- bases;
- interface;
- métricas.

---

## 25. PerformanceMetrics

Será responsável por medir os resultados do experimento.

Métricas:

- FPS;
- frame time;
- simulation time;
- quantidade de agentes;
- quantidade de threads;
- população de cada civilização;
- tempo médio de atualização.

---

## 26. Métrica Principal

O FPS será uma métrica visual importante, mas não deverá ser a única.

A principal métrica para demonstrar o multithreading deverá ser:

**Simulation Time**

Exemplo:

    Single-thread

    Simulation Time: 28 ms

    Multi-thread

    Simulation Time: 9 ms

Isso permite demonstrar diretamente quanto tempo a CPU leva para atualizar a lógica da simulação.

---

## 27. Interface

Exemplo:

    ------------------------------------------------

    Civilization Thread Simulation

    FPS: 43
    Frame Time: 23.2 ms
    Simulation Time: 17.8 ms

    Population: 8.432
    Threads: 1

    [ 1 Thread ]
    [ 2 Threads ]
    [ 4 Threads ]

    [ Pause ]
    [ Restart ]
    [ Stress Test ]

    ------------------------------------------------

---

## 28. Stress Test

Será criado um botão para aumentar rapidamente a carga da simulação.

Exemplo:

    [ STRESS TEST ]

Ao clicar:

    Population:
    500 → 10.000

Isso permitirá realizar a demonstração sem precisar esperar vários minutos para que as civilizações cresçam naturalmente.

---

## 29. Controle Manual da População

Também poderá existir uma forma de aumentar ou diminuir a população.

Exemplo:

    Population

    [-] 5000 [+]

Ou atalhos:

    1 → 500 agentes
    2 → 2.000 agentes
    3 → 5.000 agentes
    4 → 10.000 agentes
    5 → 20.000 agentes

---

## 30. Comportamento Esperado

Com pouca população, a diferença entre single-thread e multithread provavelmente será pequena.

Exemplo hipotético:

    500 agentes

    1 Thread:
    120 FPS

    4 Threads:
    120 FPS

Com uma população maior:

    5.000 agentes

    1 Thread:
    55 FPS

    4 Threads:
    90 FPS

Com carga elevada:

    15.000 agentes

    1 Thread:
    25 FPS

    4 Threads:
    60 FPS

Os números acima são apenas exemplos.

Os valores reais dependerão do hardware e da implementação.

---

## 31. Ganho Não Linear

Não será esperado que:

    2 threads = 2x desempenho

ou:

    4 threads = 4x desempenho

Existem outros custos envolvidos:

- renderização;
- sincronização;
- criação e gerenciamento das threads;
- acesso à memória;
- escalonamento do sistema operacional;
- quantidade de núcleos físicos da CPU;
- partes do programa que continuam sequenciais.

Esse comportamento também será discutido durante a apresentação.

---

## 32. Controle de FPS

A aplicação poderá permitir limitar ou liberar o FPS.

Durante os testes de desempenho, pode ser interessante deixar o FPS desbloqueado para observar melhor as diferenças entre os modos.

Exemplo:

    FPS Limit: Unlimited

Ou:

    FPS Limit: 144

---

## 33. Objetivo Visual

A aplicação deverá possuir gráficos simples.

Não será necessário utilizar sprites detalhados.

Exemplo:

    ● = habitante
    ■ = recurso
    ▲ = base

A simplicidade gráfica também ajuda a garantir que a maior parte do gargalo venha da lógica de simulação, e não da GPU.

---

## 34. MVP

O primeiro objetivo será implementar apenas o necessário para demonstrar multithreading.

### MVP obrigatório

- [ ] Criar projeto C#
- [ ] Configurar Raylib-cs
- [ ] Criar janela
- [ ] Dividir a tela em quatro regiões
- [ ] Criar quatro civilizações
- [ ] Criar agentes
- [ ] Fazer agentes se movimentarem
- [ ] Fazer população crescer
- [ ] Criar limite populacional
- [ ] Criar modo single-thread
- [ ] Criar modo com quatro threads
- [ ] Permitir selecionar o modo
- [ ] Mostrar FPS
- [ ] Mostrar tempo de simulação
- [ ] Mostrar população
- [ ] Mostrar quantidade de threads

Quando essa etapa estiver pronta, o requisito principal do projeto estará cumprido.

---

## 35. Funcionalidades de Prioridade Alta

Depois do MVP:

- [ ] adicionar recursos no mapa;
- [ ] agentes procurarem recurso mais próximo;
- [ ] agentes coletarem recursos;
- [ ] agentes retornarem para a base;
- [ ] crescimento populacional depender de recursos;
- [ ] adicionar modo com duas threads;
- [ ] adicionar botão Stress Test;
- [ ] adicionar pausa;
- [ ] adicionar reinício.

---

## 36. Funcionalidades Extras

Caso sobre tempo:

- diferentes taxas de crescimento;
- velocidade configurável da simulação;
- gráficos de desempenho;
- estatísticas individuais das civilizações;
- diferentes tipos de recursos;
- diferentes comportamentos de agentes;
- morte de agentes;
- reprodução;
- eventos aleatórios.

---

## 37. Funcionalidades que Devem Ser Evitadas Inicialmente

Para manter o escopo controlado:

- combate;
- guerras;
- diplomacia;
- construção complexa;
- árvore tecnológica;
- economia complexa;
- pathfinding avançado;
- interação entre civilizações;
- mapa procedural;
- multiplayer.

Esses elementos podem ser adicionados futuramente, mas não ajudam diretamente na demonstração de multithreading.

---

## 38. Fluxo da Aplicação

Fluxo aproximado:

    Inicialização
         ↓
    Criar janela
         ↓
    Criar quatro civilizações
         ↓
    Criar população inicial
         ↓
    Iniciar simulação
         ↓
    ┌───────────────────────────────┐
    │ Atualizar lógica             │
    │ Sincronizar workers          │
    │ Atualizar métricas           │
    │ Renderizar                   │
    │ Receber input                │
    └───────────────────────────────┘
         ↓
    Repetir até fechar aplicação

---

## 39. Arquitetura Geral

                    ┌──────────────────────────┐
                    │      MAIN THREAD         │
                    │                          │
                    │ Raylib                   │
                    │ Input                    │
                    │ Renderização             │
                    │ Interface                │
                    │ Métricas                 │
                    └────────────┬─────────────┘
                                 │
                        Estado da simulação
                                 │
              ┌──────────────────┴──────────────────┐
              │                                     │
        SINGLE-THREAD                         MULTI-THREAD
              │                                     │
          Worker 1                       Worker 1 → Civ A
              │                          Worker 2 → Civ B
              ├── Civ A                  Worker 3 → Civ C
              ├── Civ B                  Worker 4 → Civ D
              ├── Civ C
              └── Civ D

---

## 40. Exemplo da Tela Final

    ┌─────────────────────────────────────────────────────────┐
    │ FPS: 72 | Simulation: 8.4 ms | Population: 8.432       │
    │ Threads: 4                                             │
    │                                                         │
    │ [1 Thread] [2 Threads] [4 Threads] [Stress Test]       │
    ├────────────────────────────┬────────────────────────────┤
    │ CIVILIZAÇÃO A              │ CIVILIZAÇÃO B              │
    │                            │                            │
    │ ●      ●      ●            │     ●      ●               │
    │      ■       ●             │ ●          ■       ●       │
    │           ▲                │          ▲                 │
    │ ●              ●           │     ●          ●           │
    │                            │                            │
    ├────────────────────────────┼────────────────────────────┤
    │ CIVILIZAÇÃO C              │ CIVILIZAÇÃO D              │
    │                            │                            │
    │ ●    ●       ■             │       ●       ●            │
    │        ●                   │ ■          ●               │
    │           ▲                │          ▲                 │
    │ ●             ●            │    ●            ●          │
    │                            │                            │
    └────────────────────────────┴────────────────────────────┘

---

## 41. Demonstração na Apresentação

Uma possível sequência para a apresentação será:

### Etapa 1 — Baixa população

Iniciar com poucos agentes.

Mostrar que:

- 1 thread funciona normalmente;
- 4 threads também funcionam normalmente;
- praticamente não existe diferença perceptível.

### Etapa 2 — Aumentar população

Utilizar o crescimento natural ou o botão Stress Test.

Mostrar:

- aumento do Simulation Time;
- redução do FPS;
- maior carga de CPU.

### Etapa 3 — Single-thread

Utilizar apenas uma thread de simulação.

Exemplo:

    Population: 15.000
    Threads: 1

    Simulation Time: 30 ms
    FPS: 25

### Etapa 4 — Multi-thread

Alterar para quatro threads.

Exemplo:

    Population: 15.000
    Threads: 4

    Simulation Time: 10 ms
    FPS: 60

### Etapa 5 — Explicação

Explicar que as quatro civilizações possuem processamento independente e, portanto, podem ser distribuídas entre diferentes threads e núcleos da CPU.

---

## 42. Comparação Experimental

Os testes poderão ser registrados em uma tabela.

Exemplo:

| População | Threads | Simulation Time | FPS |
|-----------|---------|-----------------|-----|
| 1.000 | 1 | 2 ms | 144 |
| 1.000 | 4 | 1 ms | 144 |
| 5.000 | 1 | 12 ms | 70 |
| 5.000 | 4 | 4 ms | 120 |
| 10.000 | 1 | 24 ms | 38 |
| 10.000 | 4 | 8 ms | 85 |
| 20.000 | 1 | 45 ms | 20 |
| 20.000 | 4 | 15 ms | 55 |

Os valores serão preenchidos com resultados reais após a implementação.

---

## 43. Resultado Esperado

Ao final do projeto deverá existir uma aplicação gráfica simples capaz de:

1. simular quatro civilizações;
2. aumentar progressivamente sua população;
3. executar lógica individual para milhares de agentes;
4. processar as civilizações utilizando diferentes quantidades de threads;
5. mostrar métricas de desempenho em tempo real;
6. permitir comparar single-thread e multithread;
7. demonstrar visualmente o benefício do paralelismo.

---

## 44. Objetivo Final da Apresentação

A demonstração deverá deixar claro que, conforme o número de agentes aumenta, o processamento necessário para atualizar a simulação cresce significativamente.

Quando todas as civilizações são processadas sequencialmente por uma única thread, o tempo necessário para completar cada ciclo aumenta e o FPS diminui.

Ao dividir civilizações independentes entre múltiplas threads, parte do processamento pode ocorrer simultaneamente em diferentes núcleos da CPU.

Isso reduz o tempo necessário para atualizar a simulação e pode aumentar significativamente o desempenho geral da aplicação.

O projeto também demonstrará que o ganho obtido com multithreading não é necessariamente proporcional à quantidade de threads, pois existem custos relacionados à sincronização, renderização, memória, escalonamento e partes sequenciais do programa.