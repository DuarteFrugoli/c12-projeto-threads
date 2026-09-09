# Plano do Projeto — Simulação de Civilizações com Multithreading

## 1. Propósito do projeto

O projeto será uma aplicação gráfica em C# que simula quatro civilizações independentes. Seu objetivo principal é demonstrar, de forma visual e mensurável para uma turma, a diferença de desempenho entre processar a lógica com:

- 1 worker de simulação;
- 2 workers de simulação;
- 4 workers de simulação.

A thread principal será sempre responsável pela janela, entrada do usuário, interface e renderização. Portanto, considerando somente as threads criadas e controladas pelo projeto:

| Modo | Threads de simulação | Thread principal/renderização | Total controlado pelo projeto |
|---|---:|---:|---:|
| 1 worker | 1 | 1 | 2 |
| 2 workers | 2 | 1 | 3 |
| 4 workers | 4 | 1 | 5 |

O .NET e o sistema operacional podem criar outras threads internas. Por isso, a interface e a apresentação devem falar em **workers de simulação** e **threads controladas pelo projeto**, e não no total de threads exibido pelo Gerenciador de Tarefas.

O projeto não pretende ser um jogo completo. Os elementos de jogo existem para produzir uma carga de CPU paralelizável e deixar a diferença de desempenho fácil de observar.

---

## 2. Decisões obrigatórias

Estas decisões fazem parte da especificação e não devem ficar como opções em aberto:

- serão sempre quatro civilizações;
- as civilizações não interagem entre si;
- cada civilização possui seu próprio território, agentes, base e recursos;
- os pontos de recurso são fixos e inesgotáveis;
- a quantidade de pontos de recurso é finita e não cresce durante a execução;
- o tempo será medido com um cronômetro de tempo real;
- não haverá relógio fictício, escala de tempo ou “tempo do jogo”;
- o FPS será ilimitado pela aplicação;
- a renderização acontecerá somente na thread principal;
- os workers de simulação serão threads persistentes, não criadas a cada frame;
- o crescimento populacional será suspenso quando o FPS médio chegar a 30;
- os agentes existentes nunca serão removidos por causa de FPS baixo;
- haverá um limite populacional absoluto como segunda proteção;
- os modos principais da apresentação serão 1 worker e 4 workers;
- o modo de 2 workers será mantido como comparação intermediária.

---

## 3. Resultado que a apresentação deve mostrar

Com a mesma quantidade de agentes e o mesmo estado da simulação, a turma deverá conseguir observar que:

1. no modo de 1 worker, as quatro civilizações são atualizadas sequencialmente;
2. no modo de 4 workers, cada civilização é atualizada por um worker diferente;
3. o tempo necessário para concluir um ciclo de simulação tende a diminuir quando o trabalho é dividido entre núcleos disponíveis;
4. a redução do tempo de simulação pode aumentar o FPS, pois a thread principal espera a conclusão da lógica antes de desenhar;
5. o ganho não é necessariamente de quatro vezes, devido à sincronização, memória, renderização, escalonamento e partes sequenciais;
6. o resultado depende da quantidade de núcleos e das características do computador usado.

Não se deve prometer um número específico de FPS ou um ganho exato antes de medir no computador da apresentação.

---

## 4. Tecnologias

### Linguagem e plataforma

- C# 12;
- .NET 8 (`net8.0`);
- Raylib-cs 8.1.0;
- configuração `Release` para as medições finais;
- `System.Threading.Thread` para os workers controlados pelo projeto;
- `Barrier` ou mecanismo equivalente para coordenar o começo e o fim de cada ciclo;
- `Stopwatch` para tempo real e métricas de alta resolução.

O projeto deverá fixar no arquivo `.csproj` a versão escolhida do .NET e a versão do pacote gráfico, evitando diferenças entre computadores.

### Biblioteca gráfica

Será utilizada a Raylib por meio do Raylib-cs para:

- criar a janela;
- receber teclado e mouse;
- desenhar territórios, bases, recursos e agentes;
- desenhar botões e indicadores;
- apresentar as métricas.

