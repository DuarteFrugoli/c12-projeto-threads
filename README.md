# Simulação de Civilizações com Multithreading

Aplicação didática em C# e Raylib-cs que compara a atualização de quatro civilizações com 1, 2 e 4 workers persistentes.

## O que a demonstração mostra

- 1 worker de simulação + 1 thread principal/renderização = 2 threads controladas pelo projeto;
- 4 workers de simulação + 1 thread principal/renderização = 5 threads controladas pelo projeto;
- recursos renováveis em pontos fixos, com rotas distribuídas entre os agentes;
- no máximo 10 agentes atribuídos à mesma rota de recurso enquanto houver alternativas;
- base com 80 pontos de depósito, evitando que todos retornem ao mesmo pixel;
- nascimento natural de no máximo um agente por civilização a cada 0,05 segundo;
- cronômetro de tempo real, sem relógio fictício de jogo;
- FPS sem limite imposto pela aplicação;
- suspensão automática de novos agentes quando o FPS médio chega a 10;
- retomada após o FPS permanecer em pelo menos 15 durante 2 segundos;
- métricas de frame, renderização, simulação, p50, p95 e updates/s.
- métricas exibidas em colunas fixas e atualizadas a cada 0,5 segundo para facilitar a leitura;
- identificação do worker e do ID real da thread responsável por cada civilização;
- **linha do tempo das threads** (gráfico de Gantt) mostrando, em tempo real, qual civilização cada worker está processando, quando a thread principal renderiza ou espera e a porcentagem de tempo ocupado;
- **mina central disputada**: um estoque compartilhado pelos workers, com três modos de sincronização (sem sincronização, `lock` e `Interlocked`) e contagem das unidades duplicadas pela condição de corrida;
- **thread árbitro** no modelo produtor-consumidor: os workers enviam relatórios por um buffer limitado com semáforos, e o árbitro decide as disputas pela mina sem bloquear a simulação;
- **escalabilidade**: gráfico de speedup de 1 até 64 threads sobre a mesma carga, marcando o número de núcleos lógicos, para mostrar onde threads extras deixam de ajudar.

O plano completo está em [`docs/plan.md`](docs/plan.md). O resumo de todas as mudanças recentes está em [`ATUALIZACOES.md`](ATUALIZACOES.md).

## Requisitos

- SDK do .NET 8;
- Windows x64, Linux x64 ou macOS compatível com o pacote Raylib-cs 8.1.0.

Durante o desenvolvimento, um SDK local pode existir em `.dotnet`. Ele não é versionado.

## Executar

Com um SDK global:

```powershell
dotnet restore
dotnet run -c Release
```

Com o SDK local deste diretório:

```powershell
& .\.dotnet\dotnet.exe restore
& .\.dotnet\dotnet.exe run -c Release
```

## Controles

| Ação | Teclado | Interface |
|---|---|---|
| Usar 1 worker | `1` | `1 worker` |
| Usar 2 workers | `2` | `2 workers` |
| Usar 4 workers | `4` | `4 workers` |
| Pausar/continuar | `Espaço` | `Pausar` |
| Reiniciar | `R` | `Reiniciar` |
| Ativar/desativar Stress Test | `S` | `Stress Test` |
| Alternar tela cheia | `F11` | `Tela cheia` / `Modo janela` |
| Trocar a sincronização da mina | `M` | `Mina: ...` |
| Ligar/desligar as batalhas pela mina | `B` | `Batalhas: ON/OFF` |
| Abrir/fechar a escalabilidade | `E` | `Escalabilidade` |
| Medir a escalabilidade de novo | `Enter` | — |

Com 4 threads de simulação a janela mostra 6 threads controladas pelo projeto: 1 principal, 4 workers e 1 árbitro.

O Stress Test adiciona agentes gradualmente. Ele respeita tanto o limite populacional absoluto quanto o bloqueio de crescimento por FPS baixo.
Por ser uma ferramenta de demonstração, seus agentes não consomem o estoque de recursos; o crescimento natural continua dependendo das entregas.

## Verificações sem abrir a janela

Executar os testes internos:

```powershell
& .\.dotnet\dotnet.exe run -c Release -- --self-test
```

Validar a inicialização nativa da Raylib e renderizar 120 frames em uma janela oculta. Os argumentos opcionais são população por civilização e workers:

```powershell
& .\.dotnet\dotnet.exe run -c Release -- --smoke-test 10 1
```

