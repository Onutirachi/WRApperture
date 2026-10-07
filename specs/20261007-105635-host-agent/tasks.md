---
description: "Task list for Host Agent"
---

# Tasks: Host Agent

**Input**: Design documents from `/specs/20261007-105635-host-agent/`

**Tests**: Obrigatórios (constituição II para o Core; contrato HTTP para o Agent).

## Phase 1: Setup

- [ ] T001 Criar `src/WRApperture.Shared` (classlib) e `src/WRApperture.Host.Agent` (web), adicionar à solução
- [ ] T002 [P] Criar `tests/WRApperture.Host.Agent.Tests` (xUnit + Mvc.Testing) e adicionar à solução
- [ ] T003 Pacotes do Agent: WindowsServices, Serilog.AspNetCore, Serilog.Sinks.EventLog

## Phase 2: Foundational

- [ ] T004 [P] `ApiContract` e DTOs em `src/WRApperture.Shared/`
- [ ] T005 [P] Testes de `AgentSettingsStore` (gerar/regenerar/verificar token, hash com salt, porta inválida, admin) em `tests/WRApperture.Host.Core.Tests/Agent/`
- [ ] T006 `AgentSettings`, `IAgentSettingsStore`, `AgentSettingsStore`, `TokenHasher` em `src/WRApperture.Host.Core/Agent/`
- [ ] T007 [P] Testes e implementação de `IHostInfoProvider` em `src/WRApperture.Host.Core/HostInfo/`
- [ ] T008 Registrar os novos serviços em `AddWRAppertureHostCore()`

## Phase 3: US4 - Token e configuração (P1)

- [ ] T009 [US4] `wrahost agent show|token|port` em `src/WRApperture.Host.Cli/AgentCommands.cs`

## Phase 4: US1 + US2 - Lista de apps e info do host (P1) 🎯 MVP

- [ ] T010 [P] [US1] Testes de API: 401 sem/errado/não pareado, 200 com token, ocultos filtrados, sem caminhos expostos, `/health` aberto, 429 após 10 falhas, regeneração invalida o token antigo em `tests/WRApperture.Host.Agent.Tests/ApiTests.cs`
- [ ] T011 [US1] `TokenAuthMiddleware` e `FailedAuthLimiter` em `src/WRApperture.Host.Agent/Security/`
- [ ] T012 [US1] Endpoints `/health`, `/api/host`, `/api/apps` em `src/WRApperture.Host.Agent/Endpoints/ApiEndpoints.cs`
- [ ] T013 [US2] `AgentCertificateManager` (criar/renovar/fingerprint) em `src/WRApperture.Host.Agent/Certificates/`
- [ ] T014 [US1] `Program.cs`: Serilog, WindowsService, Kestrel HTTPS na porta configurada, DI

## Phase 5: US3 - Ícones (P2)

- [ ] T015 [P] [US3] Testes do endpoint de ícone (200 PNG, 404, 400 size, 304 ETag)
- [ ] T016 [US3] `IconCache` e endpoint `/api/apps/{alias}/icon`

## Phase 6: Polish

- [ ] T017 `dotnet build` sem warnings e `dotnet test` verde
- [ ] T018 Smoke test: subir o agente localmente e consultar `/health` (não precisa de admin se a porta e o certificado forem tratados) e checar se a mensagem de erro sem admin é clara
- [ ] T019 Atualizar `README.md` (status e uso do agente)

## Dependencies

Setup → Foundational → US4 → US1/US2 → US3 → Polish.