A Raylib não será usada para gerenciar o paralelismo. Nenhuma chamada de desenho poderá ocorrer em um worker de simulação.

---

## 5. Estrutura visual

A janela será dividida em quatro regiões iguais:

```text
┌─────────────────────────┬─────────────────────────┐
│ Civilização A — azul    │ Civilização B — vermelho│
│                         │                         │
│ agentes, recursos, base │ agentes, recursos, base │
├─────────────────────────┼─────────────────────────┤
│ Civilização C — verde   │ Civilização D — amarelo │
│                         │                         │
│ agentes, recursos, base │ agentes, recursos, base │
└─────────────────────────┴─────────────────────────┘
```

Representação sugerida:

- pequeno quadrado/ponto: agente;
- quadrado: ponto de recurso;
- triângulo: base;
- texto e barras simples para métricas.

Não serão usados sprites complexos, iluminação ou efeitos que possam transformar a GPU no principal gargalo. Os agentes serão desenhados como pequenos quadrados, pois círculos geram muito mais vértices quando a população chega aos milhares.

---

## 6. Regras da simulação

### 6.1 Civilizações

Cada civilização terá:

- identificador e cor;
- território retangular próprio;
- uma base;
- uma lista de agentes;
- uma lista imutável de pontos de recurso;
- contador de recursos armazenados;
- limite populacional;
- gerador pseudoaleatório próprio;
- estatísticas próprias.

As quatro civilizações usarão os mesmos parâmetros. Os mapas serão equivalentes em coordenadas relativas para que nenhuma civilização receba uma carga de trabalho intencionalmente maior.

### 6.2 Recursos infinitos

“Recurso infinito” significa que:

- cada território recebe uma quantidade fixa de pontos no início;
- a posição dos pontos não muda;
- um ponto nunca é consumido, removido ou esgotado;
- qualquer quantidade de agentes pode escolher o mesmo ponto;
- não existe reserva ou exclusividade de um ponto;
- coletar cria uma unidade carregada pelo agente;
- a quantidade armazenada na base aumenta quando o agente entrega a unidade.

A **lista de pontos é finita**, enquanto a **capacidade de cada ponto é infinita**. Não devem surgir novos pontos durante a execução, pois isso faria a memória e o custo da busca crescerem por um motivo diferente da população.

Como os pontos são imutáveis e pertencem a uma única civilização, a busca não precisa bloquear outros workers.

### 6.3 Agentes

Cada agente terá, no mínimo:

- posição;
- velocidade em pixels por segundo;
- estado atual;
- destino atual;
- identificador do recurso escolhido;
- indicação de que está carregando uma unidade;
- instante real em que começou a coleta;
- identificador da civilização.

Estados:

```text
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
```

Comportamento:

1. o agente sem carga percorre os pontos de recurso da sua civilização;
2. calcula a distância até cada ponto;
3. escolhe o ponto mais próximo;
4. move-se até ele;
5. aguarda o tempo real de coleta;
6. carrega uma unidade;
7. retorna à base;
8. deposita a unidade e reinicia o ciclo.

Para manter uma carga computacional contínua, previsível e visível, todos os agentes recalcularão o recurso mais próximo a cada atualização. Quando um agente estiver carregando uma unidade, o resultado ficará preparado como destino da próxima viagem. O cálculo deverá ser realmente utilizado. A distância ao quadrado pode ser usada para evitar `sqrt`, desde que o mesmo algoritmo seja usado em todos os modos.

Não haverá inicialmente:

- colisão entre agentes;
- pathfinding avançado;
- combate;
- morte;
- reprodução entre agentes;
- interação entre civilizações.

### 6.4 Crescimento populacional

Uma civilização poderá criar um agente quando:

- possuir recursos armazenados suficientes;
- estiver abaixo do limite populacional absoluto;
- o intervalo real mínimo desde o último nascimento tiver passado;
- o controlador de segurança permitir crescimento.

Ao criar um agente, o custo será retirado dos recursos armazenados. Quando o crescimento estiver bloqueado por FPS baixo, os recursos continuarão sendo coletados, mas não serão gastos. Ao voltar a permitir crescimento, o intervalo mínimo entre nascimentos continuará valendo para impedir a criação de muitos agentes no mesmo frame.

