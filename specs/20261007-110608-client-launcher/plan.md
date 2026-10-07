# Implementation Plan: Client Launcher

**Branch**: `20261007-110608-client-launcher` | **Date**: 2026-10-07 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/20261007-110608-client-launcher/spec.md`

## Summary

Um app WPF (`WRApperture.Client`) pareia com o agente do host usando token e fixação do fingerprint (TOFU). Ele mostra os apps em grade com ícones e cache offline. Ao clicar, gera um `.rdp` em modo RemoteApp e chama o `mstsc`. Também salva as credenciais RDP no Credential Manager e cria atalhos na área de trabalho. A lógica fica em `WRApperture.Client.Core`, coberta por testes. Os detalhes estão em [research.md](research.md).

## Technical Context

**Language/Version**: C# 12 / .NET 8 (`net8.0-windows10.0.17763.0`, `UseWPF`)

**Primary Dependencies**: `CommunityToolkit.Mvvm` 8, `System.Security.Cryptography.ProtectedData` 8, `Microsoft.Extensions.DependencyInjection` 8, `Serilog.Extensions.Logging` 8 e `Serilog.Sinks.File` 6. Projeto `WRApperture.Shared` (DTOs e fingerprint).

**Storage**: `%LOCALAPPDATA%\WRApperture\client\` (JSON + PNG/ICO + `.rdp`) e Windows Credential Manager.

**Testing**: xUnit em `tests/WRApperture.Client.Tests`. `HttpMessageHandler` fake para o `AgentClient`, certificado autoassinado gerado no teste para o pin validator e um teste de integração do Credential Manager com alvo de teste removido no fim.

**Target Platform**: Windows 10/11.

**Project Type**: Desktop app.

**Performance Goals**: A tela com o cache aparece em menos de 2 s (SC-004). A sincronização com o host roda em segundo plano.

**Constraints**: Nenhum segredo em texto puro em arquivo ou log. UI em PT-BR.

## Constitution Check

| Princípio | Status | Evidência |
|---|---|---|
| I. Stack | ✅ | WPF + MVVM (CommunityToolkit.Mvvm), .NET 8 |
| II. Registro via Core | ✅ N/A | O cliente não escreve no registro |
| III. Idempotente | ✅ | Regravar o `.rdp`, o atalho e as credenciais sobrescreve sem duplicar. Remover o host apaga só os arquivos dele |
| IV. Segurança | ✅ | DPAPI CurrentUser para o token, pinning SHA-256 com detecção de divergência no pareamento, senha só no Credential Manager |
| V. Observabilidade | ✅ | Serilog em arquivo em `%LOCALAPPDATA%\WRApperture\logs`. Sem banco de dados |

## Project Structure

```text
src/
├── WRApperture.Client.Core/          # NOVO (classlib)
│   ├── Model/                      # ClientState, PairedHost, CachedApp
│   ├── Agent/                      # AgentClient, CertificatePinValidator, exceções
│   ├── Storage/                    # HostStore, IconStore, ClientPaths
│   ├── Launch/                     # RdpFileBuilder, RemoteAppLauncher, IProcessStarter
│   ├── Credentials/                # ICredentialStore, WindowsCredentialStore
│   └── Shortcuts/                  # IcoWriter, DesktopShortcuts
└── WRApperture.Client/               # NOVO (WPF)
    ├── App.xaml(.cs)               # DI, Serilog
    ├── ViewModels/                 # MainViewModel, HostViewModel, AppTileViewModel, AddHostViewModel
    └── Views/                      # MainWindow, AddHostWindow, CredentialsWindow
tests/
└── WRApperture.Client.Tests/         # NOVO
```

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| Projeto extra `WRApperture.Client.Core` | Lógica testável sem WPF | Testar pelo projeto WPF acopla os testes à UI |
