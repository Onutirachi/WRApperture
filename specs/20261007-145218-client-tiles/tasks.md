---
description: "Task list for client tiles"
---

# Tasks: Grade de apps do cliente — duplo clique e nomes locais

## Phase 1: Core

- [X] T001 [P] Testes: `CachedApp.Name`/`WithCustomName` (vazio, espaços, igual ao do host), sync preserva e descarta nomes locais, `.rdp` usa o nome local, `hosts.json` antigo sem o campo carrega
- [X] T002 `CachedApp.CustomName`, `Name`, `WithCustomName`; `HostSync` preserva; `RdpFileBuilder` usa `Name`

## Phase 2: US1 — duplo clique (P1)

- [X] T003 Grade como `ListBox` com seleção; duplo clique e Enter abrem; botão direito seleciona; mesmo visual (destaque de seleção)

## Phase 3: US2 — renomear (P1)

- [X] T004 `RenameAppWindow` + `IDialogService.AskAppName`
- [X] T005 `MainViewModel`: `RenameApp`, `ResetAppName`, busca/ordem pelo nome local; menu e F2; dica com o nome do host

## Phase 4: Polish

- [X] T006 Versão 0.5.0, `README.md`, `BACKLOG.md` (remover itens 1 e 7), build de release
