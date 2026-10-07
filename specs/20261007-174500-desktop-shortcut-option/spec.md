# Feature Specification: Atalho na área de trabalho ao instalar

**Created**: 2026-10-07

**Status**: Draft

**Input**: "Consegue fazer o instalador dar opção, com padrão sim, de criar um ícone na área de trabalho do client e do host?"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Perguntar ao instalar (Priority: P1)

Ao rodar `Instalar.cmd`, o instalador pergunta "Criar atalho na área de trabalho? [S/n]". Enter ou "s"/"y" cria o atalho; "n" não cria.

- Cliente: "WRApperture" na área de trabalho do usuário.
- Host: "WRApperture (Host)" na área de trabalho pública, porque a instalação é para o PC inteiro.

**Acceptance Scenarios**:

1. **Given** uma instalação nova, **When** o usuário só aperta Enter, **Then** o atalho é criado.
2. **Given** um atalho já existente (atualização), **When** o instalador roda, **Then** o atalho é atualizado sem perguntar de novo.
3. **Given** a desinstalação, **When** ela termina, **Then** o atalho da área de trabalho sai junto.

## Requirements *(mandatory)*

- **FR-001**: Os scripts de instalação ganham `-Interactive` (pergunta; o `Instalar.cmd` usa esse parâmetro), `-NoDesktopShortcut` e `-DesktopDir`. Sem `-Interactive`, o padrão é criar o atalho.
- **FR-002**: O desinstalador do cliente remove os atalhos da área de trabalho que apontam para a pasta instalada. O do host remove "WRApperture (Host).lnk" da área de trabalho pública.
- **FR-003**: Os testes usam pastas temporárias como área de trabalho, então nunca tocam na área de trabalho real.
