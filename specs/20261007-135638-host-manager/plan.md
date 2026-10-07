# Implementation Plan: Host Manager

**Branch**: `20261007-135638-host-manager` | **Date**: 2026-10-07 | **Spec**: [spec.md](spec.md)

## Summary

Novo app WPF `WRApperture.Host.Manager` (`WRApperture.Host.exe`) sobre o Core existente.
- **Core**: `IAppSourceInspector` transforma um `.lnk` ou `.exe` em `AppDraft`, reaproveitando `ShortcutResolver` (inclusive MSI) e lendo o `FileVersionInfo` do executável. É testado com fakes.
- **UI compartilhada**: os estilos visuais saem do cliente para a biblioteca `WRApperture.Ui` (`Theme.xaml`). O cliente e o Manager usam o mesmo tema, e isso também prepara o item "tema escuro" do backlog.
- **Manager**:
  - lista de apps com arrastar e soltar;
  - editor (`AppEditorWindow`);
  - seletor de apps instalados (`DiscoveryWindow`);
  - painel "Este PC" com preparação, estado do agente via `ServiceController`, IPs, porta, fingerprint, token e reinício do agente.
- **Elevação**: o manifesto é `asInvoker`. Na abertura, o app se relança com `runas`. Se o UAC for recusado, ou com `--read-only`, ele abre em modo somente leitura.
- **Pacote**: `WRApperture.Host.exe` no zip do host. O instalador cria o atalho "WRApperture (Host)" em Common Programs, e o desinstalador o remove.

## Technical Context

- .NET 8 WPF + CommunityToolkit.Mvvm (mesma stack do cliente). O pacote novo é `System.ServiceProcess.ServiceController` 8 (estado e reinício do serviço).
- **Testes**: unitários do `AppSourceInspector` (atalho com argumentos, MSI, exe com e sem descrição, recusas) e Pester para o atalho do instalador. A UI é validada com captura de tela em modo `--read-only`.

## Constitution Check

| Princípio | Status | Evidência |
|---|---|---|
| I. Stack | ✅ | WPF/MVVM. Um pacote novo justificado (ServiceController) |
| II. Registro via Core | ✅ | Publicar, remover, preparar e gerar token só pelos serviços do Core. A UI não toca no registro |
| III. Idempotente | ✅ | Arrastar o mesmo programa atualiza o app em vez de duplicar (comportamento do `Publish`) |
| IV. Segurança | ✅ | Escritas exigem admin. Somente leitura sem elevação. Token exibido uma única vez. Aviso de pasta gravável |
| V. Observabilidade | ✅ | Serilog em `%ProgramData%\WRApperture\logs\manager-.log` |

## Project Structure

```text
src/WRApperture.Ui/                       NOVO: Theme.xaml (cores, botões, campos, SystemColors fixados)
src/WRApperture.Host.Core/Publishing/AppSourceInspector.cs   NOVO (+ IFileDescriptionReader)
src/WRApperture.Host.Manager/             NOVO (WRApperture.Host.exe)
  App.xaml(.cs)                           elevação, DI, Serilog
  ViewModels/ManagerViewModel.cs, AppItemViewModel.cs, AppEditorViewModel.cs
  Views/MainWindow, AppEditorWindow, DiscoveryWindow, TokenWindow
  Services/AgentService.cs                ServiceController + IPs
src/WRApperture.Client/App.xaml           passa a mesclar WRApperture.Ui/Theme.xaml
tests/WRApperture.Host.Core.Tests/Publishing/AppSourceInspectorTests.cs
packaging/host/*                          atalho Menu Iniciar + exe no pacote
```

## Validação

- Janela principal capturada em `--read-only`: dados reais do PC (preparação, IP, porta, estado do agente). Layout corrigido após a primeira captura (botões sobrepostos ao título, valores crus "0"/"1", texto cortado).
- `AppSourceInspector` verificado com arquivos reais via `wrahost inspect`: atalho MSI (CorelDRAW), argumentos (Git Bash, prompts do VS), ícone `.ico`, nome pela descrição do exe ("Mapa de caracteres"), recusas (app da Store ausente, `.ini`).
- Não verificado aqui: editor, seletor e token em modo administrador (UIPI impede automatizar janela elevada). Validação pelo usuário.
