---
description: "Task list for Host Manager"
---

# Tasks: Host Manager

## Phase 1: Foundational

- [X] T001 `WRApperture.Ui` com `Theme.xaml` (extraído do cliente); o cliente passa a usá-lo
- [X] T002 [P] Testes do `AppSourceInspector` (lnk com args e ícone, lnk MSI, exe com e sem FileDescription, recusas: pdf, bat, atalho quebrado, não exe)
- [X] T003 `AppDraft`, `IAppSourceInspector`, `AppSourceInspector`, `IFileDescriptionReader` + DI
- [X] T004 Projeto `WRApperture.Host.Manager` (WPF, `WRApperture.Host.exe`), App com elevação/`--read-only`, DI, Serilog

## Phase 2: US2 + US4 — lista e painel do PC (P1)

- [X] T005 `ManagerViewModel`: carregar apps (com ícones), estado de preparação, agente, IPs, porta, fingerprint
- [X] T006 `MainWindow`: lista, barra de ações, painel "Este PC", faixa de somente leitura
- [X] T007 Remover app (confirmação), preparar PC, novo token (`TokenWindow` com copiar), iniciar/reiniciar agente

## Phase 3: US1 — arrastar e editar (P1)

- [X] T008 Arrastar e soltar (`.lnk`/`.exe`, vários) e "Adicionar…" (OpenFileDialog) → `AppEditorWindow`
- [X] T009 `AppEditorWindow`: nome, executável (procurar), política e argumentos, ícone, aviso de pasta gravável; salvar (novo ou edição)

## Phase 4: US3 — apps instalados (P2)

- [X] T010 `DiscoveryWindow`: busca, marcar vários, "já publicado", publicar selecionados

## Phase 5: Packaging & Polish

- [X] T011 Pacote: `WRApperture.Host.exe` no zip, atalho "WRApperture (Host)" (Common Programs) no instalador e remoção no desinstalador + Pester
- [X] T012 Build, testes, captura de tela em `--read-only`, README/LEIA-ME, release 0.4.0
