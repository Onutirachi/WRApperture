# Feature Specification: Grade de apps do cliente — duplo clique e nomes locais

**Feature Branch**: `20261007-145218-client-tiles`

**Created**: 2026-10-07

**Status**: Draft

**Input**: Itens 1 e 7 do backlog: "abrir apps com duplo clique, como no Windows" e "renomear apps no cliente (apenas visual)".

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Abrir com duplo clique (Priority: P1)

Na grade do cliente, um clique seleciona o app e dois cliques o abrem, como na área de trabalho do Windows. Pelo teclado, as setas mudam a seleção e Enter abre.

**Why this priority**: Hoje um clique acidental abre o app e pode encerrar a sessão de quem está usando o PC remoto.

**Independent Test**: Clicar uma vez num app e ver que ele só fica destacado. Clicar duas vezes e ver que ele abre. Navegar com as setas e abrir com Enter.

**Acceptance Scenarios**:

1. **Given** a grade de apps, **When** o usuário clica uma vez num app, **Then** ele fica selecionado e nada é aberto.
2. **Given** um app, **When** o usuário dá duplo clique, **Then** o app é aberto, com o mesmo fluxo de antes (liberar o host, sobreposição de progresso).
3. **Given** um app selecionado, **When** o usuário pressiona Enter, **Then** o app é aberto.
4. **Given** um app, **When** o usuário clica com o botão direito, **Then** o app fica selecionado e o menu mostra "Abrir", "Renomear…" e "Criar atalho na área de trabalho".

---

### User Story 2 - Renomear um app só neste computador (Priority: P1)

O usuário dá um nome próprio a um app (ex.: "Sistema Contábil" em vez de "SCX32 Cliente v4"). O nome vale só no cliente: o host não é alterado.

**Why this priority**: Os nomes publicados costumam vir do executável ou do atalho e nem sempre são claros para quem usa.

**Independent Test**: Renomear um app, clicar em Atualizar, fechar e reabrir o cliente: o nome escolhido continua lá. "Restaurar nome original" volta ao nome do host.

**Acceptance Scenarios**:

1. **Given** um app, **When** o usuário escolhe "Renomear…" (ou F2) e confirma um nome, **Then** a grade mostra o novo nome e a busca e a ordem alfabética passam a usá-lo.
2. **Given** um app renomeado, **When** o cliente sincroniza com o host, **Then** o nome local é mantido, mesmo que o nome no host mude.
3. **Given** um app renomeado, **When** o usuário passa o mouse sobre ele, **Then** a dica mostra também o nome publicado no host.
4. **Given** um app renomeado, **When** o usuário escolhe "Restaurar nome original" (ou confirma um nome vazio), **Then** volta o nome do host.
5. **Given** um app renomeado, **When** o usuário cria um atalho ou abre o app, **Then** o atalho e a conexão usam o nome local.
6. **Given** um app removido do host, **When** o cliente sincroniza, **Then** o nome local desse app é descartado junto.

### Edge Cases

- Espaços no começo ou fim e quebras de linha são removidos. Um nome igual ao do host não é guardado como personalizado.
- Nomes locais são por host: o mesmo alias em dois hosts pode ter nomes diferentes.
- Com o host offline, renomear funciona (usa a lista em cache).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Um clique seleciona, duplo clique ou Enter abre o app selecionado. O botão direito seleciona e abre o menu.
- **FR-002**: O menu do app tem "Abrir" (padrão), "Renomear…", "Restaurar nome original" (só quando renomeado) e "Criar atalho na área de trabalho". F2 renomeia o app selecionado.
- **FR-003**: O nome local é guardado em `hosts.json`, por host e alias, e nunca é enviado ao host.
- **FR-004**: A sincronização preserva os nomes locais dos apps que continuam publicados.
- **FR-005**: O nome exibido (local, ou o do host) é usado na grade, na busca, na ordenação, no `.rdp` (`remoteapplicationname`) e no atalho da área de trabalho.

## Success Criteria *(mandatory)*

- **SC-001**: Nenhum app abre com um único clique.
- **SC-002**: Um nome local sobrevive à sincronização e ao reinício do cliente.
