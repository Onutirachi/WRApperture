---
description: "Task list for session handoff"
---

# Tasks: Alternância entre uso local do host e apps remotos

## Phase 1: Core (host)

- [X] T001 Models `HostSession`/`SessionKind`/`SessionConnectState` + `ISessionProvider` em `src/WRApperture.Host.Core/Sessions/`
- [X] T002 [P] Testes de `SessionPolicy` (mesmo usuário, conta Microsoft, usuário desconhecido = usuário da tela, outro usuário nunca, RemoteApp nunca) e de `RdpUserMatcher`
- [X] T003 `SessionPolicy`, `RdpUserMatcher`
- [X] T004 `WindowsSessionProvider` (WTS + processos + `CreateProcessAsUser`) + DI

## Phase 2: Agent

- [X] T005 DTOs em `WRApperture.Shared` + rotas
- [X] T006 [P] Testes dos endpoints `/api/session` e `/api/session/release` com fake provider (auth, busy, release, nunca outro usuário)
- [X] T007 Endpoints + `SessionWatcher` (com teste da decisão de iniciar o shell uma única vez)

## Phase 3: Client

- [X] T008 `AgentClient`: `GetSessionStatusAsync`, `ReleaseSessionAsync` (timeout 45 s, 404 → não suportado) + testes
- [X] T009 `PairedHost.AutoReleaseSession`; `MainViewModel`: verificar antes de abrir, diálogo, release, item de menu; `ReleaseSessionWindow`

## Phase 4: Polish

- [X] T010 Build, testes, release 0.3.0, README
