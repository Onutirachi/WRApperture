# Feature Specification: Interface em vários idiomas

**Feature Branch**: `20261007-152909-localization`

**Created**: 2026-10-07

**Status**: Draft

**Input**: Item 6 do backlog, com o pedido: "de um jeito que fique simples de adicionar novas localizações, copiando a linguagem do sistema do usuário; conforme forem implementadas novas línguas, vai se ajustando sozinho pra qualquer pessoa".

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Idioma automático (Priority: P1)

Cada pessoa vê o WRApperture no idioma de exibição do seu Windows, se ele existir. Senão, vê em inglês. Isso vale para o cliente, o app do host, o `wrahost` e os instaladores.

**Independent Test**: Com o Windows em português, tudo aparece em português. Com `WRAPPERTURE_LANG=en`, tudo aparece em inglês. Com um idioma ainda sem tradução (ex.: alemão), aparece em inglês.

**Acceptance Scenarios**:

1. **Given** o Windows em `pt-BR`, **When** qualquer parte do WRApperture abre, **Then** os textos estão em português.
2. **Given** o Windows em um idioma sem tradução, **When** abre, **Then** os textos estão em inglês.
3. **Given** uma tradução incompleta, **When** falta um texto, **Then** aparece o texto em inglês, nunca a chave crua.

---

### User Story 2 - Acrescentar um idioma (Priority: P1)

Quem traduz copia `localization/en` para `localization/<código>`, traduz os valores e gera o build. Não há lista de idiomas para atualizar nem código para mudar.

**Acceptance Scenarios**:

1. **Given** uma pasta nova `localization/es`, **When** o build é gerado, **Then** quem usa Windows em espanhol (qualquer região) vê espanhol.
2. **Given** uma tradução com chave faltando, sobrando ou com `{0}` diferente do inglês, **When** os testes rodam, **Then** eles apontam exatamente quais chaves estão erradas.

## Requirements *(mandatory)*

- **FR-001**: Os textos ficam em `localization/<idioma>/<área>.json` (JSON plano, chave → texto), embutidos no `WRApperture.Shared`.
- **FR-002**: Ordem de busca: idioma do Windows (ex.: `pt-BR`), depois o idioma pai (`pt`), depois `en`, depois a própria chave. Pode ser forçado por `WRAPPERTURE_LANG` ou `HKCU\Software\WRApperture\Ui\Language`.
- **FR-003**: Código C# usa `Strings.T("área.chave", args…)`; XAML usa `{loc:T área.chave}`; os scripts de instalação usam `T 'área.chave'`, lendo os mesmos JSONs.
- **FR-004**: Os testes garantem as mesmas chaves e os mesmos placeholders do inglês em todos os idiomas, e que toda chave usada no código existe em inglês. Os testes de mensagens rodam sempre em inglês.
- **FR-005**: Na entrega, existem `en` e `pt`.