---

## 7. Cronômetro e tempo real

O projeto não terá um relógio “in game”. Todo tempo será derivado de `Stopwatch`, que é monotônico e apropriado para medir intervalos.

Serão mantidas duas medidas:

- **tempo real de execução ativa:** tempo de cronômetro desde o início, descontando períodos em que o usuário pausou;
- **delta real:** intervalo real entre duas atualizações consecutivas, enviado pela thread principal aos workers.

Regras:

- velocidades serão expressas em pixels por segundo;
- coleta e intervalo de nascimento serão expressos em segundos reais;
- não haverá multiplicador de velocidade do tempo;
- não haverá contagem de dias, anos ou turnos fictícios;
- ao pausar, os workers não atualizam agentes e o cronômetro de execução ativa é pausado;
- ao continuar, a janela de FPS é reiniciada para que o tempo de pausa não contamine a medição;
- o relógio exibido será identificado como `Tempo real`, no formato `mm:ss.fff`.

O número interno do ciclo poderá existir apenas como contador técnico para depuração e testes. Ele não representa tempo da simulação e não deverá aparecer como relógio para o usuário.

---

## 8. FPS ilimitado

A aplicação não imporá limite de FPS.

Regras obrigatórias:

- não chamar `SetTargetFPS`;
- não inserir `Thread.Sleep`, espera artificial ou atraso no loop de renderização;
- não solicitar VSync pela aplicação;
- não oferecer botão ou configuração de limite de FPS;
- medir e mostrar o FPS realmente produzido;
- medir o frame time com `Stopwatch`.

O sistema operacional, o compositor de janelas ou o driver podem impor restrições externas. A aplicação apenas garante que não adicionará um limitador próprio.

O FPS será calculado sobre uma janela móvel de 1 segundo de tempo real, evitando decisões baseadas em um único frame.

---

## 9. Proteção de desempenho e limite populacional

Existirão duas proteções independentes.

### 9.1 Limite absoluto

Valor inicial sugerido:

- máximo de 5.000 agentes por civilização;
- máximo de 20.000 agentes no total.

Os valores poderão ser reduzidos após os testes no computador da apresentação. Nenhum controle, inclusive o Stress Test, poderá ultrapassar esse limite.

### 9.2 Bloqueio adaptativo aos 30 FPS

O controlador observará o FPS médio da janela móvel de 1 segundo.

- se o FPS médio for **menor ou igual a 30**, todo surgimento de novos agentes será suspenso;
- os agentes existentes continuarão se movimentando, procurando e coletando;
- os recursos continuarão sendo armazenados;
- nenhum agente será removido;
- a interface mostrará `Crescimento pausado — FPS baixo`;
- o Stress Test e qualquer comando manual também deverão respeitar o bloqueio.

Para evitar que o sistema ligue e desligue o crescimento repetidamente próximo de 30 FPS, será usada histerese:

- bloquear em `FPS médio <= 30`;
- liberar somente após `FPS médio >= 35` durante 2 segundos consecutivos.

Depois de liberar, os agentes surgirão respeitando o intervalo normal ou o intervalo do Stress Test. Não haverá criação acumulada em massa no primeiro frame.

Esse mecanismo reduz o risco de crescimento descontrolado, mas não promete que o FPS nunca terá uma queda momentânea abaixo de 30, pois outras aplicações, o sistema operacional e a renderização também podem causar oscilações.

Estados possíveis do crescimento:

```text
Enabled
BlockedByLowFps
BlockedByPopulationLimit
PausedByUser
DisabledForBenchmark
```

---

## 10. Modelo de execução por frame

O projeto utilizará um modelo sincronizado e fácil de explicar em sala. Simulação e renderização não alteram o mesmo estado ao mesmo tempo.

Fluxo da thread principal:

```text
Ler o cronômetro e calcular o delta real
        ↓
Receber input
        ↓
Preparar o comando do ciclo
        ↓
Liberar os workers ativos
        ↓
Aguardar todos concluírem
        ↓
Registrar o tempo da simulação
        ↓
Renderizar o estado estável
        ↓
Atualizar FPS e interface
        ↓
Repetir sem limitar o FPS
```

