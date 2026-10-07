# Feature Specification: Tema escuro

**Feature Branch**: `20261007-152301-dark-theme`

**Created**: 2026-10-07

**Status**: Draft

**Input**: Item 3 do backlog: "tema escuro — seguir o tema do Windows, com opção manual".

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Seguir o Windows (Priority: P1)

Quem usa o Windows no modo escuro vê o cliente e o app do host escuros, inclusive a barra de título. Se o modo do Windows mudar com o app aberto, o app muda junto.

**Independent Test**: Em Configurações → Personalização → Cores, alternar "Escolha o modo de aplicativo padrão" com o app aberto. As cores trocam na hora.

**Acceptance Scenarios**:

1. **Given** o Windows no modo escuro, **When** o app abre, **Then** janelas, diálogos, menus, listas, caixas de texto, combos, dicas e barras de rolagem usam a paleta escura, com texto legível.
2. **Given** o app aberto, **When** o modo do Windows muda, **Then** as cores mudam sem reiniciar.

---

### User Story 2 - Escolha manual (Priority: P2)

O usuário pode fixar "Tema claro" ou "Tema escuro", ou voltar a "Tema: seguir o Windows". No cliente, a escolha fica no menu ⋯; no app do host, no botão ◐.

**Acceptance Scenarios**:

1. **Given** "Tema escuro" escolhido, **When** o Windows está no modo claro, **Then** o app continua escuro, também depois de reiniciar.
2. **Given** uma escolha feita no cliente, **When** o app do host é aberto no mesmo usuário do Windows, **Then** ele usa a mesma escolha.

### Edge Cases

- Sem permissão para gravar a preferência, a escolha vale até fechar o app.
- Diálogos nativos do Windows (MessageBox, abrir arquivo) seguem o próprio Windows.

## Requirements *(mandatory)*

- **FR-001**: As paletas clara e escura têm as mesmas chaves (`Colors.Light.xaml`/`Colors.Dark.xaml`). As telas usam `DynamicResource`, sem cores fixas.
- **FR-002**: A preferência (`System`, `Light`, `Dark`) fica em `HKCU\Software\WRApperture\Ui\Theme` e vale para todos os apps WRApperture do usuário.
- **FR-003**: Com `System`, o tema segue `AppsUseLightTheme` e reage a mudanças em tempo real.
- **FR-004**: A barra de título acompanha o tema (`DWMWA_USE_IMMERSIVE_DARK_MODE`).
