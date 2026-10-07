---
description: "Task list for localization"
---

# Tasks: Interface em vários idiomas

## Phase 1: Base

- [X] T001 `Strings` (catálogo embutido, cadeia de idiomas, override), `TExtension`, `localization/README.md`
- [X] T002 `LocalizationTests` (chaves, placeholders, chaves usadas no código, fallback) e `TestLanguage` nos projetos de teste
- [X] T003 `common.json` (menu de tema)

## Phase 2: Extração (paralela)

- [ ] T004 [P] Cliente → `client.json`
- [ ] T005 [P] App do host → `manager.json`
- [ ] T006 [P] Núcleo do host, agente e `wrahost` → `host.json`, `agent.json`, `cli.json`
- [ ] T007 [P] Instaladores → `installer.json`, `README.txt` em inglês

## Phase 3: Integração

- [ ] T008 Juntar as áreas, build e testes completos, conferência visual em inglês e português (`--read-only`)
- [ ] T009 Versão 0.8.0, README, BACKLOG, build de release