Enquanto a thread principal desenha, os workers aguardam o próximo ciclo. Assim:

- a renderização nunca encontra uma lista sendo modificada;
- não é necessário aplicar `lock` em cada agente;
- o custo da sincronização fica visível nas métricas;
- o frame completo inclui a espera pela simulação, tornando o efeito sobre o FPS observável.

O tempo da simulação será medido na thread principal desde a liberação do trabalho até a conclusão de todos os workers. Essa é uma medida de tempo decorrido real, não a soma do tempo de CPU de cada worker.

---

## 11. Modos de threading

### 11.1 Um worker de simulação

```text
Thread principal → input, coordenação, interface e renderização
Worker 1         → civilizações A, B, C e D, nessa ordem
```

As quatro civilizações são processadas sequencialmente pelo mesmo worker. Existem duas threads controladas pelo projeto: uma principal e uma de simulação.

### 11.2 Dois workers de simulação

```text
Thread principal → input, coordenação, interface e renderização
Worker 1         → civilizações A e B
Worker 2         → civilizações C e D
```

Existem três threads controladas pelo projeto.

### 11.3 Quatro workers de simulação

```text
Thread principal → input, coordenação, interface e renderização
Worker 1         → civilização A
Worker 2         → civilização B
Worker 3         → civilização C
Worker 4         → civilização D
```

Existem cinco threads controladas pelo projeto.

### 11.4 Ciclo de vida dos workers

- os workers serão criados com `Thread`, e não por meio de tarefas livres do ThreadPool;
- permanecerão vivos e aguardando trabalho entre ciclos;
- nunca serão criados novamente a cada frame;
- receberão nomes como `Simulation Worker 1`;
- uma exceção em qualquer worker será capturada, exibida e encerrará o coordenador de forma segura;
- ao fechar ou reiniciar, todos receberão cancelamento e a aplicação aguardará `Join`;
- nenhuma thread poderá permanecer executando depois do fechamento da janela.

### 11.5 Troca de modo durante a execução

A troca entre 1, 2 e 4 workers será permitida, mas somente entre ciclos:

1. concluir o ciclo atual;
2. impedir o início de outro ciclo;
3. encerrar e aguardar os workers antigos;
4. criar o novo conjunto de workers;
5. preservar o estado das civilizações;
6. reiniciar a janela de métricas;
7. executar um curto período de aquecimento antes de exibir comparações.

O tempo gasto criando ou encerrando threads não fará parte do `Simulation Time` normal. A interface mostrará `Aquecendo métricas...` durante a estabilização.

---

## 12. Propriedade dos dados e segurança entre threads

Cada civilização será atualizada por somente um worker em cada ciclo. Esse worker será o único autorizado a modificar:

- agentes da civilização;
- recursos armazenados;
- estado da base;
- nascimento de agentes;
- estatísticas internas da civilização.

Os pontos de recurso serão imutáveis depois da inicialização. Cada civilização terá seu próprio gerador pseudoaleatório; não haverá um `Random` global compartilhado.

A thread principal só lerá o estado depois que todos os workers concluírem o ciclo e antes de liberá-los novamente. Essa regra é preferível a espalhar `lock` pelas entidades.

A sincronização deverá aceitar cancelamento para evitar deadlock ao fechar, reiniciar ou trocar o modo. Qualquer falha em um worker deve liberar a thread principal da espera e produzir uma mensagem de erro compreensível.

---

## 13. Métricas

A interface mostrará:

- FPS médio da última janela de 1 segundo;
- frame time médio em milissegundos;
- tempo do último ciclo de simulação;
- média, mediana e percentil 95 do `Simulation Time`;
- tempo de renderização;
- atualizações concluídas por segundo;
- população total e por civilização;
- workers de simulação ativos;
- threads controladas pelo projeto;
- tempo real de execução ativa;
- estado do crescimento populacional;
- limite populacional;
- quantidade de pontos de recurso;
- recursos armazenados por civilização.

