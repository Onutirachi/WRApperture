# Feature Specification: Client Launcher — abrir apps remotos com um clique

**Feature Branch**: `20261007-110608-client-launcher`

**Created**: 2026-10-07

**Status**: Draft

**Input**: User description: "Algo que rode no cliente, que enxergue os apps publicados no PC remoto e, ao clicar, abra direto a janela do app, sem a área de trabalho remota inteira. Vários apps abertos ao mesmo tempo."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Parear com um host (Priority: P1)

O usuário informa o endereço do PC host (nome ou IP) e o token de pareamento que o administrador gerou. O cliente mostra a impressão digital (fingerprint) do host para conferência. Depois que o usuário confirma, o host fica salvo.

**Why this priority**: Sem pareamento o cliente não enxerga nada. A confirmação do fingerprint protege contra se conectar a um host falso.

**Independent Test**: Adicionar um host com o token correto, conferir que o fingerprint exibido é igual ao mostrado por `wrahost agent show` e que o host aparece na lista ao reabrir o cliente.

**Acceptance Scenarios**:

1. **Given** um host com agente ativo, **When** o usuário informa endereço e token corretos, **Then** o cliente exibe o nome do host e o fingerprint e pede confirmação.
2. **Given** a confirmação, **When** o cliente é fechado e reaberto, **Then** o host continua configurado, sem pedir o token de novo.
3. **Given** um token errado, **When** o usuário tenta parear, **Then** recebe a mensagem "token inválido" e nada é salvo.
4. **Given** um endereço inalcançável, **When** o usuário tenta parear, **Then** recebe uma mensagem clara sugerindo verificar endereço, porta e firewall.

---

### User Story 2 - Ver os apps do host (Priority: P1)

O cliente mostra os apps publicados do host selecionado como uma grade de ícones com nome. Há uma busca por nome e um botão de atualizar.

**Why this priority**: É a tela principal do produto.

**Independent Test**: Com dois apps publicados, a grade mostra os dois com ícone. Publicar um terceiro no host e clicar em atualizar faz ele aparecer.

**Acceptance Scenarios**:

1. **Given** um host pareado, **When** o cliente abre, **Then** os apps aparecem com ícone e nome.
2. **Given** vários apps, **When** o usuário digita parte do nome na busca, **Then** só os apps correspondentes ficam visíveis.
3. **Given** o host desligado, **When** o cliente abre, **Then** a última lista conhecida é exibida com aviso "host offline", e os apps continuam clicáveis.
4. **Given** que o certificado do host mudou, **When** o cliente consulta, **Then** a conexão é bloqueada e o usuário é avisado. Ele pode confirmar o novo fingerprint ou cancelar.

---

### User Story 3 - Abrir um app com um clique (Priority: P1) 🎯

Ao clicar no app, só a janela dele abre no cliente. Vários apps podem ficar abertos ao mesmo tempo, na mesma sessão remota.

**Why this priority**: É o objetivo central do projeto.

**Independent Test**: Clicar no Bloco de Notas abre só a janela do Bloco de Notas. Clicar em seguida na Calculadora abre a Calculadora sem pedir login de novo, e as duas janelas ficam abertas.

**Acceptance Scenarios**:

1. **Given** um app na grade, **When** o usuário clica, **Then** a janela do app remoto abre sem área de trabalho remota.
2. **Given** um app já aberto, **When** o usuário abre outro do mesmo host, **Then** ele abre na mesma sessão, sem nova autenticação.
3. **Given** que a porta RDP do host não é a padrão, **When** o usuário abre um app, **Then** a conexão usa a porta correta.

---

### User Story 4 - Salvar credenciais do host (Priority: P2)

O usuário salva, de forma protegida pelo Windows, o usuário e a senha do PC host, para não precisar digitá-los a cada conexão.

**Why this priority**: Torna o "um clique" de fato um clique. Sem isso, o Windows pede credenciais na primeira abertura de cada sessão.

**Independent Test**: Salvar as credenciais, abrir um app e verificar que nenhuma tela de login aparece. Remover as credenciais e verificar que a tela volta.

**Acceptance Scenarios**:

1. **Given** credenciais salvas, **When** um app é aberto, **Then** a conexão não pede senha.
2. **Given** credenciais salvas, **When** o usuário as remove, **Then** a próxima conexão volta a pedir senha.

---

