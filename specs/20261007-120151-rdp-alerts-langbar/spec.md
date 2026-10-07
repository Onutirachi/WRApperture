# Feature Specification: Abrir apps sem alerta e sem barra de idioma

**Feature Branch**: `20261007-120151-rdp-alerts-langbar`

**Created**: 2026-10-07

**Status**: Draft

**Input**: Feedback do primeiro teste real: "Um menu de escolher layout do teclado fica suspenso na parte superior e atrapalha consideravelmente. Quando vou abrir o app, aparece um alerta do risco do acesso remoto e não há botão para concordar sempre; o alerta sempre surge."

## Contexto

- **Alerta**: desde a atualização de segurança de abril de 2026 (CVE-2026-26151), o Windows mostra o alerta "Conexão remota desconhecida" toda vez que um arquivo de conexão **não assinado** é aberto. Não há opção de lembrar a escolha, e a área de transferência fica bloqueada por padrão. Arquivos assinados por um publicador marcado como confiável no PC abrem sem alerta.
- **Barra de idioma**: em sessões RemoteApp não existe barra de tarefas, então o seletor de idioma e teclado do Windows aparece como uma janela flutuante sobre os apps.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Abrir apps sem alerta (Priority: P1)

Na primeira vez que abre um app, o usuário é convidado a "configurar a abertura sem alertas". Ele confirma uma única elevação de administrador. A partir daí, os apps abrem direto, sem o alerta, e a área de transferência funciona.

**Why this priority**: O alerta a cada clique anula a proposta de "um clique".

**Independent Test**: Configurar uma vez, abrir 3 apps seguidos e verificar que nenhum alerta de segurança aparece.

**Acceptance Scenarios**:

1. **Given** o cliente não configurado, **When** o usuário abre um app, **Then** o cliente oferece a configuração e explica que exige administrador uma única vez.
2. **Given** que o usuário aceita e confirma a elevação, **When** abre qualquer app, **Then** nenhum alerta de segurança do arquivo de conexão aparece.
3. **Given** que o usuário recusa, **When** abre apps, **Then** eles abrem como antes, com alerta, e o convite não é repetido a cada clique. Ele continua disponível no menu.
4. **Given** um arquivo de conexão não gerado pelo WRApperture (por exemplo, de um e-mail de phishing), **When** é aberto no mesmo PC, **Then** o alerta do Windows continua aparecendo normalmente.
5. **Given** outro usuário do mesmo PC, **When** ele configura, **Then** a configuração do primeiro usuário continua funcionando.
6. **Given** a desinstalação do cliente com remoção de dados, **When** ela conclui, **Then** a configuração de confiança é removida do PC.

---

### User Story 2 - Sem barra de idioma flutuante (Priority: P1)

Ao abrir apps remotos, nenhuma barra de idioma ou teclado fica flutuando sobre as janelas.

**Why this priority**: A barra atrapalha o uso de qualquer app.

**Independent Test**: Instalar ou atualizar o host, fazer logoff da sessão remota, abrir um app pelo cliente e verificar que não há barra flutuante.

**Acceptance Scenarios**:

1. **Given** o host preparado, **When** o usuário abre um app numa nova sessão, **Then** a barra de idioma não aparece.
2. **Given** um usuário do host cujo perfil não estava carregado durante a instalação, **When** ele roda o comando de ocultar a barra (sem administrador), **Then** a barra deixa de aparecer nas sessões dele.
3. **Given** a reversão da preparação, **When** ela conclui, **Then** a configuração anterior da barra é restaurada.

### Edge Cases

- A ferramenta de assinatura do Windows falha: o app abre mesmo assim (sem assinatura, com alerta), e a falha é registrada em log.
- O certificado de assinatura expira (validade de 10 anos) ou é removido: o cliente volta a oferecer a configuração.
- O usuário não é administrador e não tem credenciais de admin: a configuração não é possível, e os apps continuam abrindo com alerta.
- Uma sessão remota já aberta antes da correção da barra de idioma: a mudança só vale numa nova sessão (logoff), e isso é documentado.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: O cliente MUST assinar digitalmente cada arquivo de conexão que gera quando a abertura sem alertas estiver configurada.
- **FR-002**: A configuração MUST exigir uma única elevação. Ela cria uma identidade de assinatura da máquina, utilizável apenas pelos usuários que a configuraram, e a registra como publicadora confiável de conexões remotas.
- **FR-003**: A configuração MUST confiar apenas na identidade do WRApperture. Ela não pode desativar a proteção do Windows para outros arquivos de conexão.
- **FR-004**: A configuração MUST ser idempotente, preservar publicadores confiáveis já existentes e permitir vários usuários no mesmo PC.
- **FR-005**: O cliente MUST oferecer a configuração no primeiro clique em um app quando ela não estiver feita, lembrar a recusa e mantê-la acessível no menu.
- **FR-006**: Se a assinatura falhar, o cliente MUST abrir o app sem assinatura e registrar o erro.
- **FR-007**: A desinstalação com remoção de dados MUST remover a identidade de assinatura e a confiança.
- **FR-008**: A preparação do host MUST ocultar a barra de idioma para os perfis de usuário carregados, registrando o valor anterior para a reversão.
- **FR-009**: O host MUST oferecer um comando, sem administrador, para o usuário atual ocultar ou mostrar a barra de idioma.

### Key Entities

- **RDP Signing Identity**: certificado e chave da máquina no cliente, liberados para os usuários configurados e listados como publicador confiável.
- **Language Bar Setting**: preferência por usuário do host (oculta ou padrão).

## Success Criteria *(mandatory)*

- **SC-001**: Depois da configuração, 0 alertas de segurança ao abrir apps do WRApperture.
- **SC-002**: Arquivos de conexão de terceiros continuam exibindo o alerta (100%).
- **SC-003**: 0 barras de idioma flutuantes em novas sessões RemoteApp num host preparado.

## Assumptions

- O usuário do cliente consegue fornecer credenciais de administrador uma vez. Sem isso, o comportamento atual (com alerta) é mantido.
- O Windows continua honrando a política de "publicadores confiáveis de .rdp" depois da atualização de abril de 2026.
