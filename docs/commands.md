# Comandos para a apresentação

Este arquivo é uma folha de consulta rápida. Os comandos abaixo devem ser executados no **PowerShell**, a partir da pasta raiz do projeto.

## 1. Abrir o terminal na raiz do projeto

Abra o projeto no VS Code e use **Terminal > Novo Terminal**. Os comandos deste arquivo devem ser executados na pasta que contém o arquivo `C12ProjetoCiv.sln`.

Para confirmar que o terminal está na pasta correta:

```powershell
Test-Path .\C12ProjetoCiv.sln
```

O resultado esperado é `True`.

---

## 2. Comando principal para rodar o projeto

Este é o comando mais importante para a apresentação:

```powershell
& .\.dotnet\dotnet.exe run -c Release
```

Ele compila, abre a janela da aplicação e executa a simulação em modo `Release`.

Se o projeto já estiver compilado e for necessário abri-lo mais rapidamente:

```powershell
& .\.dotnet\dotnet.exe run -c Release --no-build
```

Também é possível executar diretamente o programa já compilado:

```powershell
& .\.dotnet\dotnet.exe .\bin\Release\net8.0\C12ProjetoCiv.dll
```

---

## 3. Preparação antes da apresentação

Baixar as dependências do projeto:

```powershell
& .\.dotnet\dotnet.exe restore --configfile .\NuGet.Config
```

Compilar a solução em modo `Release`:

```powershell
& .\.dotnet\dotnet.exe build .\C12ProjetoCiv.sln -c Release --no-restore
```

Executar os testes internos:

```powershell
& .\.dotnet\dotnet.exe run -c Release --no-build -- --self-test
```

O resultado esperado é:

```text
10/10 testes passaram.
```

Sequência recomendada para preparar tudo:

```powershell
& .\.dotnet\dotnet.exe restore --configfile .\NuGet.Config
& .\.dotnet\dotnet.exe build .\C12ProjetoCiv.sln -c Release --no-restore
& .\.dotnet\dotnet.exe run -c Release --no-build -- --self-test
```

Depois disso, abrir a aplicação:

```powershell
& .\.dotnet\dotnet.exe run -c Release --no-build
```

---

## 4. Controles durante a apresentação

| Tecla | Ação |
|---|---|
| `1` | Usar 1 worker de simulação |
| `2` | Usar 2 workers de simulação |
| `4` | Usar 4 workers de simulação |
| `S` | Ativar ou desativar o Stress Test |
| `Espaço` | Pausar ou continuar |
| `R` | Reiniciar a simulação |
| `F11` | Alternar entre tela cheia e modo janela |
| `Esc` | Fechar a aplicação |

Os mesmos controles aparecem como botões na parte superior da janela.

### Contagem que deve ser explicada

- 1 worker de simulação + 1 thread principal de imagem = 2 threads controladas pelo projeto;
- 2 workers de simulação + 1 thread principal de imagem = 3 threads controladas pelo projeto;
- 4 workers de simulação + 1 thread principal de imagem = 5 threads controladas pelo projeto.

O .NET e o sistema operacional podem criar outras threads internas. A apresentação deve comparar os **workers de simulação controlados pelo projeto**.

---

## 5. Sequência sugerida da demonstração

1. Abrir o projeto:

   ```powershell
   & .\.dotnet\dotnet.exe run -c Release --no-build
   ```

2. Pressionar `1` para selecionar um worker.
3. Pressionar `S` para ativar o Stress Test.
4. Observar a população aumentar, o tempo da simulação subir e o FPS cair.
5. Aguardar a população desejada; se o FPS médio chegar a 10 antes disso, mostrar o bloqueio de proteção.
6. Pressionar `4`, mantendo a mesma população e o mesmo mundo.
7. Aguardar o aviso `Aquecendo métricas` desaparecer.
8. Comparar o `Simulation Time` e o FPS.
9. Se o crescimento tiver sido bloqueado, mostrar que ele volta quando o FPS permanece em pelo menos 15 por dois segundos.
10. Pressionar `Esc` para fechar.

---

## 6. Benchmark rápido

Executar uma comparação rápida com 1.000 agentes por civilização e 240 ciclos medidos:

```powershell
& .\.dotnet\dotnet.exe run -c Release --no-build -- --benchmark 1000 240
```

Formato:

```text
--benchmark <população por civilização> <quantidade de ciclos>
```

Exemplo mais leve:

```powershell
& .\.dotnet\dotnet.exe run -c Release --no-build -- --benchmark 500 120
```

O resultado também é salvo em:

```text
artifacts\benchmark-quick.csv
```

---

## 7. Benchmark final

Executar o benchmark usado para os resultados da apresentação:

```powershell
& .\.dotnet\dotnet.exe run -c Release --no-build -- --benchmark-final 1000 10 3
```

Formato:

```text
--benchmark-final <população por civilização> <segundos por repetição> <repetições>
```

O comando acima executa:

- 1.000 agentes por civilização;
- três segundos de aquecimento antes de cada medição;
- dez segundos de medição por repetição;
- três repetições para cada modo;
- modos de 1, 2 e 4 workers.

Ele demora aproximadamente dois minutos e salva o resultado em:

```text
artifacts\benchmark-presentation.csv
```

---

## 8. Smoke test gráfico

Validar a Raylib, o OpenGL e a renderização sem deixar a janela visível:

```powershell
& .\.dotnet\dotnet.exe run -c Release --no-build -- --smoke-test 10 1
```

Formato:

```text
--smoke-test <população por civilização> <workers>
```

Calibração com a população máxima e um worker:

```powershell
& .\.dotnet\dotnet.exe run -c Release --no-build -- --smoke-test 7000 1
```

Comparação com quatro workers:

```powershell
& .\.dotnet\dotnet.exe run -c Release --no-build -- --smoke-test 7000 4
```

As capturas são salvas na pasta:

```text
artifacts\
```

---

## 9. Usando um SDK global do .NET

Se o computador possuir o SDK do .NET 8 instalado globalmente, é possível retirar `& .\.dotnet\dotnet.exe` dos comandos.

Exemplo para executar:

```powershell
dotnet run -c Release
```

Compilar:

```powershell
dotnet build .\C12ProjetoCiv.sln -c Release
```

Testar:

```powershell
dotnet run -c Release --no-build -- --self-test
```

Para verificar quais SDKs estão instalados:

```powershell
dotnet --list-sdks
```

---

## 10. Git e atualização do projeto

Verificar o estado local:

```powershell
git status
```

Baixar a versão mais recente do GitHub antes da apresentação:

```powershell
git pull origin main
```

Ver o último commit:

```powershell
git log -1 --oneline
```

Repositório:

```text
https://github.com/DuarteFrugoli/c12-projeto-threads
```

---

## 11. Comando de emergência

Se a janela não abrir depois de alguma alteração, executar novamente a preparação completa:

```powershell
& .\.dotnet\dotnet.exe restore --configfile .\NuGet.Config
& .\.dotnet\dotnet.exe clean .\C12ProjetoCiv.sln -c Release
& .\.dotnet\dotnet.exe build .\C12ProjetoCiv.sln -c Release --no-restore
& .\.dotnet\dotnet.exe run -c Release --no-build -- --self-test
& .\.dotnet\dotnet.exe run -c Release --no-build
```

Durante a apresentação, o comando que deve ficar mais fácil de encontrar é:

```powershell
& .\.dotnet\dotnet.exe run -c Release --no-build
```