### User Story 5 - Atalho na área de trabalho (Priority: P3)

O usuário cria um atalho de um app remoto na área de trabalho, com o ícone do app, e passa a abri-lo sem abrir o cliente.

**Why this priority**: É conveniência. O cliente já abre os apps.

**Independent Test**: Criar o atalho do Bloco de Notas, fechar o cliente e dar duplo clique no atalho. O app remoto abre.

**Acceptance Scenarios**:

1. **Given** um app, **When** o usuário escolhe "Criar atalho na área de trabalho", **Then** surge um atalho com o nome e o ícone do app que abre o app remoto.

### Edge Cases

- Dois hosts com apps de mesmo nome: o host selecionado define qual app é exibido. Os arquivos gerados não colidem.
- Token revogado no host (regenerado): o cliente mostra "token inválido" e oferece reparear informando o novo token.
- O host recusou por excesso de tentativas: o cliente mostra a mensagem do host e não insiste.
- Nome de app com caracteres especiais (acentos, `&`, `/`): a interface, o atalho e a conexão funcionam.
- O Windows do host mostra um aviso sobre o certificado RDP na primeira conexão: é o comportamento padrão do Windows, e o usuário pode marcar "não perguntar novamente". Isso é documentado.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Usuários MUST conseguir adicionar, listar e remover hosts (endereço, porta do agente, token).
- **FR-002**: No pareamento, o sistema MUST exibir o fingerprint do host e exigir confirmação antes de salvar.
- **FR-003**: Depois do pareamento, o sistema MUST recusar conexões cujo fingerprint seja diferente do confirmado, avisando o usuário e permitindo confirmar o novo fingerprint explicitamente.
- **FR-004**: O sistema MUST rejeitar o pareamento se o fingerprint informado pelo host divergir do observado na conexão (indício de interceptação).
- **FR-005**: O token MUST ser armazenado protegido pelo Windows para o usuário atual, nunca em texto puro.
- **FR-006**: O sistema MUST exibir os apps do host em grade com ícone e nome, com busca e atualização manual.
- **FR-007**: O sistema MUST manter a última lista de apps e os ícones de cada host, exibindo-os quando o host estiver offline.
- **FR-008**: Ao clicar em um app, o sistema MUST abrir apenas a janela do app remoto, usando o cliente de Área de Trabalho Remota do Windows.
- **FR-009**: Todas as conexões a um mesmo host MUST usar o mesmo endereço e o mesmo usuário, para que vários apps compartilhem uma única sessão.
- **FR-010**: O sistema MUST usar a porta RDP informada pelo host.
- **FR-011**: Usuários MUST conseguir salvar e remover as credenciais do host no cofre de credenciais do Windows.
- **FR-012**: Usuários MUST conseguir criar atalho na área de trabalho para um app, com o ícone do app.
- **FR-013**: Mensagens de erro MUST ser em português e indicar a ação sugerida (verificar token, rede, firewall).
- **FR-014**: O sistema MUST registrar em log as operações e os erros, sem registrar token nem senha.

### Key Entities

- **Paired Host**: nome de exibição, endereço, porta do agente, token protegido, fingerprint confirmado, porta RDP, usuário RDP (opcional), última lista de apps, data da última sincronização.
- **Remote App**: alias, nome e ícone em cache, pertencentes a um host.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Um usuário novo pareia um host e abre o primeiro app em menos de 2 minutos.
- **SC-002**: Com credenciais salvas, abrir um app exige um único clique, sem nenhuma tela de login.
- **SC-003**: Abrir 3 apps seguidos do mesmo host resulta em 3 janelas e uma única sessão remota.
- **SC-004**: A tela de apps aparece em menos de 2 segundos ao abrir o cliente, mesmo com o host offline (lista em cache).
- **SC-005**: Nenhum token ou senha aparece em texto puro em arquivos ou logs do cliente.

## Assumptions

- O cliente roda em Windows 10/11 com o cliente de Área de Trabalho Remota (`mstsc`) nativo.
- O cliente e o host estão na mesma rede local ou VPN.
- Um host pareado por vez é exibido na grade. Vários hosts podem ser cadastrados e alternados.
- Atalhos ficam na área de trabalho do usuário atual. Integração com o Menu Iniciar fica para depois.
- Ícone na bandeja e iniciar junto com o Windows ficam fora do escopo da v1.