Definições:

- `Simulation Time`: tempo real entre liberar os workers e todos terminarem;
- `Render Time`: tempo gasto desenhando o frame;
- `Frame Time`: tempo real completo entre frames;
- `Updates/s`: ciclos completos por segundo;
- `Speedup(N) = mediana com 1 worker / mediana com N workers`;
- `Eficiência(N) = Speedup(N) / N`.

O FPS é importante para a demonstração visual, mas a métrica principal para provar o benefício da paralelização será o `Simulation Time`. Se a renderização se tornar o gargalo, o tempo da simulação ainda permitirá avaliar corretamente os workers.

As métricas usarão buffers circulares ou acumuladores reutilizáveis. Não deverá haver criação de listas e objetos a cada frame apenas para calcular estatísticas.

---

## 14. Interface

Exemplo de painel:

```text
Simulação de Civilizações — Multithreading

Tempo real: 02:31.482
FPS: 42.7 (ilimitado)       Frame: 23.4 ms
Simulação: 14.8 ms          Renderização: 8.1 ms
Simulação p50/p95: 14.5 / 16.2 ms
Atualizações/s: 42.7

População: 8.432 / 20.000
Workers de simulação: 1
Threads do projeto: 2 (1 principal + 1 worker)
Crescimento: ATIVO

[1 worker] [2 workers] [4 workers]
[Pausar] [Reiniciar] [Stress Test]
```

Quando necessário:

```text
Crescimento: PAUSADO — FPS médio <= 30
Será retomado após FPS >= 35 por 2 segundos.
```

Não haverá controle de limite de FPS.

---

## 15. Controles

### Seleção de workers

- botões para 1, 2 e 4 workers;
- teclas `1`, `2` e `4` como atalhos;
- modo selecionado destacado;
- troca segura no fim do ciclo;
- estado atual preservado;
- métricas reiniciadas após a troca.

### Pausa

- para as atualizações e os nascimentos;
- mantém a janela e a interface responsivas;
- pausa o cronômetro de execução ativa;
- reinicia a janela de medição de FPS ao continuar.

### Reinício

- encerra os workers atuais;
- recria o mundo com a mesma seed configurada;
- restaura população, recursos e métricas iniciais;
- inicia novamente no modo de workers selecionado.

### Stress Test

O Stress Test não adicionará milhares de agentes em um único frame. Ele ativará um crescimento acelerado em lotes pequenos e periódicos.

O Stress Test:

- injeta agentes sem consumir o estoque de recursos, pois é uma ferramenta de demonstração;
- respeita o limite absoluto;
- respeita o bloqueio aos 30 FPS;
- para de criar agentes imediatamente quando o crescimento é bloqueado;
- mostra visualmente que está ativo;
- pode ser desligado pelo usuário;
- não remove os agentes já criados.

Isso permite aproximar a aplicação do limite com segurança e sem uma queda abrupta causada por uma única alocação enorme.

---

## 16. Parâmetros iniciais configuráveis

Os valores abaixo são pontos de partida. Devem ficar centralizados em `SimulationConfig` e ser calibrados no computador da apresentação, sem alterar as regras entre os modos.

| Parâmetro | Valor inicial sugerido |
|---|---:|
| Civilizações | 4 |
| População inicial por civilização | 10 |
| Pontos de recurso por civilização | 768 |
| Capacidade do ponto de recurso | Infinita |
| Unidade carregada por viagem | 1 |
| Custo inicial de um agente | 10 unidades |
| Duração real da coleta | 0,25 s |
| Velocidade do agente | 80 px/s |
| Limite por civilização | 5.000 |
| Limite total | 20.000 |
| Janela de FPS | 1 s |
| Bloqueio de crescimento | FPS médio <= 30 |
| Liberação de crescimento | FPS médio >= 35 por 2 s |
| Aquecimento após trocar workers | 2 s |

Se a diferença entre os modos não for visível, deverá ser ajustada primeiro a quantidade fixa de pontos examinados por agente ou a população do cenário. Não se deve adicionar `Sleep`, espera artificial ou um algoritmo diferente em cada modo.

---

