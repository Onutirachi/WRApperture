# Feature Specification: Alternância entre uso local do host e apps remotos

**Feature Branch**: `20261007-123424-session-handoff`

**Created**: 2026-10-07

**Status**: Draft

**Input**: "Quando você tenta usar o app com o usuário logado no host, aparecem uns erros e o Windows pede para deslogar. Tem como deslogar o host automaticamente ao abrir um app no cliente e encerrar os apps no cliente caso o usuário logue no host?"

## Contexto

No Windows Pro, um usuário não pode ter ao mesmo tempo uma sessão de área de trabalho (uso local) e uma sessão RemoteApp:

- **Uso local → app remoto**: o RemoteApp não consegue iniciar enquanto existe a sessão de área de trabalho do mesmo usuário. Aparecem erros, e o Windows pede logoff.
- **App remoto → uso local**: ao logar localmente, o Windows desconecta o cliente e leva a sessão RemoteApp para a tela do host. Como essa sessão não tem área de trabalho (barra de tarefas, Explorer), o uso local fica quebrado.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Abrir um app com o host em uso local (Priority: P1)

O usuário clica num app no cliente enquanto alguém (normalmente ele mesmo) está logado localmente no host. O cliente avisa que a sessão local será encerrada (programas abertos no host serão fechados e o que não foi salvo será perdido) e, com a confirmação, encerra a sessão e abre o app. O usuário pode escolher "sempre encerrar automaticamente neste host".

**Why this priority**: Hoje esse cenário falha com erros do Windows. É o caminho mais comum ao sair do PC e ir para outro.

**Independent Test**: Logar localmente no host, clicar num app no cliente, confirmar e verificar que o host vai para a tela de login e o app abre no cliente.

**Acceptance Scenarios**:

1. **Given** o host com sessão local do mesmo usuário, **When** o usuário clica num app, **Then** o cliente mostra quem está usando o host e o aviso de perda de dados não salvos.
2. **Given** a confirmação, **When** a sessão local termina, **Then** o app abre no cliente sem erros.
3. **Given** "sempre encerrar automaticamente" marcado, **When** o cenário se repete, **Then** a sessão local é encerrada sem perguntar.
4. **Given** que o usuário cancela, **When** o diálogo fecha, **Then** nada é encerrado e o app não abre.
5. **Given** o host livre ou só com sessões RemoteApp, **When** o usuário clica num app, **Then** nenhum aviso aparece.
6. **Given** um agente de versão antiga ou inalcançável, **When** o usuário clica, **Then** o app abre como antes, sem verificação.

---

### User Story 2 - Logar no host com apps remotos abertos (Priority: P1)

Com apps abertos no cliente, alguém faz login localmente no host com o mesmo usuário. Os apps somem do cliente (o Windows o desconecta), e o usuário local recebe uma área de trabalho normal com os mesmos programas abertos, sem perder trabalho.

**Why this priority**: Fecha o ciclo de alternância nos dois sentidos.

**Independent Test**: Abrir o Bloco de Notas pelo cliente e digitar algo sem salvar. Logar no host com o mesmo usuário e verificar a área de trabalho completa, com o Bloco de Notas e o texto. No cliente, a janela do app foi fechada.

**Acceptance Scenarios**:

1. **Given** uma sessão RemoteApp ativa, **When** o mesmo usuário loga no host, **Then** em até 10 s a sessão exibe a área de trabalho normal (barra de tarefas e Explorer) com os programas que estavam abertos.
2. **Given** essa sessão agora local, **When** o usuário volta a abrir um app pelo cliente, **Then** vale a US1 (aviso e encerramento).

### Edge Cases

- Usuário RDP desconhecido pelo cliente (credenciais não salvas): considera-se a sessão local da tela do host e as sessões desconectadas desse mesmo usuário.
- Conta Microsoft (e-mail) no cliente e nome de conta local diferente no host: correspondência pelo início do e-mail. Sem correspondência, **nada é encerrado** (pode ser outra pessoa), e o Windows mostra o erro original.
- Outro usuário logado localmente: não é encerrado pelo WRApperture. O próprio Windows pede a ele para liberar a sessão.
- O encerramento demora ou falha: o cliente informa e não abre o app em estado inconsistente.

## Requirements *(mandatory)*

- **FR-001**: O agente MUST informar, para um usuário RDP, quais sessões de área de trabalho no host impedem o RemoteApp (usuário, estado, se é a sessão da tela).
- **FR-002**: O agente MUST encerrar essas sessões sob pedido autenticado e responder só após a conclusão (limite de 45 s).
- **FR-003**: O agente MUST NOT encerrar sessões RemoteApp nem sessões de outros usuários.
- **FR-004**: O cliente MUST verificar o host antes de abrir um app e, havendo bloqueio, pedir confirmação com aviso explícito de perda de trabalho não salvo, exceto quando o "automático" estiver ativo para aquele host.
- **FR-005**: O "encerrar automaticamente" MUST ser por host e reversível no menu.
- **FR-006**: Se a verificação não for possível (agente antigo ou offline), o cliente MUST abrir o app como antes.
- **FR-007**: O agente MUST detectar quando a tela do host passa a exibir uma sessão RemoteApp e iniciar nela a área de trabalho do usuário.
- **FR-008**: Todas as ações sobre sessões MUST ser registradas em log (usuário, sessão, ação, motivo).

## Success Criteria *(mandatory)*

- **SC-001**: Abrir um app com o host em uso local termina com o app aberto, sem erros do Windows, em menos de 30 s.
- **SC-002**: Logar no host com apps remotos abertos resulta em área de trabalho funcional sem perda de trabalho.
- **SC-003**: 0 sessões de outros usuários encerradas pelo WRApperture.

## Assumptions

- O host é tipicamente usado por uma pessoa (a mesma que usa o cliente).
- O agente roda como LocalSystem (privilégio para listar e encerrar sessões e iniciar processos na sessão do usuário).
