---
description: "Task list for Packaging"
---

# Tasks: Packaging

**Input**: Design documents from `/specs/20261007-112159-packaging/`

**Tests**: xUnit para o `Reset` do Core. Pester para os scripts.

## Phase 1: Foundational (CLI/Core)

- [ ] T001 [P] Teste e implementação de `IAgentSettingsStore.Reset()` em `src/WRApperture.Host.Core/Agent/`
- [ ] T002 `wrahost agent show --json`, `agent token --json` e `agent reset` (remove também o certificado) em `src/WRApperture.Host.Cli/AgentCommands.cs`

## Phase 2: US4 - Build de release (P1)

- [ ] T003 `scripts/build-release.ps1` (testes → publish ×3 → montagem dos pacotes → zip). Adicionar `dist/` ao `.gitignore`

## Phase 3: US1 - Instalar host (P1) 🎯

- [ ] T004 [P] Testes Pester: dry-run lista os passos sem alterar nada, recusa sem admin (código 2)
- [ ] T005 `packaging/host/Install-WRAppertureHost.ps1` + `Instalar.cmd` + `LEIA-ME.txt`

## Phase 4: US2 - Desinstalar host (P2)

- [ ] T006 `packaging/host/Uninstall-WRAppertureHost.ps1` + teste Pester (dry-run, com e sem `-Purge`, e recusa sem admin)

## Phase 5: US3 - Cliente (P1)

- [ ] T007 [P] Testes Pester: instalação e desinstalação reais em diretórios temporários (arquivos, atalho, reinstalação preservando dados, `-RemoveUserData`)
- [ ] T008 `packaging/client/Install-WRAppertureClient.ps1`, `Uninstall-WRAppertureClient.ps1`, `Instalar.cmd`

## Phase 6: Polish

- [ ] T009 Rodar `build-release.ps1` de verdade. Validar os executáveis publicados (`wrahost --help`, agente sem admin com falha clara, cliente abre)
- [ ] T010 README (seção de instalação)

## Dependencies

T001 → T002 → T005/T006 (usam o CLI). T003 independente. T007 → T008.
