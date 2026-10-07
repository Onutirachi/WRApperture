# Feature Specification: Ícones do WRApperture

**Feature Branch**: `20261007-161848-icons`

**Created**: 2026-10-07

**Status**: Draft

**Input**: "Coloquei na raiz do projeto dois SVGs de ícone que fiz pro WRApperture, cliente e host … usar para fazer os ícones do client e do host; depois, se possível, definir um ícone para sobrepor os ícones originais, para distinguir os ícones criados pelo WRApperture."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Ícones próprios (Priority: P1)

O cliente (`WRApperture.exe`) usa o desenho da janela com o obturador. O app do host, o agente e o `wrahost` usam o da janela com a seta dupla. Os ícones aparecem no Explorer, nos atalhos do Menu Iniciar, na barra de tarefas, nas barras de título e na bandeja.

### User Story 2 - Selo nos atalhos criados pelo cliente (Priority: P1)

Os atalhos de apps criados pelo cliente (área de trabalho e Menu Iniciar) mostram o ícone do app com o selo do WRApperture (o obturador num círculo azul) no canto inferior direito. Assim, a versão remota de um app é distinguida de uma cópia instalada no próprio computador. O atalho "Área de trabalho" usa o ícone do cliente.

**Acceptance Scenarios**:

1. **Given** um app, **When** o usuário cria um atalho, **Then** o ícone tem o selo e continua nítido de 16 a 256 px.
2. **Given** um novo desenho em `assets/icons/badge.svg`, **When** `scripts/build-icons.ps1` roda e o cliente é gerado de novo, **Then** os próximos atalhos usam o novo selo.

## Requirements *(mandatory)*

- **FR-001**: Os desenhos ficam em `assets/icons/*.svg`. `scripts/build-icons.ps1` os desenha com WPF em cada tamanho (16–256 px) e gera `WRApperture.ico`, `WRApperture.Host.ico`, `client.png` e `badge.png`. Os arquivos gerados ficam no repositório, então o build normal não depende do script.
- **FR-002**: Os `.ico` dos atalhos têm vários tamanhos (16, 24, 32, 48, 256). O selo ocupa 56% do ícone até 32 px e 44% acima disso.
- **FR-003**: Atalhos criados antes desta versão mantêm o ícone antigo até serem criados de novo.
