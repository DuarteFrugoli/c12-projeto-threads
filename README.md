# Simulação de Civilizações com Multithreading

Aplicação didática em C# e Raylib-cs que compara a atualização de quatro civilizações com 1, 2 e 4 workers persistentes.

## O que a demonstração mostra

- 1 worker de simulação + 1 thread principal/renderização = 2 threads controladas pelo projeto;
- 4 workers de simulação + 1 thread principal/renderização = 5 threads controladas pelo projeto;
- recursos com pontos fixos e capacidade inesgotável;
- cronômetro de tempo real, sem relógio fictício de jogo;
- FPS sem limite imposto pela aplicação;
- suspensão automática de novos agentes quando o FPS médio chega a 30;
- retomada após o FPS permanecer em pelo menos 35 durante 2 segundos;
- métricas de frame, renderização, simulação, p50, p95 e updates/s.

O plano completo está em [`docs/plan.md`](docs/plan.md).

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

## Calibração atual

Com 20.000 agentes e 768 pontos inesgotáveis por civilização, o smoke test gráfico no computador de desenvolvimento mediu aproximadamente:

- 1 worker: 25 FPS e 37,30 ms de simulação;
- 4 workers: 66 FPS e 11,66 ms de simulação.

Esses valores são dependentes do hardware e devem ser confirmados novamente se a apresentação ocorrer em outro computador.
