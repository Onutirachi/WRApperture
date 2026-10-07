# Research: Packaging

## R1. Executáveis autônomos
- **Decision**: `dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none`, aplicado a `wrahost`, `WRApperture.Agent` e `WRApperture`.
- **Rationale**: Atende SC-004 (dispensa .NET instalado). `IncludeNativeLibrariesForSelfExtract` é necessário para o WPF (`wpfgfx`, `PresentationNative`). A compressão reduz o tamanho, mas a primeira execução fica um pouco mais lenta.
- **Alternatives**: Framework-dependent (menor, mas exige o runtime) e Native AOT (sem suporte a WPF; o COM `dynamic` do Core também não é compatível). Ambos rejeitados.

## R2. Formato do instalador
- **Decision**: Scripts PowerShell 5.1 (`Install-WRAppertureHost.ps1` etc.) com wrappers `.cmd` para duplo clique. O wrapper do host pede elevação via `Start-Process -Verb RunAs`.
- **Rationale**: Zero dependências. Cada passo fica explícito e auditável, e o modo `-DryRun` é trivial.
- **Alternatives**: WiX/MSI (curva alta, por enquanto adiado) e MSIX (não suporta serviços com LocalSystem de forma simples). Ambos rejeitados.

## R3. Serviço
- **Decision**:
  - O serviço é criado com `New-Service` (`WRAppertureAgent`, LocalSystem, `Automatic`). O caminho do binário fica entre aspas, para evitar a vulnerabilidade de *unquoted service path*.
  - Recuperação via `sc.exe failure WRAppertureAgent reset= 86400 actions= restart/5000/restart/10000/restart/60000`.
  - Na reinstalação: para o serviço, copia os binários, reaplica `sc.exe config binPath=` e inicia de novo.
- **Rationale**: O `UseWindowsService` já integra o ciclo de vida com o SCM. LocalSystem é necessário para ler o HKLM e usar a chave privada em `LocalMachine\My`.

## R4. Firewall do agente
- **Decision**: `New-NetFirewallRule -Name WRAppertureAgent-TCP -Direction Inbound -Protocol TCP -LocalPort <porta> -Action Allow -Profile Domain,Private`. A regra é recriada a cada instalação, para acompanhar mudanças de porta. Com `-AllowPublicNetwork`, o perfil `Public` também é incluído. O instalador avisa se a rede atual for `Public`.
- **Rationale**: Mesmo com token e TLS, não expor o agente em redes públicas por padrão (constituição IV). Esta regra vale só para o agente; o RDP continua sendo tratado pelo `wrahost prepare`.

## R5. Registro só via `wrahost` (constituição II)
- **Decision**: Os scripts usam `wrahost` para tudo que toca o registro:
  - `prepare` e `revert`;
  - `agent show --json` (porta, pareamento, fingerprint);
  - `agent token --json` (só se ainda não pareado);
  - novo `agent reset`, que apaga as configurações do agente (Core) e o certificado (store).
- O cliente não grava no registro. O cadastro em "Aplicativos instalados" fica de fora, e a desinstalação é pelo script ou pelo atalho.

## R6. Verificação pós-instalação
- **Decision**: `curl.exe -k -s -o NUL -w "%{http_code}" https://localhost:<porta>/health` (o `curl.exe` vem no Windows 10 1803+), com até 30 s de tentativas. Em caso de falha, o instalador mostra as últimas linhas de `%ProgramData%\WRApperture\logs\agent-*.log`.

## R7. Testes dos scripts
- **Decision**: Pester 3.4, que já vem no Windows PowerShell 5.1, em `tests/packaging/`. Cobertura:
  - `-DryRun` lista os passos esperados e não altera nada;
  - sem admin, o instalador do host recusa com código 2;
  - instalação e desinstalação **reais** do cliente em diretórios temporários (parâmetros `-InstallDir` e `-StartMenuDir`), sem admin.

## R9. Desinstalação sem PowerShell (feedback de uso)
- **Problema**: o usuário não conseguia rodar os `.ps1`. O duplo clique abre o Bloco de Notas, e a política de execução bloqueia scripts, inclusive os baixados (Mark of the Web).
- **Decision**: `Desinstalar.cmd` em cada pacote e na pasta instalada, e no cliente também um atalho "Desinstalar WRApperture" no Menu Iniciar. Eles chamam o PowerShell com `-ExecutionPolicy Bypass`, válido só para aquela chamada. Com `-Interactive`, os scripts perguntam o que manter e esperam Enter para a mensagem final ficar visível.
- **Autoexclusão**: o `.cmd` muda para `%TEMP%` e usa `(goto) 2>nul & powershell ...`. Isso desliga o `cmd` do próprio arquivo, então a pasta e o próprio `.cmd` podem ser apagados sem o erro "O sistema não pode encontrar o caminho especificado" (verificado).

## R8. Descobertos na implementação
- **Encoding**: o Windows PowerShell 5.1 lê `.ps1` sem BOM como ANSI, o que embaralha os acentos. Os scripts ficam em UTF-8 com BOM no repositório (regra no `.editorconfig`), e o `build-release.ps1` regrava os scripts do pacote com BOM.
- **`$PSScriptRoot`** não está disponível nos valores padrão de `param()` no PowerShell 5.1. Os defaults que dependem dele são calculados no corpo do script.
- **Tamanho**: cada executável self-contained leva o próprio runtime (wrahost 40 MB, agente 51 MB, cliente 74 MB). Trimming não é usado porque quebra o COM `dynamic` (firewall, `WScript.Shell`) e o WPF.
