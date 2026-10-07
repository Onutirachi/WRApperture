---
description: "Task list for Client Launcher"
---

# Tasks: Client Launcher

**Input**: Design documents from `/specs/20261007-110608-client-launcher/`

**Tests**: Na camada Client.Core (lógica). As views são validadas pelo quickstart.

## Phase 1: Setup

- [ ] T001 Criar `src/WRApperture.Client.Core`, `src/WRApperture.Client` (WPF) e `tests/WRApperture.Client.Tests`, adicionar à solução e aos pacotes

## Phase 2: Foundational

- [ ] T002 [P] Model (`ClientState`, `PairedHost`, `CachedApp`) e `ClientPaths`
- [ ] T003 [P] Testes do `HostStore` (round-trip, token não aparece no arquivo, gravação atômica, arquivo corrompido)
- [ ] T004 `HostStore` com DPAPI

## Phase 3: US1 - Pareamento (P1)

- [ ] T005 [P] [US1] Testes de `CertificatePinValidator` (pareamento registra, pin igual aceita, pin diferente rejeita) e de `AgentClient` (401/429/offline/sucesso, header de token)
- [ ] T006 [US1] `CertificatePinValidator`, exceções e `AgentClient` (`PairAsync` com checagem de divergência)

## Phase 4: US3 - Abrir app (P1) 🎯

- [ ] T007 [P] [US3] Testes do `RdpFileBuilder` (chaves obrigatórias, porta não padrão, usuário, caracteres especiais, mesmo endereço para todos os apps) e do `RemoteAppLauncher` (arquivo UTF-16, chama mstsc com o caminho)
- [ ] T008 [US3] `RdpFileBuilder`, `RemoteAppLauncher`, `IProcessStarter`

## Phase 5: US2 - Grade de apps (P1)

- [ ] T009 [US2] `IconStore` (cache PNG + ETag) com testes
- [ ] T010 [US2] WPF: `App.xaml` (DI, Serilog, tema), `MainWindow` (seletor de host, busca, atualizar, grade, aviso offline), `AddHostWindow` (pareamento em duas etapas com fingerprint)
- [ ] T011 [US2] ViewModels: carregar o cache na abertura, sincronizar em segundo plano, tratar `InvalidToken`/`CertificateChanged`/offline

## Phase 6: US4 - Credenciais (P2)

- [ ] T012 [US4] `WindowsCredentialStore` (CredWrite/CredDelete) + teste de integração + `CredentialsWindow`

## Phase 7: US5 - Atalho (P3)

- [ ] T013 [US5] `IcoWriter` (teste) + `DesktopShortcuts` + item de menu de contexto

## Phase 8: Polish

- [ ] T014 Build sem warnings, todos os testes verdes
- [ ] T015 Smoke test: o cliente inicia sem hosts (janela abre, sem crash) e o log é criado
- [ ] T016 README

## Dependencies

Setup → Foundational → US1 → US3 → US2 (UI usa US1+US3) → US4 → US5 → Polish.
