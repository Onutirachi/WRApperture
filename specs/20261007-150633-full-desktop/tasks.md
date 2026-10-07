---
description: "Task list for full desktop"
---

# Tasks: Área de trabalho completa do host

## Phase 1: Contrato e regra (host)

- [X] T001 [P] Testes de `SessionPolicy` no modo `desktop` (RemoteApp do usuário → logoff; desktop do usuário → nada; outros → desconectar; usuário desconhecido)
- [X] T002 `SessionModes`, `AgentFeatures`, `HostInfoDto.Features`, `SessionReleaseRequestDto.Mode`; `SessionPolicy.Plan(..., mode)`
- [X] T003 Agente: modo nos endpoints (400 para modo inválido), `features` em `/api/host`; testes de integração; contrato `http-api.md`

## Phase 2: Cliente Core

- [X] T004 [P] Testes: `.rdp` de área de trabalho, arquivo `@desktop.rdp`, sync guarda `AgentFeatures`, `AgentClient` envia o modo
- [X] T005 `RdpFileBuilder.BuildDesktop`, `RemoteAppLauncher.LaunchDesktop`, `PairedHost.AgentFeatures`, `HostSync`, `AgentClient`

## Phase 3: Cliente UI

- [X] T006 Bloco "Área de trabalho" (primeiro, ícone de monitor, sem renomear), abrir e criar atalho; `EnsureHostFreeAsync` com modo e fallback para agente antigo

## Phase 4: Polish

- [X] T007 Versão 0.6.0, README, BACKLOG, build de release
