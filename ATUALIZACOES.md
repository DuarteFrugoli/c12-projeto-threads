# Atualizações do projeto

Este documento resume tudo o que foi adicionado ou alterado na simulação depois da versão com 1, 2 e 4 workers. O objetivo das mudanças foi aproximar o projeto do conteúdo de **Sistemas Operacionais**: além de mostrar ganho de desempenho com threads, agora ele mostra condição de corrida, exclusão mútua, operações atômicas, produtor-consumidor, desbalanceamento de carga e o limite imposto pelo número de núcleos.

Sumário:

1. [Visão geral](#1-visão-geral)
2. [Mina central e condição de corrida](#2-mina-central-e-condição-de-corrida)
3. [Thread árbitro e produtor-consumidor](#3-thread-árbitro-e-produtor-consumidor)
4. [Batalhas pela mina](#4-batalhas-pela-mina)
5. [Linha do tempo das threads](#5-linha-do-tempo-das-threads)
6. [Escalabilidade: mais threads que núcleos](#6-escalabilidade-mais-threads-que-núcleos)
7. [Mudanças na interface](#7-mudanças-na-interface)
8. [Mudanças internas no código](#8-mudanças-internas-no-código)
9. [Novos comandos de terminal](#9-novos-comandos-de-terminal)
10. [Testes](#10-testes)
11. [Problemas encontrados e como foram resolvidos](#11-problemas-encontrados-e-como-foram-resolvidos)
12. [Resultados medidos](#12-resultados-medidos)
13. [Cuidados e limitações](#13-cuidados-e-limitações)
14. [Roteiro sugerido para a apresentação](#14-roteiro-sugerido-para-a-apresentação)
15. [Lista de arquivos](#15-lista-de-arquivos)

---

## 1. Visão geral

### Antes

- quatro civilizações totalmente independentes;
- nenhum dado compartilhado entre os workers;
- a demonstração mostrava apenas que 4 workers são mais rápidos que 1.

Esse é o caso mais fácil de paralelismo: cada thread cuida da sua parte e ninguém disputa nada. Os problemas centrais de SO (sincronização, contenção, escalonamento) não apareciam.

### Depois

| Recurso novo | Conceito de SO demonstrado |
|---|---|
| Mina central compartilhada | Condição de corrida, região crítica |
| Modos `lock` e `Interlocked` | Exclusão mútua, operação atômica (CAS), contenção |
| Thread árbitro com buffer limitado | Produtor-consumidor, semáforos, bloqueio |
| Batalhas com saque | Troca de mensagens atômica entre threads |
| Populações diferentes | Desbalanceamento de carga entre threads |
| Linha do tempo das threads | Execução sequencial e paralela, ociosidade |
| Tela de escalabilidade | Limite dos núcleos, troca de contexto, Lei de Amdahl |

Threads controladas pelo projeto com a mina ativa:

| Modo | Principal | Workers | Árbitro | Total |
|---|---:|---:|---:|---:|
| 1 worker | 1 | 1 | 1 | 3 |
| 2 workers | 1 | 2 | 1 | 4 |
| 4 workers | 1 | 4 | 1 | 6 |

---

## 2. Mina central e condição de corrida

### Como funciona

- A mina fica no cruzamento dos quatro territórios, no centro da área do mapa.
- Cada agente vai até ela a cada 5 viagens. Nas outras viagens, continua coletando nos recursos da própria civilização.
- Na mina, o agente faz tentativas de extração em tempo real (8 por segundo) até carregar 6 unidades ou esperar 2,5 segundos.
- A carga da mina é entregue na base, como a de um recurso comum.
- A thread principal regenera o estoque antes de liberar os workers, a 5.000 unidades por segundo, até o limite de 40.000.

O **estoque da mina é o único dado da simulação escrito por vários workers ao mesmo tempo**. Por isso ele é a região crítica.

### O problema: ler, calcular, gravar

Cada extração faz três passos:

1. **lê** o estoque;
2. **calcula** o rendimento (1 a 3 unidades);
3. **grava** o estoque menos o rendimento.

Se duas threads fazem isso ao mesmo tempo, uma apaga o trabalho da outra:

```text
Estoque = 100
Worker 1 lê 100            Worker 2 lê 100
Worker 1 tira 2            Worker 2 tira 3
Worker 1 grava 98          Worker 2 grava 97   <- a extração do Worker 1 sumiu
```

Saíram 5 unidades, mas o estoque só baixou 3. Duas unidades foram **duplicadas**.

### Os três modos de sincronização (tecla `M`)

| Modo | Como funciona | O que o painel mostra |
|---|---|---|
| **Sem sincronização** | Lê, calcula e grava sem proteção | Unidades duplicadas subindo com 2 ou 4 workers; sempre zero com 1 worker |
| **`lock` (Monitor)** | Só uma thread por vez entra na região crítica; as outras esperam | Zero duplicadas; quantas vezes o lock estava ocupado (contenção) e o tempo total de espera |
| **`Interlocked` (CAS)** | Calcula fora e grava com `CompareExchange`: "grave 98 somente se o estoque ainda for 100". Se outra thread mudou o valor, recalcula | Zero duplicadas; quantas retentativas foram necessárias |

Analogia para a apresentação:

- **lock** é um banheiro com chave: entra um de cada vez e os outros ficam na fila.
- **Interlocked** é editar um documento compartilhado checando se ninguém alterou antes de salvar; se alguém alterou, você refaz a sua parte.

### Como as unidades duplicadas são contadas

Cada civilização guarda quanto extraiu. Esse contador é escrito só pelo worker dela, então é sempre correto. A thread principal compara, entre os ciclos:

```text
estoque esperado = estoque inicial + total regenerado - soma do que as civilizações extraíram
duplicadas       = estoque real - estoque esperado
```

O contador recomeça do zero a cada troca de modo.

### Por que existe um cálculo entre a leitura e a escrita

O rendimento é calculado com um pequeno laço de hash entre a leitura e a gravação. Isso **alarga a janela** em que outra thread pode interferir, como acontece em código real que valida ou transforma um valor antes de gravá-lo. Sem esse trabalho, a janela dura poucos nanossegundos e a condição de corrida quase nunca aparece. O custo é **o mesmo nos três modos**, então a comparação continua justa. Vale mencionar isso na apresentação.

---

## 3. Thread árbitro e produtor-consumidor

### Fluxo

```text
Worker 1 ─┐
Worker 2 ─┼──> BoundedBuffer (64 vagas) ──> Thread árbitro ──> decisão imutável
Worker 3 ─┤         produtores                consumidor          (lida pelos workers)
Worker 4 ─┘
```

1. Ao terminar de atualizar uma civilização, o worker **produz** um relatório: quantos agentes estavam na mina, quanto extraiu e qual é a população.
2. O relatório entra num **buffer limitado**.
3. A **thread árbitro consome** os relatórios. Enquanto o buffer está vazio, ela fica bloqueada, sem gastar CPU.
4. A cada rodada de 2 segundos, o árbitro decide o resultado da disputa e publica a decisão.

### O buffer limitado (`BoundedBuffer`)

É a implementação clássica dos livros de SO:

- um **mutex** (`lock`) protege a fila;
- um **semáforo de vagas livres** bloqueia o produtor quando o buffer está cheio;
- um **semáforo de itens** bloqueia o consumidor quando o buffer está vazio.

O painel mostra o tamanho atual da fila e quantas vezes um produtor encontrou o buffer cheio. Ao reiniciar ou fechar, o buffer é **fechado**: isso acorda qualquer thread bloqueada, e nenhuma fica presa.

### Publicação sem bloqueio

A decisão do árbitro é um objeto **imutável** (`MineControl`). O árbitro troca a referência inteira de forma atômica (`Volatile.Write`), e os workers apenas leem a referência atual. **Os workers nunca esperam pelo árbitro**: se a decisão chega no meio de um ciclo, ela é aplicada no ciclo seguinte.

### Ciclo de vida

- O árbitro é criado com o mundo e sobrevive às trocas entre 1, 2 e 4 workers.
- Ao reiniciar, os workers são encerrados primeiro, depois o árbitro, e então tudo é recriado.
- Uma falha no árbitro é capturada e exibida como erro, em vez de travar a aplicação.

---

## 4. Batalhas pela mina

### Regra

A cada rodada de 2 segundos, vence a civilização que teve **mais agentes na mina** (em agentes × segundos). Quando pelo menos duas civilizações disputaram a rodada:

| Quem | O que acontece |
|---|---|
| Vencedora | Todos os agentes dela saem da mina levando a carga |
| Derrotadas | Entregam a carga como **saque** para a vencedora e saem da mina |
| Derrotadas | **Metade** dos agentes que estavam na mina **morre** |

### Proteções contra eliminação

Sem limites, a maior civilização ganharia sempre, mataria as outras e ficaria cada vez maior. Por isso:

- cada batalha mata **no máximo 5%** da população da derrotada;
- nenhuma civilização fica com **menos de 10 agentes**;
- uma civilização com **menos da metade** da população da vencedora **recua sem baixas**. No painel, isso aparece com um `r` ao lado do nome.

### O saque atravessa threads

A vencedora pode estar sendo atualizada por **outro worker no mesmo instante**. Então a derrotada não pode somar diretamente nos recursos da vencedora. A transferência passa por uma **caixa de mensagens atômica**:

- a derrotada deposita com `Interlocked.Add`;
- a vencedora retira tudo com `Interlocked.Exchange`, no começo da atualização dela.

### Ligar e desligar (tecla `B`)

As batalhas fazem as populações divergirem. Com 4 workers, cada ciclo **espera o worker mais lento**, então o ganho máximo passa a ser:

```text
ganho máximo = população total ÷ maior população
```

| Populações (A, B, C, D) | Ganho máximo com 4 workers |
|---|---:|
| 2.500, 2.500, 2.500, 2.500 | 4,0x |
| 3.000, 2.500, 2.500, 2.000 | 3,3x |
| 4.000, 2.500, 2.000, 1.500 | 2,5x |

Por isso existe o botão:

- **Batalhas ligadas:** populações diferentes. Bom para mostrar a disputa e o desbalanceamento de carga na linha do tempo.
- **Batalhas desligadas:** populações equilibradas. Bom para comparar 1 contra 4 workers. A mina continua compartilhada, então a demonstração de `lock` e `Interlocked` segue funcionando.

---

## 5. Linha do tempo das threads

Fica no painel lateral, no formato de gráfico de Gantt:

- **uma linha por thread:** principal, cada worker e árbitro;
- **workers:** cada trecho tem a cor da civilização que estava sendo processada;
- **principal:** trecho claro para renderização e faixa escura para espera pelos workers;
- **árbitro:** traços roxos curtos a cada relatório processado;
- **porcentagem à direita:** tempo ocupado no último segundo.

A janela de tempo se ajusta sozinha, entre 5 ms e 1 s, para mostrar cerca de seis frames. Os valores são atualizados a cada 0,5 s para facilitar a leitura.

O que dá para mostrar:

| Situação | O que aparece |
|---|---|
| 1 worker | A, B, C e D uma depois da outra na mesma linha: execução sequencial |
| 4 workers | As quatro cores ao mesmo tempo, uma em cada linha: execução paralela |
| Batalhas ligadas | O worker da maior civilização fica mais ocupado, e os outros ociosos |
| Árbitro | Quase 0% ocupado: ele passa o tempo bloqueado esperando relatórios |

Como funciona por dentro: cada thread grava os próprios intervalos num buffer circular (`ThreadTimeline`), e a thread principal lê para desenhar. Há um `lock` nesse buffer porque o árbitro grava enquanto a thread principal desenha.

---

## 6. Escalabilidade: mais threads que núcleos

### A pergunta

"Se 4 threads são mais rápidas que 1, por que não usar 100?"

### Como é medido (tecla `E`)

- A simulação pausa e uma tela com gráfico e tabela é aberta.
- A mesma carga é executada com 1, 2, 3, 4, 6, 8, 12, 16, 24, 32, 48 e 64 threads, mais o número de núcleos lógicos do computador.
- A carga é fixa: **64 civilizações pequenas × 120 agentes = 7.680 agentes**, sem mina. A simulação principal tem só 4 civilizações, então não daria para dividir o trabalho entre mais de 4 threads.
- Para cada quantidade: mundo recriado, 5 ciclos de aquecimento e pelo menos 12 amostras e 0,35 s de medição. Vale a mediana.
- O gráfico mostra o speedup medido, a linha ideal (N threads = N vezes mais rápido) e uma linha vertical nos núcleos lógicos.
- `Enter` mede novamente; `E` volta à simulação.

### Definições

```text
Speedup(N)    = tempo com 1 thread / tempo com N threads
Eficiência(N) = Speedup(N) / N
```

### Resultado num Apple M4 (4 núcleos de desempenho e 6 de eficiência)

| Threads | ms por ciclo | Speedup | Eficiência |
|---:|---:|---:|---:|
| 1 | 9,94 | 1,00x | 100% |
| 2 | 4,93 | 2,02x | 101% |
| 4 | 2,83 | 3,51x | 88% |
| 8 | 2,30 | 4,32x | 54% |
| 10 (núcleos) | 2,30 | 4,32x | 43% |
| 12 | 2,15 | 4,62x | 39% |
| 32 | 2,29 | 4,34x | 14% |
| 64 | 2,77 | 3,58x | 6% |

Leitura:

1. **Até 4 threads:** ganho quase ideal, porque há 4 núcleos de desempenho.
2. **De 4 a 12 threads:** pouco ganho, porque os outros núcleos são de eficiência, mais lentos.
3. **Acima disso:** o tempo **volta a subir**. Com mais threads que núcleos, o SO precisa revezá-las nos mesmos núcleos (troca de contexto), e sincronizar dezenas de threads custa caro.

Os números dependem do computador e devem ser medidos de novo na máquina da apresentação.

---

## 7. Mudanças na interface

### Layout

- Área lógica ampliada de 1280 × 800 para **1600 × 900**.
- **Painel lateral** de 400 px à direita, com três seções: linha do tempo, mina e árbitro.
- A linha de baixo de civilizações (C e D) é **espelhada verticalmente**. Assim todas as bases ficam à mesma distância da mina, e nenhuma leva vantagem.
- O rótulo de cada território fica na borda oposta à mina, para não cobrir a disputa, e agora mostra população, recursos, agentes na mina e mortos em batalha.
- A mina é um losango no centro com a cor da última vencedora, uma barra de estoque e um anel vermelho que pisca a cada batalha.
- O cabeçalho mostra a contagem de threads incluindo o árbitro, por exemplo `Threads: 6 (1 principal + 4 sim. + 1 árbitro)`.

### Controles novos

| Tecla | Botão | Ação |
|---|---|---|
| `M` | `Mina: ...` | Alterna entre sem sincronização, `lock` e `Interlocked`. O botão fica vermelho sem sincronização |
| `B` | `Batalhas: ON/OFF` | Liga ou desliga as batalhas |
| `E` | `Escalabilidade` | Abre ou fecha a tela de escalabilidade |
| `Enter` | — | Na tela de escalabilidade, mede novamente |

Os controles anteriores (`1`, `2`, `4`, `Espaço`, `R`, `S`, `F11`) continuam iguais. Reiniciar preserva o modo de sincronização e o estado das batalhas.

### Tamanho da janela

A janela abre em **1280 × 720** e o conteúdo é reduzido proporcionalmente pela câmera virtual. `F11` amplia para a tela cheia. Veja a seção 11 para entender por que ela não abre maior.

---

## 8. Mudanças internas no código

### `SimulationCoordinator`

- Aceita **qualquer quantidade de workers**, não só 1, 2 ou 4. As civilizações são divididas em blocos contíguos; quando a divisão não é exata, os primeiros workers recebem uma a mais. O construtor antigo com `WorkerMode` continua existindo.
- Grava opcionalmente a linha do tempo de cada worker.
- Executa a **regeneração da mina** como fase sequencial, na thread principal, antes de liberar os workers.
- Passa um `WorkerContext` (número do worker e token de cancelamento) para cada civilização. O token permite que um worker bloqueado no buffer seja liberado ao trocar de modo.

### `Civilization` e `Agent`

- Decisão da viagem à mina: `(id do agente + viagens concluídas) % 5 == 0`.
- Extração na mina, envio do relatório ao árbitro, aplicação do resultado das batalhas, remoção dos mortos e recebimento do saque.
- O agente ganhou estado de viagem à mina, carga extraída, marcação de morte em batalha, e `Update` passou a retornar quantas unidades foram entregues.
- Contadores por civilização em `MineExtractionStats`: tentativas, unidades, contenção, espera no lock, derrotas, mortes, saque perdido e saque recebido.

### `SimulationState`

- Cria a mina quando ela está habilitada.
- Espelha a linha de baixo.
- Calcula as unidades duplicadas (`MineRaceAnomaly`).
- Cria a carga de escalabilidade (`CreateScalingWorkload`).

### `Game`

- Cria e encerra o árbitro na ordem correta.
- Registra a linha do tempo da thread principal.
- Trata os comandos `M`, `B`, `E` e `Enter`.
- Pausa a simulação enquanto a tela de escalabilidade está aberta e mede uma quantidade de threads por frame, para a janela continuar respondendo.

### `Renderer`

- Recebe tudo por um único `FrameView`, em vez de uma lista longa de parâmetros.
- Desenha o painel lateral, a mina e a tela de escalabilidade.

### `BenchmarkRunner`

- Os comandos `--benchmark` e `--benchmark-final` **desativam a mina**, para continuarem comparáveis com os resultados documentados antes dela.

### Novas configurações (`SimulationConfig`)

| Configuração | Padrão | Significado |
|---|---:|---|
| `ContestedMineEnabled` | `true` | Liga a mina |
| `MineSyncMode` | `Lock` | Modo inicial de sincronização |
| `MineTripInterval` | 5 | Agente vai à mina a cada N viagens |
| `MineCapacity` | 40.000 | Estoque máximo |
| `MineRegenerationPerSecond` | 5.000 | Regeneração |
| `MineExtractionsPerSecond` | 8 | Tentativas por agente |
| `MineCarryCapacity` | 6 | Carga máxima por viagem |
| `MineMaxWaitSeconds` | 2,5 | Tempo máximo na mina |
| `MineYieldComputationIterations` | 2.000 | Trabalho entre leitura e escrita |
| `MineBattlesEnabled` | `true` | Liga as batalhas |
| `MineBattleCasualtyRate` | 0,5 | Fração dos derrotados na mina que morre |
| `MineBattleMaxLossFraction` | 0,05 | Perda máxima por batalha |
| `MineBattleProtectionRatio` | 0,5 | Abaixo disso a civilização recua |
| `MineBattleMinimumPopulation` | 10 | População mínima |
| `ConflictRoundSeconds` | 2 | Duração da rodada |
| `ConflictBufferCapacity` | 64 | Vagas do buffer |
| `SidePanelWidth` | 400 | Largura do painel lateral |
| `TimelineHistorySeconds` | 1 | Janela da porcentagem ocupada |

---

## 9. Novos comandos de terminal

No macOS/Linux, use `./.dotnet/dotnet`; no Windows, `& .\.dotnet\dotnet.exe`. Com o SDK global instalado, basta `dotnet`.

### Condição de corrida sem janela

```bash
dotnet run -c Release -- --race-demo 3000 300
```

Formato: `--race-demo <população por civilização> <ciclos medidos>`. Roda os três modos com 1 e 4 workers e mostra unidades extraídas, duplicadas, contenção, espera no lock e ms por ciclo.

### Escalabilidade sem janela

```bash
dotnet run -c Release -- --scaling 120
```

Formato: `--scaling <agentes em cada uma das 64 civilizações>`. Salva `artifacts/scaling.csv`.

### Smoke test com quantidade de frames

```bash
dotnet run -c Release -- --smoke-test 3000 4 1500
```

O terceiro argumento (frames) é novo e opcional, padrão 120. Com cerca de 1.500 frames os agentes já chegaram à mina e as batalhas aparecem na captura.

---

## 10. Testes

`--self-test` passou de 10 para **19 testes**. Todos passam.

| Teste novo | O que garante |
|---|---|
| mina com lock preserva o estoque | Com 4 workers e `lock`, nenhuma unidade é duplicada |
| mina com Interlocked preserva o estoque | Com 4 workers e CAS, nenhuma unidade é duplicada |
| mina sem sincronização é correta com 1 worker | Sem execução simultânea não há condição de corrida |
| buffer limitado entrega todos os itens | 4 produtores e 1 consumidor, 20.000 itens, nada perdido nem corrompido, e produtores bloqueados de fato |
| árbitro entrega a mina a quem tem mais agentes | A decisão é publicada e a vitória contabilizada |
| batalhas causam baixas e saque sem eliminar | Há mortes e saque, a população bate com as mortes e ninguém fica abaixo do mínimo |
| batalhas desligadas não matam agentes | Nenhuma batalha e população intacta |
| linha do tempo registra cada civilização | Com 1 worker, A, B, C e D aparecem em ordem |
| coordenador divide carga entre N workers | Com 1, 7, 10 e 64 workers, todas as civilizações têm dono e a divisão fica equilibrada |

Testes antigos ajustados:

- **1 e 4 workers produzem o mesmo estado:** agora roda **sem a mina**. Com um recurso compartilhado, a ordem das extrações depende do escalonamento das threads, e o resultado deixa de ser idêntico. Isso é esperado e é um bom ponto para a apresentação.
- **agentes usam rotas distribuídas:** ignora os agentes em viagem à mina, que não ocupam rotas de recursos próprios.

---

## 11. Problemas encontrados e como foram resolvidos

### Janela gigante derrubava o app no macOS

Criar uma janela de 1600 × 900 numa tela menor fazia o GLFW encerrar o processo. A primeira correção abria a janela pequena e a redimensionava depois, mas em telas Retina a Raylib não atualiza a área de desenho ao redimensionar, e a imagem aparecia ampliada e cortada. **Solução final:** a janela nasce direto em 1280 × 720, sem redimensionamento, e a câmera virtual escala o conteúdo.

### A mesma civilização vencia sempre

A escolha de quem ia à mina era um sorteio pseudoaleatório que dependia do número da civilização, mas não da seed. O sorteio favorecia sempre as mesmas civilizações, que cresciam mais rápido no início, mandavam mais agentes e venciam. **Solução:** uma regra fixa (a cada 5 viagens), igual para todas.

### Perder era vantajoso

Numa versão intermediária, quem perdia extraía mais devagar. Isso fazia os perdedores ficarem mais tempo na mina, acumularem presença e vencerem a rodada seguinte. **Solução:** a penalidade de velocidade foi removida.

### Bola de neve da vencedora

Em outra versão, só os perdedores eram expulsos. A vencedora começava a rodada seguinte com agentes já na mina e ganhava de novo. **Solução:** depois de uma batalha, **todos** saem da mina.

### A mina não fazia diferença

Depois de cerca de 100 segundos, todas as civilizações acumulavam milhares de recursos sobrando, e o crescimento passava a ser limitado pelo intervalo de nascimento. A mina dava recursos que ninguém precisava. **Solução:** batalhas com mortes e saque, com as proteções da seção 4.

### A condição de corrida quase não aparecia

Com a leitura e a escrita coladas, 3.000 agentes por civilização geraram só 180 unidades duplicadas em 300 ciclos. **Solução:** mais trabalho no cálculo do rendimento, que é o mesmo nos três modos. O resultado subiu para 1.012 duplicadas no mesmo cenário.

---

## 12. Resultados medidos

Todos os números foram medidos num Apple M4 (10 núcleos lógicos), build `Release`. Eles mudam em outro computador.

### `--race-demo 7000 300` (28.000 agentes)

| Sincronização | Workers | Extraído | Duplicadas | Contenção | Espera no lock | ms/ciclo |
|---|---:|---:|---:|---:|---:|---:|
| Sem sincronização | 1 | 63.565 | 0 | 0 | 0,0 ms | 35,69 |
| Sem sincronização | 4 | 67.195 | **2.303** | 0 | 0,0 ms | 10,39 |
| lock | 1 | 63.565 | 0 | 0 | 0,0 ms | 35,83 |
| lock | 4 | 63.498 | 0 | 1.147 | 1,9 ms | 10,43 |
| Interlocked | 1 | 63.565 | 0 | 0 | 0,0 ms | 35,83 |
| Interlocked | 4 | 63.556 | 0 | 1.238 | 0,0 ms | 10,51 |

Sem sincronização, as 4 threads "extraíram" mais do que existia, porque as unidades duplicadas se somaram ao total.

### Batalhas (4 minutos simulados, crescimento natural)

- As vencedoras mudam conforme a seed; nenhuma civilização domina por causa do sorteio.
- As populações divergem de forma visível, por exemplo 3.315, 2.491, 2.429 e 2.392.
- Cerca de 950 mortos por civilização derrotada, sem eliminar nenhuma.

### `--scaling`

Veja a tabela na seção 6.

---

## 13. Cuidados e limitações

- **O cálculo entre leitura e escrita é proposital.** Sem ele a condição de corrida é rara demais para ser vista. Explique isso na apresentação.
- **Com a mina ativa, a simulação não é determinística entre 1 e 4 workers.** O teste de determinismo e os benchmarks antigos a desativam.
- **Batalhas ligadas reduzem o ganho de 4 workers.** Desligue com `B` antes de comparar desempenho.
- **O árbitro trabalha pouco.** O objetivo é mostrar o desacoplamento produtor-consumidor, não descarregar processamento pesado.
- **A renderização continua na thread principal.** Com população muito alta, ela vira o gargalo do FPS; o tempo de simulação (`Sim.`) continua sendo a métrica certa para avaliar os workers.
- **Resultados dependem do hardware.** Meça de novo no computador da apresentação, principalmente a escalabilidade.
- **População máxima inalterada:** 7.000 por civilização, 28.000 no total.

---

## 14. Roteiro sugerido para a apresentação

1. **Comparação de desempenho.** Pressione `B` para desligar as batalhas. Mostre 1 worker com Stress Test (`S`), depois troque para 4 workers e compare `Sim.` e FPS.
2. **Linha do tempo.** Com 1 worker, mostre as quatro cores em sequência; com 4, as cores em paralelo.
3. **Condição de corrida.** Com 4 workers e população alta, pressione `M` até `Sem sincronização` e mostre as unidades duplicadas subindo. Troque para 1 worker e mostre que o contador para.
4. **Soluções.** Volte a 4 workers e passe por `lock` (contenção e espera) e `Interlocked` (retentativas). As duplicadas ficam em zero.
5. **Produtor-consumidor.** Mostre o painel do árbitro: relatórios por segundo, fila do buffer e a linha quase vazia do árbitro na linha do tempo.
6. **Batalhas.** Pressione `B` para ligar. Mostre vitórias, mortos e saque, e o worker da maior civilização mais ocupado na linha do tempo.
7. **Escalabilidade.** Pressione `E` e mostre onde a curva se afasta do ideal e onde cai depois da linha dos núcleos.

---

## 15. Lista de arquivos

### Novos

| Arquivo | Conteúdo |
|---|---|
| `src/Concurrency/ContestedMine.cs` | Mina, região crítica, três modos de sincronização, caixa de saque |
| `src/Concurrency/MineSyncMode.cs` | Enum dos modos e nomes exibidos |
| `src/Concurrency/BoundedBuffer.cs` | Buffer limitado com mutex e dois semáforos |
| `src/Concurrency/ConflictArbiter.cs` | Thread árbitro e regras das batalhas |
| `src/Concurrency/MineReports.cs` | Relatório, decisão imutável e registro de batalha |
| `src/Concurrency/MineExtractionStats.cs` | Contadores da mina por civilização |
| `src/Metrics/ThreadTimeline.cs` | Buffer circular da linha do tempo |
| `src/Metrics/MineMetrics.cs` | Taxas por segundo da mina e do árbitro |
| `src/Diagnostics/ScalingBenchmark.cs` | Medição de escalabilidade (tela e `--scaling`) |
| `src/Diagnostics/RaceDemoRunner.cs` | Comando `--race-demo` |
| `src/Rendering/FrameView.cs` | Dados de um frame para o `Renderer` |
| `src/Simulation/WorkerContext.cs` | Número do worker e token de cancelamento |
| `ATUALIZACOES.md` | Este documento |

### Alterados

| Arquivo | Mudança principal |
|---|---|
| `src/Core/Game.cs` | Árbitro, linha do tempo, comandos novos, tela de escalabilidade, tamanho da janela |
| `src/Core/SimulationConfig.cs` | Configurações da mina, batalhas e painel |
| `src/Core/SimulationState.cs` | Mina, espelhamento, contagem de duplicadas, carga de escalabilidade |
| `src/Entities/Agent.cs` | Viagem à mina, carga, morte em batalha |
| `src/Entities/Civilization.cs` | Extração, relatório, batalhas, saque |
| `src/Simulation/SimulationCoordinator.cs` | N workers, linha do tempo, fase sequencial |
| `src/Rendering/Renderer.cs` | Painel lateral, mina, tela de escalabilidade |
| `src/Rendering/UiCommand.cs`, `UiLayout.cs`, `UserInterface.cs` | Botões e teclas novos |
| `src/Diagnostics/SelfTestRunner.cs` | 9 testes novos e 2 ajustados |
| `src/Diagnostics/BenchmarkRunner.cs` | Benchmarks antigos sem a mina |
| `src/Program.cs` | `--race-demo`, `--scaling`, frames no smoke test |
| `README.md`, `docs/commands.md`, `docs/plan.md` | Documentação das novidades |

### Ambiente

Para rodar no macOS, o SDK do .NET 8 foi instalado localmente na pasta `.dotnet/`, que já é ignorada pelo Git. Com um SDK global (`brew install --cask dotnet-sdk@8`), essa pasta pode ser apagada.