## 17. Organização sugerida do código

```text
src/
├── Program.cs
├── Core/
│   ├── Game.cs
│   ├── SimulationConfig.cs
│   └── SimulationState.cs
├── Entities/
│   ├── Agent.cs
│   ├── AgentState.cs
│   ├── Civilization.cs
│   ├── ResourceNode.cs
│   └── CivilizationBase.cs
├── Simulation/
│   ├── SimulationCoordinator.cs
│   ├── SimulationWorker.cs
│   ├── WorkerMode.cs
│   ├── PopulationGrowthController.cs
│   └── SimulationClock.cs
├── Rendering/
│   ├── Renderer.cs
│   └── UserInterface.cs
├── Metrics/
│   ├── PerformanceMetrics.cs
│   ├── RollingWindow.cs
│   └── BenchmarkResult.cs
└── Diagnostics/
    └── BenchmarkRunner.cs
```

Responsabilidades principais:

- `Game`: loop principal, input e ciclo de vida;
- `SimulationState`: contém as quatro civilizações;
- `SimulationCoordinator`: distribui civilizações e sincroniza workers;
- `SimulationWorker`: atualiza somente as civilizações atribuídas;
- `SimulationClock`: fornece delta e tempo real ativo;
- `PopulationGrowthController`: aplica custo, intervalos e proteções;
- `Renderer`: somente desenho;
- `PerformanceMetrics`: coleta métricas sem controlar a simulação;
- `BenchmarkRunner`: executa cenários repetíveis.

Não é necessário criar classes separadas para uma simulação de 1 e 4 workers. Um único coordenador com atribuições diferentes reduz duplicação e garante que todos os modos executem exatamente a mesma lógica.

---

## 18. Dois tipos de demonstração

### 18.1 Demonstração ao vivo com crescimento

Essa é a parte visual da apresentação:

1. iniciar com 1 worker e poucos agentes;
2. ativar crescimento normal ou Stress Test;
3. observar o aumento do `Simulation Time` e a queda do FPS;
4. ao chegar a 30 FPS, mostrar o bloqueio automático de novos agentes;
5. anotar população e métricas atuais;
6. trocar para 4 workers, preservando exatamente o mesmo estado;
7. aguardar o período de aquecimento;
8. observar a redução do tempo de simulação e a recuperação do FPS;
9. quando o FPS permanecer acima de 35, mostrar a retomada automática do crescimento.

Essa sequência comunica visualmente por que o paralelismo ajuda.

### 18.2 Benchmark controlado

Essa etapa fornece números comparáveis. O crescimento automático ficará desativado e cada execução começará a partir do mesmo estado.

Para cada população segura escolhida:

1. usar a mesma seed;
2. usar as mesmas posições e estados dos agentes;
3. usar a mesma quantidade e posição dos recursos;
4. executar com 1, 2 e 4 workers;
5. aquecer por pelo menos 3 segundos;
6. medir por pelo menos 10 segundos;
7. repetir cada caso três vezes;
8. registrar mediana e p95;
9. executar em `Release`, fora do depurador;
10. registrar CPU, quantidade de núcleos lógicos, memória e sistema operacional.

Tabela de resultados:

| População | Workers | Simulation p50 | Simulation p95 | FPS médio | Updates/s | Speedup |
|---:|---:|---:|---:|---:|---:|---:|
| a medir | 1 | — | — | — | — | 1,00x |
| a medir | 2 | — | — | — | — | — |
| a medir | 4 | — | — | — | — | — |

Não preencher a documentação com resultados hipotéticos como se fossem reais.

### 18.3 Resultados verificados em 08/09/2026

Ambiente da medição:

- Windows 10.0.26200;
- .NET 8.0.31;
- processo x64;
- 16 processadores lógicos disponíveis;
- build `Release`, fora do depurador;
- 4.000 agentes no total;
- 768 pontos de recurso inesgotáveis por civilização;
- aquecimento de 3 segundos;
- três repetições de 10 segundos por modo.

