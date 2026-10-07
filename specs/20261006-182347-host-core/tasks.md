---
description: "Task list for Host Core"
---

# Tasks: Host Core — publicação de RemoteApps

**Input**: Design documents from `/specs/20261006-182347-host-core/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/core-api.md

**Tests**: Obrigatórios para o Core (constituição II). São escritos antes da implementação de cada story.

## Format: `[ID] [P?] [Story] Description`

## Phase 1: Setup

- [ ] T001 Criar `WRApperture.sln`, `Directory.Build.props` (net8.0-windows, Nullable, TreatWarningsAsErrors, ImplicitUsings) e `.editorconfig` na raiz
- [ ] T002 Criar `src/WRApperture.Host.Core/WRApperture.Host.Core.csproj` (classlib; DI.Abstractions, Logging.Abstractions, System.Drawing.Common)
- [ ] T003 [P] Criar `tests/WRApperture.Host.Core.Tests/WRApperture.Host.Core.Tests.csproj` (xUnit, FluentAssertions, ref Core, `InternalsVisibleTo`)
- [ ] T004 [P] Criar `src/WRApperture.Host.Cli/WRApperture.Host.Cli.csproj` (exe `wrahost`; System.CommandLine, Serilog, Serilog.Sinks.File, Serilog.Extensions.Logging; manifest `asInvoker`)

---

## Phase 2: Foundational

- [ ] T005 [P] Seams em `src/WRApperture.Host.Core/Abstractions/` (`IRegistryAccess`, `IFirewallRules`, `IPrivilegeChecker`, `IFileSystemProbe`)
- [ ] T006 [P] Models e enums em `src/WRApperture.Host.Core/Models/` (conforme data-model.md)
- [ ] T007 [P] Exceções em `src/WRApperture.Host.Core/Errors/`
- [ ] T008 [P] Interfaces públicas `IAppPublisher`, `IHostPreparation`, `IAppDiscovery`, `IIconProvider` em `src/WRApperture.Host.Core/`
- [ ] T009 Fakes em memória em `tests/WRApperture.Host.Core.Tests/Fakes/`
- [ ] T010 Implementações reais em `src/WRApperture.Host.Core/Platform/` (`WindowsRegistryAccess` HKLM 64-bit view, `WindowsPrivilegeChecker`, `FileSystemProbe` com checagem de ACL (R6), `ComFirewallRules` (R2))

**Checkpoint**: base pronta.

---

## Phase 3: User Story 1 - Publicar e despublicar (P1) 🎯 MVP

**Independent Test**: `wrahost publish` + `.rdp` do quickstart abre só o Notepad.

- [ ] T011 [P] [US1] Testes de `AliasGenerator` (diacríticos, inválidos, colisão) em `tests/.../Publishing/AliasGeneratorTests.cs`
- [ ] T012 [P] [US1] Testes de `AppPublisher` (create/update/unchanged, validação, admin, preservar não gerenciados, unpublish isolado) em `tests/.../Publishing/AppPublisherTests.cs`
- [ ] T013 [US1] `RegistryPaths`, `AliasGenerator`, `AppValidator` em `src/WRApperture.Host.Core/Publishing/`
- [ ] T014 [US1] `AppPublisher` em `src/WRApperture.Host.Core/Publishing/AppPublisher.cs` (com logs FR-014)
- [ ] T015 [US1] CLI: `list`, `publish`, `unpublish` + bootstrap (DI, Serilog, códigos de saída) em `src/WRApperture.Host.Cli/`

**Checkpoint**: MVP — publicar e abrir app remoto.

---

## Phase 4: User Story 2 - Preparar o host (P1)

**Independent Test**: `wrahost check` → `prepare` → `check` (tudo OK) → `revert`.

- [ ] T016 [P] [US2] Testes de `HostPreparation` (check, prepare idempotente, snapshot não sobrescreve, revert parcial, Home não suportado) em `tests/.../Readiness/HostPreparationTests.cs`
- [ ] T017 [US2] `SnapshotStore` em `src/WRApperture.Host.Core/Readiness/SnapshotStore.cs`
- [ ] T018 [US2] `HostPreparation` em `src/WRApperture.Host.Core/Readiness/HostPreparation.cs`
- [ ] T019 [US2] CLI: `check`, `prepare`, `revert`

---

## Phase 5: User Story 3 - Descobrir apps (P2)

**Independent Test**: `wrahost discover` lista apps do Menu Iniciar sem duplicatas.

- [ ] T020 [P] [US3] Testes de filtro/dedupe/marcação de publicado em `tests/.../Discovery/AppDiscoveryTests.cs` (resolver de atalho injetado como fake)
- [ ] T021 [US3] `ShortcutResolver` (IShellLinkW + MSI) em `src/WRApperture.Host.Core/Discovery/` e `Platform/Native/`
- [ ] T022 [US3] `AppDiscovery` em `src/WRApperture.Host.Core/Discovery/AppDiscovery.cs`
- [ ] T023 [US3] CLI: `discover`

---

## Phase 6: User Story 4 - Ícones (P2)

- [ ] T024 [US4] `IconProvider` (PrivateExtractIconsW + fallback stock icon) em `src/WRApperture.Host.Core/Icons/IconProvider.cs`
- [ ] T025 [P] [US4] Teste de integração (`Category=Integration`): PNG válido de notepad e fallback para arquivo sem ícone em `tests/.../Icons/IconProviderTests.cs`
- [ ] T026 [US4] CLI: `icon`

---

## Phase 7: Polish

- [ ] T027 `ServiceCollectionExtensions.AddWRAppertureHostCore()` e uso no CLI
- [ ] T028 `dotnet build` sem warnings e `dotnet test` verde
- [ ] T029 Smoke test não destrutivo: `wrahost list`, `check`, `discover`, `icon` e `publish` sem elevação (deve sair com código 2)
- [ ] T030 `README.md` na raiz com visão geral e link para o quickstart

## Dependencies & Execution Order

- Setup → Foundational → US1 → (US2, US3, US4 em qualquer ordem) → Polish
- US3 marca `PublishedAlias` usando `IAppPublisher` (US1)
- US4 independente de US1–US3 (só usa caminhos)

## Parallel Example

- T011 e T012 juntos. T005 a T008 juntos.

## Implementation Strategy

MVP = Phases 1–3. Depois US2 (necessária no Windows Pro "de fábrica"), US3 e US4.
