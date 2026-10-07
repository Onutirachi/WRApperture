# Feature Specification: Escolha manual do idioma

**Feature Branch**: `20261007-170853-language-choice`

**Created**: 2026-10-07

**Status**: Draft

**Input**: "Não tem uma opção de escolher linguagem manualmente em conjunto com o automático?"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Escolher o idioma na interface (Priority: P1)

Em "Idioma (Language)…" (menu ⋯ do cliente; botão ◐ do app do host), o usuário escolhe "Automático — idioma do Windows (…)" ou um dos idiomas disponíveis, cada um com o próprio nome ("English", "Português"). O app oferece reiniciar na hora, e a pergunta já aparece no idioma escolhido.

**Acceptance Scenarios**:

1. **Given** o Windows em português, **When** o usuário escolhe "English" e reinicia, **Then** o cliente, o app do host, o `wrahost` e os instaladores aparecem em inglês.
2. **Given** um idioma escolhido, **When** o usuário volta para "Automático", **Then** o WRApperture segue o idioma do Windows de novo.
3. **Given** uma pasta de tradução nova (ex.: `localization/es`), **When** o build é gerado, **Then** "Español" aparece na lista sem mudança de código.

## Requirements *(mandatory)*

- **FR-001**: A escolha fica em `HKCU\Software\WRApperture\Ui\Language` (o valor que `Strings` e os instaladores já liam). "Automático" apaga o valor. `WRAPPERTURE_LANG` continua tendo prioridade (diagnóstico).
- **FR-002**: A lista vem de `Strings.LanguageChoices()` (pastas embutidas, nome nativo da cultura).
- **FR-003**: Reiniciar o cliente libera a instância única antes de abrir o novo processo. O app do host reinicia com os mesmos argumentos e a mesma elevação.

## Correção junto

O template de `ComboBox` do tema (0.7.0) ignorava `DisplayMemberPath` e mostrava o objeto cru, por exemplo no seletor de host do cliente. Agora repassa o `ItemTemplateSelector`.
