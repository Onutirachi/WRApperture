# Feature Specification: Bandeja do Windows e iniciar com o Windows

**Feature Branch**: `20261007-155137-tray`

**Created**: 2026-10-07

**Status**: Draft

**Input**: Item 5 do backlog: "minimizar o painel para a bandeja do Windows — ícone na bandeja com o menu dos apps e opção de iniciar com o Windows".

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Apps a um clique, na bandeja (Priority: P1)

O cliente mostra um ícone na área de notificação (perto do relógio). O botão direito lista os apps do host selecionado (incluindo a área de trabalho) e abre qualquer um direto, sem mostrar a janela. O clique esquerdo mostra a janela.

**Acceptance Scenarios**:

1. **Given** o cliente aberto, **When** o usuário clica com o botão direito no ícone, **Then** vê "Abrir WRApperture", o nome do host, os apps com ícones e "Sair".
2. **Given** o menu, **When** escolhe um app, **Then** ele abre como pelo duplo clique, inclusive liberando o PC remoto e com os diálogos aparecendo mesmo com a janela escondida.

---

### User Story 2 - Fechar mantém na bandeja (Priority: P1)

Fechar a janela esconde o cliente na bandeja. Na primeira vez, um aviso explica onde ele ficou. "Sair" no menu do ícone encerra de verdade. A opção "Ao fechar, manter na bandeja" (menu ⋯, ligada por padrão) volta ao comportamento antigo.

---

### User Story 3 - Iniciar com o Windows (Priority: P2)

"Iniciar com o Windows" (menu ⋯, desligada por padrão) faz o cliente iniciar escondido na bandeja ao entrar no Windows.

**Acceptance Scenarios**:

1. **Given** a opção ligada, **When** o usuário entra no Windows, **Then** o ícone aparece na bandeja sem abrir a janela.
2. **Given** o cliente já rodando, **When** o usuário o abre de novo (Menu Iniciar), **Then** a janela existente aparece; não surge um segundo cliente.
3. **Given** a opção ligada, **When** o cliente é desinstalado, **Then** a entrada de inicialização sai, mas só se apontar para essa instalação.

## Requirements *(mandatory)*

- **FR-001**: `NotifyIcon` (WinForms, só para o ícone). O menu é refeito a cada abertura, a partir do host e dos apps atuais.
- **FR-002**: `CloseToTray` (padrão `true`) e `TrayHintShown` ficam em `hosts.json`. "Iniciar com o Windows" é o valor `WRApperture` em `HKCU\…\CurrentVersion\Run` (`"<exe>" --tray`), atualizado para o caminho atual a cada início.
- **FR-003**: Uma instância por sessão (mutex `Local\WRApperture.Client`). Uma segunda abertura sinaliza um evento para a primeira mostrar a janela e encerra.
- **FR-004**: Os diálogos não dependem da janela principal visível (sem dona: centralizados e na barra de tarefas).
- **FR-005**: O desinstalador remove a entrada `Run` quando ela aponta para a pasta instalada.
