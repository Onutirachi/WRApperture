# Feature Specification: Host Agent — catálogo de apps para o cliente

**Feature Branch**: `20261007-105635-host-agent`

**Created**: 2026-10-07

**Status**: Draft

**Input**: User description: "Agente que roda no PC host em segundo plano e expõe, pela rede, a lista de apps publicados e seus ícones, para que o app cliente mostre os apps e os abra com um clique. O acesso é protegido por um token de pareamento."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Cliente obtém a lista de apps do host (Priority: P1)

O app cliente informa o endereço do host e o token de pareamento e recebe a lista de apps publicados (alias e nome). Com isso ele monta a tela de apps e abre cada um via RemoteApp.

**Why this priority**: É o que permite ao cliente "enxergar" os apps sem nenhuma configuração manual de arquivos `.rdp`.

**Independent Test**: Com o agente rodando e dois apps publicados, uma requisição com o token correto retorna exatamente os dois apps. Com token errado ou ausente, o acesso é negado.

**Acceptance Scenarios**:

1. **Given** dois apps publicados e visíveis, **When** o cliente consulta com o token correto, **Then** recebe os dois apps com alias e nome.
2. **Given** um app publicado marcado como oculto, **When** o cliente consulta, **Then** esse app não aparece.
3. **Given** um token inválido ou ausente, **When** o cliente consulta, **Then** o acesso é negado e nenhum dado é revelado.
4. **Given** um app publicado ou removido no host, **When** o cliente consulta de novo, **Then** a lista reflete a mudança imediatamente, sem reiniciar o agente.

---

### User Story 2 - Cliente obtém os dados de conexão do host (Priority: P1)

O cliente precisa saber como conectar via RDP: o nome do host e a porta RDP. Também precisa de uma impressão digital (fingerprint) da identidade do agente, para confirmar que fala com o host certo.

**Why this priority**: Sem esses dados, o cliente não consegue gerar a conexão RemoteApp corretamente. Por exemplo, se a porta RDP tiver sido alterada, a conexão falha.

**Independent Test**: Consultar as informações do host e conferir nome, porta RDP e fingerprint com o que o host mostra localmente.

**Acceptance Scenarios**:

1. **Given** o agente rodando, **When** o cliente consulta as informações do host com token válido, **Then** recebe o nome do computador, a porta RDP configurada e a versão do agente.
2. **Given** a porta RDP alterada no host, **When** o cliente consulta, **Then** a porta informada é a nova.

---

### User Story 3 - Cliente obtém o ícone de cada app (Priority: P2)

Para cada app, o cliente baixa o ícone em PNG no tamanho desejado e o reaproveita entre execuções quando ele não mudou.

**Why this priority**: Melhora muito a usabilidade, mas não bloqueia a abertura de apps.

**Independent Test**: Pedir o ícone de um app publicado em 48 px e receber PNG de 48x48. Pedir de novo informando a versão que já tem e receber "não modificado".

**Acceptance Scenarios**:

1. **Given** um app publicado, **When** o ícone é pedido, **Then** um PNG do tamanho solicitado é retornado (16 a 256 px, padrão 48).
2. **Given** um alias inexistente, **When** o ícone é pedido, **Then** a resposta é "não encontrado".
3. **Given** o cliente já tem o ícone atual, **When** pede de novo informando a versão, **Then** recebe "não modificado", sem reenviar a imagem.

---

### User Story 4 - Administrador gera o token e configura o agente (Priority: P1)

O administrador do host gera (ou regenera) o token de pareamento e o informa no cliente. Ele também pode mudar a porta do agente e consultar o fingerprint para conferência.

**Why this priority**: Sem token não há pareamento. Regenerar o token é a forma de revogar acesso de clientes antigos.

**Independent Test**: Gerar o token, usá-lo no cliente (funciona), regenerar e verificar que o token antigo deixa de funcionar.

**Acceptance Scenarios**:

