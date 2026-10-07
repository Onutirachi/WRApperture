# Feature Specification: Atalhos dos apps no Menu Iniciar

**Feature Branch**: `20261007-154623-start-menu-shortcuts`

**Created**: 2026-10-07

**Status**: Draft

**Input**: Item 4 do backlog: "atalho na área de trabalho para os apps — já existe; avaliar deixar mais visível e adicionar ao Menu Iniciar".

## User Scenarios & Testing *(mandatory)*

### User Story 1 - App no Menu Iniciar (Priority: P1)

Pelo botão direito num app (ou na "Área de trabalho"), o usuário escolhe "Adicionar ao Menu Iniciar". O app passa a aparecer na pasta "WRApperture" do Menu Iniciar e na busca do Windows, e pode ser fixado no Iniciar ou na barra de tarefas. Ao abrir por ali, só a janela do app abre, como pelo cliente.

**Acceptance Scenarios**:

1. **Given** um app, **When** o usuário escolhe "Adicionar ao Menu Iniciar", **Then** surge `Menu Iniciar\Programas\WRApperture\<nome>.lnk`, com o ícone do app, abrindo o mesmo `.rdp` do cliente.
2. **Given** um app renomeado no cliente, **When** é adicionado, **Then** o atalho usa o nome local.
3. **Given** atalhos criados pelo WRApperture, **When** o cliente é desinstalado apagando os dados do usuário, **Then** esses atalhos saem, e a pasta "WRApperture" sai se ficar vazia. Outros arquivos da pasta são mantidos.

### User Story 2 - Opções mais visíveis (Priority: P2)

A dica de cada bloco lembra que o botão direito tem mais opções (renomear, atalhos).

## Requirements *(mandatory)*

- **FR-001**: Os atalhos do Menu Iniciar ficam em `%APPDATA%\Microsoft\Windows\Start Menu\Programs\WRApperture`.
- **FR-002**: O desinstalador, com `-RemoveUserData`, remove dessa pasta só os `.lnk` que apontam para os `.rdp` do WRApperture, e remove a pasta se ela ficar vazia.
