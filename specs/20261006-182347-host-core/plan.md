# Implementation Plan: Host Core — publicação de RemoteApps

**Branch**: `20261006-182347-host-core` | **Date**: 2026-10-06 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/20261006-182347-host-core/spec.md`

## Summary

Biblioteca `WRApperture.Host.Core` que:
1. faz o CRUD idempotente dos RemoteApps em `TSAppAllowList`;
2. diagnostica, prepara e reverte o host (RDP, NLA, allow-list, firewall), com snapshot do estado anterior;
3. descobre apps pelos atalhos do Menu Iniciar;
4. extrai ícones em PNG.

Todo acesso ao sistema passa por seams (`IRegistryAccess`, `IFirewallRules`, `IPrivilegeChecker`, `IFileSystemProbe`) para permitir testes com fakes. Um CLI fino `wrahost` expõe as operações para validação de ponta a ponta e automação. Os detalhes técnicos estão em [research.md](research.md).

## Technical Context

**Language/Version**: C# 12 / .NET 8 (`net8.0-windows10.0.17763.0`)

**Primary Dependencies**:
- `Microsoft.Extensions.DependencyInjection.Abstractions` e `Microsoft.Extensions.Logging.Abstractions` (Core)
- `System.Drawing.Common` (ícones)
- `System.CommandLine`, `Serilog`, `Serilog.Sinks.File` e `Serilog.Extensions.Logging` (CLI)
- Interop nativo próprio: `IShellLinkW`, `msi.dll`, `user32!PrivateExtractIconsW`, `shell32!SHGetStockIconInfo`
- COM `HNetCfg.FwPolicy2`

**Storage**: Registro do Windows (HKLM): `TSAppAllowList` e `SOFTWARE\WRApperture` para o snapshot. Não há banco de dados.

**Testing**: xUnit (Assert nativo; FluentAssertions v8 tem licença comercial), com fakes em memória para os seams. Testes de integração reais são marcados com `[Trait("Category","Integration")]` e ficam fora do CI padrão.

**Target Platform**: Windows 10/11 Pro/Enterprise/Education x64.

**Project Type**: Biblioteca + CLI.

**Performance Goals**: Descoberta em menos de 5 s com cerca de 300 atalhos (SC-004). CRUD em menos de 100 ms.

**Constraints**:
- Nenhuma escrita sem administrador.
- Nunca apagar `TSAppAllowList` ou `Applications` inteiras.
- A UI e as mensagens ao usuário são em PT-BR. Os logs são em inglês.

**Scale/Scope**: Dezenas de apps publicados por host.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Princípio | Status | Evidência |
|---|---|---|
| I. Stack única .NET 8 | ✅ | `net8.0-windows10.0.17763.0`. Dependências listadas acima e justificadas no research (R4, R7, R8) |
| II. Registro só via Core (NON-NEGOTIABLE) | ✅ | Todo acesso passa por `IRegistryAccess`. O CLI só chama o Core. Os testes usam `FakeRegistryAccess` |
| III. Idempotente e reversível | ✅ | `Publish` devolve `Unchanged`. `Prepare` segue o padrão check-then-set. `Revert` usa o snapshot. `DeleteKey` é limitado à subchave do app |
| IV. Segurança por padrão | ✅ | `fDisabledAllowList` forçado para 0, NLA ligada, aviso de pasta gravável, admin obrigatório. Tokens e HTTPS não se aplicam aqui (são da feature host-agent) |
| V. Observabilidade/simplicidade | ✅ | `ILogger` no Core e Serilog no CLI, gravando em `%ProgramData%\WRApperture\logs`. Sem banco de dados |

**Re-check pós-design**: ✅ Sem violações. O CLI é um projeto extra, justificado em Complexity Tracking.

## Project Structure

### Documentation (this feature)

```text
specs/20261006-182347-host-core/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/core-api.md
└── tasks.md             # /speckit-tasks
```

### Source Code (repository root)

```text
WRApperture.sln
Directory.Build.props              # net8.0-windows, Nullable, TreatWarningsAsErrors, LangVersion
src/
├── WRApperture.Host.Core/
│   ├── Abstractions/              # IRegistryAccess, IFirewallRules, IPrivilegeChecker, IFileSystemProbe
│   ├── Models/                    # PublishedApp, PublishRequest, PublishResult, DiscoveredApp, ReadinessCheck, enums
│   ├── Errors/                    # AdminRequiredException, AppValidationException, HostNotSupportedException
│   ├── Publishing/                # AppPublisher, AliasGenerator, AppValidator, RegistryPaths
│   ├── Readiness/                 # HostPreparation, SnapshotStore, checks individuais
│   ├── Discovery/                 # AppDiscovery, ShortcutResolver (IShellLinkW + MSI)
│   ├── Icons/                     # IconProvider
│   ├── Platform/                  # WindowsRegistryAccess, ComFirewallRules, WindowsPrivilegeChecker, FileSystemProbe, Native/*
│   └── ServiceCollectionExtensions.cs
└── WRApperture.Host.Cli/            # wrahost (System.CommandLine + Serilog)
tests/
└── WRApperture.Host.Core.Tests/
    ├── Fakes/                     # FakeRegistryAccess, FakeFirewallRules, FakePrivilegeChecker, FakeFileSystemProbe
    ├── Publishing/
    ├── Readiness/
    └── Discovery/
```

**Structure Decision**: Solução única `WRApperture.sln` na raiz. Esta feature cria a solução, `Directory.Build.props`, o Core, o CLI e os testes. As features seguintes (Agent, Manager, Client, Shared) adicionam projetos em `src/` e `tests/`.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| Projeto extra `WRApperture.Host.Cli` | Validação de ponta a ponta (quickstart) antes do Manager e automação por script | Só testes unitários não provam que o `mstsc` abre o app |