1. **Given** nenhum token configurado, **When** o administrador gera um, **Then** o token é exibido uma única vez e passa a valer imediatamente.
2. **Given** um token em uso, **When** o administrador regenera, **Then** o token anterior deixa de ser aceito sem reiniciar o agente.
3. **Given** nenhum token configurado, **When** um cliente tenta acessar, **Then** o acesso é negado e o agente registra em log que precisa de pareamento.

### Edge Cases

- Várias tentativas com token errado vindas do mesmo endereço: depois de um limite, novas tentativas são temporariamente bloqueadas.
- A identidade criptográfica do agente (certificado) ainda não existe ou expirou: é criada ou renovada automaticamente na inicialização, e o fingerprint muda (clientes pareados precisam reconfirmar).
- O agente é iniciado sem privilégio para ler a configuração: ele encerra com mensagem clara no log.
- A porta configurada já está em uso: o agente encerra com erro claro no log.
- A consulta de "saúde" do agente responde sem token, mas não expõe nenhum dado.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: O agente MUST rodar em segundo plano no host, iniciando com o Windows, sem usuário logado.
- **FR-002**: O agente MUST expor a lista de apps publicados visíveis (alias e nome), refletindo o estado atual a cada consulta.
- **FR-003**: O agente MUST expor as informações de conexão: nome do host, porta RDP, versão do agente e fingerprint da identidade do agente.
- **FR-004**: O agente MUST fornecer o ícone PNG de um app publicado no tamanho pedido (16–256 px), com suporte a "não modificado".
- **FR-005**: Toda rota de dados MUST exigir o token de pareamento. Uma rota de saúde, sem dados, fica isenta.
- **FR-006**: O host MUST guardar apenas uma forma derivada (hash com salt) do token, nunca o token em si.
- **FR-007**: A comparação do token MUST ser resistente a ataques de tempo.
- **FR-008**: O administrador MUST conseguir gerar ou regenerar o token. O token é exibido uma única vez e o anterior é invalidado imediatamente.
- **FR-009**: Toda comunicação MUST ser cifrada (HTTPS) com um certificado próprio do host, criado automaticamente se ausente ou expirado.
- **FR-010**: O administrador MUST conseguir alterar a porta do agente (padrão 5080) e consultar o fingerprint do certificado.
- **FR-011**: O agente MUST limitar tentativas de autenticação falhas por endereço de origem.
- **FR-012**: O agente MUST registrar em log estruturado as inicializações, as falhas de autenticação (sem registrar o token) e os erros.

### Key Entities

- **Agent Settings**: porta, hash e salt do token, identificação do certificado em uso.
- **App Summary**: o que o cliente vê de um app (alias e nome). Caminhos do executável no host não são expostos.
- **Host Info**: nome do host, porta RDP, versão do agente, fingerprint do certificado.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Um cliente pareado recebe a lista de apps em menos de 1 segundo na rede local.
- **SC-002**: 100% das requisições de dados sem token válido são recusadas.
- **SC-003**: Após regenerar o token, o token antigo é recusado na próxima requisição (0 requisições aceitas).
- **SC-004**: O agente volta a funcionar sozinho após reiniciar o host, sem intervenção.
- **SC-005**: Uma publicação ou remoção de app aparece para o cliente na próxima consulta, sem reiniciar nada.

## Assumptions

- A rede entre cliente e host é local ou VPN. Exposição direta à internet não é cenário suportado.
- A instalação do agente como serviço e a abertura da porta no firewall ficam na feature de empacotamento. Nesta feature, a validação roda o agente manualmente.
- O cliente confia no certificado no primeiro pareamento e passa a exigir o mesmo fingerprint depois (comportamento do cliente, em outra feature).
- O caminho do executável e outros detalhes internos do host não são expostos ao cliente. O RemoteApp só precisa do alias.
- O agente apenas lê os apps publicados. Publicar ou remover continua sendo feito pelo CLI ou pelo Manager.