| População total | Workers | Simulation p50 | Simulation p95 | Speedup |
|---:|---:|---:|---:|---:|
| 4.000 | 1 | 6,613 ms | 7,798 ms | 1,00x |
| 4.000 | 2 | 3,427 ms | 5,058 ms | 1,93x |
| 4.000 | 4 | 2,546 ms | 2,717 ms | 2,60x |

Calibração gráfica oculta com a população máxima de 20.000 agentes:

| Workers | FPS médio | Simulation Time médio | Render Time médio |
|---:|---:|---:|---:|
| 1 | 25,0 | 37,30 ms | 4,88 ms |
| 4 | 66,0 | 11,66 ms | 4,20 ms |

Esses valores confirmam no computador atual que a carga atravessa a região de 30 FPS com 1 worker e se recupera claramente com 4 workers. A calibração deverá ser repetida se a apresentação usar outro computador.

---

## 19. Etapas de implementação

### Etapa 1 — Estrutura mínima

- [x] criar solução e projeto C#;
- [x] fixar versões no `.csproj`;
- [x] configurar Raylib-cs;
- [x] criar a janela;
- [x] manter o FPS ilimitado;
- [x] dividir a tela em quatro territórios;
- [x] implementar o cronômetro de tempo real.

### Etapa 2 — Mundo e agentes

- [x] criar quatro civilizações determinísticas;
- [x] criar bases;
- [x] criar pontos de recurso fixos e inesgotáveis;
- [x] criar agentes e estados;
- [x] implementar busca, movimento, coleta, retorno e depósito;
- [x] confirmar que a quantidade de pontos nunca diminui ou aumenta.

### Etapa 3 — Crescimento seguro

- [x] implementar custo de nascimento;
- [x] implementar intervalo real entre nascimentos;
- [x] implementar limite por civilização e limite total;
- [x] calcular FPS médio em janela de 1 segundo;
- [x] bloquear crescimento em 30 FPS;
- [x] liberar em 35 FPS após 2 segundos;
- [x] implementar Stress Test gradual e seguro.

### Etapa 4 — Multithreading

- [x] criar workers persistentes;
- [x] implementar coordenação cancelável;
- [x] implementar atribuição para 1 worker;
- [x] implementar atribuição para 2 workers;
- [x] implementar atribuição para 4 workers;
- [x] implementar troca segura entre ciclos;
- [x] garantir encerramento e `Join` de todos os workers.

### Etapa 5 — Interface e métricas

- [x] mostrar FPS e frame time;
- [x] mostrar tempo de simulação e renderização;
- [x] mostrar p50, p95 e updates/s;
- [x] mostrar população e estado do crescimento;
- [x] mostrar workers e threads controladas pelo projeto;
- [x] implementar pausa e reinício;
- [x] destacar aquecimento e FPS baixo.

### Etapa 6 — Benchmark e preparação da apresentação

- [x] criar cenários determinísticos de população fixa;
- [x] automatizar período de aquecimento e coleta;
- [x] exportar ou copiar tabela de resultados;
- [ ] testar em `Release` no computador da apresentação;
- [x] calibrar população e quantidade de recursos no computador atual;
- [ ] repetir a calibração no computador da apresentação, caso seja outro;
- [ ] ensaiar a sequência de 1 para 4 workers;
- [ ] testar fechamento, reinício e troca de modo repetidamente.

---

## 20. Critérios de aceitação

O projeto estará pronto quando todos os itens abaixo forem verdadeiros:

### Funcionamento

- [x] existem quatro civilizações visíveis e independentes;
- [x] agentes completam todo o ciclo de coleta;
- [x] pontos de recurso nunca acabam;
- [x] o cronômetro corresponde ao tempo real de execução ativa;
- [x] pausa e continuação não causam saltos de tempo;
- [x] o FPS não é limitado pela aplicação;
- [x] crescimento para quando o FPS médio chega a 30;
- [x] crescimento só volta nas condições de recuperação definidas;
- [x] nenhuma forma de nascimento ignora o limite absoluto;
- [x] Stress Test não cria uma quantidade enorme em um único frame.

### Concorrência