Executar um benchmark headless. Os argumentos opcionais são população por civilização e quantidade de ciclos medidos:

```powershell
& .\.dotnet\dotnet.exe run -c Release -- --benchmark 1000 120
```

O benchmark desativa o crescimento, recria o mesmo cenário para cada modo e informa mediana, p95 e speedup. Ele também salva `artifacts/benchmark-quick.csv`.

Para a coleta final da apresentação, use população por civilização, segundos por repetição e número de repetições. Os valores padrão são `1000`, `10` e `3`:

```powershell
& .\.dotnet\dotnet.exe run -c Release -- --benchmark-final 1000 10 3
```

Esse modo aquece cada execução por 3 segundos, registra informações do computador e salva `artifacts/benchmark-presentation.csv`.

Demonstrar a condição de corrida da mina em cada modo de sincronização, com 1 e 4 workers. Os argumentos opcionais são população por civilização e ciclos medidos:

```powershell
& .\.dotnet\dotnet.exe run -c Release -- --race-demo 3000 300
```

Medir a escalabilidade de 1 a 64 threads sobre uma carga fixa de 64 civilizações pequenas. O argumento opcional é a população de cada uma. O resultado é salvo em `artifacts/scaling.csv`:

```powershell
& .\.dotnet\dotnet.exe run -c Release -- --scaling 120
```

## Mina central, árbitro e linha do tempo

A mina fica no cruzamento dos quatro territórios. Cada agente vai até ela a cada 5 viagens, escalonado pelo identificador, portanto 20% das viagens de todas as civilizações. Cada extração lê o estoque, calcula o rendimento e grava o novo valor, e esse trecho é a região crítica:

| Modo | O que acontece com 4 workers |
|---|---|
| Sem sincronização | Duas threads leem o mesmo estoque e uma grava por cima da outra. O painel mostra as unidades duplicadas. Com 1 worker o resultado é sempre zero. |
| `lock` (Monitor) | O estoque fica correto. O painel mostra quantas vezes uma thread encontrou o lock ocupado e quanto tempo esperou. |
| `Interlocked` (CAS) | O estoque fica correto sem bloquear. Quando outra thread grava primeiro, o cálculo é refeito, e o painel conta essas retentativas. |

O cálculo do rendimento existe de propósito entre a leitura e a escrita, como em código real que transforma um valor antes de gravá-lo. O custo é o mesmo nos três modos.

Ao terminar cada civilização, o worker envia um relatório ao árbitro por um `BoundedBuffer` (fila protegida por mutex, com um semáforo de vagas e outro de itens). A cada 2 segundos de tempo real, o árbitro declara vencedora a civilização com mais agentes na mina e publica a decisão como uma referência imutável. Os workers leem essa referência sem bloquear e aplicam o resultado:

- todos saem da mina, e a vencedora leva a carga;
- as derrotadas entregam a carga como saque, por uma caixa de mensagens atômica (`Interlocked.Add`/`Interlocked.Exchange`), porque a vencedora pode estar sendo atualizada por outro worker no mesmo instante;
- metade dos agentes derrotados que estavam na mina morre, limitado a 5% da população por batalha e a um mínimo de 10 agentes;
- uma civilização com menos da metade da população da vencedora recua sem baixas, para que a maior não elimine as outras.

Com as batalhas ligadas as populações divergem, e a linha do tempo mostra workers com cargas diferentes: com 4 workers o ciclo espera o worker mais lento, e o ganho máximo passa a ser `população total ÷ maior população`. Para comparar 1 e 4 workers com cargas iguais, desligue as batalhas (`B`); a mina continua compartilhada, e a demonstração da condição de corrida segue funcionando.

Como a mina é compartilhada, a simulação com ela deixa de ser determinística entre 1 e 4 workers. O teste de determinismo e os benchmarks `--benchmark` e `--benchmark-final` desativam a mina, para medir apenas a parte sem estado compartilhado e continuar comparáveis com os resultados anteriores.

## Calibração atual

Como referência anterior, com 20.000 agentes e 768 pontos renováveis por civilização, o smoke test gráfico no computador de desenvolvimento mediu aproximadamente:

- 1 worker: 13 FPS e 73,54 ms de simulação;
- 4 workers: 36 FPS e 27,55 ms de simulação.

Esses valores são dependentes do hardware e devem ser confirmados novamente se a apresentação ocorrer em outro computador.
O limite atual é de 28.000 agentes; a calibração nesse novo máximo deve ser executada sem outra instância da simulação disputando CPU.