- [x] o modo 1 usa um worker para as quatro civilizações;
- [x] o modo 4 usa exatamente quatro workers de simulação;
- [x] somente a thread principal chama a Raylib para desenhar;
- [x] trocar o modo não perde nem duplica agentes;
- [ ] reiniciar ou fechar não deixa workers ativos;
- [x] não ocorrem deadlocks após trocas repetidas;
- [x] os modos executam a mesma lógica e produzem o mesmo resultado para o mesmo estado e delta.

### Medição e apresentação

- [x] `Simulation Time` mede tempo real até todos os workers terminarem;
- [x] construção de threads não entra na medição normal;
- [x] métricas são reiniciadas e aquecidas após troca de modo;
- [x] benchmark usa estado, seed e população iguais;
- [x] resultados finais foram coletados em `Release` no computador atual;
- [x] a interface diferencia workers de simulação das threads internas do .NET;
- [x] a diferença entre 1 e 4 workers é observável no computador atual;
- [ ] confirmar a diferença no computador da apresentação, caso seja outro.

---

## 21. Riscos e respostas

### A renderização se tornar o gargalo

Resposta:

- usar formas simples;
- evitar texto individual por agente;
- medir `Render Time` separadamente;
- usar `Simulation Time` como métrica principal;
- ajustar a carga de busca antes de aumentar a complexidade gráfica.

### Pouca diferença entre 1 e 4 workers

Resposta:

- confirmar que o computador possui núcleos disponíveis;
- executar em `Release` e sem depurador;
- aumentar igualmente os pontos fixos examinados por agente;
- aumentar a população com o Stress Test;
- confirmar que os workers são persistentes;
- verificar se renderização ou coleta de lixo dominam o frame.

### Pausas causadas pelo coletor de lixo

Resposta:

- pré-alocar capacidade das listas;
- evitar LINQ e alocações no loop dos agentes;
- reutilizar buffers de métricas;
- não criar objetos temporários durante cálculos de distância.

### Um worker receber mais trabalho

Resposta:

- iniciar civilizações com parâmetros equivalentes;
- mostrar população por civilização;
- no benchmark, usar a mesma população em todas;
- registrar o tempo individual de cada worker apenas como diagnóstico.

### Queda abrupta abaixo de 30 FPS

Resposta:

- calcular FPS em uma janela curta e estável;
- adicionar agentes em lotes pequenos;
- bloquear todas as fontes de nascimento;
- manter também o limite populacional absoluto;
- calibrar o Stress Test no computador da apresentação.

---

## 22. Fora do escopo inicial

- combate e guerras;
- diplomacia;
- economia complexa;
- construção de cidades;
- árvore tecnológica;
- mapa procedural;
- pathfinding avançado;
- interação entre civilizações;
- multiplayer;
- recursos escassos ou regeneráveis;
- relógio de dias, anos ou eras;
- limite configurável de FPS;
- troca da lógica por algoritmos diferentes conforme o modo de workers.

Esses itens não ajudam diretamente a explicar a diferença entre processamento sequencial e paralelo e só deverão ser considerados depois da apresentação principal estar pronta.

---

## 23. Roteiro curto para a apresentação

1. Explicar que a imagem sempre usa a thread principal.
2. Selecionar 1 worker e mostrar: `2 threads do projeto = 1 principal + 1 worker`.
3. Mostrar que esse worker atualiza as quatro civilizações em sequência.
4. Ativar o Stress Test gradual.
5. Observar população, tempo da simulação e FPS.
6. Mostrar que, ao atingir 30 FPS, novos agentes deixam de surgir sem limitar o FPS e sem remover agentes.
7. Trocar para 4 workers mantendo o mesmo mundo.
8. Mostrar: `5 threads do projeto = 1 principal + 4 workers`.
9. Aguardar o aquecimento e comparar o `Simulation Time`.
10. Mostrar a recuperação do FPS e, se atingir 35 FPS de forma estável, a retomada do crescimento.
11. Exibir a tabela do benchmark controlado.
12. Explicar por que o ganho não é exatamente 4x e como o número de núcleos influencia o resultado.

Esse roteiro deve permitir que a turma veja a diferença primeiro e entenda a explicação técnica logo depois.
